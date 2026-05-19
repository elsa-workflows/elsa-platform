# Implementation Plan: BYOC Deployment Targets

**Branch**: `014-byoc-deployment-targets` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)

## Summary

Add workspace-owned deployment targets, preview runs, and deployment run tracking for one initial Azure Container Apps BYOC target.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core/Persistence.
**Primary Dependencies**: ASP.NET Core minimal APIs, workspace identity, EF Core, secure secret storage abstraction, Azure SDK or CLI adapter behind a port, xUnit, FluentAssertions.
**Storage**: Existing relational catalog database extended with deployment target/run tables; encrypted secret reference storage required.
**Testing**: Core service tests, API tests, persistence tests, adapter fakes.
**Target Platform**: Existing ASP.NET Core modular monolith.
**Project Type**: Workspace API and background-capable deployment orchestration.
**Performance Goals**: Preview returns quickly from generated plan; live deployment is asynchronous or long-running tracked status.
**Constraints**: One provider first, least privilege, audit logging, no managed hosting.

## Constitution Check

- **Manifest-first**: Pass.
- **No arbitrary code execution**: Pass.
- **Stable contracts**: Pass.
- **Schema evolution**: Pass.
- **Immutable versions**: Pass.
- **Approval separation**: Pass.
- **Explicit sources**: Pass.
- **Safe public API**: Pass.
- **Debuggability**: Pass.
- **Modular monolith**: Pass.
- **Runtime Builder readiness**: Pass.
- **Simplicity**: Pass; one provider only.

## Project Structure

```text
src/Elsa.Platform.PackageCatalog.Core/DeploymentTargets/
src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceDeploymentTargetEndpoints.cs
src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/
tests/Elsa.Platform.PackageCatalog.Core.Tests/
tests/Elsa.Platform.PackageCatalog.Api.Tests/
```

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Cloud adapter | Required for BYOC deployment | Templates alone do not deploy |
