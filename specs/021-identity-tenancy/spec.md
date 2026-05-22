# Feature Specification: Identity And Workspace Tenancy

**Feature Branch**: `codex/021-identity-tenancy`

**Created**: 2026-05-21

**Status**: Draft

**Input**: User description: "Proceed with the proposed plan to get multitenancy done and OIDC/JWT login: make Workspace the platform tenant boundary, add real OIDC/JWT login, replace trusted browser-supplied identity with backend-derived account and workspace context, centralize workspace authorization, preserve operator fallback access, and define the deployment-platform direction for workflow engines, environments, desired state, promotion, secrets, observability, and future runtime tenant reconciliation."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign In With Trusted Identity (Priority: P1)

A customer user signs in through a configured platform identity provider adapter, such as generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, or a trusted frontend/backend integration, and is recognized by the platform without supplying account or workspace identifiers directly.

**Why this priority**: Every customer-owned feature depends on a trustworthy user identity before account provisioning, workspace membership, private catalog visibility, saved configurations, deployment targets, or managed runtime ownership can be safe.

**Independent Test**: Can be tested by presenting a valid trusted identity context, requesting the current user's workspace context, and verifying the platform derives account and workspace data from the identity rather than from caller-supplied user IDs.

**Acceptance Scenarios**:

1. **Given** a valid trusted identity with issuer and subject, **When** the user requests their platform context, **Then** the system maps the identity to a catalog-local account and returns the workspaces the account belongs to.
2. **Given** the same trusted identity returns later with changed display name or email, **When** the user requests their platform context, **Then** the system keeps the same account identity and updates profile metadata.
3. **Given** a request without a trusted identity, **When** the caller requests account or workspace context, **Then** the system rejects the request and does not create account, identity, or workspace data.

---

### User Story 2 - First Sign-In Creates A Personal Workspace (Priority: P1)

A first-time signed-in user receives a durable personal workspace that becomes the default tenant boundary for customer-owned platform data.

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

Platform operators retain a separate admin access path for operational and emergency use without making the admin API key a customer login mechanism.

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

### User Story 7 - Promote Desired State Across Environments (Priority: P3)

A workspace member manages workflow definitions, feature flags, shell configuration, runtime configuration, secret references, observability bindings, and engine target bindings as versioned desired state, then promotes reviewed revisions from dev to test, stage, and production.

**Why this priority**: Users need a safe deployment flow where the source of truth is not whichever workflow engine was edited most recently, and where production changes can be diffed, validated, approved, deployed, and rolled back.

**Independent Test**: Can be tested by creating two environment revisions, comparing them, promoting selected changes into a target environment, validating required secrets and engine compatibility, and recording the deployed revision.

**Acceptance Scenarios**:

1. **Given** a workflow is authored in dev and committed into desired state, **When** the user promotes it to test, **Then** the platform shows the workflow, configuration, feature, shell, secret-reference, and observability differences before deployment.
2. **Given** a target environment is missing a required secret reference or the registered engine lacks a required capability, **When** the user attempts deployment, **Then** validation fails before any target engine state is changed.
3. **Given** production is running revision 12 and revision 14 causes a regression, **When** an authorized user chooses rollback, **Then** the platform can redeploy a previously known-good revision and records the rollback as a deployment event.

---

### User Story 8 - Observe And Govern Runtime Environments (Priority: P3)

A workspace member uses Elsa Platform as a central cockpit for environment health, structured logs, console streams, traces, metrics, deployment history, secret-reference status, and drift between desired and observed engine state.

**Why this priority**: Cross-environment governance is the main reason to centralize this in Elsa Platform rather than leaving every runtime concern inside one Elsa Studio connection.

**Independent Test**: Can be tested by configuring observability bindings for an environment, pulling logs and traces from the configured backends, correlating them with a deployment revision, and detecting when live engine state differs from desired state.

**Acceptance Scenarios**:

1. **Given** an environment has log, trace, metric, and console bindings, **When** the user opens the environment cockpit, **Then** the platform retrieves runtime telemetry from the configured providers and scopes results to the workspace and environment.
2. **Given** a workflow engine has live configuration that differs from the last deployed desired-state revision, **When** the platform checks drift, **Then** it reports the difference without silently overwriting either side.
3. **Given** Elsa Studio and Elsa Platform both expose access to secrets or configuration, **When** a user changes a value through either surface, **Then** the change is governed by the same workspace authorization, provider-backed secret rules, audit metadata, and desired-state policy.

