# Spike #108: Azure workload provider preflight

Date: 2026-08-29

Status: Blocked pending [`valence-works/elsa-production-image#26`](https://github.com/valence-works/elsa-production-image/issues/26)

## Question

Can Azure Container Apps, Azure SQL, Key Vault, managed identity, and checked-in Bicep satisfy the first Elsa 3.8 Combined Dedicated deployment proof while keeping Azure details below the provider-neutral desired-state boundary?

## Read-only preflight result

The Azure platform hypothesis remains viable, but the agreed proof cannot run with the current commercial Combined image. No Azure resource was created, updated, or deleted during this preflight, and no cloud spend was incurred.

- The enabled `Skywalker ISP` subscription exposes West Europe and has the required resource providers registered.
- Azure Container Apps quota is available: no managed environments are currently in use against a regional limit of 50, and no regional vCPU is in use against a limit of 18.
- The repository's checked-in Bicep compiles with Bicep 0.43.8, but currently describes the Elsa Control/App Service infrastructure rather than a managed Elsa workload stack.
- The current private commercial image includes `runtime-combined:3.8.0-preview.5413-build.79` and mutable aliases. The proof must resolve an immutable digest before deployment.
- The Combined image contains and configures SQLite persistence only. It does not contain the SQL Server or PostgreSQL persistence packages/configuration required for workflow, identity, and Quartz state in Azure SQL.

Substituting SQLite would change the acceptance criterion and would not test the intended production data boundary. The actual deployment is therefore blocked until the producer publishes a SQL Server-capable immutable image under issue #26.

## Recommended bounded proof

After #26 is complete, use one uniquely named disposable West Europe resource group and the following minimum inventory:

- one Azure Container Apps workload-profile v2 environment using only the consumption profile;
- one externally reachable Combined app at 0.5 vCPU, 1 GiB, and one validation replica, with `/health` readiness and `/alive` liveness probes;
- one user-assigned managed identity for private registry pull and secret access;
- one Key Vault containing generated signing and bootstrap secrets, referenced without logging values;
- one Azure SQL General Purpose serverless database after the image supports SQL Server persistence;
- direct Container Apps HTTPS ingress for the first proof.

Azure Front Door is not required to answer the first provider/IaC question. It should be proven as a separate edge concern unless a later acceptance criterion requires it.

The executable sequence is:

1. Resolve the exact Elsa version, Combined topology, features, and immutable image digest.
2. Compile and validate the checked-in Bicep plan.
3. Deploy to the disposable resource group and wait for readiness.
4. Execute a basic Elsa workflow and capture the result.
5. Reapply identical desired state and demonstrate an idempotent/no-op result.
6. Deploy a known-bad revision, verify readiness failure and traffic protection, then restore the good revision.
7. Delete the resource group and verify that no proof resources remain.

## Cost guardrail

Public West Europe retail pricing observed during the preflight was approximately:

- Azure Container Apps consumption: USD $0.000034 per vCPU-second and $0.000004 per GiB-second, before the subscription's monthly free grant;
- Azure SQL General Purpose serverless: approximately USD $0.573934 per vCore-hour, or about $0.287 per hour at a 0.5-vCore minimum while active;
- Front Door Standard: a $35 monthly base charge billed hourly, which is another reason to omit it from this first proof;
- managed identity, existing ACR usage, and the small number of Key Vault operations: negligible for this run.

A four-hour proof should remain within a conservative USD $2-4 estimate and below USD $5 even with brief Front Door use. Azure does not provide an instantaneous hard spend cap for this run, so the operative controls are a four-hour time box, one explicit inventory, scale-to-zero where applicable, whole-resource-group deletion, and post-delete verification.

## Decision state

ADR-0010 remains proposed. Nothing in preflight disqualifies Container Apps or checked-in Bicep, but the spike cannot accept them until the SQL-capable image exists and the real endpoint, workflow, retry, failure, idempotency, and cleanup evidence passes.

## References

- [Azure Container Apps billing](https://learn.microsoft.com/azure/container-apps/billing)
- [Azure Container Apps revisions](https://learn.microsoft.com/azure/container-apps/revisions)
- [Managed identity image pull](https://learn.microsoft.com/azure/container-apps/managed-identity-image-pull)
- [Azure SQL Database pricing](https://azure.microsoft.com/pricing/details/azure-sql-database/single/)
- [Azure Front Door pricing](https://azure.microsoft.com/pricing/details/frontdoor/)
