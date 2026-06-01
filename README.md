# Elsa Platform

Elsa Platform is the control plane for building, packaging, cataloging, and deploying Elsa-based systems. It brings together the pieces needed to govern professional Elsa solutions: manifest contracts, safe package inspection, catalog ingestion, runtime bundle planning, immutable deployment artifacts, workspace-scoped APIs, and an operator console.

The repository is organized as a set of bounded subsystems rather than one large application. Package Catalog owns package governance and workspace-owned catalog data. Runtime Builder turns catalog selections into deployable runtime bundles. Deployment owns artifact, environment, promotion, deployment-run, and runtime-command contracts. The React console provides a shared admin and workspace shell for those capabilities.

## What Is Here

- **Package manifests**: stable JSON contracts that describe deploy-time features, settings, infrastructure requirements, and extension metadata for Elsa packages.
- **Manifest generator**: an MSBuild-integrated generator that inspects package assemblies with metadata-only reflection and emits `elsa-package.json` during build or pack.
- **Platform API**: an ASP.NET Core API for public catalog reads, source synchronization, approvals, compatibility checks, workspace custom feeds, Runtime Builder access, and operator administration.
- **Runtime Builder**: services and contracts for runtime images, saved runtime configurations, server bundle generation, and deployment template rendering.
- **Deployment**: manifest parsing, artifact construction, path and checksum safety, deployment contracts, planning, execution, and history abstractions.
- **Platform Console**: a Vite/React admin UI served from `/admin` by the API container in production.
- **Aspire AppHost**: local orchestration and Azure-oriented publish wiring for the API host and catalog database.

## Repository Layout

```text
src/
  Elsa.Platform.AppHost                         .NET Aspire orchestration
  Elsa.Platform.Console                         React admin/workspace console
  Elsa.Platform.PackageManifests                Manifest wire contract
  Elsa.Platform.PackageManifest.Generator*      Generator, MSBuild task, and core logic
  Elsa.Platform.PackageCatalog.*                API, core services, EF Core persistence, sources
  Elsa.Platform.RuntimeBuilder.*                Runtime plans, bundles, deployment templates
  Elsa.Platform.Deployment.*                    Deployment manifests, artifacts, engine contracts
  Elsa.Platform.ServiceDefaults                 Shared host defaults

tests/
  Elsa.Platform.*.Tests                         Unit and integration-style test projects
  Elsa.Platform.Console.E2E                     Playwright console smoke tests

specs/
  001-...025-*                                  Spec Kit feature history and active plans
```

Subsystem boundaries matter. Deployment may consume catalog abstractions or client contracts, but it should not depend on catalog API, persistence, or source-provider internals. Package Catalog and Runtime Builder are sibling subsystems. The console is a platform-level shell, not a catalog-only frontend. Artifact-specific behavior belongs in producer and consumer integrations, not in the platform core.

## Platform Model

The current platform model is centered on accounts and workspaces. Workspace is the tenant boundary for customer-owned catalog and builder data. Public catalog endpoints remain anonymous where appropriate, while workspace-scoped endpoints derive account and workspace context from configured platform identity. Operator administration is separate and still supports an admin-key-backed local fallback.

Elsa Platform is the source of truth for immutable deployable artifacts and versioned desired-state revisions. Elsa Studio remains the authoring and single-engine inspection surface. Elsa runtimes remain responsible for executing deployed artifacts and owning runtime state such as workflow instances, bookmarks, queues, locks, and execution logs.

For platform-integrated Studio installations, the handoff command is **Submit to Platform**. It creates an immutable artifact in Elsa Platform. It does not release, promote, deploy, or make the workflow immediately executable. Direct runtime **Publish** remains direct-runtime terminology for non-integrated Studio installations or explicitly separated fallback behavior.

The platform is artifact-driven rather than workflow-only:

```text
producer integration -> artifact registry -> desired-state revision -> deployment run -> runtime/provider integration
```

The first-class product path is Elsa workflow artifacts, but the architecture is intentionally extensible. Workflow definitions, runtime configurations, container image references, Helm charts, or other application artifacts can all share the same control-plane envelope when they have an artifact type, metadata, digest, reference, target capability requirements, and a deployment adapter.

