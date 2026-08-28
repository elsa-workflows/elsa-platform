# Feature Specification: Identity And Workspace Tenancy

**Feature Branch**: `codex/021-identity-tenancy`

**Created**: 2026-05-21

**Status**: Draft

**Input**: User description: "Proceed with the proposed plan to get multitenancy done and OIDC/JWT login: make Workspace the platform tenant boundary, add real OIDC/JWT login, replace trusted browser-supplied identity with backend-derived account and workspace context, centralize workspace authorization, preserve operator fallback access, and define the deployment-platform direction for workflow engines, environments, desired state, promotion, secrets, observability, and future runtime tenant reconciliation."

> **Forward compatibility note**: `specs/031-organization-tenancy` supersedes this feature's tenant-boundary decision. Workspace remains the operational isolation boundary, while Organization becomes the customer tenant boundary for future work.

## Settled Product Direction

For Elsa Control-integrated installations, Elsa Studio remains the workflow authoring and single-engine runtime inspection surface, while Elsa Control is the source of truth for immutable deployable workflow artifacts and cross-environment desired-state revisions.

The Studio integration uses the command **Submit to Elsa Control** when a user is ready to hand off a workflow snapshot. This creates a platform-owned artifact; it does not release, promote, deploy, or make the workflow immediately executable. Elsa Control then owns release readiness, promotion, deployment, rollback, audit, and environment governance. Elsa runtimes remain responsible for executing deployed artifacts and owning runtime state such as instances, bookmarks, queues, and logs.

Direct runtime **Publish** behavior may still exist in non-integrated Studio installations, but Elsa Control-integrated UX must not use Publish language for the platform handoff unless it explicitly distinguishes direct runtime publishing from submitting an artifact to Elsa Control.

Runtime deployment communication is modeled as durable platform-owned deployment runs and deployment commands. A runtime integration package may consume those commands by outbound pull/sync, webhook-triggered fetch, or direct platform push, but the deployment run remains the authoritative state and the command/result contract remains transport-independent. Runtime pull is the preferred default for customer-hosted environments because it avoids requiring inbound network access to the runtime.

Git-backed desired-state versioning is the preferred long-term source-control model for deployment governance. Elsa Control should be able to attach a workspace or organization to a Git repository and write declarative desired-state files for applications, environments, runtime configurations, artifact references, secret references, observability bindings, infrastructure requirements, promotions, and rollbacks. Git versions the intended state that operators review, promote, deploy, and roll back. Deployment runs, runtime commands, engine health, verification results, drift observations, approval events, logs, and audit history remain platform operational records rather than Git-authored desired-state files.

Git integration must be optional at first. Workspaces without Git continue using platform-owned versioned desired-state records. Workspaces with Git enabled treat Git commits as the durable revision source and Elsa Control database records as indexed projections, validation state, runtime coordination state, and audit history. The existing desired-state revision `commit` concept should become platform-managed when Git is enabled instead of remaining caller-supplied metadata.

### Future GitOps Spec Seed

The future GitOps feature should define how Elsa Control maps workspace deployment state to a repository, how it writes and reads deterministic files, and how it reconciles database projections with Git commits.

Candidate Git-owned files:

- Application and environment topology, including stable application/environment identifiers, display names, tier references, and target bindings.
- Environment desired-state manifests, including artifact references, artifact identities, content digests, feature selections, feature settings, runtime image and runtime configuration, infrastructure requirements, package selections, observability bindings, and engine target bindings.
- Runtime builder outputs, including selected image, selected compatible features, feature settings, infrastructure components, generated deployment target metadata, and bundle references.
- Artifact descriptors containing immutable artifact identity, type, digest, safe display metadata, compatibility hints, and payload reference metadata, but not artifact payload bytes.
- Secret references, provider names, external key paths, version policies, and verification requirements, but never secret values.
- Promotion and rollback commits that update target environment desired-state files to point at the promoted or restored artifact and configuration references.

Non-Git operational records:

- Artifact ZIP bytes or other artifact payloads, which belong in local/object/OCI artifact storage and are referenced by digest.
- Deployment runs, runtime command claims, command heartbeats, command results, validation attempts, approval decisions, audit events, health checks, engine verification, drift observations, logs, metrics, and traces.
- Raw secrets, connection strings, provider tokens, runtime credentials, and storage credentials.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign In With Trusted Identity (Priority: P1)

A customer user signs in through a configured Elsa Control identity provider adapter, such as generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, or a trusted frontend/backend integration, and is recognized by the platform without supplying account or workspace identifiers directly.

**Why this priority**: Every customer-owned feature depends on a trustworthy user identity before account provisioning, workspace membership, private catalog visibility, saved configurations, deployment targets, or managed runtime ownership can be safe.

**Independent Test**: Can be tested by presenting a valid trusted identity context, requesting the current user's workspace context, and verifying the platform derives account and workspace data from the identity rather than from caller-supplied user IDs.

**Acceptance Scenarios**:

1. **Given** platform customer login is configured, **When** an anonymous user opens a customer-only console route, **Then** the console starts a provider-backed sign-in flow or shows a sign-in action without requiring the user to enter account or workspace identifiers.
2. **Given** the identity provider returns a valid sign-in response, **When** the platform completes the callback, **Then** the system establishes a customer-authenticated platform context and the console can request `GET /api/me/workspaces`.
3. **Given** a valid trusted identity with issuer and subject, **When** the user requests their platform context, **Then** the system maps the identity to a catalog-local account and returns the workspaces the account belongs to.
4. **Given** the same trusted identity returns later with changed display name or email, **When** the user requests their platform context, **Then** the system keeps the same account identity and updates profile metadata.
5. **Given** a request without a trusted identity, **When** the caller requests account or workspace context, **Then** the system rejects the request and does not create account, identity, or workspace data.
6. **Given** a user signs out from the console, **When** sign-out completes, **Then** the local platform session or token state is cleared and subsequent customer workspace API calls require a new trusted identity.

