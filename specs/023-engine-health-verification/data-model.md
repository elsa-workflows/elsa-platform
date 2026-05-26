# Data Model: Engine Health Verification

## Workflow Engine Registration

Existing workspace-owned engine record extended with verification metadata.

Fields:

- `Id`: Engine registration identifier.
- `WorkspaceId`: Owning workspace.
- `EnvironmentId`: Owning deployment environment.
- `Name`: Display name.
- `BaseUrl`: Registered endpoint URL.
- `Version`: Latest verified engine version, if reported.
- `CertificateStatus`: Latest trusted/untrusted/expiring certificate state.
- `CredentialVerificationStatus`: Latest credential-reference verification state.
- `CredentialLastVerifiedAt`: Latest credential verification timestamp.
- `Health`: `Healthy`, `Degraded`, or `Unreachable`.
- `LastHeartbeatAt`: Latest accepted heartbeat or verification reachability timestamp.
- `LastVerificationAt`: Latest manual verification timestamp.
- `VerificationMessage`: Safe diagnostic summary for the current verification state.
- `Capabilities`: Registered or heartbeat-advertised capability metadata.
- `Controls`: Registered runtime control metadata.

Validation rules:

- `WorkspaceId` and `EnvironmentId` must match existing workspace-owned records.
- `BaseUrl` must remain metadata; raw credentials must never be stored here.
- Stale heartbeat timestamps must not overwrite newer `LastHeartbeatAt`.
- Safe diagnostic message must be redacted before persistence.

## Engine Verification Request

Manual verification request submitted by a workspace user.

Fields:

- `EngineId`: Target engine.
- `ActorAccountId`: Initiating workspace account.
- `RequestedAt`: Server timestamp.

Validation rules:

- Actor must be a workspace member.
- Actor must have deployment setup permission.
- Target engine must belong to the workspace.

## Engine Verification Result

Result produced by the verification service and persisted to the engine record.

Fields:

- `EngineId`
- `WorkspaceId`
- `Health`
- `Version`
- `CertificateStatus`
- `CredentialVerificationStatus`
- `CredentialLastVerifiedAt`
- `LastHeartbeatAt`
- `LastVerificationAt`
- `Message`

State transitions:

- Failed reachability -> `Unreachable`.
- Reachable plus untrusted/expiring certificate or missing/expired/unverified credential -> `Degraded`.
- Reachable plus trusted certificate and verified credential -> `Healthy`.

## Engine Heartbeat Request

Trusted runtime-originated metadata update for a registered engine.

Fields:

- `EngineId`
- `EnvironmentId`
- `Version`
- `CertificateStatus`
- `CredentialVerificationStatus`
- `HeartbeatAt`
- `Capabilities` optional
- `Message` optional safe diagnostic

Validation rules:

- Caller must be authorized for the target workspace/engine.
- Engine and environment must match the workspace-owned registration.
- `HeartbeatAt` must be newer than the current accepted heartbeat.
- Missing optional capabilities preserve existing registered capabilities.
- Capability boundaries must remain valid deployment control boundaries.

## Engine Verification Event

Optional append-only audit record for verification and heartbeat updates if needed for diagnostics.

Fields:

- `Id`
- `WorkspaceId`
- `EngineId`
- `EnvironmentId`
- `Source`: `ManualVerification` or `Heartbeat`
- `Health`
- `ActorAccountId` optional
- `OccurredAt`
- `Message`

Validation rules:

- Message must be safe/redacted.
- Event must not contain raw credentials, tokens, or provider payloads.
