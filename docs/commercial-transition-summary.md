# Commercial transition completion summary

## Completed changes

- Transferred the existing GitHub repository object from
  `elsa-workflows/elsa-platform` to `valence-works/elsa-control`, preserving
  Git history and GitHub metadata supported by repository transfer.
- Renamed the product to Elsa Control and product-owned .NET identifiers to
  the `ElsaControl` root.
- Replaced the repository's MIT licence with the Elsa Control Commercial
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

## Legal review

The legal licensor is Skywalker Digital B.V., trading as Valence Works.

Qualified legal counsel must approve the licence, confirm relicensing authority
for all contributions and imported code, and review the dependency and
third-party notice inventory before commercial distribution.

## GitHub follow-up

Changing the repository from public to private detached the public fork network
and removed public star/watcher relationships where GitHub requires it. The
source repository's Copilot code-review ruleset became unavailable for the
private destination under the organisation's current GitHub plan. Environment
names transferred, but their reviewers, credentials, OIDC trust, and deployment
permissions require manual verification before use.
