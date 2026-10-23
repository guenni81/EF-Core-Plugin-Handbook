using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShopApi;

// Minimal API sample: the tenant comes from the signed-in user's claim, and PostgreSQL row
// level security does the isolation. See chapter 35 of the handbook.

var builder = WebApplication.CreateBuilder(args);

// 1. Register the tenant provider. HttpTenantProvider reads the claim from the current
//    request, so it needs the IHttpContextAccessor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddAcmeTenancy<HttpTenantProvider>();
// 2. Register the context. UseAcmeTenancy(sp) enables the plugin, resolves the tenant provider
//    from sp and adds two runtime interceptors: one sets the tenant on every opened connection,
//    the other stamps it on new entities in SaveChanges.
builder.Services.AddDbContext<ShopApiContext>((sp, options) => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Shop"))
    .UseAcmeTenancy(sp));

// TenantId as a plain GUID in JSON responses
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new TenantIdJsonConverter()));

// 3. In a real application: builder.Services.AddAuthentication().AddJwtBearer(...);
//    The identity provider issues the "tenant_id" claim.

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // DEVELOPMENT ONLY: create database, schema and the restricted app role on startup, so
    // that "docker compose up -d" followed by "dotnet run" is all it takes (see README).
    await DevelopmentDatabase.EnsureCreatedAsync(
        builder.Configuration.GetConnectionString("ShopOwner")!);

    // 4. DEVELOPMENT ONLY: simulate the tenant via a header. In production this is a
    //    security hole, because any client could pick any tenant.
    app.Use(async (context, next) =>
    {
        if (context.Request.Headers.TryGetValue("X-Demo-Tenant", out var tenant))
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(HttpTenantProvider.ClaimType, tenant.ToString())], "Demo"));
        }

        await next();
    });
}

// 5. No tenant filter in code: RLS in the database provides the separation
app.MapGet("/orders", async (ShopApiContext db) =>
    await db.Orders.OrderBy(o => o.Id).ToListAsync());

// 6. The SaveChanges interceptor sets TenantId; the policy's WITH CHECK verifies it again
app.MapPost("/orders", async (ShopApiContext db, decimal total) =>
{
    var order = new Order { Total = total };
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return Results.Created($"/orders/{order.Id}", order);
});

app.Run();
