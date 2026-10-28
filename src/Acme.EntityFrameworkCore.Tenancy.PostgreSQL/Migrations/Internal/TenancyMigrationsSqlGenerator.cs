using System.Diagnostics;
using System.Text;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Internal;

/// <summary>
/// Extends the Npgsql generator with
/// (1) declarative RLS from table annotations (Create/AlterTableOperation) and
/// (2) custom, imperative operations (roles, privileges).
/// </summary>
/// <remarks>
/// A policy depends on the columns it names: CREATE POLICY needs them, DROP COLUMN fails while a
/// policy uses them. EF orders the AlterTableOperation of a table before its new columns and
/// after its dropped ones, so the policy statements are moved around the table's column changes
/// (<see cref="MovePolicyChanges"/>).
/// </remarks>
/// <remarks>
/// There is only one IMigrationsSqlGenerator per context, and it belongs to the provider.
/// That is why the plugin derives from the Npgsql generator. Check the constructor on version
/// upgrades: <see cref="INpgsqlSingletonOptions"/> is an internal Npgsql API (EF1001).
/// </remarks>
public class TenancyMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator
{
    /// <summary>Creates the generator; parameters are passed through to Npgsql's generator.</summary>
    /// <param name="dependencies">EF's parameter object for migrations SQL generators.</param>
    /// <param name="npgsqlSingletonOptions">Npgsql's singleton options (internal API).</param>
    public TenancyMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        INpgsqlSingletonOptions npgsqlSingletonOptions)
        : base(dependencies, npgsqlSingletonOptions)
    {
    }

    // AlterTableOperations whose policy statements were moved into their own operations
    // (reference equality: operations are not copied)
    private readonly HashSet<MigrationOperation> _moved = new(ReferenceEqualityComparer.Instance);

    // ---------------------------------------------------------------- Operation list
    /// <summary>
    /// Moves the policy changes of every table around its column changes, then lets Npgsql
    /// generate the commands.
    /// </summary>
    /// <param name="operations">The operations of the migration (Up or Down).</param>
    /// <param name="model">The target model.</param>
    /// <param name="options">The generation options.</param>
    /// <returns>The commands.</returns>
