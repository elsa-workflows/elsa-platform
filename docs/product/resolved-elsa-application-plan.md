# Resolved Elsa Application Plan

Status: contract v1 (issue [#106](https://github.com/valence-works/elsa-control/issues/106))

This document defines the provider-neutral boundary between Elsa Control's catalog and
desired-state resolution and a deployment provider. The executable contract lives in
`ElsaControl.RuntimeBuilder.Abstractions.Plans`.

## Boundary

Customer intent is resolved in this order:

```text
RuntimeBuilderIntent / Elsa application desired state
        |
        | catalog policy, release-manifest verification, package compatibility
        v
ResolvedElsaApplicationPlan (this contract)
        |
        | provider capability validation and translation
        v
Provider plan / remote deployment command
```

The resolved plan describes application outcomes and immutable inputs. It deliberately
does not contain Azure resource types, provider resource IDs, credentials, raw secret
values, or a provider-specific reconciliation command.

## Contract shape

`ResolvedElsaApplicationPlan` has these independent dimensions:

| Field | Meaning | Authority |
|---|---|---|
| `SchemaVersion` | Version of this contract, independent of Elsa versions | Elsa Control contract |
| `Release` | Distribution, release line, exact version, source commit and verified release-manifest identity | Image/distribution producer, verified and projected by Control |
| `Topology` | One or more runtime components, their roles, immutable images, runtime kinds, endpoints and capabilities | Producer manifest projected by Control |
| `Packages` | Exact package source/version/manifest identities, resolved runtime kinds and selected compatible features | Package Catalog and specification metadata |
| `Configuration` | Typed configuration shape, safe non-secret values, and provider-backed secret references | Desired state plus package feature metadata |
| `Capacity` | Component replica/compute bounds and durable storage outcomes | Control policy/profile |
| `Network` | Ingress/egress and endpoint outcomes, without provider resource shape | Control policy/profile and resolved feature requirements |
| `Isolation` | Service boundary such as `Dedicated` or `Private` | Control policy/profile |
| `ReleasePolicy` | Channel, lifecycle, rollout ring, patch behavior, minor upgrade behavior and major migration behavior | Control policy |
| `ProviderCapabilities` | Provider capabilities required to realize the outcomes | Resolver and package/topology metadata |
| `Evidence` | Immutable references explaining why the release and compatibility decision was accepted | Catalog/resolution pipeline |

Release line, topology, package features, isolation and lifecycle are values, not
hard-coded branches. Adding `3.9`, `3.10`, `4.1`, `5.0` or any later line does not alter
the schema.

### Immutable identities

Every image has both a digest and a reference using that digest:

```text
registry.example/runtime@sha256:<64 hex characters>
```

Tags such as `latest` and `3.8.0-preview.5413` may be retained as discovery metadata in
the producer catalog, but they are not valid provider inputs. Package selections carry a
catalog source ID, exact package version and manifest digest. The release carries the
producer manifest reference and digest, plus its source repository and commit.

### Topology composition

`ResolvedElsaTopology.Components` is the composition point. A Combined release can have
one component with `server` and `studio` roles; a Server plus Studio release can have two
components with separate images and endpoints. The resolver and provider do not switch
schema based on an Elsa major version.

Endpoints have semantic protocol, port, visibility, TLS and path values. Capacity has
component-level replica/compute bounds and a separate durable-storage list. Network
outcomes express ingress, egress, private-connectivity and allowed destinations; Azure
Container Apps, SQL, Front Door or another provider's resource model begins only in the
provider plan.

### Configuration and secrets

Configuration entries contain a JSON type and shape metadata. A non-secret entry may
carry its resolved JSON value. A secret entry may carry only a provider-backed
`SecretReference` (for example `secret://workspace/database`), never the secret value.
The validator rejects an entry that contains both a secret flag and a value, or a secret
reference on a non-secret entry. Secret references are locators, not provider tokens.

### Upgrade semantics

The exact release is separate from `ReleasePolicy`. The policy has distinct values for:

- patch updates, which may be `automatic-within-minor` after rollout-ring validation;
- minor updates, which require explicit customer approval; and
- major migrations, which require an explicit migration workflow.

This makes a patch/minor upgrade distinguishable from a major migration without making
the plan depend on an Elsa-major enum. Migration adapters and rollback guarantees belong
to the catalog transition policy and lifecycle services, not to a provider-specific plan.

## Mapping existing Elsa Control concepts

| Existing concept | Resolved-plan mapping | Boundary rule |
|---|---|---|
| `RuntimeBuilderIntent.Image` | Release/topology lookup input | Slug/tag selects catalog metadata; the plan stores verified image digest and component topology, never the mutable tag as the deployment identity |
| `RuntimeImage` | Producer-projected topology/component metadata | Existing UI/build hints are not provider resources; topology components become the stable provider input |
| `BundlePackageSelection` | `ResolvedElsaPackage` and `ResolvedElsaFeature` | Source ID, package ID and exact version are retained; the package manifest hash/digest, runtime kinds and compatibility findings explain resolution |
| `BundlePackageSelection.Settings` | `ResolvedConfigurationShape.Entries` | Non-secret values may be resolved; secret settings become safe secret references and raw values are rejected |
| `PackageSourceSelection` | Package source identity used during resolution | Source URLs are catalog metadata; credentials and feed tokens never enter the plan |
| `InfrastructureSelection` | `ProviderCapabilities` and capacity/network outcomes | Existing provider IDs/strategies are resolver inputs or local bundle hints; Azure/provider resource identifiers do not cross this boundary |
| `PublicPackageVersionProjection` | Package/feature compatibility input and evidence | Package metadata can constrain runtime kinds, dependencies, conflicts and required capabilities |
| `CompatibilityCheckResult` | `Evidence` plus resolver findings | Compatibility is evaluated before a plan is accepted; a provider does not reimplement catalog policy |
| `RuntimeConfiguration.IntentJson` | Customer-intent input | It is not itself a provider plan and must be resolved before provider execution |
| `RuntimeConfigurationVersion` | Versioned desired-state source | A resolved plan can be persisted inside an immutable desired-state/deployment revision |
| `StructuredDesiredStateRecord` kind `RuntimeConfiguration` / `Feature` / `SecretReference` | Inputs to plan resolution | Raw payloads remain governed by existing desired-state and secret-store boundaries |
| `WorkspaceDesiredStateRevision` | Durable owner of the serialized resolved plan | Revision identity, content hash, actor and environment remain deployment-domain data |
| `DeploymentPlan` | Provider-specific output after this contract | Its target, resource changes and diagnostics may contain provider details; they must not be mistaken for customer application intent |
| `DeploymentResource` | Provider resource realization | Resource IDs, dependencies and provider metadata stay below the boundary |
| `DeploymentCommand*` | Remote apply/checkpoint transport | Commands contain safe metadata and references only; the resolved plan is not a license to place raw secrets in history |

This is an adapter boundary, not a replacement for the existing Runtime Builder,
Package Catalog or Deployment models. A later resolver service should translate those
models into this contract and a provider adapter should translate it into a provider
plan.

## Elsa 3.8 Combined example

This is the first approved deployment-proof shape. The image index digest values below
are the observed Build-79 values from
[`commercial-image-release-audit.md`](commercial-image-release-audit.md). The release
manifest and package-manifest digests are intentionally shown as placeholders because
the producer follow-up must publish the signed machine-readable manifest before a
release can become selectable.

```json
{
  "schemaVersion": "1",
  "release": {
    "distributionId": "valence-runtime",
    "releaseLine": "3.8",
    "version": "3.8.0-preview.5413",
    "sourceRepository": "https://github.com/valence-works/elsa-production-image",
    "sourceCommit": "1aeee8df455b21cf3bf3d2b26dfbd512d76da27b",
    "releaseManifestReference": "oci://valence-runtime/release-manifest",
    "releaseManifestDigest": "sha256:<verified-release-manifest-digest>"
  },
  "topology": {
    "id": "combined",
    "components": [
      {
        "id": "runtime",
        "roles": ["server", "studio"],
        "image": {
          "registryClass": "paid",
          "repository": "valenceruntimeimages.azurecr.io/runtime-combined",
          "reference": "valenceruntimeimages.azurecr.io/runtime-combined@sha256:c33707c58c5e5a5b115499874d3114911db5ca7d69135aeb29d5a142a7d893e0",
          "digest": "sha256:c33707c58c5e5a5b115499874d3114911db5ca7d69135aeb29d5a142a7d893e0"
        },
        "runtimeKinds": ["elsa.server", "elsa.studio"],
        "endpoints": [
          { "name": "api", "protocol": "https", "port": 443, "visibility": "public", "requiresTls": true, "path": "/elsa/api" },
          { "name": "studio", "protocol": "https", "port": 443, "visibility": "public", "requiresTls": true, "path": "/" }
        ],
        "capabilities": ["workflow.runtime", "workflow.studio"]
      }
    ]
  },
  "isolation": "Dedicated",
  "releasePolicy": {
    "channel": "preview",
    "lifecycle": "Preview",
    "rolloutRing": "internal",
    "patchUpdates": "automatic-within-minor",
    "minorUpdates": "explicit-approval",
    "majorMigrations": "explicit-migration"
  }
}
```

The omitted package, configuration, capacity, network, capability and evidence fields
are required by the executable v1 contract. The excerpt shows the release/topology
boundary; the actual resolver must fill every field and pass validation.

## Future Elsa 4 Server plus Studio example

The following is a schema-compatibility example, not an assertion that an approved
commercial Elsa 4 distribution exists today. The audit records upstream Elsa 4
Foundation evidence but no approved commercial image authority. The synthetic image
digests demonstrate the data shape that a future signed producer manifest can populate.

```json
{
  "schemaVersion": "1",
  "release": {
    "distributionId": "future-valence-runtime",
    "releaseLine": "4.0",
    "version": "4.0.0-preview.1",
    "sourceRepository": "https://github.com/example/approved-elsa-4-distribution",
    "sourceCommit": "0123456789abcdef0123456789abcdef01234567",
    "releaseManifestReference": "oci://future-valence-runtime/release-manifest",
    "releaseManifestDigest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
  },
  "topology": {
    "id": "server-studio",
    "components": [
      {
        "id": "server",
        "roles": ["server"],
        "image": {
          "registryClass": "paid",
          "repository": "registry.example/elsa-server",
          "reference": "registry.example/elsa-server@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        },
        "runtimeKinds": ["elsa.server"],
        "endpoints": [{ "name": "api", "protocol": "https", "port": 443, "visibility": "private", "requiresTls": true, "path": "/elsa/api" }],
        "capabilities": ["workflow.runtime"]
      },
      {
        "id": "studio",
        "roles": ["studio"],
        "image": {
          "registryClass": "paid",
          "repository": "registry.example/elsa-studio",
          "reference": "registry.example/elsa-studio@sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
          "digest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
        },
        "runtimeKinds": ["elsa.studio"],
        "endpoints": [{ "name": "ui", "protocol": "https", "port": 443, "visibility": "public", "requiresTls": true, "path": "/" }],
        "capabilities": ["workflow.studio"],
        "companionComponentId": "server"
      }
    ]
  }
}
```

The same `ResolvedElsaApplicationPlan` type represents both examples. The only
differences are catalog data and the component composition; no `Elsa3Plan`,
`Elsa4Plan`, major-version enum or major-version conditional is introduced.

## Validation and evolution

`ResolvedElsaApplicationPlanValidator.Validate` rejects:

- missing or unsupported contract versions and release identity fields;
- image/package/evidence values without a `sha256` digest;
- mutable image tag references, duplicate component/package/feature/configuration
  identities, and empty topology/runtime-kind collections;
- embedded secret values or invalid secret references;
- invalid replica, compute, storage or endpoint bounds; and
- missing policy, isolation, network, provider-capability or evidence descriptions.

`ResolvedElsaApplicationPlanSerialization.Serialize` first calls `Normalize`, which
sorts all unordered collections and digest dictionaries. Equivalent plans therefore
produce identical compact JSON suitable for a desired-state content hash. Unknown JSON
properties are ignored by the default `System.Text.Json` deserializer so additive
fields can be introduced in a later schema version. Removing or changing field meaning
requires a new schema version and an explicit compatibility/migration decision.

The v1 tests cover deterministic serialization, round-trip deserialization, immutable
identity validation, secret rejection, and both Combined and Server-plus-Studio
compositions across `3.8` and `4.0` release lines. A resolver implementation should add
catalog-backed compatibility and manifest-verification tests when the producer release
manifest issue is delivered.

## Follow-up implementation

This issue provides the stable executable boundary and its validation/serialization
rules. A subsequent resolver issue must:

1. ingest the signed producer release manifest and project its verified facts;
2. adapt `RuntimeBuilderIntent`, Package Catalog compatibility and desired-state
   records into a fully populated plan;
3. resolve feature settings into safe values or secret references;
4. derive capacity/network/isolation outcomes from the selected profile; and
5. hand the validated plan to an Azure, Docker, Kubernetes or customer-agent provider
   without leaking provider resources upward.

That work remains gated by the release authority evidence from #105 and the typed
reconciliation prerequisite from #107.
