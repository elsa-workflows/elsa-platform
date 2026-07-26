# Research: Runtime Command Sync

## Decision 1: Runtime pull/sync is the default transport

**Decision**: External runtimes poll and claim commands from Valence Control. Webhooks can trigger a faster fetch but are not authoritative.

**Rationale**: Many runtimes are behind private networks or customer firewalls. Pull/sync avoids inbound runtime access and keeps Valence Control deployment state authoritative.

**Alternatives Considered**:

- Valence Control direct push as the default. Rejected because it requires inbound runtime reachability and increases network/security coupling.
- Webhook payloads as authoritative commands. Rejected because duplicate/lost webhook delivery would make deployment state less deterministic.

## Decision 2: Commands are linked to deployment runs

**Decision**: Every deployment command belongs to a deployment run, and run history remains the console-facing source of truth.

**Rationale**: Users already understand runs and history. Commands add transport semantics without changing the console model.

**Alternatives Considered**:

- Make commands the only user-facing deployment object. Rejected because it would duplicate and disrupt existing deployment run UX.
- Keep only queued runs. Rejected because external runtime sync needs claim/lease/progress APIs.

## Decision 3: Lease token required for runtime mutations

**Decision**: Claim returns a lease token. Heartbeat, progress, complete, fail, and reject must include that lease token.

**Rationale**: Lease tokens prevent accidental or malicious updates by workers that did not claim the command and let the platform distinguish stale or duplicate attempts.

**Alternatives Considered**:

- Allow command ID alone for updates. Rejected because it cannot distinguish worker attempts safely.

## Decision 4: Stale commands require explicit recovery semantics

**Decision**: Stale claimed commands move to recovery-required or an explicit reclaimable state; they are not silently replayed.

**Rationale**: A runtime may have applied work before losing connectivity. Silent replay risks duplicate apply or overwriting state without operator awareness.

**Alternatives Considered**:

- Automatically requeue stale commands. Rejected for safety.
- Permanently fail stale commands. Rejected because some runtimes may safely reclaim after inspection.

## Decision 5: Safe command payloads only

**Decision**: Commands carry IDs, references, digests, compatibility metadata, and safe diagnostics only.

**Rationale**: Valence Control must remain a control plane and avoid storing raw secrets, workflow payloads, or runtime credentials in command records.

**Alternatives Considered**:

- Embed artifact payloads in commands. Rejected because artifact envelope and payload storage are separate concerns.
