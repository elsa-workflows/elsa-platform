-- Controlled boundary: execute this script once, as the Microsoft Entra SQL
-- administrator, after the proof database exists. The runbook substitutes the
-- three __...__ tokens from validated arguments; this file contains no secret.
--
-- The workload identity is granted DDL for first-start/upgrade migrations.
-- Revoke db_ddladmin after the migration evidence is captured if the proof
-- policy requires a reduced runtime principal.

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'__WORKLOAD_IDENTITY_NAME__')
BEGIN
    CREATE USER [__WORKLOAD_IDENTITY_NAME__] FROM EXTERNAL PROVIDER
        WITH OBJECT_ID = '__WORKLOAD_IDENTITY_OBJECT_ID__';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals role_principal ON role_principal.principal_id = drm.role_principal_id
    JOIN sys.database_principals member_principal ON member_principal.principal_id = drm.member_principal_id
    WHERE role_principal.name = N'db_datareader' AND member_principal.name = N'__WORKLOAD_IDENTITY_NAME__'
)
    ALTER ROLE db_datareader ADD MEMBER [__WORKLOAD_IDENTITY_NAME__];

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals role_principal ON role_principal.principal_id = drm.role_principal_id
    JOIN sys.database_principals member_principal ON member_principal.principal_id = drm.member_principal_id
    WHERE role_principal.name = N'db_datawriter' AND member_principal.name = N'__WORKLOAD_IDENTITY_NAME__'
)
    ALTER ROLE db_datawriter ADD MEMBER [__WORKLOAD_IDENTITY_NAME__];

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals role_principal ON role_principal.principal_id = drm.role_principal_id
    JOIN sys.database_principals member_principal ON member_principal.principal_id = drm.member_principal_id
    WHERE role_principal.name = N'db_ddladmin' AND member_principal.name = N'__WORKLOAD_IDENTITY_NAME__'
)
    ALTER ROLE db_ddladmin ADD MEMBER [__WORKLOAD_IDENTITY_NAME__];
