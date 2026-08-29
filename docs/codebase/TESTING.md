# Testing Patterns

## 1) Test Stack and Commands

- Primary test framework: xUnit 2.9.3 for .NET; Vitest 3.2.6 for the console; Playwright for browser smoke tests.
- Assertion/mocking tools: xUnit built-in assertions, Testing Library, explicit injected fakes/recording stores.

```bash
dotnet test ElsaControl.sln
cd src/Hosting/ElsaControl.Console && npm ci && npm test && npm run typecheck && npm run build
cd tests/Hosting/ElsaControl.Console.E2E && npm ci && npm run e2e
```

## 2) Test Layout

- .NET test projects mirror `src/` subsystem boundaries under `tests/`.
- C# test classes/files use the `*Tests` suffix; React tests are co-located as `*.test.tsx`.
- API integration-style tests share `ControlApiTestApplication`; browser setup lives in the console E2E package.

## 3) Test Scope Matrix

| Scope | Covered? | Typical target | Notes |
|-------|----------|----------------|-------|
| Unit | yes | compatibility, manifests, artifacts, deployment services | injected stores and deterministic time/providers |
| Integration | yes | minimal APIs, EF stores and migrations | test host plus SQLite/SQL-provider-aware suites |
| E2E | partial | console smoke and documented artifact workflow | no Elsa Cloud signup-to-running-instance flow exists |

## 4) Mocking and Isolation Strategy

- Core tests inject fake/recording store ports; HTTP behaviors use fake handlers or the ASP.NET test host.
- EF tests create isolated database fixtures and verify persistence/migration semantics.
- Common failure mode: very large API/console/persistence surfaces make fixture setup and regression localization harder.

## 5) Coverage and Quality Signals

- Coverlet is referenced, but no minimum coverage threshold or published coverage gate was found. The commercial-platform decision is to gate relevant behavior explicitly instead of introducing an arbitrary percentage target.
- CI builds and runs the full solution plus a separate `Console quality gates`
  job. The console job installs from `package-lock.json`, runs unit tests,
  TypeScript typechecking, and produces the production bundle as independent
  required steps.
- Known gaps: real Azure workload provisioning, managed-instance walking skeleton, restore/DR, multi-region/stamp behavior, and browser coverage for SaaS onboarding.

## 6) Evidence

- `.github/workflows/ci.yml`
- `Directory.Packages.props`
- `tests/Hosting/ElsaControl.Api.Tests/ControlApiTestApplication.cs`
- `src/Hosting/ElsaControl.Console/package.json`
- `tests/Hosting/ElsaControl.Console.E2E/package.json`