---

### User Story 2 - First Sign-In Creates A Personal Workspace (Priority: P1)

A first-time signed-in user receives a durable personal workspace that becomes the default resource boundary for customer-owned platform data in this slice; `specs/031-organization-tenancy` later promotes Organization to the root customer tenant boundary.

**Why this priority**: Workspace tenancy must exist before user-owned catalog sources, saved runtime configurations, deployment targets, managed runtimes, and entitlements can share one isolation model.

**Independent Test**: Can be tested by signing in with a new trusted identity twice and verifying that exactly one account, one external identity link, one personal workspace, and one owner membership exist after both requests.

**Acceptance Scenarios**:

1. **Given** a trusted identity with no existing platform account, **When** the user requests their platform context, **Then** the system creates an account, links the external identity, creates one personal workspace, creates an owner membership, and returns that workspace.
2. **Given** two concurrent first sign-in requests for the same trusted identity, **When** both complete, **Then** the system returns the same account and personal workspace without duplicates.
3. **Given** an existing account with multiple workspace memberships, **When** the user requests their platform context, **Then** the system returns every active workspace membership the account may use.

---

### User Story 3 - Enforce Workspace Authorization Everywhere (Priority: P1)

Workspace members can access only customer-owned records in workspaces they belong to, and every workspace-scoped feature uses the same authorization rules.

**Why this priority**: Multitenancy fails if one endpoint or query path can bypass workspace ownership checks, especially when callers know resource IDs.

**Independent Test**: Can be tested by creating two users in separate workspaces, seeding customer-owned records for each workspace, and proving each user can access only their own workspace data across source, package, builder, saved configuration, target, deployment, and managed runtime APIs that are in scope.

**Acceptance Scenarios**:

1. **Given** a workspace member, **When** they read or mutate records owned by their workspace, **Then** the request succeeds only if their membership role allows the operation.
2. **Given** an authenticated non-member who knows another workspace ID or resource ID, **When** they attempt to read or mutate that workspace's records, **Then** the system rejects the request and returns no private data.
3. **Given** an anonymous caller, **When** they browse public catalog data, **Then** public catalog access remains available while workspace-owned data stays hidden.

---

### User Story 4 - Use Role And Entitlement Boundaries (Priority: P2)

Workspace owners and administrators can perform privileged workspace operations, while readers can only inspect data they are entitled to view.

**Why this priority**: Workspace tenancy needs more than membership; source creation, deployment target registration, managed hosting, and future collaboration require stable role and entitlement checks.

**Independent Test**: Can be tested by assigning owner, administrator, and reader memberships in the same workspace, then verifying each role can perform only its allowed operations and entitlement-gated operations fail when the entitlement is absent or exhausted.

**Acceptance Scenarios**:

1. **Given** a workspace owner, **When** they manage workspace-owned sources, saved configurations, or deployment targets, **Then** privileged operations are allowed when relevant entitlements also allow them.
2. **Given** a workspace reader, **When** they attempt a privileged mutation, **Then** the system rejects the mutation while preserving allowed read access.
3. **Given** a workspace without a required entitlement, **When** a member attempts an entitlement-gated operation, **Then** the system rejects the operation server-side even if the frontend displays the action.

---

### User Story 5 - Preserve Operator Access Separately (Priority: P2)

Elsa Control operators retain a separate admin access path for operational and emergency use without making the admin API key a customer login mechanism.

**Why this priority**: The current admin dashboard key flow is useful as an operator fallback, but customer-facing login and workspace tenancy must not depend on a shared admin secret.

**Independent Test**: Can be tested by verifying operator-only admin endpoints remain protected by operator authorization, customer tokens cannot access operator-only functions, and operator credentials do not create customer workspace memberships.

**Acceptance Scenarios**:

1. **Given** an operator uses the existing admin access path, **When** they perform operator-only catalog or entitlement operations, **Then** those operations remain available according to operator authorization.
2. **Given** a customer user with a valid trusted identity, **When** they attempt an operator-only operation, **Then** the system rejects the request.
3. **Given** an operator signs in through the fallback admin path, **When** account workspace APIs are called, **Then** the system does not infer customer account membership from the shared operator credential.

---

### User Story 6 - Register Workflow Engines In Environments (Priority: P3)

A workspace member registers one or more workflow engines as concrete Elsa application deployments attached to an environment, so the platform can show health, capabilities, credentials status, and supported runtime controls without treating hosting infrastructure as uniform.

**Why this priority**: Deployment management needs a durable model for "an Elsa workflows application instance running somewhere" before the platform can safely deploy configuration, query runtime state, or expose operational controls.

**Independent Test**: Can be tested by creating an environment, registering a workflow engine with an endpoint and credential reference, discovering its supported capabilities, and verifying that unsupported controls are hidden or rejected.

**Acceptance Scenarios**:

1. **Given** a workspace administrator with deployment entitlement, **When** they register a workflow engine for an environment, **Then** the system stores the engine endpoint, credential reference, display metadata, health status, and capability set as workspace-owned data.
2. **Given** a registered workflow engine reports support for pausing processing and reloading configuration, **When** the user opens the environment operations view, **Then** only those supported engine-level controls are available.
3. **Given** no hosting provider adapter is configured for the engine, **When** the user looks for host infrastructure controls such as restarting a Kubernetes pod or recycling an app service, **Then** those controls are unavailable and the platform does not present a generic restart action.

