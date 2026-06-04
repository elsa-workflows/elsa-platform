# Elsa.Platform.PackageManifests

`Elsa.Platform.PackageManifests` defines the manifest wire contract shared by manifest generation, catalog ingestion, runtime validation, and Runtime Builder tooling.

The package is intentionally dependency-light and does not reference catalog persistence, package installation, Nuplane, Docker runtime internals, or generated code.

## Versioning

Manifest schema versions are independent from NuGet package versions. A package version such as `Elsa.Email` `1.2.3` can contain a manifest with schema version `1.0`.

Breaking schema changes must introduce a new schema version. Additive changes should prefer optional properties and extension metadata so older consumers can continue to read manifests.

## Extension Metadata

Manifest DTOs preserve unknown JSON members through extension data. Producers should use vendor-prefixed fields such as `x-company` for experimental metadata.

Consumers must ignore extension metadata they do not understand unless a future schema version explicitly requires otherwise.

## Runtime Kind Compatibility

Packages can declare supported application host kinds with `compatibility.runtimeKinds`. Official Elsa values are `elsa.server` for Elsa Server applications and `elsa.studio` for Elsa Studio applications. Values are strings, not enum members, so future and third-party hosts can use their own stable machine-readable identifiers.

Feature-level `compatibility.runtimeKinds` overrides the package-level default for that feature. If a package or feature omits runtime kinds, existing manifests are treated as Elsa Server compatible only. Runtime kind identifies the host type; keep behavior flags in `runtimeCapabilities`.

## Safety

The manifest contract is data only. Catalog implementations must inspect package files and manifest JSON, never load or execute package assemblies.
