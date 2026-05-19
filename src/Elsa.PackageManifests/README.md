# Elsa.PackageManifests

`Elsa.PackageManifests` defines the manifest wire contract shared by manifest generation, catalog ingestion, runtime validation, and Runtime Builder tooling.

The package is intentionally dependency-light and does not reference catalog persistence, package installation, Nuplane, Docker runtime internals, or generated code.

## Versioning

Manifest schema versions are independent from NuGet package versions. A package version such as `Elsa.Email` `1.2.3` can contain a manifest with schema version `1.0`.

Breaking schema changes must introduce a new schema version. Additive changes should prefer optional properties and extension metadata so older consumers can continue to read manifests.

## Extension Metadata

Manifest DTOs preserve unknown JSON members through extension data. Producers should use vendor-prefixed fields such as `x-company` for experimental metadata.

Consumers must ignore extension metadata they do not understand unless a future schema version explicitly requires otherwise.

## Safety

The manifest contract is data only. Catalog implementations must inspect package files and manifest JSON, never load or execute package assemblies.
