using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Internal;

/// <summary>
/// Drops one policy of a table before its column changes. Only created by
/// <see cref="TenancyMigrationsSqlGenerator"/>, never part of a migration file.
/// </summary>
internal sealed class DropRlsPolicyOperation : MigrationOperation
{
    public required string Table { get; init; }

    public string? Schema { get; init; }

    public required string Name { get; init; }
}

/// <summary>Creates one policy of a table after its column changes.</summary>
internal sealed class CreateRlsPolicyOperation : MigrationOperation
{
    public required string Table { get; init; }

    public string? Schema { get; init; }

    /// <summary>The serialized policy, as in the table annotation.</summary>
    public required string Policy { get; init; }
}
