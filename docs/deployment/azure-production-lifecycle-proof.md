# Opt-in production Azure lifecycle proof

`ProductionAzureLifecycleProofTests` is the bounded, destructive integration proof for the
managed Azure instance path. It is deliberately skipped unless the opt-in gate is set. The
test uses `WebApplicationFactory<Program>` with `Production`, the real `Program` registrations,
the configured Azure runner, hosted lifecycle workers, and the SQLite migration assembly. It
does not use `ProofHost`, a fake provider, a fake health probe, or a fake secret resolver.

## Invocation

Run this from an Azure-hosted test process with the candidate API/CLI image and a user-assigned
managed identity attached:

```sh
export ELSA_CONTROL_LIVE_AZURE_LIFECYCLE_PROOF=1
export ELSA_CONTROL_LIVE_AZURE_LIFECYCLE_PROOF_CONFIG=/run/elsa-control/live-proof.json
dotnet test tests/Hosting/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~ProductionAzureLifecycleProofTests.Production_composition_applies_reconciles_reloads_and_deletes_one_instance'
```

When the gate is absent, xUnit reports an explicit skipped test; it never silently passes.
`ELSA_CONTROL_LIVE_AZURE_LIFECYCLE_PROOF_CONFIG` is required once the gate is enabled. The
configuration file must contain the normal production settings plus these harness settings:

| Key | Requirement |
| --- | --- |
| `LiveProof:CatalogEntryPath` | Absolute or config-relative JSON file containing one trusted, previously admitted `GovernedReleaseCatalogEntry`; this bypasses producer admission by design. |
| `LiveProof:EvidenceDirectory` | Writable directory for value-free evidence on success or failure. |
| `LiveProof:InstanceId` | Required fresh canonical GUID for this run; use a new isolated database for reruns. |
| `LiveProof:ActorIssuer` / `LiveProof:ActorSubject` | Fixture identity used to create the owner account. |
| `LiveProof:ActorEmail` | Fixture account email; no credential. |
| `LiveProof:InstanceName` / `LiveProof:InstanceSlug` | Fresh instance identity. |
| `LiveProof:TimeoutSeconds` | Optional bound, 30–7200 seconds; default 1800. |
| `LiveProof:PollSeconds` | Optional bound, 1–30 seconds; default 5. |

The normal production configuration must also provide `ControlPlane:Origin` as an HTTPS origin,
`Database:Provider=Sqlite`, and an isolated absolute `ConnectionStrings:Catalog` SQLite path whose
filename contains `live-proof`. The path must not exist before the run (the harness also rejects
SQLite WAL/SHM sidecars), so an accidental rerun cannot attach to an existing catalog or instance.
It also requires
`DataProtection:KeysPath` on durable storage, all lifecycle and
provider workers enabled, the v1 instance-provider scope, pinned CLI/sqlcmd/curl/template paths,
and two versioned source Key Vault references plus the provider-owned SQL sentinel. The database
slot uses the provider-owned `secret://azure-managed/sql-connection` sentinel; signing-key and
admin-password slots remain versioned Key Vault locators. Raw secret values are not accepted.
HTTPS configuration locators are normalized to canonical `secret://` plan references.
Set `RuntimeBuilder:InstancePlans:DefaultEgress=unrestricted` explicitly for this Azure
profile; the default remains `restricted`, which this profile cannot realize. The admitted
release must declare exact `Elsa.Persistence.EFCore.SqlServer` and
`Elsa.Scheduling.Quartz.EFCore.SqlServer` package versions. Validate the catalog-to-plan-to-Azure
translation offline before provisioning; never infer missing versions from the Elsa release.

## Azure prerequisites

The host identity must be the same identity selected by `Runner:AzureCliClientId` and must be
attached to the host. It needs:

- subscription-level permission to create/delete the generated v1 sibling resource group and
  mutate its descendants;
- the governed ACR resource-group permissions, including the exact `AcrPull` role assignment;
- read access to the source Key Vault's two versioned secrets;
- SQL Entra bootstrap permission, with `Runner:SqlBootstrapObjectId` and the approved bootstrap
  login matching the identity;
- access to the immutable runtime image and its governed catalog projection.

The production host must report the pinned Azure CLI managed-identity account shape during
preflight. A developer's interactive `az login` is not a valid substitute. Production trusted
workspace headers must remain disabled; the fixture actor ID is persisted through the actual
account/workspace stores and passed to the lifecycle service.

## What the test proves

The test seeds an isolated actor, organization entitlement, and catalog entry using the real
stores, then creates a managed instance through `ElsaInstanceLifecycleService`. The accepted
operation is processed by the real hosted workers until the Azure provider reaches `Ready` and
`Healthy`. It then disposes and recreates the production factory against the same SQLite database,
submits a fresh reconcile, and verifies that the durable provider assignment is retained. Finally
it creates and consumes a real delete confirmation, waits for `Deleted`, and verifies the v1
assignment is `Deleted` with only its immutable resource-group identity retained and no
remaining workload-resource inventory.

Every polling loop is bounded. A failure attempts product cleanup once more through the lifecycle
service. The catalog database is never deleted. The evidence file contains only safe organization,
workspace, account, instance, operation, assignment, and provider-operation IDs plus stage and
cleanup status and scope (`none`, `local`, or `provider`); provider exceptions, configuration,
connection strings, secret values, and raw Azure output are not written.
If resolution fails before an assignment exists, successful local deletion is recorded as
local-only cleanup. It cannot satisfy the overall proof, which requires provider-scoped cleanup.

This proof covers provider apply/reconcile/reload/delete ownership and correlation. It does not
claim release-manifest producer admission or public customer authentication, because the composed
production verifier is intentionally fail-closed and the fixture starts from an already admitted
catalog projection.
