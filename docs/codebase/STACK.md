# Technology Stack

## 1) Runtime Summary

| Area | Value | Evidence |
|------|-------|----------|
| Primary language | C# with a TypeScript/React console | `global.json`; `src/Hosting/ElsaControl.Console/package.json` |
| Runtime + version | .NET SDK 10.0.300 / `net10.0`; React 18.3 | `global.json`; `Directory.Build.props`; console `package.json` |
| Package manager | NuGet central package management; npm lockfile | `Directory.Packages.props`; `src/Hosting/ElsaControl.Console/package-lock.json` |
| Module/build system | MSBuild solution plus Vite 6 | `ElsaControl.sln`; console `package.json` |

## 2) Production Frameworks and Dependencies

| Dependency | Version | Role in system | Evidence |
|------------|---------|----------------|----------|
| ASP.NET Core | 10.0 target | Minimal APIs, auth, OpenAPI, hosted services | `src/Hosting/ElsaControl.Api/Program.cs` |
| Entity Framework Core | 10.0.10 | SQLite and SQL Server catalog/control persistence | `Directory.Packages.props` |
| NuGet.Protocol | 7.6.0 | Package-source access and metadata ingestion | `Directory.Packages.props` |
| .NET Aspire | 13.4.6 | Local orchestration and current Azure App Service publish model | `src/Hosting/ElsaControl.AppHost/AppHost.cs` |
| React / React Router / TanStack Query | 18.3.1 / 7.18.2 / 5.90.x | Hosted operational console | `src/Hosting/ElsaControl.Console/package.json` |
| OpenTelemetry | 1.15.x | Service telemetry defaults | `Directory.Packages.props`; `src/Hosting/ElsaControl.ServiceDefaults/Extensions.cs` |

## 3) Development Toolchain

| Tool | Purpose | Evidence |
|------|---------|----------|
| xUnit 2.9.3 | .NET unit and integration-style tests | `Directory.Packages.props`; `tests/` |
| Vitest 3.2.6 + Testing Library | Console component tests | console `package.json` |
| Playwright | Browser smoke tests | `tests/Hosting/ElsaControl.Console.E2E/package.json` |
| GitHub Actions | Restore, build, and test on pushes/PRs | `.github/workflows/ci.yml` |

## 4) Key Commands

```bash
dotnet restore ElsaControl.sln
dotnet build ElsaControl.sln
dotnet test ElsaControl.sln
cd src/Hosting/ElsaControl.Console && npm ci && npm test && npm run typecheck && npm run build
```

## 5) Environment and Config

- Configuration comes from `appsettings*.json`, ASP.NET Core environment-variable overrides, Aspire parameters, and Azure deployment parameters.
- Production requires a catalog connection string, stable data-protection key path, OIDC/Entra settings, and admin/builder credentials. Exact secret values are deployment-owned and must not be committed.
- Local development defaults to SQLite and Keycloak; current production publish targets Azure App Service and Azure SQL, not an Elsa-workload Azure deployment provider.

## 6) Evidence

- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `src/Hosting/ElsaControl.Api/Program.cs`
- `src/Hosting/ElsaControl.AppHost/AppHost.cs`
- `src/Hosting/ElsaControl.Console/package.json`
