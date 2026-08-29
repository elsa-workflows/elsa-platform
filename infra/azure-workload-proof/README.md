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
3. Generate the signing key and disposable proof-administrator password locally, and generate the Azure AD managed-identity SQL connection reference locally. Seed all three into the proof Key Vault with file input; values are not printed.
4. The configured operator is granted the proof vault's narrow Key Vault Secrets Officer role; the runbook retries while RBAC propagates.
5. Run `sql-bootstrap.sql` once as the configured Microsoft Entra SQL administrator. It creates a service-principal contained user from the workload identity client ID (without Graph lookup or Directory Readers) and grants `db_datareader`, `db_datawriter` and temporary `db_ddladmin` for first-start migrations.
6. Deploy the workload phase. Container Apps reads the three Key Vault secrets through the user-assigned identity; it does not receive secret values in the template.
7. Wait for `/health` and capture only the endpoint, immutable image reference, resource IDs, plan fingerprint, revision and redacted health evidence.

The SQL bootstrap operator must have an Entra login and object ID. SQL authentication is intentionally unavailable: the server is configured with `azureADOnlyAuthentication: true`. The runtime connection is generated with the workload identity client ID, Azure AD managed identity authentication, encryption enabled and certificate trust disabled.

Use Go `sqlcmd` with `--authentication-method ActiveDirectoryDefault`; the ODBC sqlcmd is not supported. Supply one exact public IPv4 address with `--sql-bootstrap-ip`. The runbook creates a same-IP temporary SQL firewall rule, retries readiness/bootstrap, and removes the rule on success or failure. `0.0.0.0` is never used for operator access.

The generated administrator username is `proof-admin`. If an operator needs it for functional proof, retrieve the password into a private shell variable without printing it:

```bash
proof_admin_password="$(az keyvault secret show \
  --vault-name <proof-vault-name> \
  --name admin-password \
  --query value \
  --output tsv)"
```

Do not echo, log or persist that variable. Cleanup deletes and purges the disposable vault with the resource group.

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

Names are derived from the caller's unique `proofName`. The plan input includes the compiled-template SHA-256, proof ID, image digest, Elsa version, topology, ACR location, bootstrap identity, secret names and expiry. Bicep derives a stable `uniqueString` fingerprint and revision suffix; the runbook uses a stable deployment name. Repeating the exact template and inputs is therefore safe and should produce no resource changes.

Container Apps revision suffixes are immutable. If a diagnostic or fault-injection revision changes the app outside Bicep, the runbook cannot reuse the older suffix to restore the desired template. It detects that drift, retains the plan fingerprint, and selects the first free deterministic `-rN` recovery suffix. Subsequent unchanged applies reuse that recovery suffix; a compiled-template or input change produces a new plan fingerprint.

The proof uses consumption compute, a one-vCore serverless SQL database with auto-pause, and the minimum 30-day Log Analytics retention. Keep the run inside a short time box, scale to zero when idle, and inspect current subscription pricing before applying. Azure has no instantaneous hard spend cap; the controlling guardrail is immediate deletion.

After evidence, delete the entire proof group and verify it is gone:

```bash
scripts/azure-workload-proof.sh cleanup --resource-group <disposable-proof-group> \
  --proof-name <unique-suffix> \
  --registry-resource-group <runtime-acr-resource-group>
az group exists --name <disposable-proof-group>  # must return false
```

Cleanup verifies exact proof ownership tags, removes only the workload identity's AcrPull assignment, waits for resource-group deletion, purges the exact proof vault's soft-deleted record, and verifies absence. It tolerates a partial foundation but refuses to adopt or delete an unrelated group.
