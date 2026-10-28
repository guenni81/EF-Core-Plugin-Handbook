-- Tenant policy diagnosis for PostgreSQL (Acme.EntityFrameworkCore.Tenancy).
--
-- READ ONLY: the script only reads catalogs. It changes nothing, repairs nothing and grants
-- nothing. It compares the EXPECTED state, which you name, with the state of the database;
-- a mere list of existing policies could not show a missing one. Chapter "Row Level Security
-- in production", section "Checking the policies of a running database".
--
-- Configuration: session settings in the same session, before the script.
--   SET diag.expected     = 'public.Orders,public.Customers:tenant_id';
--                           -- required: schema.table[:tenant column], default column TenantId;
--                           -- the tenant-owned tables of the design-time model, without
--                           -- deliberate exceptions (HasNoRowLevelSecurity, [NoTenantIsolation])
--   SET diag.app_role     = 'shop_app';          -- required: the role the application uses
--   SET diag.setting_name = 'app.tenant_id';     -- optional (UseAcmeTenancy SettingName)
--   SET diag.policy_name  = 'tenant_isolation';  -- optional (UseAcmeTenancy PolicyName)
--   SET diag.force        = 'on';                -- optional: FORCE expected (plugin default)
--
--   psql "$OWNER_OR_AUDITOR" -c "SET diag.expected = '...'" -c "SET diag.app_role = 'shop_app'" \
--        -f docs/deployment/check-tenant-policies.postgresql.sql
--
-- Result: one row per expected table with status OK, NOT PROTECTED or NOT VERIFIABLE and the
-- reason. Read rights: the catalogs pg_class, pg_namespace, pg_roles and pg_policy, which every
-- role may read unless an administrator revoked it. Without SELECT on pg_policy the result is
-- NOT VERIFIABLE instead of an error.
WITH config AS (
    SELECT nullif(current_setting('diag.app_role', true), '') AS app_role,
           coalesce(nullif(current_setting('diag.setting_name', true), ''), 'app.tenant_id')
               AS setting_name,
           coalesce(nullif(current_setting('diag.policy_name', true), ''), 'tenant_isolation')
               AS policy_name,
           coalesce(nullif(current_setting('diag.force', true), ''), 'on')::boolean AS force,
           has_table_privilege('pg_catalog.pg_policy', 'SELECT') AS can_read_policies
),
expected AS (
    SELECT split_part(split_part(entry, ':', 1), '.', 1) AS schema_name,
           split_part(split_part(entry, ':', 1), '.', 2) AS table_name,
           coalesce(nullif(split_part(entry, ':', 2), ''), 'TenantId') AS column_name
    FROM unnest(string_to_array(
             replace(coalesce(current_setting('diag.expected', true), ''), ' ', ''), ',')) AS entry
    WHERE entry <> ''
),
app AS (
    SELECT r.oid, r.rolsuper, r.rolbypassrls
    FROM pg_catalog.pg_roles AS r
    JOIN config AS c ON r.rolname = c.app_role
),
-- pg_policy is read through query_to_xml: the query runs only with the privilege, so a missing
-- privilege yields NOT VERIFIABLE instead of "permission denied" for the whole diagnosis
policy AS (
    SELECT (xpath('/row/relid/text()', p))[1]::text::oid AS relid,
           (xpath('/row/name/text()', p))[1]::text AS name,
           (xpath('/row/cmd/text()', p))[1]::text AS cmd,
           (xpath('/row/permissive/text()', p))[1]::text::boolean AS permissive,
           (xpath('/row/roles/text()', p))[1]::text::oid[] AS roles,
           coalesce((xpath('/row/qual/text()', p))[1]::text, '') AS qual,
           coalesce((xpath('/row/chk/text()', p))[1]::text, '') AS chk
    FROM config AS c,
         unnest(xpath('/table/row', CASE WHEN c.can_read_policies THEN query_to_xml(
             'SELECT polrelid AS relid, polname AS name, polcmd AS cmd,
                     polpermissive AS permissive, polroles::text AS roles,
                     pg_get_expr(polqual, polrelid) AS qual,
                     pg_get_expr(polwithcheck, polrelid) AS chk
              FROM pg_catalog.pg_policy', true, false, '') ELSE '<table/>'::xml END)) AS p
),
checked AS (
    SELECT e.schema_name, e.table_name, e.column_name, t.oid, t.relrowsecurity,
           t.relforcerowsecurity, t.relowner,
           -- The plugin's policy: FOR ALL, permissive, USING and WITH CHECK compare the tenant
           -- column with the session setting
           EXISTS (SELECT 1 FROM policy AS p, config AS c
                   WHERE p.relid = t.oid AND p.name = c.policy_name AND p.cmd = '*'
                     AND p.permissive
                     AND position(quote_ident(e.column_name) || ' = ' IN p.qual) > 0
                     AND position(quote_ident(e.column_name) || ' = ' IN p.chk) > 0
                     AND position('current_setting(' || quote_literal(c.setting_name) IN p.qual) > 0
                     AND position('current_setting(' || quote_literal(c.setting_name) IN p.chk) > 0)
               AS has_policy,
           -- Permissive policies are combined with OR: any other one that applies to the app
           -- role widens what it may read or write
           (SELECT string_agg(p.name, ', ' ORDER BY p.name)
            FROM policy AS p, config AS c, app AS a
            WHERE p.relid = t.oid AND p.permissive AND p.name <> c.policy_name
              AND (0 = ANY (p.roles)
                   OR EXISTS (SELECT 1 FROM unnest(p.roles) AS r(oid)
                              WHERE pg_has_role(a.oid, r.oid, 'MEMBER')))) AS widening
    FROM expected AS e
    LEFT JOIN pg_catalog.pg_namespace AS n ON n.nspname = e.schema_name
    LEFT JOIN pg_catalog.pg_class AS t
        ON t.relnamespace = n.oid AND t.relname = e.table_name AND t.relkind IN ('r', 'p')
),
verdict AS (
    SELECT k.schema_name, k.table_name,
           CASE
               WHEN c.app_role IS NULL OR a.oid IS NULL
                   THEN 'NOT VERIFIABLE: diag.app_role is not set or the role does not exist'
               WHEN NOT c.can_read_policies
                   THEN 'NOT VERIFIABLE: no SELECT privilege on pg_catalog.pg_policy'
               WHEN k.oid IS NULL THEN 'NOT PROTECTED: table not found'
               WHEN a.rolsuper THEN 'NOT PROTECTED: the app role is a superuser'
               WHEN a.rolbypassrls THEN 'NOT PROTECTED: the app role has BYPASSRLS'
               WHEN pg_has_role(a.oid, k.relowner, 'MEMBER')
                   THEN 'NOT PROTECTED: the app role owns the table and can switch RLS off'
               WHEN NOT k.relrowsecurity THEN 'NOT PROTECTED: row level security is disabled'
               WHEN c.force AND NOT k.relforcerowsecurity
                   THEN 'NOT PROTECTED: FORCE ROW LEVEL SECURITY is missing, the owner is not bound'
               WHEN NOT k.has_policy
                   THEN 'NOT PROTECTED: policy ' || c.policy_name || ' is missing or does not compare '
                        || k.column_name || ' with ' || c.setting_name
               WHEN k.widening IS NOT NULL
                   THEN 'NOT PROTECTED: further permissive policies apply to the app role: '
                        || k.widening
               ELSE 'OK'
           END AS result
    FROM checked AS k
    CROSS JOIN config AS c
    LEFT JOIN app AS a ON true
)
SELECT * FROM (
    SELECT schema_name, table_name,
           split_part(result, ':', 1) AS status,
           nullif(trim(substr(result, length(split_part(result, ':', 1)) + 2)), '') AS reason
    FROM verdict
    UNION ALL
    -- Without expected tables there is nothing to compare: never report that as success
    SELECT NULL, NULL, 'NOT VERIFIABLE', 'diag.expected names no table'
    WHERE NOT EXISTS (SELECT 1 FROM expected)
) AS diagnosis
ORDER BY status = 'OK', schema_name, table_name;
