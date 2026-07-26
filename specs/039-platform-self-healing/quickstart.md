# Quickstart: Platform Self-Healing

This quickstart is the cross-repository acceptance path. It uses fake inference/GitHub adapters for deterministic local validation and a real GitHub sandbox only in the provider integration lane.

## Prerequisites

- .NET 10 SDK
- Node.js supported by the Console lockfile
- Docker when running SQL Server parity tests
- Clean feature branches in both `elsa-foundation` and `elsa-platform`
- A local NuGet feed containing the coordinated Foundation OpenTelemetry packages

## Scenario 1: Foundation post-redaction contribution

1. Send an OTLP exception log containing a known secret attribute to the Foundation ingestion pipeline.
2. Register two test contributors.
3. Verify each contributor receives the same redacted batch exactly once and before diagnostics store/live-feed publication.
4. Make one contributor fail.
5. Verify ingestion fails and neither the diagnostics store nor live feed publishes the batch.
6. Pack the Foundation OpenTelemetry projects to the local feed.

## Scenario 2: Configure repairable components

1. Build the sample ASP.NET Core workflow host with the Platform component-manifest package.
2. Verify the manifest contains the application, Elsa packages, a custom Acme package, assembly hashes, dependency edges, and source revision without local paths or credentials.
3. Upload the manifest to one Platform application revision.
4. Add a source binding only for the Acme package and approve its GitHub repository/workflow/policies.
5. Verify the Acme component is repairable, Elsa/third-party packages remain observation-only, and an overlapping conflicting binding blocks automation.

## Scenario 3: OTLP exception to one incident

1. Enable Healing discovery and repair for the application's development and production environments.
2. Send 10,000 retry-identical qualifying exception occurrences across 100 simulated instances and both environments.
3. Verify the OTLP request succeeds only after durable inbox acceptance.
4. Run the inbox worker concurrently from multiple worker identities.
5. Verify exactly one active incident and one repair work-item operation exist for the fingerprint/repository, while all occurrences and environment impact are preserved.
6. Send validation, authorization, cancellation, handled, and still-retrying failures and verify they do not create automatic repair work.

## Scenario 4: Governed repair pull request

1. Let the fake GitHub adapter create the issue and dispatch the approved workflow.
2. Exchange a signed test GitHub OIDC token bound to repository, workflow revision, run, attempt, and nonce.
3. Verify the resulting capability can read only that attempt's evidence and upload only that attempt's result.
4. Upload a reproduced repair result with a small safe patch and regression evidence.
5. Verify the agent never receives Git credentials.
6. Let the trusted publisher validate and create one draft/ready pull request through the fake provider.
7. Retry provider operations and verify no duplicate issue, branch, or PR is created.

## Scenario 5: Security negative matrix

Verify each case is denied and audited:

- wrong workspace/application/repository/workflow/run/nonce
- expired or replayed GitHub OIDC JWT
- invalid webhook HMAC or duplicate delivery
- patch path traversal, absolute path, binary, symlink, submodule, forbidden rename
- workflow/publisher/policy/permission/validation self-modification
- stale target SHA or revoked binding
- cross-workspace evidence access
- raw secret in OTLP, work item, agent input, PR, or ordinary audit view
- attempt beyond the maximum of two
- automatic merge with any failed/missing/stale/ambiguous/unknown gate

## Scenario 6: Inferred and revision-unverified repairs

1. Submit a high-confidence result that cannot reproduce the failure.
2. Verify Platform may create an explicitly unreproduced draft PR when policy permits, but auto-merge is impossible.
3. Repeat without the exact producing revision and verify the PR is revision-unverified and human-merge-only.
4. Submit insufficient confidence and verify only analysis is recorded.

## Scenario 7: Deployment-based healing

1. Record PR merge and verify the incident is not healed.
2. Submit deployment of the repaired revision to development only.
3. Complete the verification window with a positive affected-operation execution and no recurrence.
4. Verify development is healed while production keeps the incident open.
5. Deploy to production but send no relevant operation evidence; verify `Deployed—unverified`.
6. Send the matching exception again; verify failed verification and a trusted failure signal, without Platform rollback.
7. On a later repaired deployment, send positive execution and no recurrence; verify the final environment and incident become healed.

## Foundation validation

```bash
dotnet test tests/Elsa/Diagnostics/OpenTelemetry/Tests/Elsa.Diagnostics.OpenTelemetry.Tests.csproj
dotnet build Elsa.Foundation.sln
dotnet pack src/Elsa/Diagnostics/OpenTelemetry/Core/Elsa.Diagnostics.OpenTelemetry.Core.csproj -o artifacts/healing-feed
dotnet pack src/Elsa/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry.csproj -o artifacts/healing-feed
git diff --check
```

Use the actual solution filename reported by the repository if it differs.

## Platform validation

```bash
dotnet test tests/Elsa.Platform.Healing.Abstractions.Tests/Elsa.Platform.Healing.Abstractions.Tests.csproj
dotnet test tests/Elsa.Platform.Healing.Core.Tests/Elsa.Platform.Healing.Core.Tests.csproj
dotnet test tests/Elsa.Platform.Healing.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.Healing.Persistence.EntityFrameworkCore.Tests.csproj
dotnet test tests/Elsa.Platform.Healing.OpenTelemetry.Tests/Elsa.Platform.Healing.OpenTelemetry.Tests.csproj
dotnet test tests/Elsa.Platform.Healing.GitHub.Tests/Elsa.Platform.Healing.GitHub.Tests.csproj
dotnet test tests/Elsa.Platform.Healing.Agent.Tests/Elsa.Platform.Healing.Agent.Tests.csproj
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter Healing
dotnet build Elsa.Platform.sln
npm --prefix src/Elsa.Platform.Console test -- src/features/healing
npm --prefix src/Elsa.Platform.Console run typecheck
npm --prefix src/Elsa.Platform.Console run build
git diff --check
```

## Real GitHub sandbox enablement gate

Before enabling automatic merge against a real repository, run an opt-in test against a dedicated sandbox repository and GitHub App installation:

1. Issue projection and idempotent update.
2. Workflow dispatch at the approved immutable workflow revision.
3. OIDC exchange with Platform audience.
4. Trusted branch/PR publication using a narrowed installation token.
5. Webhook verification and replay rejection.
6. Required-check/branch-protection observation.
7. Human merge and one fully eligible auto-merge case.
8. Cleanup through provider APIs without granting the agent a write token.
