# Commercial transition completion summary

## Completed changes

- Transferred the existing GitHub repository object from
  `elsa-workflows/elsa-platform` to `valence-works/valence-control`, preserving
  Git history and GitHub metadata supported by repository transfer.
- Renamed the product to Valence Control and product-owned .NET identifiers to
  the `ValenceControl` root.
- Replaced the repository's MIT licence with the Valence Control Commercial
  License.
- Added `NOTICE.md` and updated `LICENSING.md`, `legal/README.md`,
  `CONTRIBUTING.md`, and the README product boundary.
- Preserved upstream Elsa Workflows identities and third-party licence notices.

## Deliberate clean break

The codebase was unreleased, so product-owned package IDs, persisted
identifiers, wire contracts, routes, configuration keys, infrastructure names,
and deployment identifiers changed without compatibility aliases. Existing
development environments must be recreated or reconfigured.

Previously published revisions remain governed by the terms under which they
were made available. No history was rewritten and no release, package,
container, or deployment artefact was published during the migration.

## Legal blocker

The legal licensor could not be verified from authoritative repository,
organisation, or company documentation. The commercial licence therefore uses
`[LEGAL ENTITY NAME]`.

Qualified legal counsel must verify the licensor, replace the placeholder,
approve the licence, confirm relicensing authority for all contributions and
imported code, and review the dependency and third-party notice inventory before
commercial distribution.

## GitHub follow-up

Changing the repository from public to private detached the public fork network
and removed public star/watcher relationships where GitHub requires it. The
source repository's Copilot code-review ruleset became unavailable for the
private destination under the organisation's current GitHub plan. Environment
names transferred, but their reviewers, credentials, OIDC trust, and deployment
permissions require manual verification before use.