The active identity and tenancy work is documented in [specs/021-identity-tenancy/plan.md](specs/021-identity-tenancy/plan.md). The current execution plan for the artifact-driven deployment model is documented in [docs/platform-artifact-deployment-execution-plan.md](docs/platform-artifact-deployment-execution-plan.md).

## Technology

- C# on .NET 10 (`net10.0`)
- ASP.NET Core authentication, authorization, OpenAPI, cookies, and JWT bearer validation
- Entity Framework Core persistence with SQLite for local development and SQL Server for production-oriented publish
- .NET Aspire AppHost for local orchestration and Azure infrastructure defaults
- React 18, TypeScript, Vite, React Query, React Router, Vitest, and Playwright for the console
- xUnit and FluentAssertions across the .NET test suite

The SDK is pinned by [global.json](global.json) to .NET SDK `10.0.300` with latest-feature roll-forward.

## Getting Started

Restore and build the solution:

```bash
dotnet restore Elsa.Platform.sln
dotnet build Elsa.Platform.sln
```

Run the API directly:

```bash
dotnet run --project src/Elsa.Platform.Api
```

The API exposes `/health`, OpenAPI metadata, public catalog endpoints, workspace endpoints, and the admin console route under `/admin`. In development it uses SQLite by default with the connection string from `appsettings.Development.json`.

Local development uses `GenericOidc`-style JWT bearer validation for workspace APIs. Generate a local token that matches `appsettings.Development.json`:

```bash
chmod +x scripts/create-local-platform-jwt.sh
TOKEN="$(scripts/create-local-platform-jwt.sh)"
curl -H "Authorization: Bearer $TOKEN" http://localhost:5220/api/me/workspaces
```

The local token defaults to issuer `https://local.elsa-platform.test`, audience `elsa-platform-dev`, subject `user-123`, and a one-hour lifetime. Pass a different subject/name/email when you need another user:

```bash
TOKEN="$(scripts/create-local-platform-jwt.sh user-456 'Grace Hopper' grace@example.test)"
```

For a browser sign-in flow, start the local Keycloak realm, API, and console dev server in separate terminals:

```bash
docker compose -f docker-compose.identity.yml up
dotnet dev-certs https --trust
ASPNETCORE_ENVIRONMENT=Keycloak dotnet run --project src/Elsa.Platform.Api --launch-profile https
cd src/Elsa.Platform.Console && npm install && npm run dev
```

Then open the console and use a workspace-only view such as Runtime Builder:

```text
https://localhost:7094/admin/runtime-builder
```

When the view needs workspace identity it links to `/api/auth/login`, which starts the OIDC authorization-code flow against Keycloak and returns to the console with an HttpOnly customer session cookie. The imported local user is:

```text
username: ada
password: password
```

Local Keycloak authorities use `127.0.0.1` instead of `localhost` so browser cookies from the console/API host are not sent to Keycloak on a different port.

The local Keycloak admin console is available at `http://127.0.0.1:8080` with `admin` / `admin`.

Run the Aspire host:

```bash
dotnet run --project src/Elsa.Platform.AppHost
```

In local Aspire runs, the dashboard starts Keycloak, the API, and the Vite-based
console. The Aspire-managed Keycloak instance imports
`dev/keycloak/elsa-platform-realm.json`, listens on `https://127.0.0.1:8080`, and
uses `admin` / `admin` for the local admin console. Keycloak data is persisted in
the `elsa-platform-keycloak-data` Aspire volume, so locally registered users
survive AppHost restarts. The imported console user is `ada` / `password`. Local
self-registration is enabled in the imported realm, so the Keycloak sign-in page
shows a registration link. If the Keycloak data volume already exists, either
enable **User registration** in the Keycloak admin console under realm login
settings or remove the local Keycloak data volume so the realm import is applied
again. When built console assets are absent, Aspire injects the actual Vite
console endpoint into the API so `/admin/*` is proxied to the console while the
OIDC callback returns to the same browser origin. Production publish still
serves the built console assets from the API container under `/admin`.

Run the console during frontend development:

```bash
cd src/Elsa.Platform.Console
npm install
npm run dev
```

The console dev server proxies relative `/api` requests to `http://localhost:5220` by default. Override `CATALOG_API_PROXY_TARGET` in `src/Elsa.Platform.Console/.env` when the API is running elsewhere.

## Verification

Run the full .NET test suite:

```bash
dotnet test Elsa.Platform.sln
```

Run console checks:

```bash
cd src/Elsa.Platform.Console
npm test
npm run typecheck
npm run build
```

Run console end-to-end smoke tests:

```bash
cd tests/Elsa.Platform.Console.E2E
npm install
npm run e2e
```

## Manifest Workflow

Runtime package authors add the manifest generator as a private build dependency:

```xml
<PackageReference Include="Elsa.Platform.PackageManifest.Generator" Version="x.y.z" PrivateAssets="all" />
```

During build or pack, the generator discovers public CShells feature classes, extracts deploy-time settings, applies XML documentation and optional source-only hints, validates the manifest contract, and includes one root `elsa-package.json` in the produced NuGet package. It intentionally ignores application-code hooks and unsupported CLR-only configuration shapes so manifests stay deploy-time focused and safe to inspect.

See [src/Elsa.Platform.PackageManifest.Generator/README.md](src/Elsa.Platform.PackageManifest.Generator/README.md) and [src/Elsa.Platform.PackageManifests/README.md](src/Elsa.Platform.PackageManifests/README.md) for the package-authoring details.

## Runtime And Deployment Flow

The long-term platform flow is:

1. Runtime packages publish an `elsa-package.json` manifest.
2. Package Catalog synchronizes package sources, validates manifests, records approvals, and exposes compatible package/version data.
3. Runtime Builder creates saved runtime configurations and bundle artifacts from catalog selections.
4. Artifact producers submit immutable artifacts to Elsa Platform. The first producer integration is Elsa Studio's **Submit to Platform** command for workflow snapshots.
5. The artifact registry stores workspace-owned metadata, digests, inspection state, and payload references without storing raw workflow content, provider tokens, credentials, or secret values in catalog tables.
6. Desired-state revisions reference artifacts plus environment-specific configuration, secret references, observability bindings, and target bindings.
7. Promotion preview, validation, dry-run, deployment, rollback, and history operate on those platform-owned revisions.
8. Deployment runs produce durable deployment commands for target runtimes or providers.
9. Runtime/provider integrations consume commands, fetch or receive artifacts, verify digests and compatibility, apply supported artifact types, and report progress/results back to the deployment run.

Runtime communication is transport-independent. The preferred default for customer-hosted runtimes is outbound runtime pull/sync, because it avoids requiring inbound network access from Elsa Platform. Webhooks are optional advisory notifications that tell a runtime to fetch authoritative commands. Direct platform push is available only for environments that explicitly support inbound connectivity and trust configuration.

The legacy in-process deployment queue worker is disabled by default with `Deployment:QueueWorker:Enabled=false`. When enabled, it performs stale-run recovery only; runtime/provider integrations remain responsible for claiming commands, applying artifacts, and reporting results.

```text
Elsa Studio integration
  Submit to Platform
  -> artifact submission

Elsa Platform
  artifact registry
  desired-state revision
  deployment run
  deployment command

Runtime integration
  pull/sync or webhook-triggered fetch
  verify artifact digest and capability compatibility
  apply artifact through runtime-specific logic
  report status and safe diagnostics
```

Elsa Platform stays agnostic about artifact internals. It owns identity, permissions, environment targeting, capabilities, validation lifecycle, run history, idempotency, and audit. Artifact producers and consumers own domain-specific packaging and application behavior.

Several of these pieces are already implemented as contracts and services; others are represented by Spec Kit plans and roadmap affordances in the console until backend contracts are ready.

## Documentation

- [Package manifest contract](src/Elsa.Platform.PackageManifests/README.md)
- [Manifest generator](src/Elsa.Platform.PackageManifest.Generator/README.md)
- [Platform console](src/Elsa.Platform.Console/README.md)
- [Active identity and workspace tenancy plan](specs/021-identity-tenancy/plan.md)
- [Artifact-driven deployment execution plan](docs/platform-artifact-deployment-execution-plan.md)
- [Platform artifact workflow E2E smoke](docs/platform-artifact-workflow-e2e-smoke.md)
- [Spec Kit feature history](specs/)
- [Platform integration packaging and host configuration](docs/platform-integration-packaging.md)

Implementation work is tracked through Spec Kit under `specs/`. Start with the current plan for active branch context before making architectural changes.
