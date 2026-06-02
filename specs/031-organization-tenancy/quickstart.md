# Quickstart: Organization Tenancy

## Scope

Verify that Organization is the customer tenant boundary, Workspace remains the operational isolation boundary, and existing workspace-owned records survive migration.

## Backend Verification

1. Run focused account/organization core tests:

   ```bash
   dotnet test tests/Elsa.Platform.PackageCatalog.Core.Tests/Elsa.Platform.PackageCatalog.Core.Tests.csproj --filter Organization
   ```

2. Run focused EF persistence tests:

   ```bash
   dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter Organization
   ```

3. Run focused API tests:

   ```bash
   dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter Organization
   ```

4. Verify the migration preserves workspace IDs:

   - Seed at least two existing workspaces with package/deployment records.
   - Apply migrations.
   - Confirm every workspace has `OrganizationId`.
   - Confirm existing workspace-scoped resources still reference the same workspace IDs.

5. Verify authorization isolation:

   - Account A belongs to Organization 1 and Workspace 1.
   - Account A does not belong to Workspace 2 in the same organization.
   - Account A cannot read Workspace 2 records.
   - Account A cannot read any workspace in Organization 2.

## Console Verification

1. Run organization/workspace context tests:

   ```bash
   cd src/Elsa.Platform.Console
   npm test -- AppShell
   ```

2. Verify the signed-in console shows organization and workspace selection separately.

3. Verify existing deployment, catalog, and runtime-builder routes continue loading after a workspace is selected.

## Documentation Verification

1. Confirm high-impact older specs include a forward note to `031-organization-tenancy`.
2. Confirm active context in `AGENTS.md` points to `specs/031-organization-tenancy/plan.md`.
3. Confirm no current feature plan says Workspace is the customer tenant boundary without qualifying Organization as the customer boundary.
