# Changelog

All notable changes to this project are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Package versions equal the handbook edition (1.2.0 = edition 1.2); the supported EF Core
version is given by the dependency range and the compatibility table in the README
(1.x = EF Core 10).

## [Unreleased]

### Added

- Handbook (both editions): the provider comparison in chapter "Without SQL: what carries over to
  Cosmos DB" is now the usage contract, with the states "supported and tested", "rejected while
  the model is built", "only under conditions" and "not verified" and the tests as evidence:
  protected and rejected mappings, temporal tables, `ITenantOwned` without a mapped tenant
  property (not validated by the Cosmos package), opt-outs and transactions. Chapter "Row Level
  Security in production" gets a table of the transaction paths (`BeginTransaction`,
  `SaveChanges`, `UseTransaction`, `TransactionScope`) in session mode and with
  `UseTransactionPooling()`; chapter "Conventions" names the evidence of the mapping matrix. The
  README names the Cosmos gap and the transaction paths with transaction pooling.
- Tests: `UseTransaction` and an ambient `TransactionScope` with `UseTransactionPooling()` fail
  closed; the PostgreSQL package rejects an ignored `TenantId` and an explicit interface
  implementation without `IsTenantProperty()`.
- Tests: combined SQL Server migrations through EF's differ, Up and Down, with reading and
  writing for two tenants afterwards: session key change with a table rename, policy schema
  change with a column rename and a schema move, and a failing migration whose rollback keeps
  table, policy and predicate function. Handbook (both editions), chapter "Test strategy": new
  section "Combinations of migrations and isolation" with the matrix and the combinations that
  remain untested.
- Handbook: `docs/handbook/check_examples.py` runs the listings marked with `% run:` and the new
  CI job `handbook-examples` calls it: the exercise's test command (it reads the TRX counters and
  requires at least one executed and no failed test, so a filter that matches nothing fails) and
  the ShopApi session, now shown in chapter "Using the plugin in ASP.NET Core", against
  PostgreSQL in a temporary container. Both editions and `docs/handbook/README.md` describe
  which listings are executed and which are shortened or simplified.
- Handbook (both editions), chapter "The update pipeline": a complete, tested teaching example
  of a provider-specific write extension. `DatabaseClockUpdateSqlGenerator` (in the integration
  test project, not part of the packages) derives from Npgsql's update SQL generator and sets
  columns marked with `HasDatabaseClockOnUpdate()` to `now()` in every `UPDATE`; EF reads the
  value back with `RETURNING`. The chapter explains why a SaveChanges or command interceptor
  cannot do this, registration and lifetime of the replaced singleton, and the limits (one
  generator per context, not for `ExecuteUpdate`, provider-specific). `UpdatePipelineTests`
  checks the SQL, the value read back and optimistic concurrency against PostgreSQL. The chapter
  is now rated volatile (internal Npgsql base class).
- Read-only diagnosis scripts `docs/deployment/check-tenant-policies.postgresql.sql` and
  `check-tenant-policies.sqlserver.sql`: they compare the expected tenant-owned tables (from the
  design-time model, without deliberate exceptions) with the running database and report per
  table OK, NOT PROTECTED (RLS or FORCE off, policy missing or not comparing the tenant column,
  further permissive policies, app role as superuser, with BYPASSRLS or as owner; on SQL Server
  disabled policy, missing FILTER or BLOCK predicates, wrong session key, temporal table, app
  user able to switch policies off) or NOT VERIFIABLE (missing read rights, unknown app role,
  no expected table). `PolicyDiagnosisTests` and `SqlServerPolicyDiagnosisTests` run them
  against correct and deliberately broken databases. Handbook (both editions), chapter "Row
  Level Security in production": new section on running and interpreting them, read rights and
  limits.

### Changed

