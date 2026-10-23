using Acme.EntityFrameworkCore.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using ShopSample;

// Steps (see README and chapter 32 of the handbook):
//   1. dotnet ef migrations add Initial        (as the owner role, see DesignTimeFactory)
//   2. Add at the VERY TOP of Up(), before the generated operations:
//          migrationBuilder.CreateApplicationRole("shop_support");
//      Reason: the policy "support_read" (TO shop_support) is created right after
//      CREATE TABLE. If the role does not exist yet at that point, the migration fails.
//   3. Add at the VERY BOTTOM of Up(), after the tables:
//          migrationBuilder.CreateApplicationRole("shop_app", canLogin: true);
//          migrationBuilder.GrantTablePrivileges("shop_app");
//      (GRANT ... ON ALL TABLES only affects tables that already exist.)
//   4. dotnet ef database update
//   5. ALTER ROLE shop_app PASSWORD '...';     (passwords do not belong in migrations)
//   6. dotnet run                              (connects as shop_app: not owner, no BYPASSRLS)

// Register the tenant provider and the context. UseAcmeTenancy(sp, ...) enables the plugin,
// resolves the tenant provider from sp and adds the runtime interceptors. ApplyQueryFilter()
// additionally adds a named EF query filter, as defense in depth on top of RLS.

var services = new ServiceCollection();
services.AddAcmeTenancy<AmbientTenantProvider>();
services.AddDbContext<ShopContext>((sp, options) => options
    .UseNpgsql(Environment.GetEnvironmentVariable("SHOP_DB")
               ?? "Host=localhost;Database=shop;Username=shop_app;Password=shop_app")
    .UseAcmeTenancy(sp, tenancy => tenancy.ApplyQueryFilter()));

await using var provider = services.BuildServiceProvider();

// Activate a tenant for the following block. The connection interceptor passes it to
// PostgreSQL as a session setting each time EF opens the connection.
var tenant = TenantId.New();
using (AmbientTenantProvider.Use(tenant))
await using (var scope = provider.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopContext>();

    // TenantId is set by the SaveChanges interceptor
    db.Customers.Add(new Customer { Name = "Contoso Ltd" });
    await db.SaveChangesAsync();

    // EF.Functions.CurrentTenant() is translated to SQL (current_setting), not run in .NET
    var query = db.Customers.Where(c => c.TenantId == EF.Functions.CurrentTenant());
    Console.WriteLine(query.ToQueryString());
    Console.WriteLine($"Customers of the tenant: {await query.CountAsync()}");
}

/// <summary>
/// For "dotnet ef": migrations run with the owner role and without interceptors. The tenancy
/// options must match the application's, otherwise migrations are generated for another model.
/// </summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ShopContext>
{
    /// <inheritdoc />
    public ShopContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ShopContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("SHOP_DB_OWNER")
                       ?? "Host=localhost;Database=shop;Username=shop_owner;Password=shop_owner")
            .UseAcmeTenancy(tenancy => tenancy.ApplyQueryFilter())
            .Options);
}
