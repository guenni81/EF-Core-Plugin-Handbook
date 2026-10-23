using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;
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
/// why it must be removed BEFORE DROP TABLE; there is no FORCE.
/// </remarks>
public class SqlServerTenancyMigrationsSqlGenerator : SqlServerMigrationsSqlGenerator
{
    private readonly ISqlServerTenancySingletonOptions _options;

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

    /// <summary>The tenancy settings of the current context.</summary>
    protected virtual SqlServerTenancySettings Settings => _options.Settings;

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
        if (oldColumn == newColumn)
            return;

        if (oldColumn is not null)
            DropPolicy(operation.Name, builder);
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
        // The policy is schema-bound to the table: remove it first (idempotent, so tables without
        // RLS need no annotation check here)
        DropPolicy(operation.Name, builder);
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

        // 2. Security policy: FILTER (reading) and BLOCK (writing, including moving rows to another
        //    tenant). Without AFTER UPDATE, an UPDATE could set a foreign TenantId.
        Statement(builder,
            $"CREATE SECURITY POLICY {PolicyName(table)} "
            + $"ADD FILTER PREDICATE {predicate} ON {target}, "
            + $"ADD BLOCK PREDICATE {predicate} ON {target} AFTER INSERT, "
            + $"ADD BLOCK PREDICATE {predicate} ON {target} AFTER UPDATE "
            + "WITH (STATE = ON)");
    }

    /// <summary>Drops the table's security policy if it exists.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="builder">The command builder.</param>
    protected virtual void DropPolicy(string table, MigrationCommandListBuilder builder)
        => Statement(builder, $"DROP SECURITY POLICY IF EXISTS {PolicyName(table)}");

    /// <summary>Name of the shared predicate function in the policy schema.</summary>
    protected virtual string PredicateFunctionName => "fn_tenant_predicate";

    /// <summary>
    /// Delimited policy name: <c>[schema].[tenant_Table]</c>. Derived from the table name only,
    /// so tables with the same name in different schemas would collide.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <returns>The delimited, schema-qualified policy name.</returns>
    protected virtual string PolicyName(string table)
        => Delimit($"tenant_{table}", Settings.PolicySchema);

    /// <summary>
    /// SCHEMABINDING only accepts two-part names, so a table without an explicit schema is
    /// qualified with the model's default schema or, failing that, with <c>dbo</c>.
    /// </summary>
    private static string TableSchema(string? schema, IModel? model)
        => schema ?? model?.GetDefaultSchema() ?? "dbo";

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