- Handbook (both editions): consistency review of the book as one work after the additions.
  Statements brought in line with the code (interceptor registration in the internal provider,
  the core's and the sample plugin's evaluatable-expression filters, opt-outs and the table
  validator, `UseTransactionPooling()` in the simplified `UseAcmeTenancy(sp)` excerpt, the
  predicate-function check in the SQL Server excerpt, sequences in `GrantTablePrivileges`, the
  fourth pinned advisory, packages and NativeAOT coverage); the new contract, diagnosis and
  update-pipeline content linked into the decision aid, the checklists, the file appendix and
  the risk table; overclaims about test coverage corrected; the short form "internal provider"
  introduced once; terminology unified (Translators, Tenant-Provider, App-Container, Session,
  Modellbau, Nullability, Design-Time-Services, cacht); straight quotes inside `\code` in the
  German edition, which babel swallowed, replaced.
- Code style: the PostgreSQL options extension's copy constructor checks its argument, the
  update pipeline test uses no primary constructor, and two code comments about `Forced` no
  longer claim that `false` is never stored.

- CI: the Cosmos DB model tests are required. They run in the build job (and in the
  compatibility workflow) with the filter `Category!=Emulator` and need no Docker; only the
  tests with the trait `Category=Emulator` run against the preview emulator, still
  non-blocking, now with a step time limit. Before, the whole Cosmos test project ran with
  `continue-on-error`, so failing model tests did not stop the build either. Handbook (both
  editions) and README describe the split.

### Fixed

- PostgreSQL: a migration that adds a tenant column to an existing table together with the
  isolation failed with `column "TenantId" does not exist`, its Down with `cannot drop column
  ... because other objects depend on it`: EF's differ orders the `AlterTableOperation` before
  new and after dropped columns, and the generator emitted the policies right there. The
  generator now overrides `Generate` for the whole operation list; `MovePolicyChanges` drops
  changed policies before the table's first column change and creates new ones after its last,
  using the table name valid at each position. New tests `MigrationOrderTests` (Up, Down,
  failing migrations with rollback, renamed tenant column with another setting name, isolation
  as the app role) and SQL order tests. Handbook (both editions): chapter "Declarative SQL from
  annotations" explains the order and its limits; chapter "Test strategy" no longer claims that
  the problem class does not arise on PostgreSQL.

- Cosmos DB: an `ITenantOwned` entity without a mapped tenant property (`Ignore(e => e.TenantId)`
  or an explicit interface implementation without `IsTenantProperty()`) is now rejected while
  the model is built, as in the relational packages. Before, the convention skipped it silently:
  no partition key, no query filter, no tenant check in `SaveChanges`. Handbook (both editions)
  and README updated.
- SQL Server: a tenant-owned entity mapped to a temporal table (`IsTemporal()`) is now rejected
  while the model is built. The security policy protected only the current table, so
  `TemporalAll()` and the other `FOR SYSTEM_TIME` queries returned other tenants' rows from the
  history table. Temporal tables without a tenant and entities excluded with
  `[NoTenantIsolation]` remain allowed. Handbook (both editions) and README document the limit.
- SQL Server: an `ITenantOwned` entity without a mapped tenant property (`Ignore(e => e.TenantId)`
  or an explicit interface implementation without `IsTenantProperty()`) is now rejected while
  the model is built, as with PostgreSQL. Before, the convention skipped it silently, so the
  table got no security policy and `SaveChanges` no tenant check. The handbook (both editions,
  chapter "Conventions") describes the validation contract.
