# Elsa Commercial Platform Current-State Assessment

Date: 2026-08-29

## Executive Assessment

Elsa Control is already a meaningful modular control-plane codebase, not a prototype shell. It contains package governance, organization/workspace identity, entitlement snapshots, immutable artifacts, desired-state revisions, promotion, runtime command synchronization, engine health, secret-store metadata, a production-oriented React console and current Azure hosting assets.

The main gap is the shortest commercial promise: Elsa Control cannot yet select a governed Elsa release/topology/features and directly realize it as a working managed Elsa workload in Azure. Existing Azure assets deploy Elsa Control itself to App Service and Azure SQL. Existing deployment behavior primarily governs application artifacts delivered to pre-registered remote engines.

## Capability Matrix

| Capability | Current evidence | Assessment |
|------------|------------------|------------|
| Package/feature metadata | NuGet source ingestion, manifests, generator contracts, approvals and compatibility services | strong reusable foundation |
| Runtime images/topologies | signed/scanned `runtime-server`, `runtime-studio`, `runtime-combined` pipeline exists, but Control config still points at obsolete `elsa-pro-* : latest` identities | strong image pipeline; broken/stale control-plane catalog contract |
| Runtime Builder | saved configurations, package planning, bundles and template outputs | reusable; must feed instance/provider flow deliberately |
| Desired state | immutable revisions, structured records, canonical hashes, validation/promotion | strong foundation; typed reconciliation migration remains |
| Deployment execution | durable runs/commands, leases, outcomes, runtime pull/sync, webhook advisory path | strong for artifact delivery to registered runtimes |
| Azure Elsa workload provider | none found | critical missing capability |
| Identity/tenancy | OIDC/JWT/cookie identity, organizations, workspaces, memberships and permission grants | substantial; SaaS signup/invitations/federation lifecycle incomplete |
| Entitlements | workspace/organization snapshots and limits used for feed/workspace policy | reusable primitive; subscription/billing lifecycle incomplete |
| Secrets | secret-store and credential-reference APIs/UI; protected local values | useful engine-credential capability, not yet complete app configuration/secrets product |
| Console | shared React shell with packages, builder, deployments, credentials and logs; router expects an absent artifact module | correct basis, but current frontend health is broken/stale; managed runtimes/operations/audit remain placeholders |
| Observability | health checks, OTel dependencies, engine verification and console logs | operational baseline, not customer/SRE product completeness |
| Backups/DR | roadmap/spec language only for managed workloads | missing |
| Commercial images | configured image identities and related repositories/pipelines under discovery | authority/version/provenance must be made explicit |

## Current Runtime and Deployment Flow

```text
Studio or API producer
  -> artifact metadata and payload reference
  -> immutable desired-state revision
  -> validation/promotion
  -> deployment run and durable command
  -> registered runtime/provider polls and claims
  -> runtime-side applier verifies and applies
  -> safe outcome and health report
```

This is well aligned with the Elsa Control Cloud customer-agent hypothesis. It is not yet an infrastructure provisioner: environments and engines are registered rather than created as managed Elsa compute/database/network resources.

## Reusable Assets

- Provider-neutral deployment abstractions, artifact identity and diagnostics.
- Workspace deployment services, permission grants, immutable revisions and run history.
- Runtime command sync and outbound-pull trust pattern.
- NuGet/package manifest pipeline and compatibility evaluation.
- Runtime Builder planning and Docker Compose/CLI-style output foundations.
- Organization/workspace accounts, authentication modes and entitlement snapshots.
- EF Core SQLite/SQL Server persistence and migrations.
- Shared console with route guards, query layer and deployment/operator modules.
- Aspire local orchestration, current Azure host deployment and OpenTelemetry service defaults.
- Existing accepted ADRs for package consolidation, remote apply/typed reconciliation and product-owned identities.

## Architectural Gaps

1. No first-class Elsa Instance aggregate combines version, topology, features, region, isolation, capacity, release channel and provider placement.
2. Version availability/lifecycle is not governed product data; configured images expose only `latest`.
3. No Azure Elsa-workload provider or stamp placement/capacity model exists.
4. No executable isolation profiles or published boundary guarantees exist.
5. No subscription/billing lifecycle, trial/grace/retention policy or usage/cost attribution exists.
6. Managed backup/restore, domain/TLS, release-ring upgrade and traffic-transition flows are missing.
7. Customer-facing observability and SRE control across organizations/stamps are incomplete.
8. Arbitrary package provenance/build/isolation is not implemented.
9. Existing frontend has the right shell but no complete SaaS instance onboarding journey.
10. Cross-repository ownership for images, specifications, runtime integration and websites must be formalized.
11. Nuplane is not integrated in production code; current references are presentation/configuration hints rather than package reconciliation.
12. The console router currently imports an absent artifact feature module, so frontend build health must be restored before treating the shell as a reliable base.
13. Current commercial image publishing uses `runtime-*` identities and signed immutable supply-chain evidence, while Elsa Control still advertises obsolete `elsaworkflows/elsa-pro-* : latest` images.
14. Elsa 3.8 commercial images and Elsa 4 Foundation/Foundation Studio are parallel release ecosystems; no central compatibility/lifecycle matrix unifies them.

## Intent-versus-Reality Divergences

- Historical deployment strategy described an in-process/CLI apply engine; ADR-0004 and production code moved apply to remote runtime/provider consumers.
- README and specs describe managed hosting and runtime operations as direction; console routes for managed runtimes, operations and audit are still placeholders.
- Runtime image metadata recognizes Server/Studio/Combined, but the catalog is static configuration and does not implement multi-major lifecycle policy.
- Azure deployment files demonstrate an Elsa Control host on App Service, not the mission's Azure deployment provider for Elsa workloads.
- Nearly all Spec Kit documents remain marked `Draft` even when their code is present, reducing confidence in specification status as program truth.
- Historical documentation reports successful console validation, but the current router references a missing artifact module; reality must be revalidated.

## Immediate Recommendation

Do not broaden SaaS UI or billing first. Establish the governed instance/version/topology contract and prove one real Azure provider path to a healthy Elsa endpoint. In parallel, complete the security/threat-model and commercial image provenance discovery that can invalidate the provider design.

## Evidence

- `README.md`
- `src/Hosting/ElsaControl.Api/appsettings.json`
- `src/Hosting/ElsaControl.Api/Program.cs`
- `src/Hosting/ElsaControl.AppHost/AppHost.cs`
- `src/Hosting/ElsaControl.Console/src/app/routes.tsx`
- `docs/adr/0004-deployment-engine-typed-reconciliation-hybrid.md`
- `specs/010-runtime-image-metadata-api/spec.md`
- `specs/015-managed-hosting-control-plane/spec.md`
- `specs/028-runtime-command-sync/spec.md`
- `specs/031-organization-tenancy/spec.md`
