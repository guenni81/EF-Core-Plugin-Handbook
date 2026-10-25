# The EF Core Plugin Handbook

[![CI](https://github.com/guenni81/EF-Core-Plugin-Handbook/actions/workflows/ci.yml/badge.svg)](https://github.com/guenni81/EF-Core-Plugin-Handbook/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/guenni81/EF-Core-Plugin-Handbook/blob/main/LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![EF Core 10](https://img.shields.io/badge/EF%20Core-10-512BD4)

A practical guide to writing **plugins for Entity Framework Core 10**, plus a complete,
tested reference plugin: **`Acme.EntityFrameworkCore.Tenancy`** adds multi-tenancy with
database-enforced **Row Level Security** to EF Core, for **PostgreSQL** (full
implementation) and **SQL Server** (second provider).

The handbook explains EF Core's extension points chapter by chapter: options extensions,
internal services, conventions, type mappings, query translation, interceptors,
diagnostics, migrations, design-time services, compiled models and custom SQL expression
nodes. Every concept is shown in the plugin in this repository, and the behavior the book
describes is backed by more than 100 tests.

> **Status:** `Acme.EntityFrameworkCore.Tenancy` is the reference implementation for the
> handbook ("Acme" is a placeholder name). It is complete and tested, but deliberately not
> published as a NuGet package: use it as a template for your own plugin or reference the
> projects directly.

## 📖 The handbook

| Edition | PDF | Sources |
|---|---|---|
| English | [EF-Core-Plugin-Handbook.pdf](https://github.com/guenni81/EF-Core-Plugin-Handbook/blob/main/docs/handbook/out/EF-Core-Plugin-Handbook.pdf) | [`docs/handbook/en`](https://github.com/guenni81/EF-Core-Plugin-Handbook/tree/main/docs/handbook/en) |
| Deutsch | [EF-Core-Plugin-Handbuch.pdf](https://github.com/guenni81/EF-Core-Plugin-Handbook/blob/main/docs/handbook/out/EF-Core-Plugin-Handbuch.pdf) | [`docs/handbook/de`](https://github.com/guenni81/EF-Core-Plugin-Handbook/tree/main/docs/handbook/de) |

42 chapters in 9 parts plus 4 appendices, about 120 pages, for developers who already use
EF Core and want to extend it. The PDFs are typeset with LaTeX (LuaLaTeX) so that text and
code excerpts are versioned together with the code. See [`docs/handbook/README.md`](https://github.com/guenni81/EF-Core-Plugin-Handbook/blob/main/docs/handbook/README.md)
for how to build them.

## Quick start

Reference `Acme.EntityFrameworkCore.Tenancy.PostgreSQL` (it brings the core package along)
and provide an `ITenantProvider` that tells the plugin the current tenant; the sample
[`HttpTenantProvider`](https://github.com/guenni81/EF-Core-Plugin-Handbook/blob/main/samples/ShopApi/HttpTenantProvider.cs) reads it from a claim.

```csharp
// Program.cs: register the tenant provider and enable the plugin
services.AddAcmeTenancy<HttpTenantProvider>();
services.AddDbContext<ShopContext>((sp, options) => options
    .UseNpgsql(connectionString)
    .UseAcmeTenancy(sp, tenancy => tenancy.ApplyQueryFilter()));
```

```csharp
// Entities opt in by implementing ITenantOwned
public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }   // mapped to uuid without any configuration
    public decimal Total { get; set; }
}
```

```csharp
// The tenant is set on SaveChanges and enforced by the database
db.Orders.Add(new Order { Total = 42 });       // TenantId is filled in automatically
await db.SaveChangesAsync();

db.Orders.Where(o => o.TenantId == EF.Functions.CurrentTenant());   // translated to SQL
```

From the model, the plugin generates the migration that enables Row Level Security and
creates the policy:

```sql
ALTER TABLE "Orders" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Orders" FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON "Orders" AS PERMISSIVE FOR ALL
    USING ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK ("TenantId" = nullif(current_setting('app.tenant_id', true), '')::uuid);
CREATE INDEX "IX_Orders_TenantId" ON "Orders" ("TenantId");
```

A connection interceptor writes the current tenant into the session (`set_config` on
PostgreSQL, `sp_set_session_context` on SQL Server) every time a connection is opened.
Because the database enforces the policies, even raw SQL and `ExecuteUpdate`/`ExecuteDelete`
cannot cross tenant boundaries, provided the application uses a restricted database role
(see [Security notes](#security-notes)).

### SQL Server

```csharp
services.AddDbContext<ShopContext>((sp, options) => options
    .UseSqlServer(connectionString, sql => sql.UseAcmeTenancy(sp)));
```

SQL Server implements isolation with a schema-bound predicate function, a security policy
per table (FILTER and BLOCK predicates) and `SESSION_CONTEXT`.

## What the plugin demonstrates

| Area | Implementation | Chapter |
|---|---|---|
| Minimal plugin | `EF.Functions.Reverse()` in five pieces as a warm-up | 2 |
| Options extension | `TenancyOptionsExtension`: service provider hash, validation, debug info | 4 |
| Internal services | Plugin interfaces, replacing and decorating provider services, `ISingletonOptions` | 5 |
| Application DI | `AddAcmeTenancy<T>()`, interceptors that use application services | 6 |
| Model building | Fluent API, data annotation, conventions, named query filter, index, validation | 7–9 |
| Composability | Tested together with EFCore.NamingConventions | 8 |
| Type mapping | `TenantId` → `uuid` via `IRelationalTypeMappingSourcePlugin`, JSON, compiled models | 10 |
| Queries | `EF.Functions.CurrentTenant()`, `TenantId.IsEmpty`, `IEvaluatableExpressionFilterPlugin` | 11–14 |
| Run time | SaveChanges and connection interceptors, `Database.ApplyTenantSession()` | 15 |
| Diagnostics | Custom event IDs, `EventDefinition`, warning-as-error, `DiagnosticSource` | 16 |
| Migrations | Annotation-driven RLS, custom operations (roles, privileges) | 17–19, 32 |
| Design time | Annotation code generator, `[DesignTimeServicesReference]` | 20–22 |
| Testing | Model, SQL, snapshot round-trip, compiled model and Testcontainers tests | 23 |
| Shipping | Versioning, stable annotation formats, packaging, CI, Public API analyzer | 24–25, 36 |
| Value generation | `TenantId` keys via convention and `HasValueGeneratorFactory(Type)` | 28 |
| Operations | RLS pitfalls, PgBouncer, legitimate cross-tenant access | 33 |
| Performance | BenchmarkDotNet project with measured overhead of the SaveChanges logic | 34 |
| ASP.NET Core | `samples/ShopApi`, DbContext factories, background jobs | 35 |
| Custom SQL nodes | `EF.Functions.IsNotDistinctFrom` with SQL generator and nullability processor | 37 |
| Aggregates | `EF.Functions.Every` (`bool_and`) with FILTER/DISTINCT support | 38 |
| Runtime annotations | Tenant property in the compiled model, design-time annotations filtered | 39 |
| Scaffolding | RLS and policies are read back from the database | 40 |
| Second provider | SQL Server: security policies and `SESSION_CONTEXT` on the same core | 41–42 |
| Debugging | Model debug view, resolved services, convention set, query trees, logging | App. D |

## Repository layout

```
src/
  Acme.EntityFrameworkCore.Tenancy              Core package (provider-independent)
  Acme.EntityFrameworkCore.Tenancy.PostgreSQL   PostgreSQL implementation (complete)
  Acme.EntityFrameworkCore.Tenancy.SqlServer    SQL Server implementation (second provider)
samples/
  ShopSample                                    Console sample incl. migration workflow
  ShopApi                                       ASP.NET Core minimal API, tenant from a claim
tests/
  ...Tenancy.Tests                              Unit tests, no database required
  ...Tenancy.IntegrationTests                   Tests against PostgreSQL (Docker)
  ...Tenancy.SqlServer.Tests                    Unit and integration tests for SQL Server (Docker)
benchmarks/
  ...Tenancy.Benchmarks                         BenchmarkDotNet micro benchmarks
docs/handbook/                                  Handbook sources (Python) and PDFs
docker-compose.yml                              PostgreSQL for the samples
```

## Building and testing

Requirements: [.NET SDK 10](https://dotnet.microsoft.com/download) (see `global.json`) and,
for the integration tests, Docker (Testcontainers starts PostgreSQL and SQL Server).

```bash
dotnet build
dotnet test tests/Acme.EntityFrameworkCore.Tenancy.Tests                # no database
dotnet test tests/Acme.EntityFrameworkCore.Tenancy.IntegrationTests     # requires Docker
dotnet test tests/Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests      # requires Docker
```

Micro benchmarks for the SaveChanges logic (BenchmarkDotNet, no database required):

```bash
dotnet run -c Release --project benchmarks/Acme.EntityFrameworkCore.Tenancy.Benchmarks
```

Building against the next EF Core preview (early warning for breaking changes in internal
APIs):

```bash
dotnet build -p:EFCorePreview=true
```

## The samples

The samples show how an application uses the plugin. They need a PostgreSQL server; the
included `docker-compose.yml` starts one with the superuser `shop_owner`, who owns the
tables. At run time the samples connect with the restricted role `shop_app` (no owner, no
superuser, no `BYPASSRLS`), so that row level security applies.

```bash
docker compose up -d          # PostgreSQL on localhost:5432
docker compose down -v        # stop and delete the data
```

**ShopApi** (ASP.NET Core minimal API) reads the tenant from the `tenant_id` claim. In the
Development environment it creates its database `shop_api`, the tables with RLS and the
role `shop_app` on startup, and the header `X-Demo-Tenant` simulates the claim:

```bash
docker compose up -d
dotnet run --project samples/ShopApi

curl -X POST -H "X-Demo-Tenant: 0199f0d2-0000-7000-8000-00000000000a" "http://localhost:5000/orders?total=10"
curl -X POST -H "X-Demo-Tenant: 0199f0d2-0000-7000-8000-00000000000b" "http://localhost:5000/orders?total=99"
curl -H "X-Demo-Tenant: 0199f0d2-0000-7000-8000-00000000000a" http://localhost:5000/orders
# -> only the order of tenant ...0a; without the header the list is empty
```

The API code contains no tenant filter at all: the separation comes from the database.

**ShopSample** (console) shows the production route with real migrations: the owner role
runs them, the application connects as `shop_app`. The steps (adding the migration, where
to put the role operations, applying it) are described at the top of
[`samples/ShopSample/Program.cs`](https://github.com/guenni81/EF-Core-Plugin-Handbook/blob/main/samples/ShopSample/Program.cs); the
default connection strings match `docker-compose.yml`. Note that roles referenced by
policies (`shop_support`) must be created **before** the generated operations, i.e. at
the very top of `Up()`.

## Security notes

* Row Level Security only protects data if the application connects with a role that is
  **not** the table owner, **not** a superuser and does **not** have `BYPASSRLS`. Run
  migrations with a separate owner role.
* `FORCE ROW LEVEL SECURITY` is enabled by default so that the policy applies to the table
  owner as well.
* The SaveChanges interceptor complements the database (defense in depth) and produces
  clear error messages. Bulk operations and raw SQL bypass it but are still covered by RLS.
* Isolation follows the connection: a connection that is already open when handed to EF,
  or that stays open while the tenant changes, keeps its previous session value. Let EF
  open connections and use one context per tenant; where that is not possible, call
  `db.Database.ApplyTenantSession()` (chapter "Interceptors").
* `AddAcmeTenancy` registers a scoped tenant provider by default. With `AddDbContextPool`,
  `AddPooledDbContextFactory` or a singleton `AddDbContextFactory`, use a provider that
  determines the tenant on every access, or register the factory as scoped (chapters
  "Public API and integration into the application" and "Using the plugin in ASP.NET Core").
* Access across tenants (reports, maintenance) belongs in a separate database role, never
  in a switch the application role could flip (chapter "Row Level Security in production").

## Compatibility

Package versions follow EF Core's major version (10.x = EF Core 10). The plugin uses some
internal EF Core and Npgsql APIs (namespaces `*.Internal`, warning EF1001); these places
are commented in the code. The code is built and tested against EF Core 10.0 and
Npgsql 10.0.

## Contributing

Issues and pull requests are welcome, for the code as well as for the handbook (in English
or German). See [CONTRIBUTING.md](https://github.com/guenni81/EF-Core-Plugin-Handbook/blob/main/CONTRIBUTING.md) and the
[CHANGELOG](https://github.com/guenni81/EF-Core-Plugin-Handbook/blob/main/CHANGELOG.md).

## License

Both the code and the handbook (all sources and both PDF editions) are licensed under the
[MIT License](https://github.com/guenni81/EF-Core-Plugin-Handbook/blob/main/LICENSE): you may use, copy, modify and share them,
also commercially, as long as the copyright and license notice is kept. The PDFs contain
the full license text on their last page. The DejaVu fonts in `docs/handbook/fonts` are
under their own free license (`LICENSE-DejaVu.txt`).
