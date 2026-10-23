using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>Tenant-owned through the <see cref="ITenantOwned"/> marker interface.</summary>
public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Tenant-owned; used for additional policies and string comparisons.</summary>
public class Customer : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Tenant-owned, but deliberately opted out via data annotation.</summary>
[NoTenantIsolation]
public class AuditEntry : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
}

/// <summary>Shared across tenants: no marker, no isolation.</summary>
public class Product
{
    public int Id { get; set; }
}

/// <summary>Tenant directory: key of type TenantId (value generation).</summary>
public class Tenant
{
    public TenantId Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Without ITenantOwned: tenant property marked via Fluent API. The property is deliberately
/// not called TenantId ("Mandant" is German for tenant) to prove that any name works.
/// </summary>
public class Contract
{
    public int Id { get; set; }
    public TenantId Mandant { get; set; }
}

/// <summary>For JSON tests: TenantId in a JSON column (only configured via lambda).</summary>
public class Shipment : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public ShipmentDetails Details { get; set; } = new();
}

/// <summary>Owned type mapped to the JSON column of <see cref="Shipment"/>.</summary>
public class ShipmentDetails
{
    public string Carrier { get; set; } = "";
    public TenantId ApprovedBy { get; set; }
}

/// <summary>Test context whose model can be adjusted per test via a callback.</summary>
public sealed class TestContext : DbContext
{
    private readonly Action<ModelBuilder>? _configureModel;

    public TestContext(DbContextOptions<TestContext> options, Action<ModelBuilder>? configureModel)
        : base(options)
    {
        _configureModel = configureModel;
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => _configureModel?.Invoke(modelBuilder);
}

/// <summary>Factory and helpers for <see cref="TestContext"/> instances without a database.</summary>
internal static class Contexts
{
    /// <summary>Creates a context with the plugin enabled on Npgsql.</summary>
    /// <param name="tenancy">Optional plugin options.</param>
    /// <param name="model">Optional additional model configuration.</param>
    /// <param name="cacheServiceProvider">
    /// false: a separate internal service provider per context, hence a freshly built model
    /// (needed because the tests use different OnModelCreating variants).
    /// </param>
    /// <param name="configure">Optional additional configuration of the options builder.</param>
    public static TestContext Create(
        Action<TenancyOptionsBuilder>? tenancy = null,
        Action<ModelBuilder>? model = null,
        bool cacheServiceProvider = false,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=localhost;Database=unused")   // no connection is ever opened
            .UseAcmeTenancy(tenancy)
            .EnableServiceProviderCaching(cacheServiceProvider);

        configure?.Invoke(builder);
        return new TestContext(builder.Options, model);
    }

    /// <summary>
    /// The design-time model. Migration tests need it: context.Model is the lean runtime model
    /// without the annotations that are only passed on at design time.
    /// </summary>
    public static IModel DesignModel(this DbContext context)
        => context.GetService<IDesignTimeModel>().Model;

    /// <summary>
    /// Migration operations from <paramref name="source"/> (null: empty database) to
    /// <paramref name="target"/>.
    /// </summary>
    public static IReadOnlyList<MigrationOperation> Diff(DbContext? source, DbContext target)
        => target.GetService<IMigrationsModelDiffer>().GetDifferences(
            source?.DesignModel().GetRelationalModel(),
            target.DesignModel().GetRelationalModel());

    /// <summary>SQL commands that the migrations SQL generator produces for the operations.</summary>
    public static IReadOnlyList<string> Sql(DbContext target, IReadOnlyList<MigrationOperation> operations)
        => target.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, target.DesignModel())
            .Select(c => c.CommandText.Trim())
            .ToList();

    /// <summary>Combines <see cref="Diff"/> and <see cref="Sql"/>.</summary>
    public static IReadOnlyList<string> MigrationSql(DbContext? source, DbContext target)
        => Sql(target, Diff(source, target));
}

/// <summary>Builds the design-time service provider used by the tooling tests.</summary>
internal static class DesignServices
{
    /// <summary>
    /// Design-time services as with "dotnet ef" (EF, Npgsql, plugin). NpgsqlDesignTimeServices is an
    /// Npgsql internal API (EF1001); the plugin replaces some of the registrations made before.
    /// </summary>
    public static ServiceProvider Create(DbContext context)
    {
        var services = new ServiceCollection()
            .AddEntityFrameworkDesignTimeServices()
            .AddDbContextDesignTimeServices(context);

        new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
        new TenancyDesignTimeServices().ConfigureDesignTimeServices(services);

        return services.BuildServiceProvider();
    }
}