---

### User Story 7 - Submit Workflow Artifacts And Promote Desired State Across Environments (Priority: P3)

A workspace member authors workflows in Elsa Studio, submits published workflow snapshots to Elsa Control as immutable deployable artifacts, combines those artifacts with feature flags, shell configuration, runtime configuration, secret references, observability bindings, and engine target bindings as versioned desired state, then promotes reviewed revisions from dev to test, stage, and production.

**Why this priority**: Users need a safe deployment flow where the source of truth is not whichever workflow engine was edited most recently. Elsa Studio remains the authoring surface and Elsa runtimes remain the execution surface, while Elsa Control owns the immutable artifacts that can be diffed, validated, approved, deployed, promoted, and rolled back.

**Independent Test**: Can be tested by submitting a workflow artifact from an Elsa Studio integration, creating two environment revisions that reference artifact versions, comparing them, promoting selected changes into a target environment, validating required secrets and engine compatibility, creating a durable deployment command, having a runtime integration claim/report the command, and recording the deployed revision.

**Acceptance Scenarios**:

1. **Given** a workflow is authored in Elsa Studio, **When** the user chooses "Submit to Elsa Control" from a Elsa Control-integrated Studio installation, **Then** Elsa Control stores an immutable deployable workflow artifact with safe metadata and a content hash without requiring Elsa Studio or the Elsa runtime to change their existing authoring or execution storage behavior.
2. **Given** a workflow artifact is available in Elsa Control desired state, **When** the user promotes it to test, **Then** the platform shows the workflow artifact, configuration, feature, shell, secret-reference, and observability differences before deployment.
3. **Given** a target environment is missing a required secret reference or the registered engine lacks a required capability, **When** the user attempts deployment, **Then** validation fails before any target engine state is changed.
4. **Given** deployment validation succeeds, **When** the user starts deployment, **Then** Elsa Control records a deployment run and command that the target runtime integration can claim by pull/sync, fetch after webhook notification, or receive by direct push according to the environment's configured transport.
5. **Given** production is running revision 12 and revision 14 causes a regression, **When** an authorized user chooses rollback, **Then** the platform can redeploy a previously known-good revision and records the rollback as a deployment event.

---

### User Story 8 - Observe And Govern Runtime Environments (Priority: P3)

A workspace member uses Elsa Control as a central cockpit for environment health, structured logs, console streams, traces, metrics, deployment history, secret-reference status, and drift between desired and observed engine state.

**Why this priority**: Cross-environment governance is the main reason to centralize this in Elsa Control rather than leaving every runtime concern inside one Elsa Studio connection.

**Independent Test**: Can be tested by configuring observability bindings for an environment, pulling logs and traces from the configured backends, correlating them with a deployment revision, and detecting when live engine state differs from desired state.

**Acceptance Scenarios**:

1. **Given** an environment has log, trace, metric, and console bindings, **When** the user opens the environment cockpit, **Then** the platform retrieves runtime telemetry from the configured providers and scopes results to the workspace and environment.
2. **Given** a workflow engine has live configuration that differs from the last deployed desired-state revision, **When** the platform checks drift, **Then** it reports the difference without silently overwriting either side.
3. **Given** Elsa Studio and Elsa Control both expose access to secrets or configuration, **When** a user changes a value through either surface, **Then** the change is governed by the same workspace authorization, provider-backed secret rules, audit metadata, and desired-state policy.

---

### User Story 9 - Use An Agentic Elsa Control Copilot (Priority: P3)

A workspace member uses an AI assistant inside Elsa Control to investigate workspace state, explain differences, draft deployment plans, propose fixes, and prepare operational actions across environments while the platform keeps identity, workspace authorization, approval, and audit controls authoritative.

**Why this priority**: Cross-environment deployment and operations become too complex for users to inspect manually at scale. A copilot experience can reduce operational effort only if it is grounded in the user's actual workspace access, cannot bypass roles or entitlements, and never performs risky changes without explicit approval.

**Independent Test**: Can be tested by asking the assistant to summarize an environment, compare desired and observed state, propose a promotion or remediation plan, and attempt privileged actions with users that have different roles and entitlements. The assistant must see only authorized data, produce reviewable plans, and require platform-mediated approval before mutations.

**Acceptance Scenarios**:

1. **Given** a workspace member asks the assistant why production differs from staging, **When** the assistant inspects desired-state revisions, deployment history, engine health, drift, and observability bindings, **Then** it returns a scoped explanation with referenced workspace artifacts and does not expose data from other workspaces.
2. **Given** a workspace administrator asks the assistant to promote a reviewed revision to production, **When** the assistant drafts the deployment plan, **Then** it shows the proposed target environment, affected engines, validation checks, required secrets, expected changes, all-or-nothing approval boundary, and rollback option before any deployment is started.
3. **Given** a workspace reader asks the assistant to restart an engine, change a secret reference, or deploy a revision, **When** the assistant attempts to prepare or execute the action, **Then** the platform blocks the mutation because the user's current role and entitlements do not allow it.
4. **Given** an assistant-generated plan includes an action that can mutate desired state, runtime state, secrets, observability configuration, deployment targets, or hosting infrastructure, **When** the user approves or rejects the plan, **Then** the platform records the assistant session, proposed action, deciding account, workspace, environment, validation result, and final outcome in the audit trail.

---

### Edge Cases

