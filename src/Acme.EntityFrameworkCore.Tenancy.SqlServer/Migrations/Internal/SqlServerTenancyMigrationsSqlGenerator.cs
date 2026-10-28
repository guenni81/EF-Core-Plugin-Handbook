using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Update;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Migrations.Internal;

/// <summary>
/// SQL Server: RLS through a schema-bound predicate function and one security policy per
/// table (FILTER for reading, BLOCK for writing).
/// </summary>
/// <remarks>
/// Differences from PostgreSQL that a plugin has to handle: CREATE FUNCTION/SCHEMA must be
/// alone in a batch (hence EXEC(N'...')); the policy binds the table via SCHEMABINDING, which is
/// why it must be removed BEFORE DROP TABLE, sp_rename and ALTER SCHEMA ... TRANSFER, and
/// restored afterwards; there is no FORCE.
/// </remarks>
public class SqlServerTenancyMigrationsSqlGenerator : SqlServerMigrationsSqlGenerator
{
    private readonly ISqlServerTenancySingletonOptions _options;

    // AlterTableOperations whose policy change is already covered by the drop/create around the
    // whole operation list (reference equality: operations are not copied)
    private readonly HashSet<MigrationOperation> _wrapped = new(ReferenceEqualityComparer.Instance);

    // Target model of the migration being generated: its annotations decide session key and
    // policy schema, not the application's current options
    private IModel? _model;

    /// <summary>Creates the generator; the first two parameters are passed on to EF's generator.</summary>
    /// <param name="dependencies">The migrations SQL generator dependencies.</param>
    /// <param name="commandBatchPreparer">Used by the base class for data operations.</param>
    /// <param name="options">Provides session key and policy schema.</param>
    public SqlServerTenancyMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        ICommandBatchPreparer commandBatchPreparer,
        ISqlServerTenancySingletonOptions options)
        : base(dependencies, commandBatchPreparer)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <summary>
    /// The settings for the migration being generated: session key and policy schema as recorded
    /// in its target model; the current options only for models that do not record them (before
    /// 1.1, or without tenant tables).
    /// </summary>
    protected virtual SqlServerTenancySettings Settings
    {
        get
        {
            var settings = _options.Settings;
            if (_model?[SqlServerRlsAnnotationNames.SessionKey] is string sessionKey)
                settings = settings with { SessionKey = sessionKey };
            if (_model?[SqlServerRlsAnnotationNames.PolicySchema] is string policySchema)
                settings = settings with { PolicySchema = policySchema };
            return settings;
        }
    }

    /// <summary>
    /// Removes the security policies of all tables whose schema-bound objects change before the
    /// first operation and restores them after the last one, with the final table, schema and
    /// column names of the target model.
    /// </summary>
    /// <remarks>
    /// SQL Server refuses sp_rename, ALTER SCHEMA ... TRANSFER, ALTER COLUMN and DROP COLUMN on a
    /// table bound by a security policy ("object participates in enforced dependencies"). EF
    /// orders a migration's operations itself, so the policy cannot be handled per operation.
    /// All commands run in the migration's transaction: if one fails, the policy is not lost.
    /// </remarks>
    /// <param name="operations">The operations of the migration (Up or Down).</param>
    /// <param name="model">The target model; needed to restore policies after renames.</param>
    /// <param name="options">The generation options.</param>
    /// <returns>The commands.</returns>
