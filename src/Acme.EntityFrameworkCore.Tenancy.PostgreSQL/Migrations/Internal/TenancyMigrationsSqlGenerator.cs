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
    /// old and new annotations.
    /// </summary>
    protected override void Generate(
        AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        base.Generate(operation, model, builder);
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

        // 3. Create new or changed policies. DROP + CREATE instead of ALTER POLICY, because
        //    ALTER POLICY can change neither FOR nor PERMISSIVE/RESTRICTIVE.
        //    DDL is transactional in PostgreSQL: no visible gap occurs.
        foreach (var (name, json) in newPolicies)
        {
            if (!oldPolicies.TryGetValue(name, out var oldJson) || oldJson != json)
                Statement(builder, CreatePolicySql(table, RlsPolicy.Deserialize(json)));
        }
    }

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
