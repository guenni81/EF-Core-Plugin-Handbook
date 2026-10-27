using Acme.EntityFrameworkCore.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Consumer;

public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public decimal Total { get; set; }
}

public class PgContext(DbContextOptions<PgContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

/// <summary>Same model without the query filter: EF Core 10 compiles no model with query filters.</summary>
public class CompiledPgContext(DbContextOptions<CompiledPgContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public class SqlContext(DbContextOptions<SqlContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

/// <summary>Options as the application configures them; dotnet-ef uses the factories below.</summary>
public static class Options
{
    public static DbContextOptionsBuilder<PgContext> Pg()
        => new DbContextOptionsBuilder<PgContext>()
            .UseNpgsql("Host=localhost;Database=consumer")
            .UseAcmeTenancy(t => t.ApplyQueryFilter());

    public static DbContextOptionsBuilder<CompiledPgContext> CompiledPg()
        => new DbContextOptionsBuilder<CompiledPgContext>()
            .UseNpgsql("Host=localhost;Database=consumer")
            .UseAcmeTenancy();

    public static DbContextOptionsBuilder<SqlContext> Sql()
        => new DbContextOptionsBuilder<SqlContext>()
            .UseSqlServer("Server=localhost;Database=consumer", s => s.UseAcmeTenancy());
}

public class PgFactory : IDesignTimeDbContextFactory<PgContext>
{
    public PgContext CreateDbContext(string[] args) => new(Options.Pg().Options);
}

public class CompiledPgFactory : IDesignTimeDbContextFactory<CompiledPgContext>
{
    public CompiledPgContext CreateDbContext(string[] args) => new(Options.CompiledPg().Options);
}

public class SqlFactory : IDesignTimeDbContextFactory<SqlContext>
{
    public SqlContext CreateDbContext(string[] args) => new(Options.Sql().Options);
}
