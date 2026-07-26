# Implementation Plan: Server-Side Planning

**Branch**: `012-server-side-planning` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)

## Summary

Add a backend planner that turns builder intent into resolved runtime state and shared findings for plan, resolve, and bundle flows.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core.
**Primary Dependencies**: ASP.NET Core minimal APIs, existing catalog queries, compatibility service, runtime image catalog, infrastructure provider catalog, xUnit, FluentAssertions.
**Storage**: No new durable storage for first planner slice.
**Testing**: Core planner tests and API integration tests.
**Target platform**: Existing ASP.NET Core modular monolith.
**Project Type**: Web service with core planning service.
**Performance Goals**: Planner uses indexed catalog data only and returns representative plans under 1 second locally.
**Constraints**: No natural-language planning, no client-side authority, no live deployment.

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
src/ValenceControl.PackageCatalog.Core/Builder/Planner/
src/ValenceControl.Api/Public/Builder/
src/ValenceControl.Api/Workspace/
tests/ValenceControl.PackageCatalog.Core.Tests/
tests/ValenceControl.Api.Tests/
```

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
