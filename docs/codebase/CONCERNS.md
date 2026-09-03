# Codebase Concerns

## 1) Top Risks (Prioritized)

| Severity | Concern | Evidence | Impact | Suggested action |
|----------|---------|----------|--------|------------------|
| high | The accepted Azure workload proof is not yet orchestrated by the Elsa Control provider | #108 proved the checked-in Bicep and live Elsa endpoint; current production provider commands do not yet realize that plan from an Elsa Instance desired state | Blocks the provider-driven Milestone B proof and Elsa Cloud walking skeleton | Implement the #125 thin vertical slice without moving Azure fields into provider-neutral contracts |
| high | ~~Console routes import an artifact feature directory absent from the current tree~~ | ~~`src/Hosting/ElsaControl.Console/src/app/routes.tsx`; no `src/Hosting/ElsaControl.Console/src/features/artifacts/`~~ | **Resolved by #140 (closes #115):** `src/Hosting/ElsaControl.Console/src/features/artifacts/` is in tree; routes still import it; current-state already records the repair. | Keep the restored module. Do not treat missing-artifacts as an open build break. |
| high | Version/topology metadata is static config with `latest`, not governed lifecycle data | API `appsettings.json` RuntimeBuilder images | Cannot safely offer Elsa 3/4 lifecycle or distinguish upgrade from migration | Introduce version/distribution catalog contracts and policy-owned data |
| high | Two very large, high-churn concentration points | `DeploymentsPage.tsx`; `DeploymentWorkspaceStore.cs`; scan metrics | Regression and parallel-delivery risk | Split by stable feature/store boundaries before heavy parallel change |
| high | Security/SRE guarantees for isolation profiles are not yet evidenced | no isolation-profile implementation or dedicated security configs found | Risk of overpromising shared/dedicated/private boundaries | Threat-model and acceptance-test each profile before launch claims |
| medium | Typed reconciliation migration in ADR-0004 remains incomplete | ADR-0004 and live deployment services | New desired-state kinds may duplicate parsing and classification | Finish the analytical-core migration before adding provider complexity |

## 2) Technical Debt

| Debt item | Why it exists | Where | Risk if ignored | Suggested fix |
|-----------|---------------|-------|-----------------|---------------|
| Monolithic deployment page/store | rapid vertical feature delivery | console deployment page; EF deployment store | conflicts and slow reviews | extract cohesive modules behind existing contracts |
| Static runtime-image catalog | initial Runtime Builder bootstrap | `appsettings.json` | ungoverned lifecycle/compatibility | move to product-owned catalog/service |
| Nuplane is described but not integrated | integration was deferred pending stable APIs | Runtime Builder metadata/docs; no Nuplane client reference | managed package installation/reconciliation assumptions are unproven | define Nuplane boundary after repository/API discovery |
| Historical specs remain `Draft` despite implemented behavior | Spec Kit used as delivery workspace without lifecycle closure | `specs/001`–`039` | planning ambiguity | audit implemented vs planned and mark/archive accurately |
| ~~CI lacks explicit frontend stages~~ | ~~solution build currently carries some frontend integration implicitly~~ | `.github/workflows/ci.yml` | **Resolved by #128:** the required `Console quality gates` job runs locked install, unit tests, TypeScript typecheck, and production build. | Keep the job required for pull requests. |

## 3) Security Concerns

| Risk | OWASP category | Evidence | Current mitigation | Gap |
|------|----------------|----------|--------------------|-----|
| Arbitrary NuGet packages execute as workload code | A08 | package/runtime architecture | approval metadata and runtime separation | no defined immutable build/provenance/isolation pipeline |
| Trusted-header identity can become dangerous if proxy boundaries are wrong | A01/A07 | trusted-header reader/config | disabled by default and allowed-network config | production deployment-mode assurance tests |
| Local protected credentials depend on key-ring availability | A02 | API startup requirement | production startup fails without stable key path | managed external vault/rotation and DR design |
| Shared-tenancy guarantees are unspecified | A01 | organization/workspace auth exists; isolation profile does not | workspace permission checks | workload/data isolation threat model and tests |

## 4) Performance and Scaling Concerns

| Concern | Evidence | Current symptom | Scaling risk | Suggested improvement |
|---------|----------|-----------------|-------------|-----------------------|
| One relational catalog contains broad control-plane domains | EF context/store | no measured symptom documented | migration/locking and noisy-neighbor risk | load test critical queries and define partition/stamp strategy |
| Large console module | ~249 KB source file in scan | high churn | slow review/build and fragile state coupling | split routes/forms/read models by capability |
| In-memory caches/recent logs | API registrations | process-local behavior | inconsistent multi-instance views | document/cache only safe data or use shared providers |

## 5) Fragile/High-Churn Areas

| Area | Why fragile | Churn signal | Safe change strategy |
|------|-------------|-------------|----------------------|
| Deployment console | many product flows in one file | top current file by size; renamed predecessor had 18 recent changes | extract one route/capability at a time with component tests |
| Deployment EF store | many aggregates in one implementation | ~154 KB; predecessor high churn | preserve store interface, split partial responsibilities incrementally |
| API composition root | central registration and endpoint wiring | predecessor among highest churn | move subsystem registrations to extension methods with composition tests |

## 6) Product-Owner Questions

No Phase 0 product-owner question remains open. Recommended launch policies were accepted into `docs/product/decisions.md`. Evidence-gated technical hypotheses remain tracked as Spikes and Proposed ADRs rather than owner questions.

## 7) Evidence

- `docs/codebase/.codebase-scan.txt`
- `src/Hosting/ElsaControl.Api/appsettings.json`
- `src/Hosting/ElsaControl.AppHost/AppHost.cs`
- `src/Hosting/ElsaControl.Console/src/app/routes.tsx`
- `docs/adr/0004-deployment-engine-typed-reconciliation-hybrid.md`
- `specs/015-managed-hosting-control-plane/spec.md`