---

### User Story 9 - Use An Agentic Platform Copilot (Priority: P3)

A workspace member uses an AI assistant inside Elsa Platform to investigate workspace state, explain differences, draft deployment plans, propose fixes, and prepare operational actions across environments while the platform keeps identity, workspace authorization, approval, and audit controls authoritative.

**Why this priority**: Cross-environment deployment and operations become too complex for users to inspect manually at scale. A copilot experience can reduce operational effort only if it is grounded in the user's actual workspace access, cannot bypass roles or entitlements, and never performs risky changes without explicit approval.

**Independent Test**: Can be tested by asking the assistant to summarize an environment, compare desired and observed state, propose a promotion or remediation plan, and attempt privileged actions with users that have different roles and entitlements. The assistant must see only authorized data, produce reviewable plans, and require platform-mediated approval before mutations.

**Acceptance Scenarios**:

1. **Given** a workspace member asks the assistant why production differs from staging, **When** the assistant inspects desired-state revisions, deployment history, engine health, drift, and observability bindings, **Then** it returns a scoped explanation with referenced workspace artifacts and does not expose data from other workspaces.
2. **Given** a workspace administrator asks the assistant to promote a reviewed revision to production, **When** the assistant drafts the deployment plan, **Then** it shows the proposed target environment, affected engines, validation checks, required secrets, expected changes, all-or-nothing approval boundary, and rollback option before any deployment is started.
3. **Given** a workspace reader asks the assistant to restart an engine, change a secret reference, or deploy a revision, **When** the assistant attempts to prepare or execute the action, **Then** the platform blocks the mutation because the user's current role and entitlements do not allow it.
4. **Given** an assistant-generated plan includes an action that can mutate desired state, runtime state, secrets, observability configuration, deployment targets, or hosting infrastructure, **When** the user approves or rejects the plan, **Then** the platform records the assistant session, proposed action, deciding account, workspace, environment, validation result, and final outcome in the audit trail.

---

### Edge Cases

