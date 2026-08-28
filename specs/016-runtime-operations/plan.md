# Implementation Plan: Runtime Operations

**Branch**: `016-runtime-operations` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)

## Summary

Add operational visibility and lifecycle safety for managed runtimes after managed hosting exists.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core/Persistence.
**Primary Dependencies**: ASP.NET Core minimal APIs, managed hosting records, log/metric adapter ports, backup adapter ports, EF Core, xUnit and its built-in assertions.
**Storage**: Operational event, backup, and upgrade records in existing relational database.
**Testing**: Core service tests, API tests, adapter fakes.
**Target platform**: Existing ASP.NET Core modular monolith.
**Constraints**: Requires managed hosting; first slice is operational control, not full SLA automation.

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
- **Simplicity**: Pass.

## Project Structure

```text
src/ElsaControl.PackageCatalog.Core/RuntimeOperations/
src/ElsaControl.Api/Workspace/WorkspaceRuntimeOperationsEndpoints.cs
src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/
tests/ElsaControl.PackageCatalog.Core.Tests/
tests/ElsaControl.Api.Tests/
```

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Operational adapters | Required for logs, metrics, backups, upgrades | Managed hosting status alone cannot operate runtimes |
