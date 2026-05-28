# Research: Engine Health Verification

## Decision: Verification Is Control-Plane Metadata Only

**Rationale**: The deployment UX PRD explicitly defers live deployment apply, live drift detection, and telemetry provider calls. Engine verification should establish whether the platform can safely consider an engine reachable and credential/certificate metadata current, then persist that metadata for cockpit and validation gates.

**Alternatives considered**:

- **Run a full deployment dry-run during verification**: Rejected because deployment apply/dry-run adapters are a separate future slice.
- **Inspect workflow runtime state during verification**: Rejected by the Control Plane First constitution principle.

## Decision: Use A Probe Abstraction With Deterministic First Implementation

**Rationale**: Production probing will eventually need provider-specific or engine-API-specific behavior. A small core abstraction lets the feature be tested deterministically now while API/persistence/console contracts stabilize.

**Alternatives considered**:

- **Hard-code HTTP probing in API endpoints**: Rejected because it couples domain behavior to API hosting details and makes tests brittle.
- **Require engines to heartbeat before any manual verification exists**: Rejected because setup users need a direct way to verify a newly registered endpoint.

## Decision: Heartbeat Updates Are Single-Engine Metadata Mutations

**Rationale**: Heartbeats should be cheap and bounded: update only the target engine, reject cross-workspace IDs, preserve capabilities unless explicitly supplied, and prevent stale timestamps from overwriting newer metadata.

**Alternatives considered**:

- **Heartbeat updates entire cockpit state**: Rejected because it increases blast radius and cross-workspace risk.
- **Always replace capabilities from heartbeat payloads**: Rejected because missing optional capability metadata would accidentally erase registered controls.

## Decision: Health Classification Remains Simple

**Rationale**: Existing cockpit health has `Healthy`, `Degraded`, and `Unreachable`. The feature can classify results without adding a new enum: failed reachability is `Unreachable`; reachable with credential/certificate concern is `Degraded`; reachable with trusted certificate and verified credential reference is `Healthy`.

**Alternatives considered**:

- **Add `Verifying` or `Unknown` engine health values**: Rejected because pending state is a UI mutation state, and unverified persisted engines already start as `Unreachable`.
- **Treat missing credentials as unreachable**: Rejected because the engine may be reachable but unsafe for deployment/control actions, which is better represented as degraded.

## Decision: Safe Diagnostic Messages Only

**Rationale**: Verification details help users resolve setup issues, but diagnostics must not expose raw credentials, tokens, provider responses, or stack traces.

**Alternatives considered**:

- **Return raw provider/engine failure payloads**: Rejected because it can leak secrets or implementation details.
- **Return no diagnostic text**: Rejected because users would not know whether the failure was reachability, certificate, or credential verification.
