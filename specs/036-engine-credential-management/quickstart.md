# Quickstart: Engine Credential Management UI

## Scenario 1: Manage credentials without setup wizard

1. Sign in to the Console as a workspace user with deployment setup permission.
2. Select a workspace.
3. Open `Deployments` -> `Engine credentials`.
4. Verify the page explains platform-to-engine credentials and excludes runtime secret management.
5. Register an engine credential store.
6. Register a credential reference under that store.
7. Confirm the reference appears in the active references list.

## Scenario 2: Local encrypted credential safety

1. Create a `Local encrypted database` store.
2. Create a credential reference and enter a credential value.
3. Confirm the list shows `Protected local credential` instead of the entered value.
4. Rotate the credential with a new non-empty value.
5. Confirm neither the old nor new raw credential appears in the UI.

## Scenario 3: Usage before lifecycle action

1. Assign a credential reference to one or more engines.
2. Open `Deployments` -> `Engine credentials`.
3. Expand the reference usage count.
4. Confirm the affected application, environment, and engine names are listed.
5. Start archive or rotation and confirm the usage disclosure is shown before submit.

## Scenario 4: Engine setup integration

1. Open engine registration when no references exist.
2. Follow the route to manage engine credentials.
3. Create a store and reference.
4. Return to engine registration or edit flow.
5. Confirm the new active reference is selectable for the same workspace.

## Validation commands

```bash
npm run test -- src/features/deployments/DeploymentsPage.test.tsx
npm run typecheck
git diff --check
```

If backend behavior changes:

```bash
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --no-restore --filter WorkspaceDeploymentApiTests
dotnet build Elsa.Platform.sln --no-restore
```
