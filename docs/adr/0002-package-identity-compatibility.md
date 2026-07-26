# ADR-0002: Package Identity Compatibility During Valence Control Consolidation

Date: 2026-05-19

## Status

Accepted

## Context

The package catalog consolidation renames source projects and namespaces from the standalone catalog repository shape into `ValenceControl.*` platform packages. The affected public package candidates are `ValenceControl.PackageManifests` and `ValenceControl.PackageManifest.Generator`.

NuGet flat-container checks performed on 2026-05-19 returned 404 for both package IDs, so there is no known public nuget.org package identity conflict at the time of consolidation.

## Decision

Use the `ValenceControl.*` package identities as the target package names for platform packages.

Before publishing, re-check nuget.org, private feeds, and downstream repositories. If an old package identity is already consumed outside this migration, provide a compatibility package or explicit deprecation path for at least one release cycle.

## Consequences

- Source project names and namespaces can align with the platform architecture now.
- Publishing remains gated by a final package identity verification.
- Compatibility shims are deferred until there is evidence they are needed.
