# Quickstart: Artifact To Engine Deployment

## End-To-End Smoke Scenario

1. Start the Elsa Control API and hosted console with workspace identity enabled.
2. Register or upload a `elsa.workflow-definition` artifact and refresh inspection until it is valid and downloadable.
3. Create a desired-state revision that references the artifact record.
4. Register a workflow engine in the same environment and advertise `artifact.elsa.workflow-definition.apply`.
5. Open the revision detail page and select the engine.
6. Verify deployability returns Deployable and the deploy button is enabled for a user with deployment execution permission.
7. Queue deployment and verify exactly one deployment run and one runtime command are created for the revision, target engine, and mode.
8. Poll runtime commands for the engine.
9. Claim the command with worker ID `runtime-sync-01`.
10. Download the artifact through the runtime command artifact download endpoint using the active lease.
11. Verify the downloaded bytes match the command digest.
12. Complete the command with observed digest, runtime reference, and per-artifact Applied outcome.
13. Reload the revision detail page and verify the deployment run succeeded and run history shows safe command/apply metadata.

## Blocked Capability Scenario

1. Register another engine in the same environment without `artifact.elsa.workflow-definition.apply`.
2. Select that engine on the revision detail page.
3. Verify deployability is Blocked, names the missing canonical capability, and suggests refreshing heartbeat or installing the runtime applier.
4. Verify queue deployment is blocked before a command is created.

## Lease Download Rejection Scenario

1. Queue and claim a deployment command.
2. Attempt runtime artifact download without `X-Elsa-Command-Lease`.
3. Attempt runtime artifact download with a wrong lease token.
4. Attempt runtime artifact download after lease expiry.
5. Verify each attempt is rejected and no artifact bytes are streamed.

## Partial Apply Scenario

1. Create a revision with two artifact records.
2. Configure the runtime worker test double to apply the first artifact and fail the second.
3. Submit a failed final report with per-artifact outcomes.
4. Verify the run is failed or recovery-required, the first artifact remains recorded as Applied, the second as Failed, and no automatic rollback is reported.

## Verification Commands

```sh
dotnet test tests/ElsaControl.Deployment.Core.Tests/ElsaControl.Deployment.Core.Tests.csproj --filter Deployability
dotnet test tests/ElsaControl.Deployment.Core.Tests/ElsaControl.Deployment.Core.Tests.csproj --filter DeploymentCommand
dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentCommand
dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter RuntimeCommand
dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter Deployability
cd src/ElsaControl.Console && npm test -- --run deployments
cd src/ElsaControl.Console && npm run typecheck
git diff --check
```

## Known Scope Boundaries

- Direct push deployment transport remains out of scope.
- Runtime-local workflow apply implementation remains owned by runtime integrations.
- Automatic rollback after partial apply is out of scope; recovery must be explicit.
- Secret store/provider configuration UX is a separate setup specification unless tasks naturally touch the field labels or dropdown source.