- OIDC configuration is incomplete, disabled, or references an unavailable provider metadata endpoint; customer login is unavailable with an operator-visible configuration error rather than silently accepting an unverified identity.
- A trusted identity token is expired, has the wrong audience, has an untrusted issuer, or lacks a stable subject; the request is rejected before account or workspace data is read or created.
- The identity provider callback fails because of invalid state, replay, mismatched redirect URI, authorization denial, or code exchange failure; no customer account, workspace, or session is created.
- A browser refreshes or opens a second tab while the customer session is valid; workspace context loads from the established Elsa Control identity without requiring caller-supplied account or workspace IDs.
- A browser still holds an expired, revoked, or invalid access token/session; the console clears unusable auth state, returns to sign-in, and does not retry workspace APIs with forged headers.
- A deployment uses a backend-for-frontend session instead of direct browser bearer tokens; API authorization still resolves the same trusted issuer and subject and never trusts frontend-provided membership data.
- The same email address appears under different issuer and subject pairs; account linkage remains based on trusted issuer and subject, not email alone.
- A user is removed from a workspace while their browser still holds a valid session; subsequent workspace requests use current membership and deny access.
- A workspace is soft-deleted; no private records owned by that workspace are exposed or mutable through customer APIs.
- A caller supplies account ID, workspace ID, role, entitlement, or membership claims that conflict with server records; server records are authoritative.
- Workspace source URLs or deployment target metadata contain secrets or tokens; customer-facing responses do not expose raw secrets.
- Public catalog data and health endpoints remain available to anonymous users where already designed as public.
- A workflow engine is unreachable, presents invalid credentials, or has an untrusted certificate; deployment and control operations fail closed while preserving existing desired state.
- A requested runtime control is not advertised by the workflow engine or a configured hosting adapter; the platform rejects the operation instead of guessing how to perform it.
- A hosting provider can restart infrastructure but the workflow engine can only reload shells or configuration; the UI and audit trail distinguish host operations from engine API operations.
- A runtime is behind a firewall or private network; runtime pull/sync remains available without requiring inbound access from Elsa Control.
- A webhook notification is delayed, duplicated, or lost; the runtime can still discover pending deployment commands from the platform-owned command queue.
- A runtime claims a command but stops reporting progress; Elsa Control marks the run stale or recovery-required without automatically issuing a duplicate apply.
- Required secret values are missing, inaccessible, or stored in a provider unavailable to the target environment; deployment validation fails before applying changes.
- Observability storage is unavailable or returns partial results; the environment cockpit reports telemetry degradation without changing engine state.
- Live workflow engine state drifts from the platform-owned desired state; the platform reports drift and requires an explicit reconcile, import, or redeploy action.
- A Elsa Control-integrated Elsa Studio installation still exposes the old direct runtime Publish command; the UI must clearly distinguish direct runtime publishing from "Submit to Elsa Control" and must not imply that submission makes the workflow immediately executable.
- Runtime tenant manifests and tenant-specific deployment reconciliation are not implemented by the identity foundation; they remain later deployment-platform scope but must fit under the workspace/environment model.
- The assistant cannot retrieve required context because a provider is unavailable or the user lacks access; it reports the missing context and does not invent state, secrets, approvals, or validation results.
- An assistant prompt asks for cross-workspace data, operator-only data, raw secrets, hidden credentials, or a role bypass; the platform denies the tool access even if the assistant attempts to request it.
- Workspace data inspected by the assistant contains adversarial instructions, misleading workflow names, deployment descriptions, environment metadata, or observability payloads; the platform treats that content as untrusted data and prevents assistant responses from revealing data outside the user's authorized scope.
- An assistant proposes a deployment, rollback, runtime control, secret change, or desired-state mutation; the platform requires explicit user approval through normal workspace authorization before execution.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST authenticate customer users from a trusted identity context that provides a verifiable issuer and stable subject.
- **FR-001a**: System MUST expose a pluggable Elsa Control identity adapter boundary so deployments can configure Generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, trusted backend, or custom integrations without changing account/workspace tenancy behavior.
- **FR-001b**: System MUST support configurable claim mapping for subject, display name, and email metadata.
- **FR-001c**: System MUST provide a customer login flow for the console that supports a standards-based OIDC authorization-code flow with PKCE or an equivalent backend-mediated flow that verifies the provider response before creating a platform-authenticated context.
- **FR-001d**: System MUST provide customer logout behavior that clears platform session or token state and, when configured, redirects to or invokes the upstream identity provider logout endpoint.
- **FR-001e**: System MUST expose enough non-secret runtime configuration for the console to determine whether customer login is enabled, which sign-in entry point to use, and how to recover from missing or expired customer identity.
- **FR-001f**: System MUST attach customer authentication to console API calls through a secure session or bearer-token mechanism without storing provider secrets in the browser.
- **FR-001g**: System MUST support provider-specific issuer, audience, redirect URI, post-logout redirect URI, scope, and claim mapping configuration for Microsoft Entra, Auth0, Keycloak, and generic OIDC-compatible providers.
- **FR-001h**: System MUST fail closed when provider metadata, signing keys, state validation, code exchange, token validation, or required claims cannot be verified.
- **FR-002**: System MUST reject browser-supplied user IDs, account IDs, workspace memberships, roles, or entitlements as authority for customer identity.
- **FR-003**: System MUST map each trusted `issuer + subject` pair to one catalog-local account through an external identity record.
- **FR-004**: System MUST create one personal workspace and owner membership for a first-time trusted identity.
- **FR-005**: System MUST make workspace membership the primary authorization boundary for customer-owned records in this slice; `specs/031-organization-tenancy` supersedes the root tenant boundary with Organization.
- **FR-006**: System MUST allow authenticated users to list only active workspaces they are members of.
- **FR-007**: System MUST enforce workspace membership for every customer-owned record read or write.
- **FR-008**: System MUST enforce workspace role requirements for privileged workspace operations.
- **FR-009**: System MUST enforce workspace entitlement snapshots for entitlement-gated operations.
- **FR-010**: System MUST ensure public catalog endpoints expose only public catalog-owned data and never workspace-owned private data to anonymous callers.
- **FR-011**: System MUST ensure authenticated workspace package, source, builder, saved configuration, deployment target, deployment run, and managed runtime operations expose only records visible to the selected workspace.
- **FR-012**: System MUST keep Elsa Control identity separate from operator authentication.
- **FR-013**: System MUST preserve an operator-authorized path for platform administration and entitlement management.
- **FR-014**: System MUST prevent customer identities from invoking operator-only operations unless separately granted operator authorization.
- **FR-015**: System MUST update profile metadata from trusted identity context without changing stable account identity.
- **FR-016**: System MUST evaluate workspace membership and role from current server-side records on every workspace-scoped request.
- **FR-017**: System MUST record enough audit metadata to identify the account, external identity, workspace, membership role, and operator/customer authorization path involved in security-sensitive operations.
- **FR-018**: System MUST provide a local or test-only trusted identity mode that cannot be enabled accidentally as a browser-supplied production identity mechanism.
- **FR-019**: System MUST define the boundary between platform workspaces and future runtime tenant/deployment tenant scopes so later features can add nested tenant concepts without changing the account/workspace model.
- **FR-020**: System MUST define a workflow application grouping within a workspace so related environments, workflow engines, desired state, deployments, and observability bindings can be managed together.
- **FR-021**: System MUST define an environment as a workspace-owned deployment target context, such as dev, test, stage, or production, that can contain workflow engine registrations, desired state, secret references, observability bindings, and deployment history.
- **FR-022**: System MUST represent a workflow engine as a registered Elsa workflows application deployment with endpoint metadata, credential reference, health status, advertised capabilities, and optional hosting provider metadata.
- **FR-023**: System MUST store workflow engine credentials as secret references or provider-backed handles and MUST NOT expose raw engine API credentials in customer-facing responses, audit logs, workflow artifacts, or desired-state revisions.
- **FR-024**: System MUST categorize runtime controls by boundary: workflow operations, workflow engine API operations, shell operations, and hosting infrastructure operations.
- **FR-025**: System MUST expose only controls supported by the workflow engine capability set or an explicitly configured hosting provider adapter.
- **FR-026**: System MUST NOT provide a generic "restart" operation unless the target capability defines whether the action restarts workflow processing, reloads configuration, restarts a shell, or restarts hosting infrastructure.
- **FR-027**: System MUST treat platform-owned immutable deployable artifacts and desired-state revisions as the canonical deployment source of truth and treat workflow engine state as applied or observed state.
- **FR-028**: System MUST version environment desired state including workflow artifact references, feature settings, shell configuration, runtime configuration, secret references, observability bindings, and engine target bindings.
- **FR-029**: System MUST allow authorized users to compare desired-state revisions across environments before promotion or deployment.
- **FR-030**: System MUST validate required secrets, engine reachability, engine capabilities, workspace entitlements, and operation-specific roles before applying a desired-state revision to an environment.
- **FR-031**: System MUST record deployment attempts, deployed revisions, validation failures, rollbacks, actor identity, target environment, target engine, and resulting status.
- **FR-032**: System MUST support rollback by redeploying a previously recorded desired-state revision when the target environment and engine capabilities remain compatible.
- **FR-033**: System MUST represent runtime deployment work as durable platform-owned deployment commands linked to deployment runs, with command identity, target runtime, artifact identity, action, idempotency key, expiration, status, and safe diagnostics.
- **FR-034**: System MUST allow runtime integrations to consume deployment commands through transport-independent mechanisms including runtime pull/sync, webhook-triggered fetch, and direct platform push where explicitly configured.
- **FR-035**: System MUST prefer runtime pull/sync as the default transport for customer-hosted runtimes that cannot or should not expose inbound endpoints to Elsa Control.
- **FR-036**: System MUST require runtime integrations to report command acceptance, progress, validation result, apply result, final status, observed artifact digest, and safe diagnostics back to the platform deployment run.
- **FR-037**: System MUST make deployment commands idempotent and MUST NOT automatically issue duplicate apply commands when a claimed command becomes stale without explicit recovery handling.
- **FR-038**: System MUST model secrets as provider-backed references with environment scope, provider identity, external key/path, optional version policy, and verification status; secret values MUST remain outside workflow artifacts and desired-state revisions unless a future provider explicitly supports encrypted sealed-secret semantics.
- **FR-039**: System MUST allow environment observability bindings for structured logs, console streams, OpenTelemetry-compatible traces, and metrics without requiring Elsa Control to own the underlying telemetry stores.
- **FR-040**: System MUST scope environment observability results to the requesting workspace, environment, workflow engine, shell, workflow artifact, workflow instance, or deployment revision where the provider supports those dimensions.
- **FR-041**: System MUST detect and report drift between platform-owned desired state and observed workflow engine state without silently overwriting either side.
- **FR-042**: System MUST support an opt-in Elsa Studio integration that handles the Studio publish lifecycle by offering a "Submit to Elsa Control" command that creates a platform-owned deployable workflow artifact.
- **FR-043**: System MUST keep Elsa Studio and Elsa Control aligned on shared authorization, secret-provider, and audit rules when both surfaces expose workflow, configuration, or secret management.
- **FR-044**: System MUST position Elsa Studio as the single-engine authoring and runtime inspection surface, while Elsa Control owns deployable workflow artifacts, release readiness, cross-environment promotion, deployment, governance, fleet visibility, and workspace-level controls.
- **FR-045**: System MUST leave room for future runtime tenant and deployment tenant concepts as nested or environment-specific concerns under workspace ownership, without preventing Organization from becoming the root customer tenant boundary in `specs/031-organization-tenancy`.
- **FR-046**: System MUST expose an AI assistant boundary that can read, reason over, and summarize workspace-authorized platform context including workspaces, environments, engines, workflow artifacts, desired-state revisions, deployment history, drift, validation results, and observability metadata.
- **FR-047**: System MUST enforce the same current account, workspace membership, role, entitlement, and operator/customer separation rules for every assistant tool call as for direct API calls.
- **FR-048**: System MUST require explicit platform-mediated approval before an assistant can execute any mutation to desired state, deployments, runtime controls, engine registrations, secret references, observability bindings, entitlements, or workspace membership.
- **FR-049**: System MUST present assistant-generated action plans in a reviewable, versioned, immutable form that identifies target workspace, environment, engine, affected resources, validation checks, expected impact, required approvals, all-or-nothing execution boundary, and rollback or undo path where applicable.
- **FR-050**: System MUST audit assistant sessions that perform or prepare security-sensitive operations, including prompt/session identity, account, workspace, environment, proposed actions, tool calls, approvals, validation failures, executed mutations, and final outcomes.
- **FR-051**: System MUST prevent the assistant from exposing raw secrets, engine credentials, provider tokens, or data outside the user's authorized workspace scope.
- **FR-052**: System MUST distinguish assistant recommendations from executed platform state changes so users and auditors can tell whether the system only suggested an action or actually applied it.
- **FR-053**: System MUST treat workspace content supplied to the assistant as untrusted input and validate assistant responses so prompt injection in workflow artifacts, environment metadata, deployment descriptions, or observability data cannot cause disclosure beyond the requesting user's authorized scope.
- **FR-054**: System MUST enforce the all-or-nothing execution boundary for an approved assistant mutation plan; if any step fails, the platform MUST stop remaining steps, roll back or compensate already-applied steps where possible, and audit/report any residual partial state before allowing another plan execution.
- **FR-055**: System MUST support a future Git-backed desired-state mode where a workspace or organization can bind deployment desired state to a configured Git repository.
- **FR-056**: System MUST keep Git-backed desired-state files declarative and deterministic so semantically equivalent platform state does not produce noisy diffs.
- **FR-057**: System MUST store application topology, environment desired state, artifact descriptors, runtime configuration, infrastructure requirements, secret references, observability bindings, promotion results, and rollback targets as Git-versioned desired-state files when Git-backed mode is enabled.
- **FR-058**: System MUST NOT store artifact payload bytes, raw workflow payloads, raw secrets, connection strings, provider tokens, runtime credentials, deployment run events, runtime command events, health checks, drift observations, logs, metrics, traces, or audit history as Git-authored desired-state files.
- **FR-059**: System MUST keep artifact payloads in artifact storage and reference them from Git only by immutable identity, type, digest, safe metadata, compatibility hints, and payload reference metadata.
- **FR-060**: System MUST make promotion create or select a target desired-state revision by changing target environment desired-state references through a platform-mediated Git commit when Git-backed mode is enabled.
- **FR-061**: System MUST make rollback create a new desired-state revision that points back to a previously known-good artifact and configuration set rather than mutating history in place.
- **FR-062**: System MUST treat Elsa Control database revision records as indexed projections of Git commits, validation state, runtime coordination state, and audit metadata when Git-backed mode is enabled.
- **FR-063**: System MUST preserve non-Git operation for workspaces that have not enabled Git-backed desired-state storage.
- **FR-064**: System MUST make the desired-state revision commit identifier platform-managed in Git-backed mode and prevent clients from supplying arbitrary commit metadata as authority.

