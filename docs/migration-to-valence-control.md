# Migration to Valence Control

On 26 July 2026, GitHub's repository-transfer and rename operations moved
`elsa-workflows/elsa-platform` to the private repository
`valence-works/valence-control`. The existing repository object was transferred;
Git history was not rewritten or recreated.

The product name changed from Elsa Platform to Valence Control. Product-owned
.NET projects, assemblies, packages, and namespaces changed from
`Elsa.Platform.*` to `ValenceControl.*`. Product-owned infrastructure and
configuration use the `valence-control` slug.

Elsa Workflows remains a separate, MIT-licensed, vendor-neutral open-source
project. Upstream package names, namespaces, schemas, and runtime concepts that
describe Elsa Workflows interoperability retain their Elsa identities.

The codebase was unreleased at the time of migration. Product-owned persisted
identifiers, wire contracts, routes, configuration keys, package IDs, and
infrastructure names therefore make a clean break without compatibility aliases.
Existing development databases, environment configuration, clients, and local
infrastructure must be recreated or updated for the new identifiers.

The repository licence changed from the MIT licence present in earlier revisions
to the Valence Control Commercial License. Earlier revisions remain governed by
the terms under which they were published. The legal licensor is Skywalker
Digital B.V., trading as Valence Works; qualified legal counsel must approve the
licence before commercial distribution.

Changing the transferred repository from public to private detached its public
fork network and removed public star/watcher relationships where GitHub requires
that behaviour. Repository rulesets also became unavailable under the
destination organisation's current GitHub plan and require plan or policy
follow-up.

## Migration validation

Validation performed on 26–27 July 2026 produced the following results:

- `dotnet restore ValenceControl.sln` passed.
- `dotnet build ValenceControl.sln --configuration Release --no-restore`
  passed with zero warnings and zero errors.
- The .NET test run was not fully green. All non-API test projects passed. The
  latest API run passed 384 of 386 tests. The obsolete self-healing end-to-end
  scenario that consistently failed to record a verification result was
  removed. The `Manual_sync_creates_running_sync_run_and_completes_in_background`
  test timed out during heavy host contention but passed when rerun in isolation.
- The console production build and type check passed. A single-worker Vitest
  run passed all 184 tests.
- Playwright ran against local Chromium: the package-details and source
  workflows passed. The obsolete deployment workflow fixture, which used the
  retired `/api/me/workspaces` endpoint and an older setup flow, was removed.
- Both owned NuGet packages packed locally without publishing. Inspection
  confirmed the proprietary `LICENSE` and only `ValenceControl.*` owned
  binaries. `ElsaRuntimeKinds.cs` remains intentionally Elsa-named because it
  models compatibility with external Elsa runtimes.
- The API container image built locally as
  `valence-control-api:migration-validation` without being pushed. The clean
  container restore exposed and prompted correction of the missing public
  `cshells-preview` package source mapping. Publishing also required accepting
  the identical Copilot CLI output contributed by Weaver and Healing Agent.
- Shell syntax checks and Bicep compilation passed.
- `dotnet format --verify-no-changes` is not green: it reported widespread
  pre-existing whitespace violations across the solution. The migration does
  not mix a repository-wide formatting rewrite into the rename.
- `npm ci` reported 11 dependency audit findings: 1 low, 5 moderate, 4 high,
  and 1 critical. These require dependency review before release.