#pragma warning disable RS0027 // the override has to keep EF's signature with its optional parameters
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
#pragma warning restore RS0027
    {
        _moved.Clear();
        try
        {
            return base.Generate(MovePolicyChanges(operations), model, options);
        }
        finally
        {
            _moved.Clear();
        }
    }

    /// <summary>
    /// Drops the changed or removed policies of a table before its first column change and
    /// creates the new or changed ones after its last, instead of at its AlterTableOperation.
    /// </summary>
    /// <remarks>
    /// Adding a tenant column together with the isolation yields AlterTable, AddColumn,
    /// CreateIndex: the policy has to follow the column. The reverse yields DropIndex, DropColumn,
    /// AlterTable: the policy has to go before the column. Renamed tables keep their identity, so
    /// every statement uses the table name that is valid at its position. All statements stay in
    /// the migration's transaction; ENABLE/DISABLE and FORCE stay at the AlterTableOperation.
    /// </remarks>
    /// <param name="operations">The operations of the migration.</param>
    /// <returns>The operations with the policy statements as operations of their own.</returns>
    protected virtual IReadOnlyList<MigrationOperation> MovePolicyChanges(
        IReadOnlyList<MigrationOperation> operations)
    {
        // A table's identity across renames: current name -> id
        var ids = new Dictionary<(string? Schema, string Name), int>();
        var table = new int?[operations.Count];
        var first = new Dictionary<int, int>();
        var last = new Dictionary<int, int>();
        var changes = new Dictionary<int, AlterTableOperation>();
        var next = 0;

        int Id(string? schema, string name)
        {
            if (!ids.TryGetValue((schema, name), out var id))
                ids[(schema, name)] = id = next++;
            return id;
        }

        void Touch(int index, string? schema, string name)
        {
            var id = Id(schema, name);
            table[index] = id;
            first.TryAdd(id, index);
            last[id] = index;
        }

        for (var i = 0; i < operations.Count; i++)
        {
            switch (operations[i])
            {
                case RenameTableOperation rename:
                    var id = Id(rename.Schema, rename.Name);
                    ids.Remove((rename.Schema, rename.Name));
                    // EF's differ sets NewSchema to the target schema; null is the default schema
                    ids[(rename.NewSchema, rename.NewName ?? rename.Name)] = id;
                    break;
                case AddColumnOperation column:
                    Touch(i, column.Schema, column.Table);
                    break;
                case AlterColumnOperation column:
                    Touch(i, column.Schema, column.Table);
                    break;
                case RenameColumnOperation column:
                    Touch(i, column.Schema, column.Table);
                    break;
                case DropColumnOperation column:
                    Touch(i, column.Schema, column.Table);
                    break;
                case AlterTableOperation alter
                    when !PoliciesEqual(alter.OldTable.GetRlsPolicies(), alter.GetRlsPolicies()):
                    Touch(i, alter.Schema, alter.Name);
                    changes[table[i]!.Value] = alter;
                    break;
            }
        }

        if (changes.Count == 0)
            return operations;

        var result = new List<MigrationOperation>(operations.Count + 2 * changes.Count);
        for (var i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];
            var id = table[i];
            var alter = id is { } t ? changes.GetValueOrDefault(t) : null;

            if (alter is not null && first[id!.Value] == i)
            {
                var oldPolicies = alter.OldTable.GetRlsPolicies();
                var newPolicies = alter.GetRlsPolicies();
                var (schema, name) = TableOf(operation);
                foreach (var (policy, json) in oldPolicies)
                {
                    if (!newPolicies.TryGetValue(policy, out var newJson) || newJson != json)
                        result.Add(new DropRlsPolicyOperation { Table = name, Schema = schema, Name = policy });
                }
            }

            result.Add(operation);
            if (operation == alter)
                _moved.Add(alter);

            if (alter is not null && last[id!.Value] == i)
            {
                var oldPolicies = alter.OldTable.GetRlsPolicies();
                var (schema, name) = TableOf(operation);
                foreach (var (policy, json) in alter.GetRlsPolicies())
                {
                    if (!oldPolicies.TryGetValue(policy, out var oldJson) || oldJson != json)
                        result.Add(new CreateRlsPolicyOperation { Table = name, Schema = schema, Policy = json });
                }
            }
        }

        return result;
    }

    // ---------------------------------------------------------------- Dispatch
    /// <summary>
    /// Central dispatch: routes the plugin's own operations to their generators; all other
    /// operations go to Npgsql.
    /// </summary>
    protected override void Generate(
        MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        switch (operation)
        {
            case CreateApplicationRoleOperation createRole:
                Generate(createRole, builder);
                break;
            case DropApplicationRoleOperation dropRole:
                Generate(dropRole, builder);
                break;
            case GrantTablePrivilegesOperation grant:
                Generate(grant, builder);
                break;
            case DropRlsPolicyOperation drop:
                Statement(builder, $"DROP POLICY {Delimit(drop.Name)} ON {Delimit(drop.Table, drop.Schema)}");
                break;
            case CreateRlsPolicyOperation create:
                Statement(builder,
                    CreatePolicySql(Delimit(create.Table, create.Schema), RlsPolicy.Deserialize(create.Policy)));
                break;
            default:
                base.Generate(operation, model, builder);
                break;
        }
    }

    // ---------------------------------------------------------------- Declarative RLS
    /// <summary>Emits CREATE TABLE, followed by the table's RLS statements.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="terminate"/> is <see langword="false"/> and the table has RLS configuration.
    /// </exception>
    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        // Unterminated SQL (terminate: false) cannot take further statements.
        // Npgsql handles comments the same way.
        if (!terminate && operation.HasRlsConfiguration())
            throw new ArgumentException(
                "RLS configuration cannot be emitted in an unterminated CREATE TABLE.");

        base.Generate(operation, model, builder, terminate);

        if (terminate)
            GenerateRowLevelSecurity(operation.Name, operation.Schema, previous: null, operation, builder);
    }

    /// <summary>
    /// Emits ALTER TABLE, followed by the RLS statements derived from the difference between the
    /// old and new annotations; only the RLS state if the policy statements were moved.
    /// </summary>
    protected override void Generate(
        AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        base.Generate(operation, model, builder);
        if (_moved.Contains(operation))
            GenerateRowLevelSecurityState(Delimit(operation.Name, operation.Schema), operation.OldTable,
                operation, builder);
        else
            GenerateRowLevelSecurity(operation.Name, operation.Schema, operation.OldTable, operation, builder);
    }

    // DropTable/RenameTable: nothing to do. PostgreSQL drops or moves the policies itself.

    /// <summary>
    /// Compares the RLS annotations of <paramref name="previous"/> and <paramref name="current"/>
    /// and emits the statements needed to get from one to the other.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="schema">The schema, or <see langword="null"/> for the default schema.</param>
    /// <param name="previous">The old annotations, or <see langword="null"/> for a new table.</param>
    /// <param name="current">The new annotations.</param>
    /// <param name="builder">The builder that receives the commands.</param>
    protected virtual void GenerateRowLevelSecurity(
        string tableName,
        string? schema,
        IReadOnlyAnnotatable? previous,
        IReadOnlyAnnotatable current,
        MigrationCommandListBuilder builder)
    {
        var table = Delimit(tableName, schema);
        var oldPolicies = previous?.GetRlsPolicies() ?? new Dictionary<string, string>();
        var newPolicies = current.GetRlsPolicies();

        // 1. Drop removed or changed policies (first, so a changed policy can be recreated)
        foreach (var (name, json) in oldPolicies)
        {
            if (!newPolicies.TryGetValue(name, out var newJson) || newJson != json)
                Statement(builder, $"DROP POLICY {Delimit(name)} ON {table}");
        }

        // 2. RLS state of the table
        GenerateRowLevelSecurityState(table, previous, current, builder);

        // 3. Create new or changed policies. DROP + CREATE instead of ALTER POLICY, because
        //    ALTER POLICY can change neither FOR nor PERMISSIVE/RESTRICTIVE.
        //    DDL is transactional in PostgreSQL: no visible gap occurs.
        foreach (var (name, json) in newPolicies)
        {
            if (!oldPolicies.TryGetValue(name, out var oldJson) || oldJson != json)
                Statement(builder, CreatePolicySql(table, RlsPolicy.Deserialize(json)));
        }
    }

    // ENABLE/DISABLE and FORCE/NO FORCE: independent of the columns, so never moved
    private void GenerateRowLevelSecurityState(
        string table, IReadOnlyAnnotatable? previous, IReadOnlyAnnotatable current,
        MigrationCommandListBuilder builder)
    {
        var wasEnabled = previous?.IsRlsEnabled() ?? false;
        var isEnabled = current.IsRlsEnabled();
        if (wasEnabled != isEnabled)
        {
            var action = isEnabled ? "ENABLE" : "DISABLE";
            Statement(builder, $"ALTER TABLE {table} {action} ROW LEVEL SECURITY");
        }

        var wasForced = previous?.IsRlsForced() ?? false;
        var isForced = current.IsRlsForced();
        if (wasForced != isForced)
        {
            var action = isForced ? "FORCE" : "NO FORCE";
            Statement(builder, $"ALTER TABLE {table} {action} ROW LEVEL SECURITY");
        }
    }

    private static bool PoliciesEqual(
        IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> current)
        => previous.Count == current.Count
           && previous.All(p => current.TryGetValue(p.Key, out var json) && json == p.Value);

    // Table of a column or table operation at its position in the list
    private static (string? Schema, string Name) TableOf(MigrationOperation operation)
        => operation switch
        {
            AlterTableOperation t => (t.Schema, t.Name),
            AddColumnOperation c => (c.Schema, c.Table),
            AlterColumnOperation c => (c.Schema, c.Table),
            RenameColumnOperation c => (c.Schema, c.Table),
            DropColumnOperation c => (c.Schema, c.Table),
            _ => throw new UnreachableException(),
        };

    /// <summary>
    /// Builds a <c>CREATE POLICY</c> statement. Public and virtual: reusable and customizable.
    /// </summary>
    /// <param name="delimitedTable">The already delimited (quoted) table name.</param>
    /// <param name="policy">The policy to create.</param>
    /// <returns>The SQL statement without terminator.</returns>
    public virtual string CreatePolicySql(string delimitedTable, RlsPolicy policy)
    {
        var sql = new StringBuilder()
            .Append("CREATE POLICY ").Append(Delimit(policy.Name))
            .Append(" ON ").Append(delimitedTable)
            .Append(policy.Permissive ? " AS PERMISSIVE" : " AS RESTRICTIVE")
            .Append(" FOR ").Append(policy.Command.ToString().ToUpperInvariant());

        if (policy.Roles is { Length: > 0 })
            sql.Append(" TO ").AppendJoin(", ", policy.Roles.Select(DelimitRole));

        // USING/WITH CHECK: the developer's SQL, deliberately not escaped. It comes from the
        // model (code), never from user input.
        if (policy.Using is not null)
            sql.Append(" USING (").Append(policy.Using).Append(')');

        if (policy.WithCheck is not null)
            sql.Append(" WITH CHECK (").Append(policy.WithCheck).Append(')');

        return sql.ToString();
    }

    // ---------------------------------------------------------------- Custom operations
    /// <summary>Emits an idempotent <c>CREATE ROLE</c> wrapped in a <c>DO</c> block.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="builder">The builder that receives the command.</param>
    protected virtual void Generate(CreateApplicationRoleOperation operation, MigrationCommandListBuilder builder)
    {
        // Distinguish the literal (for the comparison) from the identifier (for CREATE ROLE)!
        // PostgreSQL has no CREATE ROLE IF NOT EXISTS, hence the DO block. NOBYPASSRLS keeps
        // the application role subject to the policies.
        var literal = Literal(operation.Name);
        var identifier = Delimit(operation.Name);
        var login = operation.CanLogin ? "LOGIN" : "NOLOGIN";

        builder
            .AppendLine("DO $$")
            .AppendLine("BEGIN")
            .AppendLine($"    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = {literal}) THEN")
            .AppendLine($"        CREATE ROLE {identifier} {login} NOSUPERUSER NOBYPASSRLS;")
            .AppendLine("    END IF;")
            .AppendLine("END")
            .Append("$$");
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    /// <summary>Emits <c>DROP ROLE IF EXISTS</c>.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="builder">The builder that receives the command.</param>
    protected virtual void Generate(DropApplicationRoleOperation operation, MigrationCommandListBuilder builder)
        => Statement(builder, $"DROP ROLE IF EXISTS {Delimit(operation.Name)}");

    /// <summary>
    /// Grants schema usage, DML on all tables, sequence usage, and default privileges for tables
    /// and sequences created later.
    /// </summary>
    /// <remarks>
    /// The default privileges cover objects that the role running this migration creates later
    /// (no FOR ROLE clause), e.g. the sequence of a serial column or a HiLo sequence; objects
    /// created by other roles are not covered.
    /// </remarks>
    /// <param name="operation">The operation.</param>
    /// <param name="builder">The builder that receives the commands.</param>
    protected virtual void Generate(GrantTablePrivilegesOperation operation, MigrationCommandListBuilder builder)
    {
        var schema = Delimit(operation.Schema);
        var role = Delimit(operation.Role);

        Statement(builder, $"GRANT USAGE ON SCHEMA {schema} TO {role}");
        Statement(builder,
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {role}");
        Statement(builder, $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO {role}");
        // Default privileges only apply to tables and sequences created later by the role running
        // this migration (no FOR ROLE clause).
        Statement(builder,
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} "
            + $"GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {role}");
        Statement(builder,
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT USAGE, SELECT ON SEQUENCES TO {role}");
    }

    // ---------------------------------------------------------------- Helpers
    /// <summary>Appends one terminated statement and ends the command.</summary>
    /// <remarks>
    /// <c>EndStatement</c> ends the command like the base class does and honors transaction
    /// suppression.
    /// </remarks>
    /// <param name="builder">The builder that receives the command.</param>
    /// <param name="sql">The statement without terminator.</param>
    protected virtual void Statement(MigrationCommandListBuilder builder, string sql)
    {
        builder
            .Append(sql)
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    /// <summary>Formats a value as an SQL string literal via the string type mapping.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The escaped literal, e.g. <c>'shop_app'</c>.</returns>
    protected virtual string Literal(string value)
        => Dependencies.TypeMappingSource.FindMapping(typeof(string))!.GenerateSqlLiteral(value);

    // Npgsql's own DelimitIdentifier helpers are private -> our own via the dependencies
    /// <summary>Delimits (quotes) an identifier.</summary>
    /// <param name="identifier">The identifier.</param>
    /// <returns>The delimited identifier, e.g. <c>"shop_app"</c>.</returns>
    protected virtual string Delimit(string identifier)
        => Dependencies.SqlGenerationHelper.DelimitIdentifier(identifier);

    /// <summary>Delimits (quotes) a schema-qualified name.</summary>
    /// <param name="name">The object name.</param>
    /// <param name="schema">The schema, or <see langword="null"/>.</param>
    /// <returns>The delimited name, e.g. <c>"public"."Orders"</c>.</returns>
    protected virtual string Delimit(string name, string? schema)
        => Dependencies.SqlGenerationHelper.DelimitIdentifier(name, schema);

    // Role keywords must not be quoted: "PUBLIC" would name a (non-existent) role
    private string DelimitRole(string role)
        => role.ToUpperInvariant() is "PUBLIC" or "CURRENT_USER" or "SESSION_USER" or "CURRENT_ROLE"
            ? role.ToUpperInvariant()
            : Delimit(role);
}
