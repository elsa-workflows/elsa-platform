<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan:
specs/024-artifact-registry/plan.md
<!-- SPECKIT END -->

## Active Technologies
- C# on .NET 10. + ASP.NET Core authentication/authorization, ASP.NET Core cookies, JWT bearer validation, EF Core, existing `Elsa.Platform.PackageCatalog.*` account/workspace services, xUnit and FluentAssertions for tests. (codex/021-identity-tenancy)
- Existing catalog EF Core stores and migrations for accounts, external identities, workspaces, memberships, entitlements, and workspace-owned resources. (codex/021-identity-tenancy)
- C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console. + ASP.NET Core minimal APIs, existing workspace identity/authorization, EF Core catalog persistence, `Elsa.Platform.Deployment.Abstractions`, `Elsa.Platform.Deployment.Engine`, React Router, TanStack Query, Vitest, Playwright where needed, xUnit, and FluentAssertions. (022-deployment-ux)
- Existing catalog relational database through `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Secret values remain outside the database; deployment records store provider-backed references only. (022-deployment-ux)
- C# on .NET 10 for API/Core/Persistence/worker; TypeScript/React for the hosted console. + ASP.NET Core minimal APIs, ASP.NET Core hosted services for the first in-process queue worker, existing workspace identity, new workspace permission grants, EF Core catalog persistence, `Elsa.Platform.Deployment.Abstractions`, `Elsa.Platform.Deployment.Engine`, React Router, TanStack Query, Vitest, Playwright where needed, xUnit, and FluentAssertions. (022-deployment-ux)
- Existing catalog relational database through `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Deployment tables store provider-backed secret references only, structured desired-state records, permission grants, confirmation metadata, queued run state, and append-only history. (022-deployment-ux)
- C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console. + ASP.NET Core minimal APIs, existing workspace identity/authorization and deployment permission grants, EF Core catalog persistence, `Elsa.Platform.Deployment.Core` workspace services, React Router, TanStack Query, Vitest, Playwright where needed, xUnit, and FluentAssertions. (023-engine-health-verification)
- Existing catalog relational database through `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Engine records gain verification metadata; optional append-only verification event records may be added if needed for audit/debugging. (023-engine-health-verification)

## Recent Changes
- codex/021-identity-tenancy: Added C# on .NET 10. + ASP.NET Core authentication/authorization, ASP.NET Core cookies, JWT bearer validation, EF Core, existing `Elsa.Platform.PackageCatalog.*` account/workspace services, xUnit and FluentAssertions for tests.
