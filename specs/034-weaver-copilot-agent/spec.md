# Feature Specification: Weaver Copilot Agent

**Feature Branch**: `034-weaver-copilot-agent`

**Created**: 2026-06-07

**Status**: Draft

**Input**: User description: "Use GitHub Speckit to implement Weaver as specified in the PRD. Use GitHub Copilot SDK to implement Weaver as a full agentic AI, not merely a chat UI. Include documentation on API keys and configuration options."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Explain Current Workspace Page (Priority: P1)

A workspace member opens Weaver from any admin console page and asks what the current page means, what needs attention, and what their next useful action is. Weaver uses the current organization, workspace, account, route, and visible entity context to answer from authorized platform data only.

**Why this priority**: A page-aware, read-only assistant is the smallest useful Weaver experience and proves the core session, context, authorization, streaming, and redaction boundaries without creating operational risk.

**Independent Test**: Can be tested by opening Weaver on a deployment environment page, asking for a summary, and verifying that the response cites authorized environment, engine, revision, and validation state while denying hidden or unauthorized data.

**Acceptance Scenarios**:

1. **Given** a workspace member with deployment read access is viewing an environment page, **When** they ask Weaver "what is wrong here?", **Then** Weaver summarizes blockers, engine health, revision state, and next checks from authorized workspace data.
2. **Given** a workspace member lacks permission for a requested data area, **When** they ask Weaver to inspect that data, **Then** Weaver reports insufficient access and does not expose the data.
3. **Given** the current page contains workflow names, descriptions, metadata, or logs, **When** Weaver uses that data, **Then** it treats the content as untrusted evidence and not as instructions.

---

### User Story 2 - Investigate Deployments And Draft Plans (Priority: P1)

A deployment operator asks Weaver to investigate drift, compare revisions, diagnose blockers, or prepare a deployment, promotion, rollback, engine registration, secret reference, or runtime control plan. Weaver inspects scoped platform state and returns a structured immutable plan when an operational change is requested.

**Why this priority**: Weaver becomes genuinely agentic only when it can chain investigation tools, reason over domain state, and produce reviewable operational plans instead of prose-only advice.

**Independent Test**: Can be tested by asking Weaver to prepare a promotion from Test to Production and verifying that the saved plan contains target environment, source revision, target engines, validations, blockers, expected impact, approval boundary, and rollback path.

**Acceptance Scenarios**:

1. **Given** a deployment operator asks why Production differs from Test, **When** Weaver investigates, **Then** it correlates revisions, deployment runs, engine health, drift records, validation results, artifact references, and audit events.
2. **Given** an operator asks Weaver to prepare a promotion plan, **When** validation passes, **Then** Weaver creates a reviewable immutable plan with target entities, impact, execution boundary, and rollback path.
3. **Given** validation fails or required context is unavailable, **When** Weaver drafts a plan, **Then** the plan remains blocked and shows actionable reasons instead of guessing.

---

### User Story 3 - Approve And Execute Agent Plans (Priority: P2)

A permitted user reviews a Weaver-generated plan, approves it through platform UI, and Weaver executes the approved plan through existing Elsa Control domain services and APIs while recording audit events and progress.

**Why this priority**: Executing approved plans closes the loop from assistant to operational agent while preserving human authorization and platform control.

**Independent Test**: Can be tested by approving a valid deployment plan and verifying that the existing deployment run or runtime command flow is invoked, audit records are created, and duplicate approvals are idempotent.

**Acceptance Scenarios**:

1. **Given** a user without required mutation permissions reviews a plan, **When** they try to approve or execute it, **Then** the platform blocks execution.
2. **Given** a permitted user approves a valid plan, **When** execution begins, **Then** Weaver runs existing platform APIs, streams progress, and records the approving account, plan version, validation result, and outcome.
3. **Given** a plan execution partially fails, **When** Weaver reports the result, **Then** remaining steps stop and a safe diagnostic plus rollback or remediation guidance is recorded.

---

### User Story 4 - Configure Weaver Safely (Priority: P2)

A platform administrator configures Weaver using GitHub Copilot-backed or BYOK model provider settings, chooses availability at global, organization, or workspace scope, and verifies that runtime credentials, quotas, telemetry, and kill switches work.

