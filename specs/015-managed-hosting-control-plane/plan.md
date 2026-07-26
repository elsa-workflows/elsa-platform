# Implementation Plan: Managed Hosting Control Plane

**Branch**: `015-managed-hosting-control-plane` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)

## Summary

Introduce a narrow managed hosting control plane for provisioning, lifecycle, URL, and health status of one supported Elsa runtime shape.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core/Persistence.
**Primary Dependencies**: ASP.NET Core minimal APIs, EF Core, deployment adapter ports, health polling, background jobs, xUnit, FluentAssertions.
**Storage**: Managed environment, instance, resource, and event records in existing relational database.
**Testing**: Core service tests, API tests, adapter fakes.
**Target platform**: Existing ASP.NET Core modular monolith.
**Constraints**: One region/provider/shape first; no billing/custom domains/SLA.

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
- **Simplicity**: Pass; narrow hosted shape only.

## Project Structure

```text
src/ValenceControl.PackageCatalog.Core/ManagedHosting/
src/ValenceControl.Api/Workspace/WorkspaceManagedHostingEndpoints.cs
src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/
tests/ValenceControl.PackageCatalog.Core.Tests/
tests/ValenceControl.Api.Tests/
```

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Hosted infrastructure adapter | Required to provision managed environments | BYOC preview/export does not operate hosted runtimes |
