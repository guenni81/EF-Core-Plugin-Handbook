-- Tenant policy diagnosis for SQL Server (Acme.EntityFrameworkCore.Tenancy.SqlServer).
--
-- READ ONLY: the script only reads catalog views. It changes nothing, repairs nothing and
-- grants nothing. It compares the EXPECTED state, which you name, with the state of the
-- database; a mere list of existing policies could not show a missing one. Chapter "Row Level
-- Security in production", section "Checking the policies of a running database".
--
-- Configuration: session context in the same session, before the script.
--   EXEC sp_set_session_context N'diag.expected', N'dbo.Invoice,sales.Bills:Mandant';
--        -- required: schema.table[:tenant column], default column TenantId; the tenant-owned
--        -- tables of the design-time model, without deliberate exceptions ([NoTenantIsolation])
--   EXEC sp_set_session_context N'diag.app_user', N'shop_app';     -- required: database user
--   EXEC sp_set_session_context N'diag.session_key', N'TenantId';  -- optional (sessionKey)
--   EXEC sp_set_session_context N'diag.policy_schema', N'acme';    -- optional (policySchema)
--
-- Result: one row per expected table with status OK, NOT PROTECTED or NOT VERIFIABLE and the
-- reason. Read rights: VIEW DEFINITION on the database (or db_owner). Catalog views show only
-- objects the caller has rights on; without VIEW DEFINITION a missing policy cannot be told
-- from an invisible one, so the result is NOT VERIFIABLE.
DECLARE @expected nvarchar(max) = CAST(SESSION_CONTEXT(N'diag.expected') AS nvarchar(max));
DECLARE @app sysname = CAST(SESSION_CONTEXT(N'diag.app_user') AS sysname);
DECLARE @key sysname = COALESCE(CAST(SESSION_CONTEXT(N'diag.session_key') AS sysname), N'TenantId');
DECLARE @policySchema sysname =
    COALESCE(CAST(SESSION_CONTEXT(N'diag.policy_schema') AS sysname), N'acme');
DECLARE @canSee bit = CASE WHEN IS_MEMBER(N'db_owner') = 1
    OR HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION') = 1 THEN 1 ELSE 0 END;
DECLARE @function nvarchar(300) = QUOTENAME(@policySchema) + N'.[fn_tenant_predicate]';
-- The predicate function must read the key the application sets (LIKE pattern escaped)
DECLARE @functionReadsKey bit = CASE WHEN OBJECT_DEFINITION(OBJECT_ID(@function, N'IF'))
    LIKE N'%SESSION[_]CONTEXT(N''' + REPLACE(REPLACE(@key, N'[', N'[[]'), N'_', N'[_]') + N''')%'
    THEN 1 ELSE 0 END;
DECLARE @appId int = CASE WHEN @app IS NOT NULL THEN DATABASE_PRINCIPAL_ID(@app) END;
-- The app user can switch a policy off: db_owner, or ALTER ANY SECURITY POLICY / CONTROL
DECLARE @appMaySwitchOff bit = CASE WHEN EXISTS (
        SELECT 1 FROM sys.database_role_members AS m
        WHERE m.member_principal_id = @appId AND m.role_principal_id = DATABASE_PRINCIPAL_ID(N'db_owner'))
    OR EXISTS (
        SELECT 1 FROM sys.database_permissions AS p
        WHERE p.grantee_principal_id = @appId AND p.class = 0 AND p.state IN ('G', 'W')
          AND p.permission_name IN (N'ALTER ANY SECURITY POLICY', N'CONTROL'))
    THEN 1 ELSE 0 END;

WITH expected AS (
    SELECT PARSENAME(name, 2) AS schema_name, PARSENAME(name, 1) AS table_name,
           COALESCE(NULLIF(column_name, N''), N'TenantId') AS column_name
    FROM (SELECT LEFT(value, CHARINDEX(N':', value + N':') - 1) AS name,
                 SUBSTRING(value, CHARINDEX(N':', value + N':') + 1, 128) AS column_name
          FROM STRING_SPLIT(REPLACE(COALESCE(@expected, N''), N' ', N''), N',')
          WHERE value <> N'') AS entries
),
checked AS (
    SELECT e.schema_name, e.table_name, e.column_name, t.object_id, t.temporal_type,
           -- The plugin's predicate on the tenant column, in an enabled policy
           (SELECT STRING_AGG(CONCAT(s.predicate_type_desc, N' ', s.operation_desc), N',')
            FROM sys.security_predicates AS s
            JOIN sys.security_policies AS p ON p.object_id = s.object_id
            WHERE s.target_object_id = t.object_id AND p.is_enabled = 1
              AND s.predicate_definition LIKE N'%' + REPLACE(@function, N'[', N'[[]') + N'([[]'
                  + REPLACE(e.column_name, N'[', N'[[]') + N'])%') AS predicates,
           (SELECT COUNT(*) FROM sys.security_predicates AS s
            JOIN sys.security_policies AS p ON p.object_id = s.object_id
            WHERE s.target_object_id = t.object_id AND p.is_enabled = 0) AS disabled
    FROM expected AS e
    LEFT JOIN sys.tables AS t ON t.object_id = OBJECT_ID(QUOTENAME(e.schema_name) + N'.'
                                                         + QUOTENAME(e.table_name))
),
verdict AS (
    SELECT schema_name, table_name,
           CASE
               WHEN @canSee = 0
                   THEN N'NOT VERIFIABLE: no VIEW DEFINITION on the database'
               WHEN @appId IS NULL
                   THEN N'NOT VERIFIABLE: diag.app_user is not set or the user does not exist'
               WHEN object_id IS NULL THEN N'NOT PROTECTED: table not found'
               WHEN @appMaySwitchOff = 1
                   THEN N'NOT PROTECTED: the app user can switch security policies off'
               WHEN temporal_type = 2
                   THEN N'NOT PROTECTED: temporal table, the policy does not cover its history table'
               WHEN disabled > 0 AND predicates IS NULL
                   THEN N'NOT PROTECTED: the security policy is disabled (STATE = OFF)'
               WHEN predicates IS NULL OR predicates NOT LIKE N'%FILTER %'
                   THEN N'NOT PROTECTED: no FILTER predicate of ' + @function + N' on '
                        + column_name
               WHEN predicates NOT LIKE N'%BLOCK AFTER INSERT%'
                    OR predicates NOT LIKE N'%BLOCK AFTER UPDATE%'
                   THEN N'NOT PROTECTED: BLOCK predicates AFTER INSERT and AFTER UPDATE are missing'
               WHEN @functionReadsKey = 0
                   THEN N'NOT PROTECTED: ' + @function + N' does not read the session key ' + @key
               ELSE N'OK'
           END AS result
    FROM checked
)
SELECT schema_name, table_name, status, reason
FROM (
    SELECT schema_name, table_name,
           LEFT(result, CHARINDEX(N':', result + N':') - 1) AS status,
           NULLIF(LTRIM(SUBSTRING(result, CHARINDEX(N':', result + N':') + 1, 4000)), N'') AS reason
    FROM verdict
    UNION ALL
    -- Without expected tables there is nothing to compare: never report that as success
    SELECT NULL, NULL, N'NOT VERIFIABLE', N'diag.expected names no table'
    WHERE NOT EXISTS (SELECT 1 FROM expected)
) AS diagnosis
ORDER BY CASE WHEN status = N'OK' THEN 1 ELSE 0 END, schema_name, table_name;
