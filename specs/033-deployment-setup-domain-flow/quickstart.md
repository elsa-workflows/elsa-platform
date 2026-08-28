# Quickstart: Deployment Setup Domain Flow

## Scenario 1: Environment Creation Is Separate

1. Open Deployments > Applications > an application > Add environment.
2. Verify the form asks for Application, Environment, and Tier only.
3. Submit a valid environment.
4. Verify the environment page opens and shows no engines until one is registered.

Expected result: An environment exists without any workflow engine registration.

## Scenario 2: Register Engine From Environment

1. Open an environment with no engines.
2. Choose Register engine.
3. Enter an engine name and engine base URL.
4. Choose a registered credential reference.
5. Submit.

Expected result: The engine appears in the environment engine registrations list and health verification runs.

## Scenario 3: Secret Store And Credential Reference Pickers

1. Register a secret store for the workspace.
2. Register a credential reference inside that store.
3. Open Register engine.
4. Select the secret store.
5. Verify the credential reference picker shows only references from that store.

Expected result: The user can complete engine registration without guessing provider strings or reference formats.

## Validation Commands

```bash
dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --no-restore
dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --no-restore --filter DeploymentWorkspace
cd src/ElsaControl.Console && npm run typecheck
cd src/ElsaControl.Console && npm test -- DeploymentsPage
git diff --check
```
