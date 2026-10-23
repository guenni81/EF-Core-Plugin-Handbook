using System.Data;
using System.Data.Common;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Scaffolding.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Scaffolding;

/// <summary>
/// During scaffolding (dotnet ef dbcontext scaffold), additionally reads the RLS status and
/// policies and attaches them as annotations to the tables. EF copies table annotations to the
/// entity types; the annotation code generator writes them as HasRowLevelSecurity/HasPolicy
/// into the generated context.
/// </summary>
/// <remarks>
/// Inherits from <c>NpgsqlDatabaseModelFactory</c> in an *.Internal namespace (internal Npgsql
/// API, EF1001): there is only one IDatabaseModelFactory.
/// </remarks>
public class TenancyDatabaseModelFactory : NpgsqlDatabaseModelFactory
{
    /// <summary>Creates the factory.</summary>
    /// <param name="logger">Scaffolding logger.</param>
    public TenancyDatabaseModelFactory(IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger)
        : base(logger)
    {
    }

    /// <summary>Reads the database model via Npgsql, then adds RLS status and policies.</summary>
    /// <param name="dbConnection">Connection to the database being scaffolded.</param>
    /// <param name="options">Filter options (tables, schemas).</param>
    /// <returns>The database model including the RLS annotations.</returns>
    public override DatabaseModel Create(DbConnection dbConnection, DatabaseModelFactoryOptions options)
    {
        var model = base.Create(dbConnection, options);

        // Open only if needed and restore the caller's connection state afterwards
        var opened = false;
        if (dbConnection.State != ConnectionState.Open)
        {
            dbConnection.Open();
            opened = true;
        }

        try
        {
            ReadRowLevelSecurity(dbConnection, model);
            ReadPolicies(dbConnection, model);
        }
        finally
        {
            if (opened)
                dbConnection.Close();
        }

        return model;
    }

    /// <summary>Reads tables with RLS enabled from pg_class (relrowsecurity, relforcerowsecurity).</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="model">The model whose tables receive the annotations.</param>
    protected virtual void ReadRowLevelSecurity(DbConnection connection, DatabaseModel model)
    {
        // relkind 'r' = ordinary table, 'p' = partitioned table
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT n.nspname, c.relname, c.relforcerowsecurity
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p') AND c.relrowsecurity
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Only tables Npgsql has read (respects the user's table/schema filter)
            if (FindTable(model, reader.GetString(0), reader.GetString(1)) is not { } table)
                continue;

            // Defaults are not stored, as in the Fluent API (no empty migrations)
            table[RlsAnnotationNames.Enabled] = true;
            if (reader.GetBoolean(2))
                table[RlsAnnotationNames.Forced] = true;
        }
    }

    /// <summary>Reads all policies from pg_policies.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="model">The model whose tables receive the annotations.</param>
    protected virtual void ReadPolicies(DbConnection connection, DatabaseModel model)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT schemaname, tablename, policyname, permissive, roles::text[], cmd, qual, with_check
            FROM pg_catalog.pg_policies
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (FindTable(model, reader.GetString(0), reader.GetString(1)) is not { } table)
                continue;

            // roles is name[]; the ::text[] cast lets Npgsql read it as string[].
            // qual/with_check come back as deparsed SQL text, normalized by PostgreSQL.
            var roles = reader.GetFieldValue<string[]>(4);
            var policy = new RlsPolicy(
                Name: reader.GetString(2),
                Command: Enum.Parse<PolicyCommand>(reader.GetString(5), ignoreCase: true),
                Permissive: reader.GetString(3) == "PERMISSIVE",
                // Role "public" is the default without a TO clause
                Roles: roles is ["public"] ? null : roles,
                Using: reader.IsDBNull(6) ? null : reader.GetString(6),
                WithCheck: reader.IsDBNull(7) ? null : reader.GetString(7));

            table[RlsAnnotationNames.PolicyPrefix + policy.Name] = policy.Serialize();
        }
    }

    // A table without a schema is treated as being in the default schema "public"
    private static DatabaseTable? FindTable(DatabaseModel model, string schema, string name)
        => model.Tables.FirstOrDefault(t => t.Name == name && (t.Schema ?? "public") == schema);
}
