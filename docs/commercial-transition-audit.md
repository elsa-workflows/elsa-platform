# Commercial transition audit

Audit date: 2026-07-21

This is a factual repository audit prepared for a possible future transition from the current MIT licence to a source-available commercial model. It is not a legal opinion and does not determine copyright ownership, licence compatibility, or whether relicensing is legally available.

The audit was performed against the local repository before this transition work was committed, at checkout commit `af6c054fc932fb0c3d5b4682b2f39f7e981c1e70`. The working tree already contained substantial uncommitted Platform Self-Healing changes before this audit. Those changes are intentionally not treated as a clean release revision.

## Repository identity

| Item | Finding |
| --- | --- |
| Repository name | `elsa-platform` |
| Current remote | `https://github.com/elsa-workflows/elsa-platform.git` |
| Additional remote | `package-catalog` → `https://github.com/elsa-workflows/elsa-package-catalog.git` |
| Default branch | `main` |
| Local working branch | `chore/commercial-license-transition` |
| Default branch revision at audit time | `210d8fa84cb85654ddd29c392bd1f685d3d564f6` (`origin/main`) |
| Audit checkout revision before transition commits | `af6c054fc932fb0c3d5b4682b2f39f7e981c1e70` |
| Transition branch tip after documentation commits | `67708b43fa6e757288b4b1bdfc7084d4b0887505` |
| Current licence | MIT License, from `LICENSE` |
| Repository visibility | Public |
| Local tags | None |
| Remote tags observed | None from `git ls-remote --tags origin` |
| Releases observed | No releases were returned by the read-only GitHub CLI query |

The GitHub repository metadata identifies `main` as the default branch and the repository as public. No repository transfer was performed.

## Licensing inventory

| Path or source | Relevant text or metadata | Required future action |
| --- | --- | --- |
| `LICENSE` | Full MIT License text; copyright line is `Copyright (c) 2021 Elsa Workflows` | Keep authoritative until a legally reviewed transition is approved. Preserve for historical MIT revisions. |
| `README.md` | Now states that the repository is currently MIT-licensed and that a future source-available commercial model is being prepared | Keep wording factual; update only after the final model and effective revision are approved. |
| `LICENSING.md` | States that current and previously published revisions remain under MIT and that future terms are not published | Replace or extend only after legal review; it does not modify `LICENSE`. |
| `legal/COMMERCIAL-LICENSE-PLACEHOLDER.md` | Explicitly says that no commercial licence text is approved and that the file is not a licence | Replace only with an approved document. Do not publish clauses in the placeholder. |
| `legal/README.md` | Lists possible future legal documents, including licence, commercial, evaluation, production, contributor, and trademark documents | Populate only with reviewed documents. |
| `src/Elsa.Platform.PackageManifests/Licensing/LicenseManifest.cs` | Defines package-manifest fields named `Expression`, `Url`, and `RequiresAcceptance` | Confirm the contract’s intended meaning and package metadata policy before publishing packages under new terms. This is a data contract, not the repository licence. |
| `src/Elsa.Platform.PackageManifest.Generator.MSBuild/GenerateElsaPackageManifestTask.cs`, `src/Elsa.Platform.PackageManifest.Generator.Core/Generation/*`, and `src/Elsa.Platform.PackageManifest.Generator.Core/Overrides/*` | Reads or overrides `PackageLicenseExpression` and package-manifest licence metadata | Review generated package metadata and update its source/repository URL and licence behavior when the future model is approved. |
| `tests/Elsa.Platform.PackageManifest.Generator.MSBuild.Tests/MsBuildGeneratorOptionsMapperTests.cs` and `tests/Elsa.Platform.PackageManifest.Generator.Core.Tests/ProjectPackageMetadataTests.cs` | Test fixtures use the string `MIT` as package metadata | Keep as historical/contract test data until package metadata policy changes; do not interpret this as a repository-wide licence declaration. |
| `src/Elsa.Platform.Console/package-lock.json` | Dependency metadata contains 284 `MIT`, 14 `ISC`, 7 `Apache-2.0`, 3 `BSD-2-Clause`, 3 `BSD-3-Clause`, 1 `CC-BY-4.0`, 1 `MIT-0`, and 1 `Unlicense` label | Preserve required third-party notices and perform a complete dependency licence review before commercial distribution. |
| `tests/Elsa.Platform.Console.E2E/package-lock.json` | Dependency metadata contains 3 `Apache-2.0` and 1 `MIT` labels | Include in the dependency review if test or release artifacts redistribute these dependencies. |
| NuGet project metadata | No `PackageLicenseExpression`, `PackageLicenseUrl`, SPDX header, or package-level copyright metadata was found in the checked-in project files | Decide and implement package metadata only after the future licence is approved. |