### Key Entities *(include if feature involves data)*

- **Trusted Identity Context**: Verified sign-in context for a customer user, containing issuer, subject, and optional profile metadata.
- **Elsa Control Identity Provider**: Configured adapter that verifies and normalizes customer identity from Generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, trusted backend, or custom integration.
- **Customer Login Session**: Browser-facing authenticated state created only after a provider response or token is verified, used by the console to call customer APIs without exposing provider secrets.
- **OIDC Client Configuration**: Deployment-owned settings for provider authority, client identifier, redirect URIs, logout behavior, scopes, expected audience, issuer, and claim mappings.
- **Account**: Catalog-local user record linked to trusted external identities and workspace memberships.
- **External Identity**: Stable mapping from trusted issuer and subject to one account.
- **Workspace**: Durable resource boundary for customer-owned platform data; starts as a personal workspace in this slice and becomes nested under Organization in `specs/031-organization-tenancy`.
- **Workspace Membership**: Relationship between an account and workspace, including active state and role.
- **Workspace Role**: Permission level for operations within a workspace, such as owner, administrator, source administrator, deployer, or reader.
- **Workspace Entitlement Snapshot**: Server-enforced capability and limit snapshot for a workspace.
- **Operator Principal**: Separate administrative identity used for platform operations and emergency access, not a customer account membership.
- **Customer-Owned Resource**: Any record whose visibility and mutation rights are scoped to a workspace, including private package sources, saved runtime configurations, deployment targets, deployment runs, and managed runtime environments.
- **Workflow Application**: Workspace-owned grouping for related workflow environments, engines, desired-state revisions, deployments, secrets, and observability configuration.
- **Environment**: Workspace-owned deployment context such as dev, test, stage, or production, containing desired state, workflow engine registrations, secret references, observability bindings, and deployment history.
- **Workflow Engine**: Registered Elsa workflows application instance running in any hosting environment, reachable through an endpoint and credential reference and described by health and capability metadata.
- **Engine Capability**: Advertised operation or feature supported by a workflow engine or hosting adapter, such as pause processing, reload configuration, restart shell, drain workers, or host restart.
- **Shell**: Runtime isolation unit inside a workflow engine that can have its own service configuration, feature set, and operational lifecycle when supported by the engine.
- **Workflow Artifact**: Elsa Control-owned immutable deployable snapshot produced by a Elsa Control-integrated Elsa Studio "Submit to Elsa Control" command or equivalent future ingestion path, containing opaque workflow definition content, safe metadata, source identifiers, schema/version information, and a content hash.
- **Artifact Submission**: Audited handoff from Elsa Studio or another producer into Elsa Control that creates a workflow artifact without making that artifact immediately executable in a runtime.
- **Desired-State Revision**: Versioned source of truth for an environment, including workflow artifact references, feature settings, shell configuration, runtime configuration, secret references, observability bindings, and target bindings.
- **Git-Backed Desired-State Store**: Optional workspace or organization repository binding where Elsa Control writes and reads deterministic desired-state files and records the resulting commit as the revision source.
- **Desired-State File**: Declarative file stored in Git that represents application topology, environment desired state, artifact descriptors, runtime configuration, infrastructure requirements, secret references, observability bindings, promotion results, or rollback targets without operational event history or secret values.
- **Desired-State Projection**: Elsa Control database record derived from a Git commit, used for fast reads, validation status, deployment coordination, cockpit summaries, and audit correlation.
- **Deployment**: Attempt to apply a desired-state revision to one environment and one or more workflow engines, with validation results, actor metadata, status, and rollback relationship.
- **Deployment Command**: Durable platform-owned instruction linked to a deployment run that asks a specific runtime integration to validate, dry-run, apply, or roll back one artifact or desired-state revision with idempotency and expiration metadata.
- **Runtime Sync Worker**: Optional runtime-side integration component that authenticates outbound to Elsa Control, discovers pending deployment commands, claims work, fetches artifacts, applies supported artifact types, and reports progress/results.
- **Deployment Webhook Notification**: Optional non-authoritative notification that tells a runtime integration a command may be available; the runtime still fetches the authoritative command from Elsa Control before acting.
- **Secret Reference**: Environment-scoped pointer to a secret stored in a provider such as engine storage, Azure Key Vault, or another configured provider, including verification and version policy metadata but not the raw secret value.
- **Observability Binding**: Environment-scoped connection to structured logs, console streams, OpenTelemetry-compatible traces, or metrics providers used by the platform cockpit.
- **AI Assistant Session**: Workspace-scoped copilot interaction that can inspect authorized platform context, produce explanations and action plans, invoke approved platform tools, and emit audit records for proposed and executed actions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time user with a valid trusted identity can receive an account and personal workspace in a single request, and repeated requests return the same records.
- **SC-002**: A user can complete customer sign-in from the console through a configured OIDC-compatible provider and load workspace context without entering or supplying account, workspace, role, or entitlement identifiers.
- **SC-003**: Requests lacking trusted identity cannot create or access account or workspace context.
- **SC-004**: Invalid provider callbacks, expired tokens, wrong audience, wrong issuer, invalid state, or missing subject are rejected without creating customer account, identity, workspace, or session records.
- **SC-005**: Customer sign-out clears console authentication state so the next workspace API call requires a new trusted identity.
- **SC-006**: Cross-workspace access tests prove a user cannot read or mutate another workspace's customer-owned records even when they know the workspace ID or resource ID.
- **SC-007**: Public catalog browsing continues to work anonymously while workspace-owned sources and packages remain hidden from anonymous users.
- **SC-008**: Role and entitlement tests prove privileged and entitlement-gated operations are denied server-side when the caller lacks the required membership, role, or entitlement.
- **SC-009**: Operator-only operations remain available through operator authorization and are denied to ordinary customer identities.
- **SC-010**: Security-sensitive operations produce audit metadata that distinguishes account/workspace customer actions from operator actions.
- **SC-011**: A workspace administrator can register a workflow engine in an environment using a secret reference and see health plus supported capabilities without exposing raw credentials.
- **SC-012**: Unsupported runtime or hosting operations are unavailable or rejected with a clear capability error rather than executed through a guessed provider-specific action.
- **SC-013**: A user can compare two environment desired-state revisions and identify changed workflow artifacts, feature settings, shell configuration, runtime configuration, secret references, observability bindings, and target bindings before deployment.
- **SC-014**: Deployment validation blocks applying a revision when required secrets are missing, target engines are unreachable, capabilities are incompatible, or workspace entitlements are insufficient.
- **SC-015**: Deployment history identifies the applied revision, actor, environment, workflow engine, validation outcome, final status, and rollback source when applicable.
- **SC-016**: Runtime sync tests prove a runtime integration can claim a deployment command by outbound pull, verify the referenced artifact digest, report progress, and complete the deployment run without requiring inbound network access.
- **SC-017**: Duplicate webhook notifications or repeated command polls do not create duplicate apply attempts because deployment commands are idempotent.
- **SC-018**: Environment observability views can retrieve structured logs, console streams, OpenTelemetry-compatible traces, or metrics from configured providers and correlate results to a workspace environment and deployment revision.
- **SC-019**: Drift detection can report differences between desired state and observed engine state without mutating either source automatically.
- **SC-020**: Assistant access tests prove the assistant can summarize and compare only data visible to the requesting account's current workspace membership, role, and entitlement state.
- **SC-021**: Assistant mutation tests prove deployment, rollback, runtime control, secret-reference, and desired-state changes require explicit approval of the exact immutable plan artifact, enforce all-or-nothing plan execution on step failure, and produce audit records that distinguish proposed actions from executed actions.
- **SC-022**: Prompt-injection tests prove adversarial workspace content cannot cause assistant responses to expose raw secrets, hidden credentials, operator-only data, or data from another workspace.
- **SC-023**: In Git-backed mode, creating or promoting a desired-state revision produces a deterministic Git commit whose files reference artifact identity, digest, runtime configuration, feature settings, infrastructure requirements, secret references, observability bindings, and target bindings without raw payloads or secrets.
- **SC-024**: In Git-backed mode, rollback creates a new commit that restores a previous desired-state artifact and configuration reference set while preserving prior commits and deployment history.
- **SC-025**: Workspaces without Git-backed mode can still create, compare, promote, deploy, and roll back platform-owned desired-state revisions through existing versioned records.
- **SC-026**: Git projection tests prove the Elsa Control database can rebuild or validate environment desired-state summaries from a Git commit and still keep deployment runs, runtime commands, health, drift, approval, and audit records outside Git-authored desired-state files.