- A trusted identity token is expired, has the wrong audience, has an untrusted issuer, or lacks a stable subject; the request is rejected before account or workspace data is read or created.
- The same email address appears under different issuer and subject pairs; account linkage remains based on trusted issuer and subject, not email alone.
- A user is removed from a workspace while their browser still holds a valid session; subsequent workspace requests use current membership and deny access.
- A workspace is soft-deleted; no private records owned by that workspace are exposed or mutable through customer APIs.
- A caller supplies account ID, workspace ID, role, entitlement, or membership claims that conflict with server records; server records are authoritative.
- Workspace source URLs or deployment target metadata contain secrets or tokens; customer-facing responses do not expose raw secrets.
- Public catalog data and health endpoints remain available to anonymous users where already designed as public.
- A workflow engine is unreachable, presents invalid credentials, or has an untrusted certificate; deployment and control operations fail closed while preserving existing desired state.
- A requested runtime control is not advertised by the workflow engine or a configured hosting adapter; the platform rejects the operation instead of guessing how to perform it.
- A hosting provider can restart infrastructure but the workflow engine can only reload shells or configuration; the UI and audit trail distinguish host operations from engine API operations.
- Required secret values are missing, inaccessible, or stored in a provider unavailable to the target environment; deployment validation fails before applying changes.
- Observability storage is unavailable or returns partial results; the environment cockpit reports telemetry degradation without changing engine state.
- Live workflow engine state drifts from the source-controlled desired state; the platform reports drift and requires an explicit reconcile, import, or redeploy action.
- Runtime tenant manifests and tenant-specific deployment reconciliation are not implemented by the identity foundation; they remain later deployment-platform scope but must fit under the workspace/environment model.
- The assistant cannot retrieve required context because a provider is unavailable or the user lacks access; it reports the missing context and does not invent state, secrets, approvals, or validation results.
- An assistant prompt asks for cross-workspace data, operator-only data, raw secrets, hidden credentials, or a role bypass; the platform denies the tool access even if the assistant attempts to request it.
- Workspace data inspected by the assistant contains adversarial instructions, misleading workflow names, deployment descriptions, environment metadata, or observability payloads; the platform treats that content as untrusted data and prevents assistant responses from revealing data outside the user's authorized scope.
- An assistant proposes a deployment, rollback, runtime control, secret change, or desired-state mutation; the platform requires explicit user approval through normal workspace authorization before execution.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST authenticate customer users from a trusted identity context that provides a verifiable issuer and stable subject.
- **FR-001a**: System MUST expose a pluggable platform identity adapter boundary so deployments can configure Generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, trusted backend, or custom integrations without changing account/workspace tenancy behavior.
- **FR-001b**: System MUST support configurable claim mapping for subject, display name, and email metadata.
- **FR-002**: System MUST reject browser-supplied user IDs, account IDs, workspace memberships, roles, or entitlements as authority for customer identity.
- **FR-003**: System MUST map each trusted `issuer + subject` pair to one catalog-local account through an external identity record.
- **FR-004**: System MUST create one personal workspace and owner membership for a first-time trusted identity.
- **FR-005**: System MUST make workspace membership the primary platform tenant boundary for customer-owned records.
- **FR-006**: System MUST allow authenticated users to list only active workspaces they are members of.
- **FR-007**: System MUST enforce workspace membership for every customer-owned record read or write.
- **FR-008**: System MUST enforce workspace role requirements for privileged workspace operations.
- **FR-009**: System MUST enforce workspace entitlement snapshots for entitlement-gated operations.
- **FR-010**: System MUST ensure public catalog endpoints expose only public catalog-owned data and never workspace-owned private data to anonymous callers.
- **FR-011**: System MUST ensure authenticated workspace package, source, builder, saved configuration, deployment target, deployment run, and managed runtime operations expose only records visible to the selected workspace.
- **FR-012**: System MUST keep platform identity separate from operator authentication.
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
- **FR-023**: System MUST store workflow engine credentials as secret references or provider-backed handles and MUST NOT expose raw engine API credentials in customer-facing responses, audit logs, or source-controlled desired state.
- **FR-024**: System MUST categorize runtime controls by boundary: workflow operations, workflow engine API operations, shell operations, and hosting infrastructure operations.
- **FR-025**: System MUST expose only controls supported by the workflow engine capability set or an explicitly configured hosting provider adapter.
- **FR-026**: System MUST NOT provide a generic "restart" operation unless the target capability defines whether the action restarts workflow processing, reloads configuration, restarts a shell, or restarts hosting infrastructure.
- **FR-027**: System MUST treat source-controlled desired state as the canonical deployment source of truth and treat workflow engine state as applied or observed state.
- **FR-028**: System MUST version environment desired state including workflow definitions, feature settings, shell configuration, runtime configuration, secret references, observability bindings, and engine target bindings.
- **FR-029**: System MUST allow authorized users to compare desired-state revisions across environments before promotion or deployment.
- **FR-030**: System MUST validate required secrets, engine reachability, engine capabilities, workspace entitlements, and operation-specific roles before applying a desired-state revision to an environment.
- **FR-031**: System MUST record deployment attempts, deployed revisions, validation failures, rollbacks, actor identity, target environment, target engine, and resulting status.
- **FR-032**: System MUST support rollback by redeploying a previously recorded desired-state revision when the target environment and engine capabilities remain compatible.
- **FR-033**: System MUST model secrets as provider-backed references with environment scope, provider identity, external key/path, optional version policy, and verification status; secret values MUST remain outside source-controlled desired state unless a future provider explicitly supports encrypted sealed-secret semantics.
- **FR-034**: System MUST allow environment observability bindings for structured logs, console streams, OpenTelemetry-compatible traces, and metrics without requiring Elsa Platform to own the underlying telemetry stores.
- **FR-035**: System MUST scope environment observability results to the requesting workspace, environment, workflow engine, shell, workflow definition, workflow instance, or deployment revision where the provider supports those dimensions.
- **FR-036**: System MUST detect and report drift between source-controlled desired state and observed workflow engine state without silently overwriting either side.
- **FR-037**: System MUST keep Elsa Studio and Elsa Platform aligned on shared authorization, secret-provider, and audit rules when both surfaces expose workflow, configuration, or secret management.
- **FR-038**: System MUST position Elsa Studio as the single-engine authoring and runtime inspection surface, while Elsa Platform owns cross-environment promotion, deployment, governance, fleet visibility, and workspace-level controls.
- **FR-039**: System MUST leave room for future runtime tenant and deployment tenant concepts as nested or environment-specific concerns under workspace ownership, rather than replacing workspace as the platform tenant boundary.
- **FR-040**: System MUST expose an AI assistant boundary that can read, reason over, and summarize workspace-authorized platform context including workspaces, environments, engines, desired-state revisions, deployment history, drift, validation results, and observability metadata.
- **FR-041**: System MUST enforce the same current account, workspace membership, role, entitlement, and operator/customer separation rules for every assistant tool call as for direct API calls.
- **FR-042**: System MUST require explicit platform-mediated approval before an assistant can execute any mutation to desired state, deployments, runtime controls, engine registrations, secret references, observability bindings, entitlements, or workspace membership.
- **FR-043**: System MUST present assistant-generated action plans in a reviewable, versioned, immutable form that identifies target workspace, environment, engine, affected resources, validation checks, expected impact, required approvals, all-or-nothing execution boundary, and rollback or undo path where applicable.
- **FR-044**: System MUST audit assistant sessions that perform or prepare security-sensitive operations, including prompt/session identity, account, workspace, environment, proposed actions, tool calls, approvals, validation failures, executed mutations, and final outcomes.
- **FR-045**: System MUST prevent the assistant from exposing raw secrets, engine credentials, provider tokens, or data outside the user's authorized workspace scope.
- **FR-046**: System MUST distinguish assistant recommendations from executed platform state changes so users and auditors can tell whether the system only suggested an action or actually applied it.
- **FR-047**: System MUST treat workspace content supplied to the assistant as untrusted input and validate assistant responses so prompt injection in workflow definitions, environment metadata, deployment descriptions, or observability data cannot cause disclosure beyond the requesting user's authorized scope.
- **FR-048**: System MUST enforce the all-or-nothing execution boundary for an approved assistant mutation plan; if any step fails, the platform MUST stop remaining steps, roll back or compensate already-applied steps where possible, and audit/report any residual partial state before allowing another plan execution.

