-- One-time setup by the database administrator, before the first deployment. Not a migration:
-- it creates the roles the migrations and the application run as.
--
--   psql -v owner_password=... -v app_password=... -f docs/deployment/setup-roles.sql
--
-- shop_owner owns the schema and runs the migration bundle (DDL, RLS, policies).
-- shop_app is what the application connects as: DML only, subject to the policies.
-- The consumer test (eng/consumer-test/run.sh --nativeaot) runs this script unchanged.

CREATE ROLE shop_owner LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD :'owner_password';
CREATE ROLE shop_app LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD :'app_password';
CREATE DATABASE shop OWNER shop_owner;

\connect shop
-- Since PostgreSQL 15 the schema public belongs to the database owner; nobody else may
-- create objects in it, shop_app included.
GRANT USAGE ON SCHEMA public TO shop_app;

-- Tables and sequences that shop_owner creates later, i.e. every migration, are usable by
-- shop_app without a GRANT in each migration.
ALTER DEFAULT PRIVILEGES FOR ROLE shop_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO shop_app;
ALTER DEFAULT PRIVILEGES FOR ROLE shop_owner IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO shop_app;
