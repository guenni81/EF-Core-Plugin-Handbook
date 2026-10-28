using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

// In the Migrations namespace: migration files always import it
namespace Microsoft.EntityFrameworkCore.Migrations;

/// <summary>Custom operations for hand-written parts of migrations.</summary>
public static class TenancyMigrationBuilderExtensions
{
    /// <summary>CREATE ROLE ... NOSUPERUSER NOBYPASSRLS, if the role does not exist.</summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="name">The role name (lowercase letters, digits, underscore).</param>
    /// <param name="canLogin">Whether the role may log in.</param>
    /// <returns>A builder for further annotations on the operation.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a simple identifier.</exception>
    public static OperationBuilder<CreateApplicationRoleOperation> CreateApplicationRole(
        this MigrationBuilder migrationBuilder, string name, bool canLogin = false)
    {
        var operation = new CreateApplicationRoleOperation
        {
            Name = TenancyIdentifiers.ValidateIdentifier(name, "role name"),
            CanLogin = canLogin,
        };
        migrationBuilder.Operations.Add(operation);
        return new OperationBuilder<CreateApplicationRoleOperation>(operation);
    }

    /// <summary>DROP ROLE IF EXISTS.</summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="name">The role name (lowercase letters, digits, underscore).</param>
    /// <returns>A builder for further annotations on the operation.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a simple identifier.</exception>
    public static OperationBuilder<DropApplicationRoleOperation> DropApplicationRole(
        this MigrationBuilder migrationBuilder, string name)
    {
        var operation = new DropApplicationRoleOperation
        {
            Name = TenancyIdentifiers.ValidateIdentifier(name, "role name"),
        };
        migrationBuilder.Operations.Add(operation);
        return new OperationBuilder<DropApplicationRoleOperation>(operation);
    }

    /// <summary>
    /// GRANT SELECT, INSERT, UPDATE, DELETE on all tables and USAGE, SELECT on all sequences of a
    /// schema, plus default privileges for tables and sequences created later.
    /// </summary>
    /// <remarks>
    /// The default privileges apply to objects that the role running the migration creates later;
    /// tables and sequences created by another role need privileges of their own.
    /// </remarks>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="role">The role name (lowercase letters, digits, underscore).</param>
    /// <param name="schema">The schema; delimited when the SQL is generated.</param>
    /// <returns>A builder for further annotations on the operation.</returns>
    /// <exception cref="ArgumentException"><paramref name="role"/> is not a simple identifier.</exception>
    public static OperationBuilder<GrantTablePrivilegesOperation> GrantTablePrivileges(
        this MigrationBuilder migrationBuilder, string role, string schema = "public")
    {
        var operation = new GrantTablePrivilegesOperation
        {
            Role = TenancyIdentifiers.ValidateIdentifier(role, "role name"),
            Schema = schema,
        };
        migrationBuilder.Operations.Add(operation);
        return new OperationBuilder<GrantTablePrivilegesOperation>(operation);
    }
}
