# Production Azure workload templates

This directory is the bounded Azure resource authority used by the managed-instance provider runner. It is separate from the disposable validation stack and has no lifecycle or cleanup semantics tied to a temporary run.

## Contract

- `main.bicep` is a resource-group deployment for one managed workload: user-assigned identity, Key Vault, Azure SQL, Log Analytics, a Container Apps environment and (when `deployWorkload` is true) the HTTPS workload app.
- `acr-pull-role.bicep` is deployed in the existing runtime registry resource group because the `AcrPull` assignment is scoped to the registry. Its contract is `registryName`, `workloadIdentityId` and `workloadPrincipalId`.
- `sql-bootstrap.sql` is executed once by the configured Microsoft Entra SQL administrator. The host substitutes `__WORKLOAD_IDENTITY_NAME__` and `__WORKLOAD_IDENTITY_CLIENT_ID__`; no secret or password is part of the file.
- `modules/` contains only the resource-shaped modules referenced by `main.bicep`. The three files above remain at the directory root because the provider runner resolves them by name.

The image is immutable: callers provide a repository and a 64-character SHA-256 digest, and the app receives `repository@sha256:digest`. Outputs are limited to resource identifiers, endpoint and safe fingerprint metadata. Secret values, connection strings and provider credentials stay outside the template and its outputs.

## Release data

The template does not select an Elsa generation or feed by branching on a version. `elsaVersion`, `releaseLine`, `sqlWorkflowPackageVersion` and `sqlQuartzPackageVersion` are caller-supplied values and are included in the deterministic plan identity. `releaseVersion` can carry a producer release version independently of the runtime version. The release package feed name and service index are configurable; the generic public NuGet index is only the safe default and can be replaced by the producer-owned feed for a release.

The workload name, owner, plan fingerprint and release values are retained as safe resource tags and as `ELSA_RELEASE_LINE` / `ELSA_RELEASE_VERSION` environment metadata. This keeps ownership and release provenance visible without embedding a particular Elsa release line in infrastructure code.

## Identity and data protection

The SQL server uses Microsoft Entra-only administration and the workload identity is created as a contained service-principal user by `sql-bootstrap.sql`. Key Vault uses RBAC: the workload can read secrets and the bootstrap operator can seed them, but neither receives broad vault administration through the template. SQL backup retention remains explicit so the provider can make its own recovery decision.

Production deployment is deliberately bounded to the provider's governed regions and public HTTPS ingress profile. Private networking, edge routing, topology expansion and release admission remain separate provider/catalog decisions.
