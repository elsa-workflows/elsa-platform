# Third-party licence review

Last reviewed: 2026-07-27

The generated `THIRD-PARTY-INVENTORY.md` records 307 resolved NuGet
package/version entries, 324 npm entries, four container-image references, and
five GitHub Actions references. No package has unknown licence metadata.

This inventory answers:

- what third-party component and version is resolved;
- whether it is a direct, transitive, runtime, development, container, or
  build-time dependency;
- where it is used;
- what licence and attribution metadata the package publishes;
- where the resolved package came from and, where available, its content hash
  or lockfile integrity value.

It does not itself prove that every required licence or notice text is present
in a final container, package, source archive, or generated artefact. That
requires inspecting each release candidate or its SBOM and comparing the
shipped contents with the obligations recorded here.

## Commercial-use follow-up

Three resolved json-everything binaries use a file-based
`OSMFEULA.txt` in addition to describing their source code as MIT licensed:

- `Json.More.Net` 3.0.1;
- `JsonPointer.Net` 7.0.1;
- `JsonSchema.Net` 9.2.0.

The packaged agreement states that its maintenance fee applies to
revenue-generating users with annual gross revenue of at least US$10,000,
unless a separate support or maintenance arrangement applies. It also states
that users may self-compile the MIT-licensed source instead of using the
publisher's binary release.

Before commercial release, Valence Works must therefore choose and document
one of these paths:

1. confirm that the fee does not apply;
2. arrange the applicable maintenance or support terms; or
3. replace the downloaded binaries with a reviewed, reproducible build from
   the corresponding MIT-licensed source.

Qualified legal counsel should confirm the chosen path. This review does not
invent or interpret commercial terms beyond the text distributed in the
resolved NuGet packages.

## Release-candidate review

The current Docker and Compose references use tags rather than immutable
digests. For each release candidate:

- resolve and record the final image digests;
- generate an SBOM from the final Valence Control image;
- retain required operating-system, .NET, Node, Keycloak, and other base-image
  notices;
- inspect generated NuGet packages, frontend bundles, and source archives for
  copied third-party code and licence files;
- ship the required third-party notices alongside every applicable
  distribution.
