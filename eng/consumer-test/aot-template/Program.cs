using Acme.EntityFrameworkCore.Tenancy;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aot;

public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public decimal Total { get; set; }
}

public sealed class Tenants : ITenantProvider
{
    public TenantId? CurrentTenantId { get; set; }
}

public class AotContext : DbContext
{
    public AotContext(DbContextOptions<AotContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    public static DbContextOptions<AotContext> Options(string cs, Tenants? tenants)
    {
        var b = new DbContextOptionsBuilder<AotContext>().UseNpgsql(cs).UseAcmeTenancy();
        if (tenants is not null)
            b.AddInterceptors(new TenantConnectionInterceptor(tenants), new TenantSaveChangesInterceptor(tenants));
        return b.Options;
    }
}

public class Factory : IDesignTimeDbContextFactory<AotContext>
{
    public AotContext CreateDbContext(string[] args) => new(AotContext.Options("Host=localhost", null));
}

/// <summary>
/// Writes rows of two tenants through the plugin's interceptors and reads as one of them; the
/// database's RLS has to hide the other tenant's rows. Exit code 0 on success.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var cs = args[0];
        var tenants = new Tenants();
        TenantId a = TenantId.New(), b = TenantId.New();
        foreach (var t in new[] { a, b })
        {
            tenants.CurrentTenantId = t;
            await using var db = new AotContext(AotContext.Options(cs, tenants));
            db.Orders.Add(new Order { Total = 1 });
            await db.SaveChangesAsync();
        }

        tenants.CurrentTenantId = a;
        await using var reader = new AotContext(AotContext.Options(cs, tenants));
        // 0m, not 0: EF's query precompilation in 10.0 rejects the implicit int-to-decimal conversion
        var rows = await reader.Orders.Where(o => o.Total > 0m).ToListAsync();
        var mine = await reader.Orders.Where(o => o.TenantId == EF.Functions.CurrentTenant()).CountAsync();
        Console.WriteLine($"rows={rows.Count} allA={rows.All(o => o.TenantId == a)} mine={mine}");
        return rows.Count > 0 && rows.All(o => o.TenantId == a) && mine == rows.Count ? 0 : 1;
    }
}
