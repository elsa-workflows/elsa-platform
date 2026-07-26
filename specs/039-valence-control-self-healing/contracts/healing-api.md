# API Contract: Workspace Healing v1

Base route: `/api/workspaces/{workspaceId:guid}/healing`

Interactive routes require workspace membership plus the stated Healing permission. All IDs are resolved within the route workspace before returning any data.

## Permissions

- `healing.read`
- `healing.configure`
- `healing.incident.report`
- `healing.deployment.report`
- `healing.evidence.elevate`
- `healing.repair.retry`
- `healing.repair.stop`
- `healing.verification.waive`
- `healing.automerge.configure`

Workspace owners receive the permission set by default. Grants are explicit, persisted, revocable, and audited.

## Configuration

### Get application configuration

`GET /applications/{applicationId}/configuration`

Returns effective application/environment settings, thresholds, budgets, kill switches, policy references, manifest readiness, and provider readiness.

### Update application configuration

`PUT /applications/{applicationId}/configuration`

Requires `healing.configure`. Automatic merge changes additionally require `healing.automerge.configure` and a target-bound one-use confirmation.

### Emergency stop

`POST /applications/{applicationId}/stop`

Immediately blocks new repair dispatch, publication, and merge. It does not delete incidents or stop observability ingestion.

## Component manifests and ownership

### Upload component manifest

`POST /applications/{applicationId}/revisions/{revisionId}/component-manifests`

Uses the component-manifest contract and requires an idempotency key.

### List/get manifests

- `GET /applications/{applicationId}/component-manifests`
- `GET /applications/{applicationId}/component-manifests/{manifestId}`

### Create/update binding

- `POST /applications/{applicationId}/source-ownership-bindings`
- `PUT /applications/{applicationId}/source-ownership-bindings/{bindingId}`

Mutations require `healing.configure`. Activation requires owner approval, an authorized provider connection, unambiguous selector validation, and valid policy references.

### Suspend/revoke binding

- `POST /applications/{applicationId}/source-ownership-bindings/{bindingId}/suspend`
- `POST /applications/{applicationId}/source-ownership-bindings/{bindingId}/revoke`

Active attempts are prevented from new publication/merge after suspension or revocation.

## Explicit incident intake

`POST /applications/{applicationId}/environments/{environmentId}/incidents`

Requires `healing.incident.report`; configuration authority alone does not grant machine-intake authority.

Request includes profile version, revision, occurred time, occurrence ID, operation, curated class, exception evidence, retry state, component hint, and trace correlation. It cannot include repository/workflow/branch/merge routing.

Responses:

- `202 Accepted`: durable inbox append succeeded; returns inbox ID and idempotent replay state.
- `400`: malformed/unsupported profile.
- `403`: caller cannot report for the application/environment.
- `409`: idempotency key reused with a different payload hash.
- `413`: bounded evidence limit exceeded.

## Incidents

### List incidents

`GET /incidents?applicationId=&environmentId=&status=&severity=&repairable=&cursor=&take=`

Returns safe summaries with occurrence impact, attribution state, work item, repair/merge status, and per-environment verification.

### Get incident

`GET /incidents/{incidentId}`

Returns the canonical incident, episodes, safe evidence metadata, component attribution, attempts, provider projections, policy decisions, environment impacts, and audit summary. Default response excludes elevated evidence.

### Request evidence elevation

`POST /incidents/{incidentId}/evidence-requests`

Requires `healing.evidence.elevate`, a purpose, requested fields/tier, and target-bound confirmation. Returns an audited decision and, if approved, a new expiring evidence bundle reference.

### Retry/stop repair

- `POST /incidents/{incidentId}/repair/retry`
- `POST /incidents/{incidentId}/repair/stop`

Requires the corresponding permission. Retry cannot exceed platform maximum attempt limits; stop is idempotent.

### Waive environment verification

`POST /incidents/{incidentId}/environments/{environmentId}/waive`

Requires `healing.verification.waive`, reason, expiry/terminal intent, and confirmation. Waiver is per episode/environment and audited.

## Deployment observations

`POST /applications/{applicationId}/environments/{environmentId}/deployment-observations`

Requires `healing.deployment.report`; configuration authority alone does not grant delivery-reporting authority. Authenticated delivery identities submit revision, deployed time, source, source observation ID, and evidence digest. Requests require an idempotency key. Valence Control-managed deployment completion invokes the same application contract internally without impersonating an external caller.

## Audit and usage

- `GET /audit?applicationId=&incidentId=&cursor=&take=`
- `GET /usage?applicationId=&from=&to=`

Audit responses contain safe structured events only. Usage reports bounded counts/durations/provider/inference units, not prompts, source, or protected evidence.

## Repository workflow capability API

These routes use short-lived incident-scoped capability tokens obtained through the GitHub OIDC exchange, not interactive workspace authentication.

- `POST /workload/exchange`: validate GitHub OIDC token and attempt nonce; issue capability token.
- `GET /workload/attempts/{attemptId}/evidence`: return the authorized bounded bundle.
- `POST /workload/attempts/{attemptId}/proposal`: submit bounded inert source context and create one managed proposal without repository tools.
- `POST /workload/attempts/{attemptId}/proposals/{proposalId}/finalize-exchange`: exchange the proposal nonce and fresh OIDC identity for a finalization capability after repository validation.
- `POST /workload/attempts/{attemptId}/heartbeat`: extend the current valid lease within limits.
- `POST /workload/attempts/{attemptId}/result`: upload one bounded repair result envelope idempotently.

The complete capability vocabulary is `evidence.read`, `proposal.create`, `proposal.finalize`, `attempt.heartbeat`, and `result.upload`; no provider mutation capability exists. Initial identity exchange grants only evidence read, proposal creation, and heartbeat. Finalization exchange grants only exact-proposal finalization and result upload. The token audience, phase, attempt/proposal IDs, immutable repository/workflow/run claims, expiry, nonce, and allowed operations must all match.

## GitHub webhook API

`POST /integrations/github/webhooks`

- Reads a size-capped raw body.
- Verifies `X-Hub-Signature-256` before JSON parsing.
- Requires unique `X-GitHub-Delivery`.
- Allows only configured event/action combinations.
- Validates App installation and immutable repository identity.
- Returns `202` after durable delivery append; processing is asynchronous and idempotent.

## Error contract

Errors use RFC 9457 problem details with a stable `code`, correlation ID, and safe detail. Authorization failures do not reveal whether a cross-workspace resource exists.