**Why this priority**: A real agentic assistant cannot ship without a clear operational configuration path, especially for API keys, Copilot/BYOK choices, model selection, and disabling behavior.

**Independent Test**: Can be tested by configuring Weaver with a fake or test model provider, confirming disabled states, validating missing-key errors, and checking documented environment variables/options.

**Acceptance Scenarios**:

1. **Given** Weaver is disabled globally, **When** a user opens the drawer, **Then** the UI explains Weaver is unavailable and no model session starts.
2. **Given** a BYOK provider is configured, **When** Weaver starts a session, **Then** provider credentials are read from configured secret sources and are not persisted in assistant transcripts or SDK session state.
3. **Given** configuration is invalid or a provider is unreachable, **When** a session is requested, **Then** Weaver returns an actionable configuration error and records a safe diagnostic.

---

### User Story 5 - Audit And Review Agent Activity (Priority: P3)

Administrators and auditors review Weaver sessions, prompts, tool calls, proposed plans, approvals, executions, failures, redactions, and trace references without exposing sensitive data.

**Why this priority**: Agentic operations need auditability and incident review. This can follow the MVP once the assistant and approval flow are present.

**Independent Test**: Can be tested by running one read-only session, one blocked plan, and one approved execution, then confirming that audit/session views show the correct actor, workspace, tools, plan state, approval, and outcome.

**Acceptance Scenarios**:

1. **Given** Weaver answers a read-only question, **When** an administrator views the session record, **Then** the prompt, answer summary, tool calls, and redaction markers are visible.
2. **Given** Weaver prepares or executes a sensitive plan, **When** audit records are inspected, **Then** the actor, workspace, target entities, approval decision, execution result, and trace IDs are recorded.
3. **Given** tool results include secret-like values, **When** records are stored or displayed, **Then** values are redacted or omitted.

### Edge Cases

- Weaver is opened without an active organization or workspace.
- Weaver is opened on a route whose entity was deleted, archived, or moved to another workspace.
- The active user changes organization, workspace, role, or permissions during an active session.
- A prompt asks for raw secrets, provider credentials, cross-workspace data, operator-only data, or authorization bypass.
- Workspace content contains adversarial instructions or misleading metadata.
- The Copilot SDK runtime process fails, times out, disconnects, or emits an unsupported event.
- A user submits another prompt while Weaver is still working.
- A plan references entities that changed after the plan was generated.
- BYOK credentials are unavailable on session resume.
- Model provider quota, policy, or network failures occur mid-turn.
- Tool results are too large for useful context.
- Existing direct UI workflows continue while Weaver is disabled or degraded.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST create backend Weaver sessions bound to account, organization, workspace, route, page context, mode, provider, model, and Copilot SDK session identity.
- **FR-002**: System MUST stream assistant messages, tool activity, errors, waiting states, and completion states to the console drawer.
- **FR-003**: System MUST provide Inspect, Plan, and Operate modes with mode-specific tool access and UI affordances.
- **FR-004**: System MUST expose only workspace-authorized read tools to Weaver, and each tool MUST enforce the same authorization as direct API calls.
- **FR-005**: System MUST support agent specialization for deployment investigation, deployment planning, runtime operation, catalog guidance, secret-reference reasoning, and organization guidance.
- **FR-006**: System MUST use tool allowlists, permission handlers, and pre-tool checks to deny unauthorized, unsafe, or out-of-scope tool calls.
- **FR-007**: System MUST redact secrets, provider credentials, tokens, connection strings, local paths, raw artifact payloads, and oversized unsafe data before tool results reach the model or persisted records.
- **FR-008**: System MUST treat all workspace-authored content and browser page content as untrusted evidence, not instructions.
- **FR-009**: System MUST draft operational changes as immutable reviewable plans before any mutation can execute.
- **FR-010**: System MUST require explicit platform-mediated approval before executing any plan that mutates desired state, deployment runs, runtime controls, engine registrations, secret references, observability bindings, memberships, entitlements, or workspace setup.
- **FR-011**: System MUST execute approved plans through existing domain services and APIs rather than direct database writes or generic HTTP calls synthesized by the model.
- **FR-012**: System MUST record assistant sessions, prompts, responses, tool calls, redaction decisions, plans, approvals, executions, failures, and outcomes in platform storage or audit records.
- **FR-013**: System MUST distinguish recommendations, saved plans, approved plans, queued execution, successful execution, failed execution, and rejected execution in UI and audit records.
- **FR-014**: System MUST support resumable sessions while keeping platform persistence authoritative over SDK-local session state.
- **FR-015**: System MUST support GitHub Copilot-backed and BYOK provider modes, including OpenAI-compatible, Azure AI Foundry, and Anthropic-style provider configuration where supported by the SDK.
- **FR-016**: System MUST never persist provider API keys in assistant messages, tool calls, plans, audit records, or Copilot SDK session state.
- **FR-017**: System MUST provide global, organization, and workspace feature flags or equivalent kill switches.
- **FR-018**: System MUST provide configuration documentation for enabling Weaver, provider choice, API keys, model selection, reasoning effort, runtime process settings, telemetry, limits, and disabling Weaver.
- **FR-019**: System MUST expose safe diagnostics for SDK/runtime/provider failures without leaking credentials or sensitive prompt content.
- **FR-020**: System MUST support cancellation or aborting active Weaver turns.
- **FR-021**: System MUST enforce timeouts, concurrency limits, and usage limits appropriate for hosted operation.
- **FR-022**: System MUST keep direct console workflows usable when Weaver is disabled, misconfigured, or unavailable.