The terms “licence” and “licensing” also occur as package-manifest metadata or runtime entitlement concepts in the files under `specs/001-package-catalog/`, `specs/001-platform-package-catalog-consolidation/`, `specs/002-package-manifest-generator/`, and `specs/010-runtime-image-metadata-api/`, as well as the corresponding API, generator, manifest, and console model files. These references describe product data or planned validation behavior; they should not be confused with a statement that the repository licence has changed.

## Copyright and contribution inventory

### Git history

The local history contains 565 commits from two distinct author identities:

| Author identity | Commit count | Observation |
| --- | ---: | --- |
| Sipke Schoorstra `<sipkeschoorstra@outlook.com>` | 563 | Principal recorded author in the repository history. |
| `copilot-swe-agent[bot]` `<198982749+Copilot@users.noreply.github.com>` | 2 | Two commits address Keycloak configuration/authentication fixes. |

Commit authorship is evidence about repository history only. It does not establish copyright ownership, employment ownership, assignment, or the right to relicense.

### Explicit notices

`LICENSE` is the only checked-in file containing an explicit copyright or licence notice. No source-file SPDX headers, `NOTICE`, `COPYING`, or additional copyright-header files were found.

### External or shared-code evidence

- Commit `cf7fd1d522fdc6ce37ceb88efdcf24453f8705ce` is titled `Import package catalog baseline`, and the repository retains a `package-catalog` remote pointing to `elsa-workflows/elsa-package-catalog`.
- The repository consumes `Elsa.Diagnostics.OpenTelemetry` and `Elsa.Diagnostics.OpenTelemetry.Core` packages from the Elsa ecosystem.
- Documentation references `elsa-workflows/elsa-core`, `elsa-workflows/elsa-package-catalog`, `valence-works/cshells`, and other external repositories.
- These findings indicate code, specifications, packages, or history that require provenance review. They do not establish that any code was copied unlawfully or that any party owns it.

### Contributor controls

Before this branch, no `CONTRIBUTING.md`, CLA, copyright-assignment document, DCO configuration, `.mailmap`, `CODEOWNERS`, pull-request template, issue template, or bot configuration for automatically merging external contributions was found. This branch adds a temporary `CONTRIBUTING.md` notice but does not introduce a CLA, assignment, or DCO.

Human review is required for:

- employment, contractor, customer, and company IP-assignment records;
- the two bot-authored commits and the human operators or organizations behind them;
- the package-catalog consolidation and any shared Elsa source;
- external package and template provenance;
- any contributions that entered through branches or pull requests but are not represented by a separate author identity in the local history.

## Package and artifact inventory

### NuGet

The checked-in publication workflow (`.github/workflows/packages.yml`) explicitly packs:

- `Elsa.Platform.PackageManifests` (the project has no explicit `PackageId` in its project file); and
- `Elsa.Platform.PackageManifest.Generator`.

The current source tree also contains four explicitly packable projects: `Elsa.Platform.PackageManifest.Generator`, `Elsa.Platform.Healing.Client`, `Elsa.Platform.Healing.ComponentManifest`, and `Elsa.Platform.Healing.ComponentManifest.Generator.MSBuild`. The Healing projects are not included in the current publication workflow’s explicit pack list. `Elsa.Platform.PackageManifests` is packed by the workflow despite having no explicit package ID or package licence metadata. The workflow publishes preview packages to Feedz.io and release packages to NuGet.org, using `FEEDZ_API_KEY` and `NUGET_API_KEY` secrets.

The workflow has no licence validation gate, SBOM generation, third-party notice generation, package signing, or provenance attestation step. The root `LICENSE` is not declared as package content in the checked-in packable project files, so package inclusion of the repository notice requires verification.

No GitHub Packages publication configuration was found. `NuGet.config` references `elsa-workflows/elsa-3`, `elsa-workflows/elsa-4`, and a `valence-works/consolelogstream` Feedz source; ownership and continued authorization for each feed require confirmation.

### npm

`src/Elsa.Platform.Console/package.json` and `tests/Elsa.Platform.Console.E2E/package.json` are both marked `private: true` or otherwise configured as private development packages. The checked-in lockfiles point to the public npm registry. No npm publication workflow was found.

### Containers

