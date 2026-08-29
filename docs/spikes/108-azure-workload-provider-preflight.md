# Spike #108: Azure workload provider conclusion

Date: 2026-08-29

Status: Complete — hypothesis accepted

The historical `preflight` filename is retained so existing issue, review and runbook links remain stable.

## Question

Can Azure Container Apps, Azure SQL, Key Vault, managed identity, and checked-in Bicep satisfy the first Elsa 3.8 Combined Dedicated deployment proof while keeping Azure details below the provider-neutral desired-state boundary?

## Conclusion

Yes, within the tested Dedicated, single-region boundary. Accept Azure Container Apps, Azure SQL, managed identity, Key Vault, direct Container Apps HTTPS ingress, and checked-in Bicep as the initial Azure workload realization stack. Elsa Control should orchestrate this stack through provider-neutral desired state and provider-owned plan/apply/checkpoint commands. Aspire remains local developer orchestration.

This conclusion does not make the disposable proof production infrastructure. The next implementation slice is issue #125: move the proven resource and lifecycle behavior behind the Azure deployment provider while retaining the same immutable inputs, health gates, retry semantics, and cleanup ownership checks.

## Evidence matrix

| Hypothesis | Result | Evidence |
| --- | --- | --- |
| A signed Elsa 3.8 Combined image can run from an immutable reference | Passed | Signed release manifest `sha256:f29bad4d2b965eef17b54b2815dcc4c5e9d2d21d01b9e026f811d750d8d65c6d`; Combined image `sha256:e3a8176ded39f892c58cbe3fe05b3872e0cd98244450a8dc1a9319586d70219b`; signatures, indexes, 28 layer bindings, and retained evidence verified independently. |
| Azure SQL can provide durable Elsa workflow state | Passed | A workflow definition was published and instance `ij6Th57tp0iAdQw_PwGadg` finished with zero incidents. Both remained queryable after a healthy revision replacement. |
| Managed identity and Key Vault can avoid persisted credentials | Passed | A user-assigned identity pulled the private image, read Key Vault references, and authenticated to Azure SQL as a contained user. Secret values never entered Bicep parameters, outputs, source, or retained evidence. |
| Readiness and liveness express the required failure boundary | Passed | Healthy SQL-backed Combined returned `/alive` 200 and `/health` 200. An invalid-SQL revision returned direct-revision `/alive` 200 and `/health` 503. |
| Multiple revisions can protect public traffic | Passed after hardening | Stable traffic remained at 100% while a candidate warmed at zero traffic. Failed or uncertain promotion restores the stable revision. The accepted rollout produced 360 probe pairs with zero public failures. |
| Identical desired state is safe to reapply | Passed | Unchanged apply reused the immutable healthy revision; 240 probe pairs had zero failures. The final exact-head reapply also completed with 480/480 successful probes. |
| Bootstrap access is temporary and fail-closed | Passed after hardening | The exact-IP SQL firewall rule and exact temporary Entra administrator were removed and verified absent. Cleanup refuses unrelated administrators and assignments. |
| All disposable and external proof resources can be removed | Passed; future convergence hardened | The live run eventually reached verified absence: resource group absent, external ACR deployment count zero, proof AcrPull count zero, and deleted-vault count zero. Because Container Apps environment deletion exceeded five minutes, #150/#151 subsequently extended the bounded convergence and added offline-tested authoritative absence checks for future runs. |
| The proof has bounded cost | Passed with reporting limitation | Consumption Container Apps, serverless SQL, and one disposable group bounded the run. Same-day Azure usage data had not posted when queried, so no reliable actual charge was available; no resource survived cleanup. |

The retained, secret-safe live evidence is recorded on issue #147. Issue #150 and PR #151 record the post-run cleanup hardening and independent read-only absence verification. The checked-in operational contract lives in [`infra/azure-workload-proof`](../../infra/azure-workload-proof/README.md).

## Accepted boundary

- **Compute:** Azure Container Apps, multiple-revision mode, consumption workload profile for the initial proof and vertical slice.
- **Data:** Azure SQL database per Dedicated instance for the initial paid isolation profile.
- **Identity and secrets:** user-assigned managed identity, contained SQL user, and Key Vault references. Bootstrap administrator and firewall access are temporary.
- **Ingress:** direct Container Apps HTTPS ingress for the provider slice. Front Door, custom domains, and managed edge routing remain issue #121 concerns.
- **IaC:** checked-in Bicep modules are the production resource-realization authority; the provider supplies parameters and lifecycle orchestration.
- **Desired-state boundary:** version, topology, features, release channel, isolation, and immutable artifact identity remain provider-neutral data. Azure resource names, revisions, stamps, identity IDs, and deployment checkpoints remain provider-owned facts.
- **Region:** West Europe for the initial managed offering.

## Alternatives and deferrals

- **AKS:** deferred. The proof found no requirement that justifies its additional operational surface.
- **Azure App Service for Elsa workloads:** rejected for this slice; the existing App Service assets host Elsa Control and do not provide the required workload revision model.
- **Front Door in the first provider slice:** deferred. Direct managed HTTPS ingress answered the compute/data/provider question without introducing a monthly edge base cost.
- **SQLite:** rejected for managed Dedicated state because it would not prove the intended durable SQL boundary.
- **SDK-created resources without checked-in IaC:** rejected as the source of truth. Provider orchestration may use Azure SDKs around a reviewed declarative plan, but must not create a second resource model.

## Observed constraints and follow-up work

- Container Apps managed-environment deletion can remain `ScheduledForDelete` for more than five minutes; cleanup uses a 20-minute bounded convergence window and authoritative postconditions.
- Azure role assignment and ingress operations are eventually consistent; uncertain command results require independent state verification.
- The proof intentionally used public service endpoints. Private networking, custom domains, Front Door, zone redundancy, backup/restore, production observability, and HA remain separate acceptance gates.
- The runbook briefly recreates the exact bootstrap Entra administrator during an existing-server reapply because ARM requires it to reconcile the SQL declaration. Successful and failure paths remove it and verify absence.
- Azure Container Apps acceptance does not accept Shared multi-tenancy or arbitrary customer packages. The first paid profile remains Dedicated; isolation expansion remains evidence-gated.

## Delivery consequences

1. Implement #125 as the provider-owned Elsa 3.8 Combined vertical slice using these accepted contracts.
2. Execute #126 through the provider, not the disposable proof runner, to complete Milestone B.
3. Keep edge/TLS productization in #121 and recovery evidence in #129.
4. Use #114 to define the provider-neutral Elsa Instance aggregate and lifecycle without Azure-shaped fields.
