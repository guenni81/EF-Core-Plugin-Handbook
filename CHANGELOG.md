# Changelog

All notable changes to this project are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Package versions equal the handbook edition (1.1.0 = edition 1.1); the supported EF Core
version is given by the dependency range and the compatibility table in the README
(1.x = EF Core 10).

## [Unreleased]

### Added

- Deployment template in `docs/deployment`: `setup-roles.sql` (one-time roles `shop_owner` and
  `shop_app`, default privileges for tables of later migrations) and `migrations-deploy.yml`
  (GitHub Actions, not active here: build, migration bundle, bundle as the owner, then the
  rolling deployment). The NativeAOT consumer test now runs this sequence against PostgreSQL:
  the script unchanged, the bundle twice as the owner, DDL refused for the application role,
  the application as `shop_app`. Handbook: new section "Deployment: pipeline, roles and zero
  downtime" (no `Migrate()` on startup, expand and contract under RLS, flow diagram).
- `UseTransactionPooling()` (PostgreSQL options builder) with the new
  `PostgreSqlTransactionSessionInterceptor` for PgBouncer in transaction mode: together with
  `UseAcmeTenancy(applicationServices)` the tenant is set per transaction with
  `set_config(..., true)` in `TransactionStarted` instead of per connection, and every
  `SaveChanges` runs in a transaction (`AutoTransactionBehavior.Always`). Commands outside a
  transaction have no tenant (fail closed); a `TenantConnectionInterceptor` refuses the session
  value with a `TenancyException` when the option is set. Tested in `TransactionPoolingTests`;
  handbook section "Transaction pooling" rewritten.
- `ProtectBulkOperations()` (PostgreSQL options builder, SQL Server provider builder) with the
  new `TenantQueryExpressionInterceptor` from the core package: `ExecuteUpdate` calls that set
  the tenant property fail with a `TenancyException` when the query is compiled. It is
  registered as `IInterceptor`, because EF never calls query expression interceptors that are
  registered as `ISingletonInterceptor` (recorded in `BulkOperationTests`; appendix A corrected).
- Tests for the session interceptor and transactions against PostgreSQL and SQL Server:
  connection opened by `BeginTransactionAsync`, `ApplyTenantSession` inside an explicit
  transaction, reading and writing under an ambient `TransactionScope`. No code change was
  needed: EF opens the connection before a local transaction exists, and both providers enlist
  in an ambient transaction when the connection opens.
- `TenantIdJsonConverter` (core package): `TenantId` serializes with System.Text.Json as a plain
  GUID string, also as a dictionary key, selected by an attribute on the type; invalid values
  throw `JsonException`. The ShopApi sample no longer needs its own converter.

### Changed

- `TenantMaterializationInterceptor` looks up the tenant provider once per context instance
  (`ConditionalWeakTable`, keys held weakly) instead of on every materialized row; the tenant
  itself is still read on every row, also across leases of a pooled context.

## [1.1.0] - 2026-10-27

Packages 1.1.0, handbook edition 1.1. Package versions now equal the handbook edition; the
first release, published as packages 10.0.0, corresponds to edition 1.0 (tag `v1.0`).

### Added

- Handbook (both editions): new chapter 4 "Choosing the right extension point", a decision
  guide from the problem to the mechanism, with decision trees, simpler alternatives, public
  or internal API, cardinality, lifetime and typical wrong choices for every extension point.
- Appendix A lists `IValueGeneratorSelector`, `IQueryTranslationPostprocessorFactory` and the
  lifetimes of the design-time services; the glossary distinguishes the book's runtime
  annotations from EF's runtime annotation API.
- `DesignTimeServicesTests`: the plugin's design-time services keep EF's and Npgsql's
  lifetimes.
- Handbook (both editions): new chapter 16 "Materialization: from rows to objects": shaping,
  constructor binding, tracking modes, identity resolution and fixup, the four
  `IMaterializationInterceptor` hooks, lifetime and thread safety, limits and alternatives.
- `VerifyMaterializedTenants()` (PostgreSQL options builder, SQL Server provider builder):
  an optional singleton materialization interceptor from the core package that throws when a
  query materializes an entity of another tenant, for example over a connection opened
  outside of EF. Unit and integration tests, including the failure cases of a singleton
  interceptor created per context or remembering the tenant.

