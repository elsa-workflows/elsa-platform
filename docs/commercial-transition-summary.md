# Commercial transition summary

## What was inspected

- Repository identity, remotes, default branch, current revision, visibility, local and remote tags, and releases.
- The current `LICENSE`, licensing references, copyright notices, package-manifest licensing metadata, README, contribution controls, and Git history.
- NuGet project and central package metadata, npm manifests and lockfile licence labels, package publication workflows, container build and deployment paths, Feedz/NuGet references, and external GitHub URLs.
- GitHub Actions workflows and checked-in repository governance files.
- Git history authors, package-catalog consolidation evidence, external Elsa repository references, and dependency review gaps.

## What changed on this branch

- Added `LICENSING.md` with non-final transition information.
- Added `legal/README.md` and the exact non-licence `legal/COMMERCIAL-LICENSE-PLACEHOLDER.md`.
- Added `CONTRIBUTING.md` with a temporary licensing-transition notice and no CLA, copyright assignment, or DCO.
- Added `docs/commercial-transition-audit.md`.
- Added `docs/repository-transfer-checklist.md`.
- Added `docs/relicensing-checklist.md`.
- Added this summary.
- Added a small `Licensing status` section to `README.md`; existing README changes in the working tree were preserved.
- Created the local branch `chore/commercial-license-transition`.

## What was intentionally not changed

- `LICENSE` remains the MIT License.
- No commercial or source-available licence text was drafted.
- No pricing, production-use restriction, commercial-use restriction, or commercial badge was added.
- No historical copyright or licence notice was removed.
- No Git history, tags, branches, releases, packages, GitHub settings, organization settings, repository transfer, push, or pull request was changed.
- No CLA, copyright assignment, or DCO was introduced.

## Current licence and final-MIT tag status

The current repository remains MIT-licensed under `LICENSE`. The current committed revision is `af6c054fc932fb0c3d5b4682b2f39f7e981c1e70`, authored by Sipke Schoorstra, with subject `feat(healing): govern repair pull requests`. The working tree was dirty before this task, so the revision is not marked as the final MIT revision. The local and remote `last-mit-licensed-revision` tag did not exist and was not created.

## Contributor findings

The local history contains 563 commits by Sipke Schoorstra and two commits by `copilot-swe-agent[bot]`. No explicit CLA, DCO, copyright assignment, `.mailmap`, CODEOWNERS, issue template, or pull-request template was found. Commit authorship is not treated as proof of ownership. The package-catalog import baseline and external Elsa packages require provenance review.

## Potential blockers and legal-review items

- Final licence selection, legal review, licensor identity, permitted-use policy, and effective revision are unresolved.
- Copyright and contribution authority for all code, imported/shared code, and bot-assisted changes is not documented.
- NuGet, transitive dependency, npm attribution, and container base-image review is incomplete.
- The destination GitHub organization and repository name are not confirmed.
- Current GitHub settings, secrets, Apps, webhooks, deploy keys, environments, branch protections, Pages, package permissions, and releases require manual inventory.
- The app-only Azure workflow references `src/Elsa.Platform.PackageCatalog.Api/Dockerfile`, while the checked-in Dockerfile is `src/Elsa.Platform.Api/Dockerfile`; this should be resolved before release automation is relied on.

## Recommended next action

Have the repository owner confirm a clean, intended final MIT revision and contributor/ownership records. Then obtain qualified legal review of the relicensing model and dependency inventory. Keep this branch local until those decisions are complete; do not push the transition tag or transfer the repository as part of this preparation.
