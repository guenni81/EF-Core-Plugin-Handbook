# Changelog

All notable changes to this project are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow the major
version of EF Core (10.x = EF Core 10).

## [Unreleased]

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

### Changed

- SQL Server upgrade note: the first migration after upgrading from 10.0 contains an
  `AlterDatabaseOperation` that records session key and policy schema and only verifies the
  existing predicate function. Change the session key in a later migration of its own;
  changing it in the same migration is rejected because the old value is not recorded.
  Apply migrations before deploying an app version with a new session key (until then the
  database fails closed).

- SQL Server: security policies are dropped by the table they bind and the plugin's predicate
  function instead of by name, so policies created by 10.0 (`tenant_<table>`) are found and
  replaced with the new name the next time their table changes; policies of others are never
  dropped. `PolicyName(string)` and `DropPolicy(string, builder)` of the migrations SQL
  generator are obsolete.

- `Directory.Packages.props`: the EF Core range lives in the `EFCoreVersion` property;
  `Microsoft.Extensions.DependencyInjection` follows it, and the unit tests take Roslyn from
  `Microsoft.EntityFrameworkCore.Design` instead of pinning their own version. Pinned
  companion packages made the test and sample projects fail to restore with newer EF
  patches.

### Fixed

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

## [10.0.0]

- Initial release: the EF Core Plugin Handbook (English and German) and the reference
  plugin `Acme.EntityFrameworkCore.Tenancy` for EF Core 10 with PostgreSQL and SQL Server.
