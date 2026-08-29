# Disposable Azure workload proof

This directory is the checked-in Bicep authority for issue [#146](https://github.com/valence-works/elsa-control/issues/146), a disposable Elsa 3.8 Combined proof in **West Europe**. Azure-specific resource realization stays here; provider-neutral desired state does not gain Azure fields.

The stack deliberately contains no secret values, SQL passwords, connection-string values, or mutable image tags. The image is composed as `imageRepository@sha256:imageDigest`, and `imageDigest` is exactly 64 hexadecimal characters. The existing `valenceruntimeimages` ACR is the only resource outside the disposable group, and receives one deterministic `AcrPull` assignment through `acr-pull-role.bicep`.

## Files

- `main.bicep` — resource-group deployment for the proof identity, proof-only Key Vault, Azure SQL, short-retention Log Analytics, Container Apps environment and (optionally) the workload app.
- `modules/` — resource-shaped modules. `container-app.bicep` wires the immutable image, ACR/Key Vault managed identity, HTTPS ingress, multiple revisions, scale-to-zero and health probes.
- `acr-pull-role.bicep` — resource-group deployment run against the existing ACR's resource group because the role assignment must be scoped to the registry.
- `sql-bootstrap.sql` — explicit, one-time Entra contained-user boundary. It is run as the configured SQL Entra administrator and grants the workload identity the migration/runtime roles needed for the proof.

## Offline validation

From the repository root:

```bash
scripts/validate-azure-workload-proof.sh
```

This runs Bicep build/lint and offline contract tests. It never creates a resource group or Azure resource. To run a read-only what-if, set the documented environment variables and `AZURE_WORKLOAD_PROOF_WHAT_IF=1`; the resource group must already exist and the script does not create it.

## Disposable apply sequence

`scripts/azure-workload-proof.sh` is fail-closed. `validate` is the default safe path, `what-if` requires an existing group, and `apply` requires `DISPOSABLE_PROOF_APPLY=YES`. The apply path uses a unique resource group supplied by the caller, not an existing workload group.

The apply path is two-phase so secret values never enter Bicep parameters, deployment plans, outputs or source control:

1. Deploy the foundation with `deployWorkload=false`.
2. Grant the workload identity `AcrPull` on `valenceruntimeimages` using the separate role template.
3. Generate the signing key locally and generate the Azure AD managed-identity SQL connection reference locally. Seed both into the proof Key Vault with file input; values are not printed.
4. Run `sql-bootstrap.sql` once as the configured Microsoft Entra SQL administrator. This creates the workload identity as a contained external user and grants `db_datareader`, `db_datawriter` and temporary `db_ddladmin` for first-start migrations. Remove `db_ddladmin` after migration evidence if the proof policy requires least privilege.
5. Deploy the workload phase. Container Apps reads the two Key Vault secrets through the user-assigned identity; it does not receive secret values in the template.
6. Wait for `/health` and capture only the endpoint, immutable image reference, resource IDs, plan fingerprint, revision and redacted health evidence.

The SQL bootstrap operator must have an Entra login and object ID. SQL authentication is intentionally unavailable: the server is configured with `azureADOnlyAuthentication: true`. The runtime connection is generated with the workload identity client ID, Azure AD managed identity authentication, encryption enabled and certificate trust disabled.

Use an interactive Entra user for the `FROM EXTERNAL PROVIDER` step. Azure SQL must be able to resolve the workload service principal through the external provider; if tenant policy blocks Graph resolution, stop and record the failure rather than falling back to SQL authentication or putting a password into the deployment.

## What is provisioned

| Resource | Proof setting |
| --- | --- |
| User-assigned managed identity | ACR pull, Key Vault secret read, SQL contained user |
| Key Vault | RBAC, soft delete, public access for this no-VNet proof, 7-day retention |
| Azure SQL | Entra-only logical server; GP serverless 0.5 minimum, 60-minute auto-pause, local backup redundancy |
| Log Analytics | PerGB2018, minimum 30-day retention; ACA console/system logs and metrics |
| Container Apps environment | West Europe, consumption-backed, no zone redundancy |
| Container App | External HTTPS-only ingress, port 8080, multiple revisions, latest revision at 100%, 0–1 replicas, startup/readiness/liveness probes |

Front Door, custom domains, private networking, VNet integration and production HA are intentionally excluded from this proof. They are separate provider/edge decisions.

## Determinism, cost and cleanup

Names are derived from the caller's unique `proofName`. The plan input includes proof ID, image digest, Elsa version, topology, ACR location, bootstrap identity, secret names and expiry. Bicep derives a stable `uniqueString` fingerprint and revision suffix; the runbook uses a stable deployment name. Repeating the same inputs is therefore safe and should produce no resource changes. Evidence can additionally record a SHA-256 of the compiled template.

The proof uses consumption compute, a one-vCore serverless SQL database with auto-pause, and the minimum 30-day Log Analytics retention. Keep the run inside a short time box, scale to zero when idle, and inspect current subscription pricing before applying. Azure has no instantaneous hard spend cap; the controlling guardrail is immediate deletion.

After evidence, delete the entire proof group and verify it is gone:

```bash
scripts/azure-workload-proof.sh cleanup --resource-group <disposable-proof-group> \
  --proof-name <unique-suffix> \
  --registry-resource-group <runtime-acr-resource-group>
az group exists --name <disposable-proof-group>  # must return false
```

The cleanup command removes the deterministic ACR role assignment before deleting the proof group. If the ACR administrator is separate, run the equivalent role-assignment removal under that administrator's authority. Do not delete or mutate the shared ACR itself.
