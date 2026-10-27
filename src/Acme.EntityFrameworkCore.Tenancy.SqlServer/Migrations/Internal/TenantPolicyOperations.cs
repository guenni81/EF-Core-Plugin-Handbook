using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Migrations.Internal;

/// <summary>
/// Removes the plugin's security policy of a table before a schema-bound change. Only created by
/// <see cref="SqlServerTenancyMigrationsSqlGenerator"/>, never part of a migration file.
/// </summary>
internal sealed class DropTenantPolicyOperation : MigrationOperation
{
    public required string Table { get; init; }

    public string? Schema { get; init; }
}

/// <summary>Restores the plugin's security policy with the table's final names.</summary>
internal sealed class CreateTenantPolicyOperation : MigrationOperation
{
    public required string Table { get; init; }

    public string? Schema { get; init; }

    public required string Column { get; init; }
}
