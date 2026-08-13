# Implementation Plan: Deployment Template Expansion

**Branch**: `013-deployment-template-expansion` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)

## Summary

Extend the bundle generation service with target-specific renderers while keeping Docker Compose as the default target.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core.
**Primary Dependencies**: Existing bundle generation service, planner, runtime image metadata, System.Text.Json/YAML text rendering, xUnit and its built-in assertions.
**Storage**: No new durable storage.
**Testing**: Renderer snapshot tests and API integration tests.
**Target platform**: Existing ASP.NET Core modular monolith.
**Project Type**: Web service with pluggable target renderers.
**Performance Goals**: Target rendering remains local and deterministic.
**Constraints**: No live deployment, no cloud credentials, no stored generated artifacts.

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
src/ValenceControl.PackageCatalog.Core/Builder/Renderers/
src/ValenceControl.PackageCatalog.Core/DeploymentTemplates/
tests/ValenceControl.PackageCatalog.Core.Tests/
tests/ValenceControl.Api.Tests/
```

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