#pragma warning disable RS0027 // the override has to keep EF's signature with its optional parameters
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
#pragma warning restore RS0027
    {
        _wrapped.Clear();
        _model = model;
        try
        {
            return base.Generate(WrapSchemaBoundChanges(operations, model), model, options);
        }
        finally
        {
            _wrapped.Clear();
            _model = null;
        }
    }

    /// <summary>
    /// Surrounds the operations with a policy drop for every affected table (original name) and
    /// a policy creation for every affected table that keeps a tenant column (final name).
    /// </summary>
    /// <param name="operations">The operations of the migration.</param>
    /// <param name="model">The target model.</param>
    /// <returns>The operations, possibly surrounded by policy operations.</returns>
    /// <exception cref="InvalidOperationException">A policy could not be restored.</exception>
    protected virtual IReadOnlyList<MigrationOperation> WrapSchemaBoundChanges(
        IReadOnlyList<MigrationOperation> operations, IModel? model)
    {
        // Current name -> original name, and original name -> final name
        var original = new Dictionary<(string? Schema, string Name), (string? Schema, string Name)>();
        var final = new Dictionary<(string? Schema, string Name), (string? Schema, string Name)>();
        var affected = new List<(string? Schema, string Name)>();
        var columns = new Dictionary<(string? Schema, string Name), string?>();
        var created = new HashSet<(string? Schema, string Name)>();
        var dropped = new List<(string? Schema, string Name)>();
        string? oldPolicySchema = null;
        var settingsChanged = false;
        var verifySettings = false;

        (string? Schema, string Name) Original(string? schema, string name)
            => original.TryGetValue((schema, name), out var o) ? o : (schema, name);

        void Affect(string? schema, string name)
        {
            var key = Original(schema, name);
            if (!affected.Contains(key))
                affected.Add(key);
            final.TryAdd(key, (schema, name));
        }

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case RenameTableOperation rename:
                    var key = Original(rename.Schema, rename.Name);
                    Affect(rename.Schema, rename.Name);
                    // EF's differ sets NewSchema to the target schema; null is the default schema
                    var renamed = (rename.NewSchema, rename.NewName ?? rename.Name);
                    original.Remove((rename.Schema, rename.Name));
                    original[renamed] = key;
                    final[key] = renamed;
                    break;
                case RenameColumnOperation column:
                    Affect(column.Schema, column.Table);
                    break;
                case AlterColumnOperation column:
                    Affect(column.Schema, column.Table);
                    break;
                case DropColumnOperation column:
                    Affect(column.Schema, column.Table);
                    break;
                case AlterTableOperation table when TenantColumn(table) != TenantColumn(table.OldTable):
                    Affect(table.Schema, table.Name);
                    columns[Original(table.Schema, table.Name)] = TenantColumn(table);
                    _wrapped.Add(table);
                    break;
                case CreateTableOperation create:
                    created.Add((create.Schema, create.Name));
                    break;
                case AlterDatabaseOperation database when SettingsChanged(database):
                    // Session key or policy schema changed: the function is rebuilt, so every
                    // policy that uses it has to go first
                    settingsChanged = true;
                    oldPolicySchema = database.OldDatabase[SqlServerRlsAnnotationNames.PolicySchema] as string;
                    break;
                case AlterDatabaseOperation database when SettingsRecordedFirst(database):
                    // First migration of this context, or the first after an upgrade from 1.0: an
                    // existing function is of unknown origin and must already fit
                    verifySettings = true;
                    break;
                case DropTableOperation drop:
                    // Dropped later in the same migration: nothing to restore
                    dropped.Add(Original(drop.Schema, drop.Name));
                    if (affected.Contains(Original(drop.Schema, drop.Name)))
                        final.Remove(Original(drop.Schema, drop.Name));
                    break;
            }
        }

        if (settingsChanged && model is not null)
        {
            // Every protected table of the target that already exists before this migration
            var renamedBack = original.ToDictionary(e => e.Key, e => e.Value);
            foreach (var table in model.GetRelationalModel().Tables)
            {
                if (TenantColumn(table) is null || created.Contains((table.Schema, table.Name)))
                    continue;

                var key = renamedBack.TryGetValue((table.Schema, table.Name), out var o) ? o : (table.Schema, table.Name);
                if (!affected.Contains(key))
                    affected.Add(key);
                final.TryAdd(key, (table.Schema, table.Name));
            }

            // Tables dropped by this migration are no longer in the target model, but their
            // policies still use the function until DROP TABLE: remove them before it as well
            // (a table without a plugin policy makes the drop a no-op) and restore none
            foreach (var table in dropped)
            {
                if (!affected.Contains(table))
                    affected.Add(table);
                final.Remove(table);
            }
        }

        if (affected.Count == 0 && !settingsChanged && !verifySettings)
            return operations;

        var result = new List<MigrationOperation>();
        if (verifySettings)
            result.Add(new VerifyTenantPredicateOperation());
        foreach (var table in affected)
            result.Add(new DropTenantPolicyOperation { Table = table.Name, Schema = table.Schema });

        // Without a recorded old schema (snapshot before 1.1) the function can only be found in
        // the schema that is configured now
        if (settingsChanged)
            result.Add(new DropTenantPredicateOperation { Schema = oldPolicySchema ?? Settings.PolicySchema });

        result.AddRange(operations);

        foreach (var table in affected)
        {
            if (!final.TryGetValue(table, out var target))
                continue;

            // Final tenant column: the target model knows it; without a model only an
            // AlterTableOperation of this migration can tell
            string? column;
            if (model?.GetRelationalModel().FindTable(target.Name, target.Schema) is { } targetTable)
                column = TenantColumn(targetTable);
            else if (columns.TryGetValue(table, out var known))
                column = known;
            else if (model is null)
                throw new InvalidOperationException(
                    $"The migration changes table '{target.Name}', which may carry a tenant security "
                    + "policy, but has no target model to restore it from. Generate the migration "
                    + "with 'dotnet ef migrations add' so that it has a designer file.");
            else
                // Never skip silently: the policy was dropped above and would stay dropped
                throw new InvalidOperationException(
                    $"Table '{target.Name}' (schema '{target.Schema}') is changed by the migration but "
                    + "not found in its target model, so its tenant security policy cannot be restored.");

            if (column is not null)
                result.Add(new CreateTenantPolicyOperation
                    { Table = target.Name, Schema = target.Schema, Column = column });
        }

        return result;
    }

    /// <summary>Generates the plugin's own policy operations; everything else goes to EF.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="model">The target model; may be <see langword="null" />.</param>
    /// <param name="builder">The command builder.</param>
    protected override void Generate(MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        switch (operation)
        {
            case DropTenantPolicyOperation drop:
                DropPolicy(drop.Table, TableSchema(drop.Schema, model), builder);
                break;
            case VerifyTenantPredicateOperation:
                VerifyPredicateFunction(builder, onlyIfExists: true);
                break;
            case DropTenantPredicateOperation function:
                Statement(builder,
                    $"DROP FUNCTION IF EXISTS {Delimit(PredicateFunctionName, function.Schema)}");
                break;
            case CreateTenantPolicyOperation create:
                CreatePolicy(create.Table, TableSchema(create.Schema, model), create.Column, builder);
                break;
            default:
                base.Generate(operation, model, builder);
                break;
        }
    }

    /// <summary>Creates the table and, for tenant-owned tables, its security policy.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="model">The target model; may be <see langword="null" />.</param>
    /// <param name="builder">The command builder.</param>
    /// <param name="terminate">Whether to terminate the command.</param>
    protected override void Generate(
        CreateTableOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
    {
        base.Generate(operation, model, builder, terminate);

        // With terminate == false the caller continues the same command; the policy statements
        // must not be appended to an unfinished CREATE TABLE
        if (terminate && TenantColumn(operation) is { } column)
            CreatePolicy(operation.Name, TableSchema(operation.Schema, model), column, builder);
    }

    /// <summary>
    /// Drops and/or creates the policy when the tenant column annotation changes (RLS switched
    /// on or off, or the column renamed).
    /// </summary>
    /// <param name="operation">The operation; <c>OldTable</c> carries the previous annotations.</param>
    /// <param name="model">The target model; may be <see langword="null" />.</param>
    /// <param name="builder">The command builder.</param>
    protected override void Generate(AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        base.Generate(operation, model, builder);

        var oldColumn = TenantColumn(operation.OldTable);
        var newColumn = TenantColumn(operation);
        if (oldColumn == newColumn || _wrapped.Contains(operation))
            return;

        if (oldColumn is not null)
            DropPolicy(operation.Name, TableSchema(operation.Schema, model), builder);
        if (newColumn is not null)
            CreatePolicy(operation.Name, TableSchema(operation.Schema, model), newColumn, builder);
    }

    /// <summary>Drops the policy, then the table.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="model">The target model; may be <see langword="null" />.</param>
    /// <param name="builder">The command builder.</param>
    /// <param name="terminate">Whether to terminate the command.</param>
    protected override void Generate(
        DropTableOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
    {
        // The policy is schema-bound to the table: remove it first (only a policy of this plugin
        // bound to this table, so tables without RLS need no annotation check here)
        DropPolicy(operation.Name, TableSchema(operation.Schema, model), builder);
        base.Generate(operation, model, builder, terminate);
    }

    /// <summary>Creates schema and predicate function if missing, then the table's security policy.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="schema">The table schema (required for SCHEMABINDING).</param>
    /// <param name="column">The tenant column.</param>
    /// <param name="builder">The command builder.</param>
    protected virtual void CreatePolicy(string table, string? schema, string column, MigrationCommandListBuilder builder)
    {
        var policySchema = Settings.PolicySchema;
        var function = Delimit(PredicateFunctionName, policySchema);
        var target = Delimit(table, schema);
        var predicate = $"{function}({Delimit(column)})";

        // 1. Create schema and function idempotently (must be alone in a batch -> EXEC). All tables
        //    share the function; GenerateSqlLiteral doubles the quotes of the nested SQL.
        Statement(builder,
            $"IF SCHEMA_ID({Literal(policySchema)}) IS NULL EXEC({Literal($"CREATE SCHEMA {Delimit(policySchema)}")})");

        var functionSql =
            $"CREATE FUNCTION {function}(@TenantId uniqueidentifier) "
            + "RETURNS TABLE WITH SCHEMABINDING AS RETURN SELECT 1 AS [Allowed] "
            + $"WHERE @TenantId = CAST(SESSION_CONTEXT({Literal(Settings.SessionKey)}) AS uniqueidentifier)";
        Statement(builder,
            $"IF OBJECT_ID({Literal(function)}, N'IF') IS NULL EXEC({Literal(functionSql)})");

        // The function is shared by all tables, and possibly by other contexts in the same
        // database: if it reads another session key, fail instead of binding this table to it
        VerifyPredicateFunction(builder, onlyIfExists: false);

        // 2. Security policy: FILTER (reading) and BLOCK (writing, including moving rows to another
        //    tenant). Without AFTER UPDATE, an UPDATE could set a foreign TenantId.
        Statement(builder,
            $"CREATE SECURITY POLICY {PolicyName(table, schema ?? "dbo")} "
            + $"ADD FILTER PREDICATE {predicate} ON {target}, "
            + $"ADD BLOCK PREDICATE {predicate} ON {target} AFTER INSERT, "
            + $"ADD BLOCK PREDICATE {predicate} ON {target} AFTER UPDATE "
            + "WITH (STATE = ON)");
    }

    /// <summary>Drops the table's security policy if it exists (legacy name only).</summary>
    /// <param name="table">The table name.</param>
    /// <param name="builder">The command builder.</param>
    [Obsolete("Drops only the pre-1.1 policy name. Use DropPolicy(table, schema, builder), which "
              + "finds the plugin's policy by the table it is bound to.")]
    protected virtual void DropPolicy(string table, MigrationCommandListBuilder builder)
        => DropPolicy(table, "dbo", builder);

    /// <summary>
    /// Drops the policy of this plugin that is bound to the table, whatever its name: the
    /// pre-1.1 name <c>tenant_Table</c> as well as <c>tenant_schema_Table</c>. Policies of
    /// others, and tables without one, stay untouched.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="schema">The table schema.</param>
    /// <param name="builder">The command builder.</param>
    protected virtual void DropPolicy(string table, string schema, MigrationCommandListBuilder builder)
    {
        // A plugin policy uses the predicate function; the LIKE pattern escapes [ and _
        var pattern = $"%[[]{PredicateFunctionName.Replace("_", "[_]", StringComparison.Ordinal)}]%";
        Statement(builder,
            "DECLARE @policy nvarchar(400) = (SELECT TOP (1) "
            + "QUOTENAME(SCHEMA_NAME(p.schema_id)) + N'.' + QUOTENAME(p.name) "
            + "FROM sys.security_policies AS p WHERE EXISTS (SELECT 1 FROM sys.security_predicates AS s "
            + "WHERE s.object_id = p.object_id "
            + $"AND s.target_object_id = OBJECT_ID({Literal(Delimit(table, schema))}) "
            + $"AND s.predicate_definition LIKE {Literal(pattern)}));\n"
            + "IF @policy IS NOT NULL EXEC(N'DROP SECURITY POLICY ' + @policy)");
    }

    /// <summary>Name of the shared predicate function in the policy schema.</summary>
    protected virtual string PredicateFunctionName => "fn_tenant_predicate";

    /// <summary>Delimited pre-1.1 policy name <c>[policy schema].[tenant_Table]</c>.</summary>
    /// <param name="table">The table name.</param>
    /// <returns>The delimited, schema-qualified policy name.</returns>
    [Obsolete("Tables with the same name in different schemas collide. Use PolicyName(table, schema).")]
    protected virtual string PolicyName(string table)
        => Delimit($"tenant_{table}", Settings.PolicySchema);

    /// <summary>
    /// Delimited policy name <c>[policy schema].[tenant_schema_Table]</c>: unique for tables with
    /// the same name in different schemas (the convention rejects the remaining ambiguities).
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="schema">The table schema.</param>
    /// <returns>The delimited, schema-qualified policy name.</returns>
    protected virtual string PolicyName(string table, string schema)
        => Delimit(SqlServerTenancyNames.PolicyName(schema, table), Settings.PolicySchema);

    /// <summary>
    /// SCHEMABINDING only accepts two-part names, so a table without an explicit schema is
    /// qualified with the model's default schema or, failing that, with <c>dbo</c>.
    /// </summary>
    private static string TableSchema(string? schema, IModel? model)
        => schema ?? model?.GetDefaultSchema() ?? "dbo";

    /// <summary>
    /// Fails if the predicate function reads another session key than the settings of this
    /// migration: it may belong to another context or to an older configuration.
    /// </summary>
    /// <param name="builder">The command builder.</param>
    /// <param name="onlyIfExists">Whether a missing function is acceptable.</param>
    protected virtual void VerifyPredicateFunction(MigrationCommandListBuilder builder, bool onlyIfExists)
    {
        var function = Delimit(PredicateFunctionName, Settings.PolicySchema);
        var keyPattern = "%SESSION[_]CONTEXT(" + Literal(Settings.SessionKey)
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal) + ")%";
        var message = $"{function} reads a different session key than '{Settings.SessionKey}'. Contexts with "
                      + "different session keys need different policy schemas; change the key of an existing "
                      + "installation in a migration of its own after upgrading.";

        Statement(builder,
            (onlyIfExists ? $"IF OBJECT_ID({Literal(function)}, N'IF') IS NOT NULL AND " : "IF ")
            + $"OBJECT_DEFINITION(OBJECT_ID({Literal(function)})) NOT LIKE {Literal(keyPattern)} "
            + $"THROW 50000, {Literal(message)}, 1");
    }

    // Both values recorded before and after, and different: a change the migration can carry out
    private static bool SettingsChanged(AlterDatabaseOperation operation)
        => operation[SqlServerRlsAnnotationNames.SessionKey] is string sessionKey
           && operation[SqlServerRlsAnnotationNames.PolicySchema] is string policySchema
           && operation.OldDatabase[SqlServerRlsAnnotationNames.SessionKey] is string oldSessionKey
           && operation.OldDatabase[SqlServerRlsAnnotationNames.PolicySchema] is string oldPolicySchema
           && (oldSessionKey != sessionKey || oldPolicySchema != policySchema);

    // Recorded now, unknown before (first migration, or snapshot before 1.1). Without values in
    // the target (Down to such a snapshot) the database keeps what it has.
    private static bool SettingsRecordedFirst(AlterDatabaseOperation operation)
        => operation[SqlServerRlsAnnotationNames.SessionKey] is string
           && operation.OldDatabase[SqlServerRlsAnnotationNames.SessionKey] is null;

    private static string? TenantColumn(IReadOnlyAnnotatable annotatable)
        => annotatable[SqlServerRlsAnnotationNames.TenantColumn] as string;

    // EndStatement closes the MigrationCommand: every statement is sent as a separate batch
    private void Statement(MigrationCommandListBuilder builder, string sql)
    {
        builder.Append(sql).AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    // N'...' literal with correctly doubled quotes, via the provider's string mapping
    private string Literal(string value)
        => Dependencies.TypeMappingSource.FindMapping(typeof(string))!.GenerateSqlLiteral(value);

    private string Delimit(string name, string? schema = null)
        => Dependencies.SqlGenerationHelper.DelimitIdentifier(name, schema);
}
