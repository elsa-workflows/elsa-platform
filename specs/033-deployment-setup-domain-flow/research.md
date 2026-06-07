# Research: Deployment Setup Domain Flow

## Decision: Environment Creation Is Environment-Only

**Decision**: The add-environment form creates only a deployment environment. Engine registration is presented as the next action from the environment page.

**Rationale**: Environment and engine are different domain concepts with different lifecycle timing. Allowing zero-engine environments supports planned deployment lanes and avoids forcing endpoint/credential decisions too early.

**Alternatives considered**:
- Keep the combined form and add helper copy. Rejected because the form would still encode the wrong lifecycle.
- Use a multi-step wizard that creates both records. Rejected because it still implies an engine is part of environment creation.

## Decision: Secret Registry Stores Metadata Only

**Decision**: Add workspace-scoped secret-store and credential-reference metadata records for deployment setup. Do not store raw secret values or provider tokens.

**Rationale**: Pickers need an authoritative local list, but the platform safety boundary requires secrets to remain in external systems.

**Alternatives considered**:
- Query provider APIs directly during engine registration. Deferred because provider adapters, OAuth, browsing permissions, and latency/error handling need separate design.
- Continue free-text provider/reference input. Rejected because users cannot discover valid values and validation remains weak.

## Decision: Preserve Legacy Engine Credential Strings

**Decision**: Keep existing engine `CredentialProvider` and `CredentialReference` string fields. New engine registration can derive these from selected registry metadata while legacy rows remain readable.

**Rationale**: Existing persisted data and tests depend on provider/reference strings. Additive registry metadata avoids a breaking migration.

**Alternatives considered**:
- Replace engine credential strings with foreign keys immediately. Rejected because it would require mapping all legacy rows and would make unknown external references unreadable.

## Decision: Archive Instead Of Delete

**Decision**: Secret stores and credential references use active/archived status. Archived records are hidden from new engine registration but remain readable for history and existing engines.

**Rationale**: Deployment setup is auditable control-plane data. Deletion would make past engine registrations and deployment history harder to understand.

**Alternatives considered**:
- Hard delete unused records. Rejected because determining safe deletion across engines/history is broader than this feature.