### Key Entities *(include if feature involves data)*

- **Trusted Identity Context**: Verified sign-in context for a customer user, containing issuer, subject, and optional profile metadata.
- **Platform Identity Provider**: Configured adapter that verifies and normalizes customer identity from Generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, trusted backend, or custom integration.
- **Account**: Catalog-local user record linked to trusted external identities and workspace memberships.
- **External Identity**: Stable mapping from trusted issuer and subject to one account.
- **Workspace**: Durable tenant boundary for customer-owned platform data; starts as a personal workspace and can support organization workspaces later.
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
- **Desired-State Revision**: Versioned source of truth for an environment, including workflows, feature settings, shell configuration, runtime configuration, secret references, observability bindings, and target bindings.
- **Deployment**: Attempt to apply a desired-state revision to one environment and one or more workflow engines, with validation results, actor metadata, status, and rollback relationship.
- **Secret Reference**: Environment-scoped pointer to a secret stored in a provider such as engine storage, Azure Key Vault, or another configured provider, including verification and version policy metadata but not the raw secret value.
- **Observability Binding**: Environment-scoped connection to structured logs, console streams, OpenTelemetry-compatible traces, or metrics providers used by the platform cockpit.
- **AI Assistant Session**: Workspace-scoped copilot interaction that can inspect authorized platform context, produce explanations and action plans, invoke approved platform tools, and emit audit records for proposed and executed actions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time user with a valid trusted identity can receive an account and personal workspace in a single request, and repeated requests return the same records.
- **SC-002**: Requests lacking trusted identity cannot create or access account or workspace context.
- **SC-003**: Cross-workspace access tests prove a user cannot read or mutate another workspace's customer-owned records even when they know the workspace ID or resource ID.
- **SC-004**: Public catalog browsing continues to work anonymously while workspace-owned sources and packages remain hidden from anonymous users.
- **SC-005**: Role and entitlement tests prove privileged and entitlement-gated operations are denied server-side when the caller lacks the required membership, role, or entitlement.
- **SC-006**: Operator-only operations remain available through operator authorization and are denied to ordinary customer identities.
- **SC-007**: Security-sensitive operations produce audit metadata that distinguishes account/workspace customer actions from operator actions.
- **SC-008**: A workspace administrator can register a workflow engine in an environment using a secret reference and see health plus supported capabilities without exposing raw credentials.
- **SC-009**: Unsupported runtime or hosting operations are unavailable or rejected with a clear capability error rather than executed through a guessed provider-specific action.
- **SC-010**: A user can compare two environment desired-state revisions and identify changed workflows, feature settings, shell configuration, runtime configuration, secret references, observability bindings, and target bindings before deployment.
- **SC-011**: Deployment validation blocks applying a revision when required secrets are missing, target engines are unreachable, capabilities are incompatible, or workspace entitlements are insufficient.
- **SC-012**: Deployment history identifies the applied revision, actor, environment, workflow engine, validation outcome, final status, and rollback source when applicable.
- **SC-013**: Environment observability views can retrieve structured logs, console streams, OpenTelemetry-compatible traces, or metrics from configured providers and correlate results to a workspace environment and deployment revision.
- **SC-014**: Drift detection can report differences between desired state and observed engine state without mutating either source automatically.
- **SC-015**: Assistant access tests prove the assistant can summarize and compare only data visible to the requesting account's current workspace membership, role, and entitlement state.
- **SC-016**: Assistant mutation tests prove deployment, rollback, runtime control, secret-reference, and desired-state changes require explicit approval of the exact immutable plan artifact, enforce all-or-nothing plan execution on step failure, and produce audit records that distinguish proposed actions from executed actions.
- **SC-017**: Prompt-injection tests prove adversarial workspace content cannot cause assistant responses to expose raw secrets, hidden credentials, operator-only data, or data from another workspace.