- `src/Elsa.Platform.Api/Dockerfile` builds the React console and the .NET API.
- Base images are `node:22-alpine`, `mcr.microsoft.com/dotnet/sdk:10.0.300`, and `mcr.microsoft.com/dotnet/aspnet:10.0`.
- The base-image references use mutable tags rather than digests.
- The Dockerfile has no OCI licence/source/revision labels and does not explicitly copy `LICENSE` or third-party notices into the final image.
- Azure deployment pushes `${AZURE_CONTAINER_REGISTRY_ENDPOINT}/elsa-platform/api:<commit>` to an Azure Container Registry.
- The infrastructure script uses `src/Elsa.Platform.Api/Dockerfile`.
- The app-only path in `.github/workflows/azure-api-deploy.yml` currently names `src/Elsa.Platform.PackageCatalog.Api/Dockerfile`, which does not match the checked-in Dockerfile path and should be corrected or confirmed before a release transition.
- The same workflow tests `tests/Elsa.Platform.PackageCatalog.Api.Tests/Elsa.Platform.PackageCatalog.Api.Tests.csproj`, which is also absent from the checked-in tree.
- No Docker image licence labels were found.

### Other artifacts

No Helm chart or Kubernetes manifest was found. No GitHub Packages reference, release artifact manifest, or Pages deployment configuration was found. The repository has no local or remote Git tags in the inspected refs.

## Repository-transfer impact

The current repository is `elsa-workflows/elsa-platform`. A future destination organization and repository name are not yet confirmed and must remain placeholders until approved.

### References requiring review

- `docs/platform-integration-packaging.md`, `docs/runtime-transport-trust-policy.md`, `docs/platform-artifact-workflow-e2e-smoke.md`, `src/Elsa.Platform.Healing.Client/README.md`, and multiple specification documents contain `github.com/elsa-workflows/...` links.
- `docs/deployment-platform-phased-strategy.md` and related ADRs link to other Elsa repositories and should be reviewed separately from the Platform transfer.
- `NuGet.config` and `.github/workflows/packages.yml` refer to Feedz organizations and feeds; package ownership, tokens, and publication destinations must be confirmed.
- The workflow publishes to the Feedz `elsa-3` feed while `NuGet.config` also references an `elsa-4` preview feed; the intended feed and transition ownership need confirmation.
- Azure deployment uses GitHub Actions environments, repository variables, OIDC identity, Azure resource names, registry permissions, and secrets. These are external configuration and cannot be transferred by editing this repository.
- The container image path includes the stable `elsa-platform/api` name inside a destination registry and needs an ownership decision.

### GitHub settings and integrations requiring manual verification

The checked-in files do not expose branch protection, required checks, repository secrets, environment secrets, GitHub Apps, webhooks, deploy keys, Pages settings, billing, installed integrations, or organization-level policies. These must be recorded before transfer and verified afterward. No remote settings were modified.

There is no `CODEOWNERS` file, issue template, pull-request template, or Pages configuration in the repository. Their absence is a governance gap to resolve separately; this branch does not disable or create remote settings.

## Dependency licence review

### Direct .NET dependencies detected locally

Project files reference the following direct packages; versions are centrally declared in `Directory.Packages.props` unless a project or SDK supplies them:

```text
Aspire.Hosting.Azure.AppService       Aspire.Hosting.Azure.Sql
Aspire.Hosting.JavaScript             Aspire.Hosting.Keycloak
ConsoleLogStreaming.AspNetCore       Elsa.Diagnostics.OpenTelemetry
Elsa.Diagnostics.OpenTelemetry.Core  FluentAssertions
GitHub.Copilot.SDK                    JetBrains.Annotations
JsonSchema.Net                        MessagePack
Microsoft.AspNetCore.Authentication.JwtBearer
Microsoft.AspNetCore.Authentication.OpenIdConnect
Microsoft.AspNetCore.Mvc.Testing      Microsoft.AspNetCore.OpenApi
Microsoft.Build.Framework             Microsoft.Build.Utilities.Core
Microsoft.EntityFrameworkCore.Design Microsoft.EntityFrameworkCore.SqlServer
Microsoft.EntityFrameworkCore.Sqlite  Microsoft.EntityFrameworkCore.Tools
Microsoft.Extensions.Caching.Memory   Microsoft.Extensions.Configuration.Binder
Microsoft.Extensions.Http.Resilience  Microsoft.Extensions.Logging.Abstractions
Microsoft.Extensions.Options           Microsoft.Extensions.ServiceDiscovery
Microsoft.IdentityModel.Protocols.OpenIdConnect
Microsoft.IdentityModel.Tokens        Microsoft.NET.Test.Sdk
Microsoft.OpenApi                      NuGet.Protocol
NuGet.Versioning                       Octokit
OpenTelemetry.Exporter.OpenTelemetryProtocol
OpenTelemetry.Extensions.Hosting      OpenTelemetry.Instrumentation.AspNetCore
OpenTelemetry.Instrumentation.Http    OpenTelemetry.Instrumentation.Runtime
SQLitePCLRaw.bundle_e_sqlite3          System.IdentityModel.Tokens.Jwt
System.Reflection.MetadataLoadContext YamlDotNet
coverlet.collector                     xunit
xunit.runner.visualstudio
```

