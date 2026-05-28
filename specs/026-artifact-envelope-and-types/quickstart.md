# Quickstart: Artifact Envelope And Types

## API Smoke Scenario

1. Start the Platform API with workspace identity enabled.
2. Sign in as a workspace member with deployment setup permission.
3. Submit a valid `elsa.workflow-definition` envelope through the workspace artifact registration API.
4. Confirm the response includes artifact type, schema version, producer metadata, safe display metadata, compatibility hints, digests, payload reference summary, and submission metadata.
5. Submit the same envelope again.
6. Confirm the response is idempotent and does not create a duplicate record.
7. Submit the same artifact identity with a different digest.
8. Confirm the request fails with a conflict.
9. Submit an envelope with an unknown artifact type.
10. Confirm the request fails before persistence.
11. Submit metadata with secret-like keys or values.
12. Confirm unsafe metadata is rejected or redacted and not persisted.

## Console Smoke Scenario

1. Open `/admin/artifacts` as a workspace member.
2. Confirm artifacts show type, producer, display metadata, digest, compatibility summary, and inspection state.
3. Select an `elsa.workflow-definition` artifact.
4. Confirm details show envelope metadata and safe diagnostics.
5. Confirm payload content, workflow JSON, manifest JSON, credentials, tokens, connection strings, and raw secret values are not displayed.
6. Filter or scan artifacts by type.
7. Confirm legacy artifacts without explicit envelope fields still render with default type/producer metadata.

## Verification Commands

```sh
dotnet test tests/Elsa.Platform.Deployment.Artifacts.Tests/Elsa.Platform.Deployment.Artifacts.Tests.csproj --filter ArtifactEnvelope
dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceArtifact
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceArtifact
cd src/Elsa.Platform.Console && npm test -- --run artifacts
cd src/Elsa.Platform.Console && npm run typecheck
git diff --check
```

## Verification Results

- Not run yet. This spec defines the next implementation slice.

## Known Scope Boundaries

- Studio **Submit to Platform** UX is covered by `027-studio-submit-to-platform`.
- Runtime command polling, claiming, and completion are covered by `028-runtime-command-sync`.
- Workflow artifact application inside Elsa runtimes is covered by `029-workflow-artifact-runtime-applier`.
- Object storage upload, OCI publication, artifact signing, and payload encryption are future provider-specific slices.