- SQL Server: a migration that changes the session key or the policy schema and drops a protected
  table at the same time failed with error 3729 ("Cannot DROP FUNCTION ... because it is being
  referenced"): the dropped table's policy was only removed with `DROP TABLE`, after the shared
  predicate function. The generator now removes the policies of dropped tables before the
  function as well and restores only those of the target model; the Down path is covered by
  tests too.
- PostgreSQL: `HasRowLevelSecurity(force: false)` lost against the convention default
  `ForceRowLevelSecurity = true`: the method removed the annotation, and the convention set FORCE
  again. It now stores `false` explicitly; the annotation provider normalizes `false` away for the
  relational model, so unchanged models still produce no migration and snapshots stay as before.
  The handbook (both editions) replaces the rule "do not store default values" with the
  distinction between explicit model configuration and its normalization for migrations.
- PostgreSQL: `GrantTablePrivileges` now also sets default privileges for sequences
  (`ALTER DEFAULT PRIVILEGES ... GRANT USAGE, SELECT ON SEQUENCES`). A serial column or HiLo
  sequence created by a later migration failed for the app role with `42501: permission denied
  for sequence`. As for tables, the default privileges apply to objects the migrating role
  creates later. API documentation and the handbook (both editions, chapter "Retrofitting and
  operating multi-tenancy") describe it.
- Handbook (both editions), exercises appendix: the test command used `--filter
  "Name~Inequality_with"`, which the xUnit adapter does not support; it ran no test and still
  exited with 0. It now uses `FullyQualifiedName~Inequality_with`, which runs the intended test.

## [1.2.0] - 2026-10-28

Packages 1.2.0, handbook edition 1.2.

**Breaking change** (binary; the packages are not published on NuGet, so the minor version
suffices): the core package no longer depends on `Microsoft.EntityFrameworkCore.Relational`. The
relational building blocks moved into the new package
`Acme.EntityFrameworkCore.Tenancy.Relational`, with unchanged namespaces:
`TenantSessionInterceptor`, `TenantTableMappingValidator` and `Database.ApplyTenantSession()`.
Users of the PostgreSQL or SQL Server package need no change, both reference the new package.
Assemblies that reference only the core package and use relational APIs (`ToTable`,
`HasColumnType`, ...) must now reference `Microsoft.EntityFrameworkCore.Relational` themselves.

**Breaking change**: `EF.Functions.IsNotDistinctFrom`, `IsDistinctFrom` and `Every` moved from
the PostgreSQL tenancy package into the new sample package
`Acme.EntityFrameworkCore.Functions.PostgreSQL` (class `AcmePgDbFunctionsExtensions`, still in
the namespace `Microsoft.EntityFrameworkCore`); activate it with
`UseNpgsql(cs, o => o.UseAcmeFunctions())`. Calls to the functions compile unchanged. The tenancy
plugin no longer replaces `IQuerySqlGeneratorFactory` and
`IRelationalParameterBasedSqlProcessorFactory` and therefore combines with plugins that do;
its internal API surface shrinks from 13 to 10 Npgsql types.

### Added

- Handbook (both editions), chapter 8: a diagram of the application container, the context
  instance and EF's internal provider, showing how the scoped tenant provider reaches the
  singleton interceptors without a leak and without a value in the hash.
- Handbook (both editions), chapter 40: messages from a broker. The sender writes the tenant
  into a header, a consume filter sets it before the scope and the context exist, and messages
  without a valid tenant are rejected instead of being processed for all tenants.
- Handbook (both editions), chapter 39: second-level caches and multi-tenancy. With row level
  security the tenant is in neither the SQL text nor the cache key, so a cache hands one
  tenant's rows to another without asking the database; the tenant belongs in every key.
- Handbook (both editions), chapter 18: Dapper and raw ADO.NET on the context's connection,
  depending on who opens it (EF, the driver itself, transaction pooling); `RawAdoNetTests`
  checks all three cases against PostgreSQL.
- `EnrichOpenTelemetry()` (PostgreSQL options builder, SQL Server provider builder, default:
  off): a command interceptor from the relational package tags `Activity.Current` with
  `tenant.id` before every command, only when an activity is recorded and a tenant is set.
  Integration tests with an `ActivityListener`; handbook (both editions) chapter 19 explains
  which span receives the tag with the EF Core instrumentation and with the driver's tracing.
- Test mode for applications in the core package: `UseAcmeTenancyForTesting(sp)` lets business
  logic tests run on SQLite or InMemory with the application-side behavior of the plugin
  (TenantId mapped as a Guid, SaveChanges interceptor, `modelBuilder.HasTenantQueryFilters(this)`)
  and refuses PostgreSQL, SQL Server and Cosmos DB. The PostgreSQL and SQL Server packages keep
  refusing other providers. Handbook (both editions): new section in chapter 26.
- `HasTenantQueryFilters` moved from the Cosmos package into the core
  (`TenancyModelBuilderExtensions`), since it needs neither SQL nor Cosmos.
- Handbook (both editions), chapter 10: the tenant filter combined with an application's soft
  delete filter through EF Core 10 named query filters, with an important box on switching off
  filters by name only; a unit test checks all three combinations.
- Handbook (both editions): new chapter 48 "Without SQL: what carries over to Cosmos DB" in part
  XI, with an important box on the missing row level security (raw access through the SDK and
  FromSql, architectural alternatives: container or account per tenant), a comparison of the
  three providers and the list of what was deliberately not ported. Chapter 1 (levels),
  chapter 3 (packages), chapter 27 (version dependency), chapter 29 (checklist), chapter 46 and
  appendix B mention the Cosmos package.
- Package `Acme.EntityFrameworkCore.Tenancy.Cosmos`, a deliberately lean contrast example on the
  core package alone: tenant as partition key by convention (stored as a string, as Cosmos
  requires), a named query filter on the current tenant via
  `modelBuilder.HasTenantQueryFilters(this)` that turns queries into single-partition queries,
  and the core's SaveChanges interceptor, materialization check and key value generation.
  Deliberately not ported: migrations, scaffolding, SQL nodes, aggregates, session interceptor,
  bulk update protection. Tests run against the Linux Cosmos DB emulator (pinned vNext tag) in
  a CI job of their own that does not block the build yet.
- `TenantSaveChangesInterceptor.Tenants` is public, so that packages without a session (Cosmos)
  can read the tenant provider of a context.
- Package `Acme.EntityFrameworkCore.Functions.PostgreSQL` (see above); the handbook (both
  editions) introduces it in chapters 42 and 43 with a note that the examples are deliberately
  unrelated to tenancy, and lists it in chapter 3, appendix B and the README.
- Package `Acme.EntityFrameworkCore.Tenancy.Relational` (see above); `PackageLayerTests` make
  sure the core stays free of relational types; the consumer test packs four packages.
- Handbook (both editions): chapter 46 "What belongs in the core" distinguishes three layers
  (core, relational, provider) with a three-stage rule of thumb and a note to separate the
  relational layer from the start; the package architecture in chapter 3, appendix B and the
  chapters on the session interceptor and table mappings follow the new package.
- Handbook (both editions): every chapter shows its version dependency (stable, medium,
  volatile) at its start; the upgrade playbook in chapter 27 lists the chapters per rating and
  says to check the volatile ones (internal API, EF1001) first in a major upgrade.

### Changed

- SQL Server and Cosmos packages: `PopulateDebugInfo` writes one entry per setting, like the
  PostgreSQL package, so that EF can name the option that differs between two contexts.
- The materialization check's error message no longer assumes row level security; it names the
  checks for relational providers and for Cosmos DB.
- Handbook (both editions): new outline of the back parts without renumbering any chapter.
  Part VIII "Advanced extension points" (chapters 30–35), new part IX "Operation and
  integration" (36–41), part X "Deep extensions" (42–45), new part XI "Multiple providers"
  (46–47, the place for further providers), appendices as part XII. Part labels are named
  (`part:providers` instead of `part:IX`); the checklist in chapter 29 follows the new parts.
- README: states up front that the repository is a handbook with a reference implementation,
  not a library; the quick start no longer reads as if there were a NuGet package. The
  repository layout describes `docs/handbook` correctly and lists `docs/deployment` and
  `eng/consumer-test`; the table "What the plugin demonstrates" lists chapter 30 (bulk
  operations).

### Fixed

- `Database.ApplyTenantSession()` with `UseTransactionPooling()` failed with "activate the
  plugin with UseAcmeTenancy", although the plugin was active; the message now explains that
  the tenant is set per transaction and the commands belong in one (test in
  `TransactionPoolingTests`, chapter 36).
- Handbook (both editions): the title page gave 45 chapters and 4 appendices; the structure
  counts are now taken from the document itself (47 chapters, appendices A–F), and the
  versions name the SQL Server provider as well.
- Handbook (both editions): page 2 held only the box "Public and internal APIs"; the table of
  contents now follows on the same page instead of starting a new one.
- Design-time services were not loaded in applications: EF Core reads
  `[DesignTimeServicesReference]` only from the startup and the migrations assembly, so the
  attribute in the PostgreSQL package had no effect and snapshots kept raw annotations. The
  attribute moved from the package into the application (`eng/consumer-test/template/
  DesignTimeServices.cs`); the consumer test now checks the fluent API in the snapshot.
  Chapter 23 and the checklist say where the line belongs.
- Code comments: `TenantActivityEnrichmentInterceptor` is an `IInterceptor` and therefore
  scoped (one instance per context), not one instance for all contexts; the query
  compilation benchmark no longer claims a replaced SQL generator and nullability processor.
- Handbook (both editions), integration and consistency review:
  - New section "The path through the book" in chapter 2: design-time and runtime model,
    and how the parts follow the pipeline (including why the update pipeline sits in
    part VIII).
  - Lifetimes: the OpenTelemetry interceptor is scoped, `IRelationalAnnotationProvider` a
    singleton per internal service provider; the model is cached per internal service provider
    and context type; the query expression interceptor runs at compilation after a cache miss;
    model validation runs before the finalized conventions, so plugin validation sees the
    runtime model.
  - Counts and facts: internal types per package (PostgreSQL 10, SQL Server 2, Cosmos 1;
    `NpgsqlMigrationsSqlGenerator` is public), four packed libraries in the consumer test,
    package overview including Cosmos and the Functions sample, SQL Server resets the session
    context on reuse of a pooled connection, JSON values with a value converter
    (`JsonConvertedValueReaderWriter`), appendix A cardinality of `ICommandBatchPreparer`.
  - Code excerpts: excerpts that left out registrations or settings are marked as excerpts or
    simplified, unbound listings that do not match their file literally are marked
    simplified, appendix B lists the missing source files and no longer names files that do
    not exist.
  - Cross-references: the decision guide routes to complex types and JSON, the query pipeline
    and diagnostics; references that pointed to chapters not covering the topic are fixed.
  - Repetitions replaced by references: the 21-provider limit, the opening of "Scaffolding in
    practice", the CI patch/preview paragraph, a duplicate checklist item.
  - Terminology aligned in the German edition (Interceptors, Extension-Method, threadsicher,
    Entity-Typ, Eigentümer, Context, Annotations, Massenoperationen, Transaction-Pooling).

## [1.1.0] - 2026-10-28

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
- Benchmarks `MaterializationInterceptorBenchmark` (PostgreSQL via Testcontainers; 10, 100
  and 1000 rows without, with an empty and with the tenant-verifying materialization
  interceptor) and `QueryCompilationBenchmark` (first compilation and cache hit for EF Core,
  the plugin and the plugin with query filter). The benchmark project now selects classes with
  `--filter`. Handbook: new section "Reading and query compilation" with the results and a
  recommendation for `VerifyMaterializedTenants()`: most of its cost comes from EF leaving the
  compiled materialization path as soon as any materialization interceptor is registered.
- `CustomOperatorTests`: the pgvector cosine distance `<=>` as a custom SQL expression node
  (test code only: translator, SQL generator derived from the plugin's, nullability processor),
  with checks for precedence, propagated nullability and the failure without the processor.
  Handbook: new section "Practical example: operators for vector search and pgvector".
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
- `TenantMaterializationInterceptor` looks up the tenant provider once per context instance
  (`ConditionalWeakTable`, keys held weakly) instead of on every materialized row; the tenant
  itself is still read on every row, also across leases of a pooled context.

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
- `IS [NOT] DISTINCT FROM` as an operand of another operator is now parenthesized
  (`TenancyNpgsqlQuerySqlGenerator.RequiresParentheses`). Before, comparing two such
  expressions produced SQL that PostgreSQL rejected with a syntax error, because it binds `<>`
  tighter than `IS`.

## [1.0.0]

Published as packages 10.0.0, when package versions still followed EF Core.

- Initial release: the EF Core Plugin Handbook (English and German) and the reference
  plugin `Acme.EntityFrameworkCore.Tenancy` for EF Core 10 with PostgreSQL and SQL Server.
