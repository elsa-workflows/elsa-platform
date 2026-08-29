# External Integrations

## 1) Integration Inventory

| System | Type | Purpose | Auth model | Criticality | Evidence |
|--------|------|---------|------------|-------------|----------|
| NuGet feeds | API | Discover packages and ingest manifests | feed credentials through configured sources | high | `src/PackageCatalog/ElsaControl.PackageCatalog.Sources.NuGet/` |
| OIDC/Entra/Keycloak | identity | Customer and operator sign-in | authorization-code cookie session and JWT validation | high | API `Program.cs`; AppHost |
| Elsa runtime/provider | HTTP command channel | Claim/apply deployment commands and report state | scoped engine credential/API authentication | high | runtime applier and trust-policy docs |
| Azure SQL / SQLite | relational DB | Catalog and control-plane persistence | managed identity/connection string | high | EF projects; AppHost |
| Azure App Service/ACR | hosting | Current Elsa Control host deployment | managed identity and azd/Aspire | medium | `infra/`; AppHost |
| GitHub Copilot SDK | API/process | Weaver-assisted investigations/plans | configured runtime client | low/optional | `src/Weaver/` |
| SignalR | stream | Live console log updates | authenticated console session | medium | console-log registration and console client |

## 2) Data Stores

| Store | Role | Access layer | Key risk | Evidence |
|-------|------|--------------|----------|----------|
| Catalog relational DB | Accounts, organizations, workspaces, packages, deployments, safe command/history metadata | EF Core stores | one large context/store and migration coupling | persistence projects |
| Artifact payload store | Immutable upload content outside catalog rows | workspace artifact upload service/local provider | production provider/lifecycle is incomplete | API configuration and artifact services |
| ASP.NET Data Protection key ring | Encrypt local engine credential values | Data Protection | production availability depends on stable shared storage | API `Program.cs` |

## 3) Secrets and Credentials Handling

- Credentials come from ASP.NET configuration, secret Aspire parameters, identity-managed access, or credential-store references.
- Development config contains explicit local-only keys/passwords; production values are parameterized.
- External secret providers store safe locators; local values are protected ciphertext and are not echoed on reads.
- Rotation APIs exist for engine credential references; full SaaS secret lifecycle and customer-facing configuration shapes remain incomplete.

## 4) Reliability and Failure Behavior

- NuGet/runtime HTTP clients use configured timeouts and Microsoft HTTP resilience where registered; exact policies vary by integration.
- Deployment commands support leases, retries/recovery state, idempotency metadata and safe outcome reporting.
- The in-process queue worker is disabled by default and only performs stale recovery when enabled; remote consumers perform apply.
- Circuit-breaker/failover policy: no uniform platform policy is currently documented; each external integration must define its retry, timeout and failure behavior before production acceptance.

## 5) Observability for Integrations

- Shared service defaults configure health checks and OpenTelemetry; console logging has recent-buffer and SignalR streaming support.
- Engine heartbeat/verification records health and reachability.
- Missing gaps include customer-grade metrics/traces/log isolation, SLOs, stamp capacity visibility and billing/cost attribution.

## 6) Evidence

- `src/Hosting/ElsaControl.Api/Program.cs`
- `src/Hosting/ElsaControl.AppHost/AppHost.cs`
- `src/PackageCatalog/ElsaControl.PackageCatalog.Sources.NuGet/`
- `src/Workflows/ElsaControl.Workflows.RuntimeApplier/`
- `docs/runtime-transport-trust-policy.md`
- `specs/035-engine-secret-stores/spec.md`