- Handbook (both editions): chapter 12 "The query pipeline" rewritten as the map for all
  query extensions: one sample query followed through every stage with EF's real logs and
  SQL, the three points in time (per query shape, per parameter constellation, per
  execution), responsible components, extension points, single services, where
  provider-internal API begins, lifetimes, and how to locate a change before choosing a
  mechanism. Appendix D shows the query compilation logs; `DebuggingTests` checks them.

- Handbook (both editions): new chapter 8 "State, lifetimes and thread safety", the central
  rule which state belongs where (application container vs. internal provider, singleton
  per internal provider, scoped per context instance, pooling, factories, background jobs,
  open connections), with five failure cases. `StateAndLifetimeTests` reproduces cases 1-4,
  each wrong variant next to the correct one; appendix A lists `IResettableService`.

- Handbook (both editions): chapter 6 "Registering services" explains plugin composition
  systematically: multiple registration vs. single services, the rules of `TryAdd`,
  `TryAddEnumerable`, `Replace`, decorators, `ReplaceService`, options extensions and
  evaluation order in EF Core 10, a compatibility matrix for typical combinations of two
  plugins, integration through public logic, and a coexistence checklist before
  publishing. `CompositionMatrixTests` reproduces every rule and conflict.

- Handbook (both editions): "What happens if ...?" failure cases at the mechanisms they
  belong to (incomplete hash, wrong lifetime of a plugin interface, convention bypassing the
  configuration source, convention running too early, wrong nullability, missing
  evaluatable filter, async-only interceptor, runtime instead of design-time model, scoped
  state with pooling), in a new `failure` box; existing warnings on the same topics were
  turned into failure cases instead of being repeated. `FailureCaseTests` verifies every
  case against EF Core 10.

- Handbook (both editions): chapter "Versioning and compatibility" gets an upgrade
  playbook for the next EF Core major version: failure classes and the tests of the sample
  that detect them, a 16-step workflow with commands (compiler error classification, EF1001
  inventory), CI channels for patches and previews, and an upgrade checklist.
- `compatibility.yml`: weekly workflow that tests against the newest stable EF Core and
  provider 10.0.x patches (`-p:EFCoreVersion=10.0.*`); stable packages only.
- `ServiceRegistrationTests` (both provider packages): every registered EF service uses the
  lifetime and cardinality of EF's service table.
- `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` contain the public API (10.0.0 shipped,
  `VerifyMaterializedTenants` unshipped); RS0016 and RS0017 are errors.

- Handbook (both editions): appendix A rewritten as a quick reference from the mechanism to
  its properties, the counterpart of the decision guide: task, lifetime, cardinality, public
  / provider base / pubternal, typical registration, composability (exclusive replacements
  marked), preferred mechanism or last resort, elevated upgrade risk and chapter, grouped by
  area; a second table lists the extension points without a service of their own. Every
  lifetime and cardinality was checked again against EF Core 10 and Npgsql 10. New
  `\tablegroup` macro for group headings in tables.

- Handbook (both editions): new chapter "Tenant-safe relationships" (visibility versus
  referential consistency, composite foreign keys with the tenant, uniqueness per tenant, shared
  master data, join tables, delete rules), a threat model in "Row Level Security in production"
  (protected data, trusted components, assumptions, the app role setting the session itself,
  SQL injection, hardening options not implemented) and the new chapter "Retrofitting and
  operating multi-tenancy" (expand, fill, guard, protect; app versions, Down and rollback;
  retries, explicit transactions, lost connections, session reinitialization). Sources: the
  PostgreSQL, SQL Server and Npgsql documentation.
- Tests for these statements: `RelationshipTests` (cross-tenant reference, existence leak,
  vanishing `Include`, cascade into another tenant, composite keys, unique per tenant),
  `ThreatModelTests` and `SqlServerThreatModelTests` (the app role switching tenants, FK check
  on SQL Server, `@read_only`), `OperationsTests` (retrofit with guard, Down, retry after a
  terminated session, transaction inside the execution strategy); `TestDatabase` helper.

