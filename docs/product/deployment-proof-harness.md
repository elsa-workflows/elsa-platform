# Deployment-proof harness

Tracking issue: [#109](https://github.com/valence-works/elsa-control/issues/109)

The deployment-proof harness is the repeatable highest-seam contract for Milestone B. It starts
with an exact Elsa release selection and ends with a healthy, usable endpoint, a basic workflow
result, a repeated apply/no-op check, and verified cleanup. The orchestration is provider-neutral;
the fake provider proves the contract now and the durable `AzureProviderProofAdapter` provides
the real-provider seam for #126.

## Run the fake-provider proof

```bash
dotnet test tests/Deployment/ElsaControl.Deployment.Proof.Tests/ElsaControl.Deployment.Proof.Tests.csproj
```

The tests are deliberately disposable and do not call Azure, create resources, or require
credentials. The fake provider exercises the same `IDeploymentProofProvider` seam that a real
provider must implement.

The Azure adapter is intentionally not enabled by this test command. A live proof host must
register an admitted `IAzureProviderProofPlanFactory`, a concrete `IAzureProviderRunner`, and an
`IAzureProviderProofWorkflowProbe`; the API worker remains disabled until that host has supplied
those dependencies and the disposable Azure prerequisites below.

## Inputs and prerequisites

`DeploymentProofInput` requires:

- an exact Elsa version such as `3.8.0-preview.5413` (the catalog is not limited to a fixed set
  of versions);
- an explicit topology such as `Combined`;
- an explicit feature set such as `DefaultAuthentication`, `Liquid`, `StructuredLogs`, and
  `OpenTelemetry`;
- an image repository/reference; and
- an immutable `sha256:<64 hexadecimal characters>` image digest.

`DeploymentProofEnvironment` requires a disposable environment name, region, provider name, and
zero or more **secret reference names**. Secret values are not accepted by the harness. A real
provider resolves credentials inside its own execution boundary (managed identity, Key Vault,
or another approved provider) and returns only safe metadata.

For the real Azure proof, the operator must additionally provide an enabled Azure subscription,
permission to create and delete the disposable resource group, the approved West Europe region,
the exact image digest and release metadata, and a documented cost ceiling. Those prerequisites
belong to the real-provider run and are not hidden in this fake-provider test.

## Stage contract and evidence

`DeploymentProofHarness` runs these stages in order:

1. **Selection** — resolves and echoes the exact version, topology, features, image reference,
   and digest.
2. **Plan** — creates a deterministic provider plan/fingerprint without mutation.
3. **Provision** — creates or reconciles the disposable provider resources and returns an endpoint.
4. **Health** — waits for and verifies endpoint health.
5. **Workflow** — executes one basic workflow through the healthy endpoint.
6. **RepeatApply** — re-applies the already-provisioned plan once and requires an explicit no-op.
7. **Cleanup** — deletes the disposable resources and confirms cleanup. It is attempted whenever
   planning completed, even if provisioning returned an error after creating resources; the
   provider derives the disposable target from the plan and environment when no deployment result
   is available.

Each result is a structured `DeploymentProofStageResult` with a stage, status, safe code, message,
timestamps, duration, and evidence. Failures identify the stage and are reported without stack
traces, payloads, credentials, or raw provider exceptions. `DeploymentProofReport.ToJson()` is the
portable evidence artifact for a CI job or issue comment.

The harness records safe identifiers and diagnostics only. Its serializer redacts common accidental
`password=`, `secret=`, `token=`, `credential=`, connection-string, and authorization assignments,
but this is a last line of defense: provider adapters must never return secret values.

## Failure and recovery behavior

The fake provider supports explicit failure injection at selection, plan, provision, health,
workflow, repeat-apply, and cleanup. The tests verify each failure seam and that a target is still
sent through cleanup after a later failure or a partial provision failure. If selection or planning
fails before a plan exists, cleanup is recorded as skipped with a safe reason.

The harness is not an Azure provider, an SLO definition, or a production lifecycle controller.
It is the executable seam and evidence contract used to validate those implementations.