## Assumptions

- Workspace is the platform tenant boundary for this feature.
- A customer may belong to multiple workspaces, but personal workspace creation is the first self-service path.
- Organization workspaces, invitations, billing purchase flows, and central customer-service ownership are later features unless already represented by entitlement snapshots.
- Existing account/workspace records from the custom-feed feature are reused and normalized instead of creating a second identity model.
- Existing API-key dashboard access remains an operator fallback while customer login moves to trusted identity.
- Elsa Platform owns desired state, deployment orchestration, environment governance, and fleet visibility; workflow engines own runtime execution and expose runtime controls through explicit capabilities.
- The live workflow engine is observed or applied state, not the canonical source of truth for cross-environment deployment.
- Desired state is expected to be stored in a workspace-controlled source repository or equivalent versioned store so environment state can be diffed, promoted, audited, and rolled back.
- Environment names such as dev, test, stage, and production are conventions, not hard-coded tenant semantics.
- There is no canonical workflow engine environment; canonical state is the versioned desired state that can be deployed to any compatible environment.
- Secret values are managed by configured providers; source-controlled desired state stores references, requirements, and policies rather than plaintext values.
- Elsa Studio remains the preferred single-engine workflow authoring and runtime inspection experience, while Elsa Platform provides the central cockpit for environment promotion, governance, deployment, observability, and fleet operations.
- Elsa runtime tenant concepts and deployment tenant overlays are separate nested concerns and are intentionally deferred from the identity foundation, but future deployment features must preserve workspace ownership as the outer platform boundary.
- The AI assistant is a copilot for platform operations, not an independent authority; it acts through the same workspace-scoped APIs, validation, approval, and audit controls as a user-driven workflow.
- Assistant memory, retrieval, and tool execution are scoped by workspace and current user authorization, and any future cross-workspace or operator assistant mode requires separate operator authorization.
- Assistant-generated mutation plans are approved and executed as one atomic plan by default under FR-048; partial step approval is out of scope unless a future requirement defines compensating rollback behavior and per-step audit semantics.
- Assistant-generated mutation plans are frozen when presented for approval; execution uses that same plan artifact and must not regenerate or alter the action set after approval.
