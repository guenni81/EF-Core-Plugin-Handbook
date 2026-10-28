using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Operations;

/// <summary>Creates a database role for the application if it does not exist (idempotent).</summary>
public class CreateApplicationRoleOperation : MigrationOperation
{
    /// <summary>The role name; validated by the <c>MigrationBuilder</c> extension method.</summary>
    public required string Name { get; init; }

    /// <summary>Allow LOGIN (the password is deliberately NOT managed in migrations).</summary>
    public bool CanLogin { get; init; }
}

/// <summary>Drops a role if it exists.</summary>
public class DropApplicationRoleOperation : MigrationOperation
{
    /// <summary>The role name; validated by the <c>MigrationBuilder</c> extension method.</summary>
    public required string Name { get; init; }
}

/// <summary>
/// Grants a role DML privileges on all tables and usage of all sequences of a schema, including
/// those that the migrating role creates later.
/// </summary>
public class GrantTablePrivilegesOperation : MigrationOperation
{
    /// <summary>The role that receives the privileges.</summary>
    public required string Role { get; init; }
    /// <summary>The schema whose tables are covered; defaults to <c>public</c>.</summary>
    public string Schema { get; init; } = "public";
}
