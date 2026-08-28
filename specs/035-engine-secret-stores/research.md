# Research: Engine Credential Secret Stores

## Decision: Secret stores remain workspace-scoped and engine-credential-only

**Rationale**: The platform needs credentials only to call registered engines for control-plane actions such as manifest provisioning notifications. Runtime secrets are managed by runtimes, and artifact secret references are deployment payload metadata, not platform engine credentials. Keeping the model workspace-scoped matches existing deployment setup ownership and lets references be reused across applications, environments, and engines in the same workspace.

**Alternatives considered**:
- Environment-scoped stores: rejected because the user explicitly chose workspace scope and because many environments can share the same platform-to-engine credential pattern.
- General-purpose secret management: rejected because it would confuse runtime secret ownership and expand the feature beyond engine setup.

## Decision: Add explicit store types instead of relying on free-text provider names

**Rationale**: The current provider string is too vague for UX, validation, and safety rules. Store type drives whether raw credential material is accepted, whether verification is possible, and what locator fields are understandable to users.

**Alternatives considered**:
- Continue free-text provider names: rejected because it preserves the confusing setup flow the feature is meant to fix.
- Provider-specific tables per store type: rejected for this iteration because the existing registry can support first-class types with safe metadata and focused validation.

## Decision: Local encrypted database stores accept secret material only during create/rotation

**Rationale**: Local storage is one of the requested supported types and differs materially from external providers. It must accept engine credential values, protect them, and never reveal them later. Lists and details should expose only safe metadata such as name, status, timestamps, and whether protected material is present.

**Alternatives considered**:
- Exclude local storage from MVP: rejected because it is explicitly requested and useful for local/test deployments.
- Store local values as plain reference strings: rejected because it would violate the feature's safety boundary.

## Decision: External store types capture safe locators only

**Rationale**: Azure Key Vault, Kubernetes Secrets, environment variable names, and generic external references are externally governed sources. Elsa Control should capture enough locator metadata to identify the credential but must not collect the raw secret value for these types.

**Alternatives considered**:
- Browse provider contents during setup: deferred because provider integrations, auth, and browsing policies are broader than this feature.
- Require verification before reference creation: rejected because environment variable names and generic external references may not be verifiable by design.

## Decision: Generic external reference means customer-governed metadata only

**Rationale**: Generic external reference is useful as an escape hatch for providers not modeled yet or for enterprise secret catalogs. It should not imply that Elsa Control can resolve, browse, verify, or fetch the secret. Examples include an internal secret catalog URI, a ticket-controlled credential record, a vendor-specific vault URI, or a provider type not yet first-class.

**Alternatives considered**:
- Remove generic external reference: rejected because users asked for it and it preserves extensibility.
- Treat generic references as raw URI secrets: rejected because it blurs the safety boundary and creates false verification expectations.

## Decision: Engine credentials can be deferred

**Rationale**: Users may need to model applications, environments, and engines before credentials exist. Deferred credentials should be explicit and should block or mark unavailable only the platform-to-engine actions that require credentials.

**Alternatives considered**:
- Require credentials for engine registration: rejected because it creates the current setup dead-end.
- Allow silent empty credentials: rejected because users need clear status and actionability.