No licence expressions for these NuGet dependencies are declared locally. This audit therefore does not claim that they are compatible with a future commercial distribution. A lockfile/SBOM scan and review of package notices and transitive dependencies are required.

### npm dependencies detected locally

The two npm lockfiles expose licence labels locally. The labels observed are MIT, ISC, Apache-2.0, BSD-2-Clause, BSD-3-Clause, CC-BY-4.0, MIT-0, and Unlicense. No GPL, LGPL, AGPL, MPL, or SSPL label was found in the checked-in lockfile metadata, but absence from metadata is not legal clearance. The `CC-BY-4.0` entry requires attribution review, and all notices should be preserved as applicable.

### Review flags

- Copyleft: none detected from the local npm licence labels; NuGet and transitive dependency status remains unverified.
- Source-available or proprietary dependencies: `ConsoleLogStreaming.AspNetCore`, Elsa diagnostics packages, and `GitHub.Copilot.SDK` require provenance and distribution-term review; this audit makes no classification conclusion.
- Unknown or incomplete metadata: NuGet package licences, Docker base-image terms, and dependencies embedded in produced packages are not fully represented in the repository.
- Attribution: npm lockfile labels and any third-party notices in NuGet or container layers require an attribution inventory.
- No checked-in SBOM, generated dependency licence report, or third-party notices file was found.

## Risk summary

### Clear as a repository fact

- `LICENSE` remains the MIT License.
- The repository is public and remains under `elsa-workflows/elsa-platform`.
- No remote transfer, push, force-push, history rewrite, tag push, or organization-setting change was performed.
- The new documents expressly say that the future model is planned and not yet effective.

### Needs confirmation

- Whether the current committed revision is the intended final MIT revision; the working tree is dirty and includes unrelated-to-this-audit implementation changes.
- The final company-controlled organization and repository name.
- Current GitHub releases, settings, secrets, environments, branch protection, webhooks, Apps, deploy keys, Pages, and package permissions.
- Ownership and permitted use of the package-catalog baseline, external Elsa packages, bot-authored commits, and any unrecorded contributions.
- The incorrect Dockerfile path in the app-only Azure workflow.

### Needs legal review

- Whether Skywalker Digital B.V., trading as Valence Works, is the correct legal licensor and how the trade name should appear.
- Copyright ownership and authority to relicense all contributions and imported/shared code.
- The future source-available/commercial model, definitions of permitted use, redistribution, hosted service, modification, affiliates, contractors, and termination.
- Third-party dependency and base-image terms, attribution, and commercial distribution obligations.
- Future contributor terms and treatment of new external pull requests.

### Blocks relicensing

- No final licence has been selected or legally approved.
- Copyright and contribution authority has not been documented for all code and imported material.
- Third-party dependency and artifact distribution review is incomplete.
- The effective revision and transition release have not been approved.

## Final-MIT revision marker

The tag `last-mit-licensed-revision` did not exist locally or remotely when inspected. It was intentionally not created because the working tree had uncommitted changes and there is uncertainty about whether the default-branch commit `210d8fa84cb85654ddd29c392bd1f685d3d564f6` should be the final MIT revision. The self-healing checkout commit `af6c054fc932fb0c3d5b4682b2f39f7e981c1e70` is not the default-branch candidate.

After human confirmation on a clean, approved revision, the annotated tag can be created locally with:

```bash
git tag -a last-mit-licensed-revision 210d8fa84cb85654ddd29c392bd1f685d3d564f6 -m "Final repository revision intended to remain published as the last MIT-licensed revision before the planned commercial licensing transition. Previously published versions remain available under their original terms."
git show --format=fuller --stat last-mit-licensed-revision
```

Do not push the tag until separately approved.
