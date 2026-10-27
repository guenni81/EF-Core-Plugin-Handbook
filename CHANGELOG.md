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

### Fixed

- The handbook said EF only warns after 20 internal service providers; by default it throws.
- The old query pipeline diagram placed the NULL optimization in postprocessing and the
  query interceptor in preprocessing; the materialization chapter said the value converter
  is inlined into the shaper (the shaper calls its compiled delegate); the performance
  chapter listed the evaluatable filter as running once per query shape only.
- `TenancyDesignTimeServices` registered `IDatabaseModelFactory` as a singleton although EF
  and Npgsql register it as scoped and it depends on the scoped scaffolding logger.

## [10.0.0]

- Initial release: the EF Core Plugin Handbook (English and German) and the reference
  plugin `Acme.EntityFrameworkCore.Tenancy` for EF Core 10 with PostgreSQL and SQL Server.
