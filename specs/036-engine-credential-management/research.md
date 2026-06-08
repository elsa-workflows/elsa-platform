# Research: Engine Credential Management UI

## Decision: Add a dedicated Deployments navigation item

**Decision**: Add an "Engine credentials" entry under the existing Deployments navigation group, with a route under `/admin/deployments/credentials`.

**Rationale**: Engine credential stores are deployment setup metadata, not package catalog or runtime operations data. Placing the page next to Applications, Artifacts, and Tiers keeps the workflow discoverable without implying runtime secret management.

**Alternatives considered**:
- Add a global Settings or Secrets page. Rejected because it suggests a broader secret-management subsystem and risks confusing engine credentials with runtime secrets.
- Keep the feature only in the new application wizard. Rejected because users already could not discover where to create references.

## Decision: Extract the existing setup wizard credential panel

**Decision**: Reuse and extend the current `SecretStoresPanel` behavior as a shared credential management surface rather than building a separate duplicate form.

**Rationale**: The wizard already supports supported store types, local protected values, external locator-only references, usage disclosure, and archive calls. Extracting or extending it reduces drift between setup and management workflows.

**Alternatives considered**:
- Duplicate forms on a new page. Rejected due to high risk of inconsistent validation, copy, and secret-handling behavior.
- Remove credential creation from the setup wizard. Rejected because the wizard still needs a fast path for first-time setup users.

## Decision: Use existing API contracts first

**Decision**: Use existing store/reference list, create, update, rotate, usage, and archive endpoints unless implementation reveals a concrete contract gap.

**Rationale**: The prior engine credential feature already added workspace-scoped APIs and tests. This addition is primarily a Console discoverability and lifecycle-management feature.

**Alternatives considered**:
- Add new dedicated credential-management endpoints. Rejected because this would duplicate existing deployment credential contracts.
- Add provider browsing/resolution endpoints. Rejected because provider browsing is outside current scope and would require provider-specific security decisions.

## Decision: Usage details load on demand

**Decision**: Show reference usage counts in lists and load detailed engine usage only when a user expands usage or starts archive/rotation actions.

**Rationale**: The list should remain lightweight and provider-independent. Detailed usage is only needed for impact decisions.

**Alternatives considered**:
- Always load usage details for every reference. Rejected because it scales poorly and adds unnecessary requests for large workspaces.
- Hide usage details until archive only. Rejected because administrators also need proactive impact review.

## Decision: Preserve archived visibility but keep active default

**Decision**: Show active stores/references by default with controls to inspect archived items.

**Rationale**: Archived references may still explain existing engine assignments, but active items are the common management target and the only eligible choices for new assignments.

**Alternatives considered**:
- Hide archived items entirely. Rejected because existing engines can still point to archived references and need understandable history.
- Mix active and archived items by default. Rejected because archived records should not look assignable.
