# Coding Conventions

## 1) Naming Rules

| Item | Rule | Example | Evidence |
|------|------|---------|----------|
| Files | PascalCase for C# and React components; camelCase for TS modules | `WorkspaceDeploymentService.cs`, `deploymentApi.ts` | representative source trees |
| Functions/methods | PascalCase in C#; camelCase in TypeScript | `CreateRunAsync`, `getDeploymentCredentialReferences` | core/API files |
| Types/interfaces | PascalCase; C# interfaces start with `I` | `IWorkspaceDeploymentStore` | deployment core |
| Constants/env vars | PascalCase constants; hierarchical config maps with `__` in environment variables | `CustomerAuthenticationDefaults`, `Database__Provider` | API/AppHost |

## 2) Formatting and Linting

- Formatter: no repository-wide formatter configuration was found.
- Linter: no ESLint configuration was found; TypeScript compiler checks and .NET analyzers are the present static checks.
- Relevant enforced settings: nullable C# enabled, implicit usings enabled, deterministic builds, TypeScript project references/strict app config.
- Run commands: `dotnet build ElsaControl.sln`; `npm run typecheck`; `npm run build`.

## 3) Import and Module Conventions

- C# groups framework and `ElsaControl.*` usings at file scope; DI registration is centralized in host composition.
- Console feature code uses the `@/` alias for cross-feature/root imports and relative imports locally.
- Public contracts live in `*.Abstractions`; hosting and persistence implementations are not re-exported as domain contracts.

## 4) Error and Logging Conventions

- API binding errors flow through ASP.NET Problem Details and `BadRequestExceptionHandler`; domain services return typed outcomes/diagnostics where deployment state must be persisted.
- Logging uses `ILogger<T>` and structured templates; external/runtime reports are restricted to safe metadata and diagnostics.
- Secret values must not be returned by GET APIs or placed in logs/history; local credential values use ASP.NET Data Protection and require a persistent production key ring.

## 5) Testing Conventions

- C# tests use `*Tests.cs` in subsystem-mirrored test projects and xUnit built-in assertions.
- Test doubles are commonly explicit in-memory/recording store implementations injected through ports; API tests use a shared test application fixture.
- Console tests are co-located as `*.test.tsx`; browser tests live in `tests/Hosting/ElsaControl.Console.E2E`.
- Coverage threshold: no enforced minimum was found; frontend acceptance uses explicit install, test, typecheck, build and critical-flow browser gates rather than an arbitrary percentage target.

## 6) Evidence

- `Directory.Build.props`
- `src/Hosting/ElsaControl.Console/tsconfig.app.json`
- `src/Hosting/ElsaControl.Api/Program.cs`
- `tests/Hosting/ElsaControl.Api.Tests/ControlApiTestApplication.cs`
- `tests/Deployment/ElsaControl.Deployment.Core.Tests/WorkspaceDeploymentServiceTests.cs`