## Assumptions

- Workspace was the platform tenant boundary for this feature slice; `specs/031-organization-tenancy` supersedes that root-boundary decision with Organization while preserving workspace resource isolation.
- A customer may belong to multiple workspaces, but personal workspace creation is the first self-service path.
- Organization workspaces, invitations, billing purchase flows, and central customer-service ownership are later features unless already represented by entitlement snapshots.
- Existing account/workspace records from the custom-feed feature are reused and normalized instead of creating a second identity model.
- Existing API-key dashboard access remains an operator fallback while customer login moves to trusted identity.
- Customer-facing login should prefer a backend-for-frontend session built on a standards-based OIDC authorization-code flow, with direct SPA bearer-token ownership reserved for deployments that explicitly require it; both patterns must produce the same trusted identity contract for workspace tenancy.
- The console is allowed to improve UX by hiding unavailable workspace actions, but backend membership, role, entitlement, and identity checks remain authoritative.
- Provider-specific app registration, client secret storage, and redirect URI ownership are deployment responsibilities; Elsa Control stores only configuration required to validate provider responses and establish customer-authenticated API access.
- Elsa Control owns immutable deployable workflow artifacts, desired-state revisions, deployment orchestration, environment governance, and fleet visibility; workflow engines own runtime execution and expose runtime controls through explicit capabilities.
- The live workflow engine is observed or applied state, not the canonical source of truth for cross-environment deployment.
- Workflow artifacts are created by an opt-in Elsa Studio integration that replaces direct-runtime publishing in Elsa Control-integrated installations with the command "Submit to Elsa Control"; future producers such as CLI import, CI automation, or package upload should create the same artifact type.
- "Submit to Elsa Control" means creating an immutable deployable artifact in Elsa Control; it does not mean release approval, promotion, deployment, or immediate runtime execution.
- Desired state is expected to be stored as platform-owned versioned records or an equivalent workspace-controlled versioned store so environment state can be diffed, promoted, audited, and rolled back.
- Git-backed desired-state storage is the preferred long-term workspace-controlled versioned store, but it is optional at first so local, development, and simpler installations can keep using platform-owned versioned records.
- Git-backed desired-state files represent declarative intent only. Elsa Control operational history remains in Elsa Control-controlled persistence even when the desired state came from Git.
- A Git-backed desired-state repository may be workspace-scoped initially and organization-scoped later after `specs/031-organization-tenancy` is fully adopted.
- Git commits authored by Elsa Control should be deterministic, reviewable, and attributable to the account or automation that caused the desired-state change.
- External Git edits are a future reconciliation concern; the initial GitOps spec can start with Elsa Control-mediated writes before adding bidirectional import, pull request workflows, branch policies, or conflict resolution.
- Deployment runs and commands are the authoritative coordination records. Webhooks, direct runtime endpoints, polling, and long-polling are interchangeable transports around that command contract, not independent sources of deployment truth.
- Runtime pull/sync is the preferred default for customer-hosted environments; direct platform push is reserved for environments with explicit inbound connectivity and trust configuration.
- Webhook notifications are advisory triggers only and should carry enough information to fetch a command, not enough information to apply an artifact without consulting Elsa Control.
- Environment names such as dev, test, stage, and production are conventions, not hard-coded tenant semantics.
- There is no canonical workflow engine environment; canonical state is the versioned desired state that can be deployed to any compatible environment.
- Secret values are managed by configured providers; workflow artifacts and desired-state revisions store references, requirements, and policies rather than plaintext values.
- Elsa Studio remains the preferred single-engine workflow authoring and runtime inspection experience, while Elsa Control provides the central cockpit for artifact management, release readiness, environment promotion, governance, deployment, observability, and fleet operations.
- Elsa runtime tenant concepts and deployment tenant overlays are separate nested concerns and are intentionally deferred from the identity foundation, but future deployment features must preserve workspace ownership as the outer platform boundary.
- The AI assistant is a copilot for platform operations, not an independent authority; it acts through the same workspace-scoped APIs, validation, approval, and audit controls as a user-driven workflow.
- Assistant memory, retrieval, and tool execution are scoped by workspace and current user authorization, and any future cross-workspace or operator assistant mode requires separate operator authorization.
- Assistant-generated mutation plans are approved and executed as one atomic plan by default under FR-054; partial step approval is out of scope unless a future requirement defines compensating rollback behavior and per-step audit semantics.
- Assistant-generated mutation plans are frozen when presented for approval; execution uses that same plan artifact and must not regenerate or alter the action set after approval.