- `eng/consumer-test/run.sh`: packs the three libraries and checks them as a customer gets
  them, in a temporary directory with its own NuGet cache: package source mapping and SHA-512
  prove the packages just built are resolved; `dotnet-ef` as a local tool creates migrations and
  scripts for PostgreSQL and SQL Server (RLS objects, no pending model changes), a compiled model
  is generated, compiled and loaded with `UseModel`; `--nativeaot` publishes a NativeAOT
  application with precompiled queries and runs it against PostgreSQL in Docker. New CI job
  `consumer`. Handbook (both editions): section "What is actually checked as a consumer".

- Handbook (both editions): two reading paths ("My first plugin", "Reference for advanced
  readers") with prerequisites, chapters and a checkable result; appendix E with five exercises
  and solutions, each tied to a test of the sample; appendix F with curated, verified primary
  sources (documentation and source code at v10.0.0 tags). The checklist names the chapter
  with the reasoning for every section and covers the new topics; two duplicated passages in
  chapter "Registering services" now refer to the full explanation.
- `check_code.py` verifies 59 excerpts bound with `% source:` against their source files
  (comments and elisions ignored, optional `(strings)`); unbound excerpts of repository files
  must be marked as simplified. New `check_structure.py`: both editions in sync per chapter,
  reference targets, datatable widths. Both run in CI.

### Changed

- SQL Server upgrade note: the first migration after upgrading from 1.0 contains an
  `AlterDatabaseOperation` that records session key and policy schema and only verifies the
  existing predicate function. Change the session key in a later migration of its own;
  changing it in the same migration is rejected because the old value is not recorded.
  Apply migrations before deploying an app version with a new session key (until then the
  database fails closed).

- SQL Server: security policies are dropped by the table they bind and the plugin's predicate
  function instead of by name, so policies created by 1.0 (`tenant_<table>`) are found and
  replaced with the new name the next time their table changes; policies of others are never
  dropped. `PolicyName(string)` and `DropPolicy(string, builder)` of the migrations SQL
  generator are obsolete.

- `Directory.Packages.props`: the EF Core range lives in the `EFCoreVersion` property;
  `Microsoft.Extensions.DependencyInjection` follows it, and the unit tests take Roslyn from
  `Microsoft.EntityFrameworkCore.Design` instead of pinning their own version. Pinned
  companion packages made the test and sample projects fail to restore with newer EF
  patches.

### Fixed

- NuGet advisories (NU1902/NU1903): the SQL Server package depends on
  `System.Security.Cryptography.Pkcs` 9.0.22, because EF Core SqlServer 10.0.x brings
  Microsoft.Data.SqlClient 6.1 with Pkcs 9.0.4 to 9.0.11 (GHSA-846f-f764-76mx,
  GHSA-2fmv-rv2c-59x7) and no SqlClient version carries the fix yet. Test and sample projects
  reference `System.Security.Cryptography.Xml` 9.0.22 against the MSBuild packages of EF Core
  Design 10.0.0 (tooling only). Testcontainers 4.16.0 brings SSH.NET 2026.0.0 (tests only); the
  container images are now named explicitly. The build has no warnings left, the audit stays on,
  and both CI channels fail on any vulnerable package.

- NativeAOT: the `TenantId` type mappings had no value comparers of their own, so EF created
  them through `MakeGenericMethod` on first tracking and a NativeAOT application failed. Both
  mappings now use the shared instances of the new `TenantIdValueComparer`.
- Documented limit: EF Core 10 generates no compiled model for entities with query filters, so
  the plugin's optional query filter excludes compiled models and NativeAOT; the consumer test
  expects this error explicitly.

- Handbook (both editions), factual corrections with sources: EF Core *n* does not generally
  ship only for .NET *n* (EF Core 9 targets .NET 8; checked against the NuGet packages and
  "EF Core releases and planning"). Constructor binding does not prefer a parameterless
  constructor; EF prefers more service parameters, then fewer property parameters, linked to
  `ConstructorBindingFactory` at v10.0.0 and recorded in `ConstructorBindingTests`.
  `current_setting` being `STABLE` allows, but does not guarantee, a single evaluation per
  statement; the policy performance section now explains the plan and how to measure it.
  Blanket statements about protected tables and the SQL Server package follow the support
  matrix.

- `TenantSaveChangesInterceptor` rejected `DbContext.Update` of an untracked entity of the own
  tenant, because EF marks every property as modified, the tenant property included. Only an
  actual change of the value is rejected now; for an unchanged value the flag is cleared, so
  the `UPDATE` does not write the tenant column. An empty original value (shadow tenant
  property) is unknown and left to the database policy, which also stops forged entities with
  the ID of a foreign row (0 rows, `DbUpdateConcurrencyException`). Unit tests and PostgreSQL
  integration tests (sync and async).

- SQL Server: changing `sessionKey` or `policySchema` produced no migration; the interceptor
  then set the new key while the predicate function kept reading the old one, and historical
  migrations generated objects from the current options. Both values are now recorded as
  model annotations (`Acme:SqlServerRls:SessionKey`, `Acme:SqlServerRls:PolicySchema`) and so
  in the snapshot; a change becomes an `AlterDatabaseOperation` that rebuilds the function and
  all policies in one transaction (Up and Down), and the generator reads the values from the
  migration's own target model. An existing function whose session key does not match fails
  the migration (`THROW 50000`) instead of being shared silently, also between contexts in the
  same database.

- SQL Server: renaming a tenant column or a protected table, moving it to another schema, and
  altering or dropping its columns failed with "object participates in enforced
  dependencies", because the schema-bound security policy was only handled for
  `AlterTableOperation`, after EF's `RenameColumnOperation`, and not at all for
  `RenameTableOperation`. The generator now drops the policies of all affected tables before
  the migration's operations and recreates them afterwards with the final table, schema and
  column names from the target model, inside the migration's transaction (Up and Down). It
  stops instead of dropping a policy when there is no target model. Integration tests against
  SQL Server with EF-generated diffs: create, rename column and table, move schema, migrate
  back, drop, isolation checked after every step.
- SQL Server: tables with the same name in different schemas got the same policy name. New
  policies are named `tenant_<schema>_<table>`; ambiguous or overlong names are rejected while
  the model is built.

- Security: tenant isolation skipped derived and owned entity types without checking their
  tables. A TPC hierarchy with an abstract `ITenantOwned` base created concrete tables without
  RLS, and the restricted app role could read foreign rows without a tenant; TPT tables, owned
  collections in their own table, entity splitting and implicit many-to-many join tables were
  unprotected as well. The new `TenantTableMappingValidator` (core package, used by both
  provider conventions) rejects these mappings while the model is built; TPH, owned types in
  the owner's table or as JSON, table splitting and join entities with a tenant property stay
  supported, deliberately excluded types are not checked. Both annotation providers now find
  the annotated type of a shared table even if it is not the first mapping. Unit tests for
  both providers and PostgreSQL integration tests with the restricted role; support matrix in
  chapter "Conventions" and the README.

- Trimming/AOT warnings that only showed in full builds: `AddAcmeTenancy<T>` now annotates
  its type parameter (IL2087); the query filter of the isolation convention suppresses IL3050
  with a justification (conventions do not run with the compiled model NativeAOT requires).
- The handbook's rule for `argumentsPropagateNullability` was wrong: a function with
  propagating arguments gets its nullability from those arguments alone, so functions that
  produce NULL themselves must not mark any argument. `CurrentTenantTranslator` now states
  its nullability explicitly (never NULL for EF, matching the CLR type `TenantId`); the
  generated SQL is unchanged.
- The handbook said EF only warns after 20 internal service providers; by default it throws.
- The old query pipeline diagram placed the NULL optimization in postprocessing and the
  query interceptor in preprocessing; the materialization chapter said the value converter
  is inlined into the shaper (the shaper calls its compiled delegate); the performance
  chapter listed the evaluatable filter as running once per query shape only.
- The handbook and the XML documentation of the singleton options said EF validates
  `ISingletonOptions` for every context; EF calls `Validate` when a context uses a provider
  not built for exactly its options, above all with `UseInternalServiceProvider`. The
  exception message now names that cause. The pooling note described the interceptor instead
  of the tenant provider as the part that must read the tenant on every access.
- `TenancyDesignTimeServices` registered `IDatabaseModelFactory` as a singleton although EF
  and Npgsql register it as scoped and it depends on the scoped scaffolding logger.

## [1.0.0]

Published as packages 10.0.0, when package versions still followed EF Core.

- Initial release: the EF Core Plugin Handbook (English and German) and the reference
  plugin `Acme.EntityFrameworkCore.Tenancy` for EF Core 10 with PostgreSQL and SQL Server.
