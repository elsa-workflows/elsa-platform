# ADR-0003: Deprecate Standalone Package Catalog Repository

Date: 2026-05-19

## Status

Accepted

## Context

Package Catalog is platform control-plane infrastructure and now lives in `elsa-platform` alongside Runtime Builder, package manifest contracts, package manifest generation, and deployment-facing package validation contracts.

The standalone `elsa-workflows/elsa-package-catalog` repository had no open issues and no open PRs when checked on 2026-05-19. Its README was updated on `main` in commit `cf7411d` to point active development to `elsa-platform`.

## Decision

Treat `elsa-platform` as the source of truth for Package Catalog development.

Keep the old repository available as a deprecated historical repository until the platform consolidation branch is merged and maintainers confirm no private/internal consumers still depend on direct updates there.

## Consequences

- New work should be opened against `elsa-platform`.
- The old repository can be archived after the merge and consumer confirmation gates pass.
- Historical commits remain accessible through the imported history and the old repository.