### Key Entities *(include if feature involves data)*

- **Weaver Session**: Workspace-scoped assistant interaction linked to account, organization, route context, Copilot SDK session, provider, model, and status.
- **Weaver Message**: User or assistant message with content, redaction status, timestamps, and visibility metadata.
- **Weaver Tool Call**: One agent-requested operation with tool name, redacted arguments, authorization result, duration, status, and result summary.
- **Weaver Plan**: Immutable proposed operational change with type, target entities, validation state, impact, blockers, approval boundary, and rollback guidance.
- **Weaver Approval**: Human decision on a plan version, including deciding account, permission snapshot, confirmation metadata, and timestamp.
- **Weaver Execution**: Attempt to execute an approved plan, including linked deployment runs, runtime commands, audit records, status, diagnostics, and timestamps.
- **Weaver Provider Configuration**: Safe configuration describing provider type, model, reasoning effort, credential source, runtime process behavior, telemetry, limits, and enablement scope.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A permitted user can open Weaver on a deployment environment page and receive a page-aware explanation with cited platform data in under 10 seconds for seeded local data.
- **SC-002**: Unauthorized Weaver prompts for deployment data, cross-workspace data, or operator-only data are denied in all tested permission scenarios.
- **SC-003**: A deployment operator can generate a structured promotion or rollback plan with target, impact, validation, blockers, and rollback path without manually visiting more than one page.
- **SC-004**: All mutating Weaver actions require approval and produce audit/session records linking actor, workspace, plan version, tools, and outcome.
- **SC-005**: Secret-redaction tests prove no raw provider keys, tokens, connection strings, or secret values are returned to the model, UI, or persisted transcript.
- **SC-006**: Weaver configuration documentation enables a developer or administrator to configure a local BYOK provider and understand all supported settings without reading source code.
- **SC-007**: Existing console workflows remain usable with Weaver disabled or provider configuration missing.

## Assumptions

- The implementation will use the official GitHub Copilot SDK for .NET on the backend.
- The first shippable slice may use deterministic local/fake provider behavior for tests while retaining real Copilot SDK integration points behind configuration.
- Existing workspace identity, organization context, deployment permissions, action confirmations, deployment runs, runtime controls, audit patterns, and console layout will be reused.
- First-party Elsa Control data will be exposed through typed domain tools rather than generic filesystem, shell, or HTTP tools.
- BYOK is the preferred hosted/enterprise configuration path; GitHub Copilot-backed mode remains available when a deployment has appropriate GitHub authentication.
- A separate full secret-store registry may still be needed for deep secret reference management; Weaver v1 must not require raw secret access.
