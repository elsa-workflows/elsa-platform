# PRD: Weaver Copilot Agent

**Feature Branch**: `034-weaver-copilot-agent`

**Created**: 2026-06-07

**Status**: Draft

**Input**: Investigate how Elsa Platform can use the GitHub Copilot SDK to turn Weaver from a placeholder chat drawer into a full agentic workspace assistant, then update the Weaver PRD with the recommended product and technical direction.

## Research Summary

GitHub Copilot SDK is now a viable foundation for Weaver. GitHub announced the SDK as generally available on 2026-06-02, with stable API support for embedding Copilot's agent runtime into applications and services. The SDK exposes the same agent runtime behind Copilot, including planning, tool invocation, streaming, multi-turn sessions, and file operations. Official SDKs exist for TypeScript, Python, Go, .NET, Rust, and Java; Elsa Platform should use the .NET SDK in the backend because the platform is already a .NET control plane.

Primary sources:

- GitHub changelog, 2026-06-02: https://github.blog/changelog/2026-06-02-copilot-sdk-is-now-generally-available/
- GitHub Copilot SDK repository: https://github.com/github/copilot-sdk
- Getting started: https://docs.github.com/en/copilot/how-tos/copilot-sdk/getting-started
- Custom agents: https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/custom-agents
- Hooks: https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/hooks
- MCP: https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/mcp
- Authentication: https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth/authenticate
- BYOK: https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth/byok
- Session persistence: https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/session-persistence

## Product Thesis

Weaver should not be a generic chat box. Weaver should be an agentic control-plane assistant that understands the current organization, workspace, route, page entity, permissions, deployment topology, package catalog state, runtime state, and audit requirements. It should explain what is happening, investigate platform state through scoped tools, draft operational plans, and execute only approved plans through Elsa Platform APIs.

The GitHub Copilot SDK changes the implementation strategy: instead of building our own agent loop, tool orchestration, streaming, sub-agent delegation, and session protocol, Elsa Platform should wrap the Copilot SDK with platform-specific tools, permission gates, audit hooks, and UI plan review.

## Goals

- Replace the static Weaver drawer with a real backend-backed assistant.
- Use Copilot SDK sessions as Weaver's agent runtime.
- Make Weaver page-aware and workspace-aware from the first prompt.
- Provide domain-specific tools for safe inspection of deployments, artifacts, runtimes, packages, organizations, workspaces, tiers, and audit events.
- Support agent-generated plans for deployment, rollback, engine registration, runtime controls, secret reference remediation, and setup guidance.
- Require explicit platform approval for all mutating actions.
- Persist user-visible session transcripts, plans, approvals, tool calls, and outcomes in Elsa Platform audit storage.
- Use hooks and permission handlers to enforce authorization and prevent prompt injection, cross-workspace access, and secret disclosure.

## Non-Goals

- Weaver will not bypass normal workspace permissions or deployment permission grants.
- Weaver will not receive raw secrets, provider credentials, OAuth tokens, database connection strings, or hidden operator-only data.
- Weaver will not execute shell/file tools against the Elsa host in production.
- Weaver will not directly mutate platform state from free-form chat.
- Weaver will not become a general code-editing agent inside the product console.
- Cross-organization or platform-operator Weaver mode is out of scope for v1.

## Users

- Workspace reader: asks Weaver to explain current state and recommended next checks.
- Deployment operator: asks Weaver to investigate drift, compare revisions, prepare deployment plans, and guide runtime actions.
- Workspace admin: reviews Weaver plans and approves sensitive operations.
- Platform operator: future role for cross-workspace support and incident workflows.

## Recommended Architecture

### Backend Runtime

Add a backend Weaver service, preferably `Elsa.Platform.Weaver` or an API/Core pair under existing platform boundaries. The service owns Copilot SDK client lifecycle, session creation/resume, tool registration, permissions, event streaming, and audit integration.

Use the .NET SDK package `GitHub.Copilot.SDK`. The SDK documentation states .NET 8.0 or later is supported, which fits the platform's .NET 10 direction. The SDK can spawn the bundled CLI runtime or connect to an external CLI server. For production, prefer a controlled hosted runtime process with explicit `COPILOT_HOME`/base directory, bounded concurrency, telemetry, and lifecycle supervision.

### Session Model

Each Weaver interaction belongs to one Elsa Platform assistant session:

- Organization ID
- Workspace ID
- Account ID
- Current route and page context
- Optional application/environment/engine/revision/artifact IDs
- Copilot SDK session ID
- Mode: Inspect, Plan, Operate
- Status: Active, WaitingForUser, WaitingForApproval, Executing, Completed, Failed, Archived

Use Copilot SDK resumable sessions for continuity, but do not rely on SDK session storage as the authoritative audit store. Elsa Platform must persist its own transcript, tool-call metadata, plan snapshots, approvals, and execution outcomes.

### Agent Model

Create Weaver as an orchestrator with scoped sub-agents:

- `weaver-orchestrator`: routes intent, explains current context, decides whether to inspect, plan, or ask for clarification.
- `deployment-investigator`: reads applications, environments, engines, revisions, runs, validation, blockers, drift, and history.
- `deployment-planner`: drafts immutable deployment, promotion, rollback, and remediation plans.
- `runtime-operator`: prepares runtime control plans and can execute approved runtime commands.
- `catalog-guide`: answers package, source, artifact, runtime builder, and compatibility questions.
- `secret-steward`: reasons about secret references and providers without seeing raw secret values.
- `organization-guide`: handles account, organization, workspace, membership, entitlement, and permission questions.

Each sub-agent must receive an explicit tool allowlist. The default orchestrator should not have direct access to high-context or mutating tools unless it delegates through a scoped agent.

### Tooling Strategy

Use Copilot SDK custom tools for first-party platform operations. Avoid exposing generic shell, filesystem, or edit tools in production Weaver sessions.

Read tools can skip permission prompts when they are workspace-scoped and already enforce authorization server-side:

- `get_current_context`
- `list_workspace_applications`
- `get_application_detail`
- `list_application_revisions`
- `get_revision_detail`
- `get_environment_detail`
- `list_engine_registrations`
- `get_engine_health`
- `get_deployment_runs`
- `get_drift_report`
- `get_artifact_detail`
- `get_package_detail`
- `get_workspace_permissions`
- `search_audit_events`

Plan tools produce immutable proposed actions, not mutations:

- `draft_deployment_plan`
- `draft_promotion_plan`
- `draft_rollback_plan`
- `draft_engine_registration_plan`
- `draft_secret_reference_plan`
- `draft_runtime_control_plan`

Mutation tools require approval and must execute existing platform APIs:

- `execute_approved_deployment_plan`
- `execute_approved_rollback_plan`
- `execute_approved_runtime_control`
- `execute_approved_engine_registration`

MCP should be optional. Use Copilot SDK MCP integration for external systems only when needed, such as GitHub issues, documentation repositories, or customer support systems. First-party Elsa Platform data should be exposed through in-process custom tools or an internal HTTP tool boundary with the same authorization model as platform APIs.

### Hooks And Guardrails

Use Copilot SDK hooks as a mandatory safety layer:

- Session start hook injects organization, workspace, account, route, visible entity IDs, role summary, and tool policy.
- User prompt submitted hook attaches current page context and strips browser/page-injected instructions from untrusted UI data.
- Pre-tool hook enforces workspace scope, authorization, tool allowlist, mutation approval status, argument validation, and risk classification.
- Post-tool hook redacts secrets, local paths, credentials, tokens, and oversized payloads before results are sent back to the model.
- Tool failure hook records diagnostics and supplies safe retry guidance.
- Session end hook writes final transcript and usage metadata.
- Error hook converts SDK/runtime failures into user-facing platform errors and audit records.

### Authentication And Model Provider

Support two deployment modes:

1. GitHub Copilot-backed mode using GitHub OAuth, GitHub App user tokens, or environment tokens.
2. BYOK mode using enterprise-managed OpenAI, Azure AI Foundry, Anthropic, or OpenAI-compatible providers.

For Elsa Cloud or enterprise deployment, BYOK should be strongly considered because it avoids tying platform assistant availability to individual GitHub Copilot seats. GitHub Copilot-backed mode remains useful for self-hosted developer/admin deployments.

The chosen provider, model, reasoning effort, and request metadata must be stored per session for audit and support. API keys must never be persisted in Copilot session state; provider credentials must come from platform secret providers at session start/resume.

### UI Requirements

Weaver remains a global drawer, but it becomes more than chat:

- Header shows mode, current workspace, and whether Weaver is read-only or can prepare plans.
- Suggested prompts adapt to the current route.
- Responses cite platform objects and tool results.
- Tool activity is visible as a collapsible timeline.
- Plans render as structured cards with target, impact, validation, blockers, approvals, execution boundary, and rollback path.
- Risky actions are approved from plan cards, not by typing "yes" into chat.
- The drawer supports streaming responses, queued follow-up prompts, abort/cancel, and session resume.
- Users can open a dedicated session detail/audit page from any Weaver conversation.

## Functional Requirements

- **FR-001**: System MUST create backend Weaver sessions that bind Copilot SDK session identity to Elsa account, organization, workspace, route, and page context.
- **FR-002**: System MUST stream Copilot SDK assistant events to the console drawer without requiring page refresh.
- **FR-003**: System MUST expose only workspace-authorized read tools to Weaver.
- **FR-004**: System MUST expose mutating capabilities only as approved plan execution tools.
- **FR-005**: System MUST use Copilot SDK custom agents or equivalent configuration to separate investigation, planning, runtime operation, catalog guidance, and secret-reference reasoning.
- **FR-006**: System MUST use Copilot SDK permission handlers and hooks to approve, deny, or alter tool calls before execution.
- **FR-007**: System MUST deny any tool call whose arguments refer to a workspace, organization, application, environment, engine, revision, artifact, account, or secret outside the requesting user's authorized scope.
- **FR-008**: System MUST redact raw secrets, provider credentials, tokens, connection strings, and unsafe payloads before tool results reach the model.
- **FR-009**: System MUST persist assistant transcripts, tool calls, proposed plans, approval decisions, execution attempts, and outcomes in platform storage.
- **FR-010**: System MUST distinguish assistant-generated recommendations from executed state changes in UI and audit records.
- **FR-011**: System MUST require platform-mediated approval before executing any plan that mutates desired state, deployment runs, runtime controls, engine registrations, secret references, observability bindings, membership, entitlement, or workspace setup.
- **FR-012**: System MUST execute approved plans through existing domain services and APIs rather than bypassing validation or persistence layers.
- **FR-013**: System MUST support resumable sessions without treating SDK session persistence as the only source of truth.
- **FR-014**: System MUST support BYOK provider configuration without persisting provider API keys in Copilot session state.
- **FR-015**: System MUST expose OpenTelemetry traces for SDK session creation, prompt handling, tool execution, plan approval, and plan execution.
- **FR-016**: System MUST provide a kill switch to disable Weaver globally, per organization, or per workspace.
- **FR-017**: System MUST provide usage limits, timeout handling, and cancellation for long-running agent turns.
- **FR-018**: System MUST treat page content, workflow names, environment metadata, artifact metadata, observability payloads, and logs as untrusted data.
- **FR-019**: System MUST allow administrators to inspect Weaver audit events and plan history.
- **FR-020**: System MUST keep current direct UI workflows functional when Weaver is disabled.

## Key User Stories

### User Story 1 - Explain Current Page

A workspace member opens any console page and asks Weaver what the page means and what needs attention. Weaver reads only authorized current-page context, explains the state, identifies blockers or missing setup, and cites the relevant platform entities.

Acceptance:

1. Given a deployment environment page with blockers, when the user asks "what is wrong here?", then Weaver explains blockers from deployment validation, engine health, revision state, and permissions.
2. Given the user lacks deployment read permission, when they ask the same question, then Weaver reports insufficient access instead of leaking hidden data.

### User Story 2 - Investigate Drift And Deployment History

A deployment operator asks Weaver why production differs from staging. Weaver inspects revisions, runs, artifacts, engine health, drift, validation, and audit history, then returns a concise explanation and next checks.

Acceptance:

1. Weaver identifies the source revision, deployed revision, desired revision, and drift records.
2. Weaver clearly labels missing context instead of inventing missing data.
3. Weaver includes no raw artifact payloads or secrets.

### User Story 3 - Draft A Promotion Plan

A deployment operator asks Weaver to prepare a promotion from Test to Production. Weaver drafts an immutable plan with target revision, target environment, affected engines, validation checks, blockers, required approvals, execution boundary, and rollback path.

Acceptance:

1. The plan can be saved and reviewed independently of chat.
2. The plan cannot execute until approved through the platform UI.
3. If validation fails, execution remains disabled and blockers are shown.

### User Story 4 - Execute An Approved Plan

A user with the correct permission approves a Weaver-generated deployment plan. Weaver executes existing platform APIs and reports progress, final status, and audit links.

Acceptance:

1. The execution records the approving account, workspace, plan version, validations, and commands.
2. Duplicate approval or execution attempts are idempotent.
3. Partial failures stop remaining steps and produce a safe diagnostic.

### User Story 5 - Agentic Secret Reference Guidance

A user asks Weaver how to fix missing secret references. Weaver identifies which references are missing or unverified, explains the selected secret store/provider, and drafts a remediation plan without retrieving or displaying raw secret values.

Acceptance:

1. Raw secret values are never requested from tools.
2. Weaver can propose where a reference should be configured.
3. Any secret-store mutation requires approval and normal permissions.

## Data Model

- **WeaverSession**: Session ID, Copilot session ID, account, organization, workspace, route context, mode, provider, model, status, created/updated timestamps.
- **WeaverMessage**: Session ID, role, content, created timestamp, source, visible/redacted flags.
- **WeaverToolCall**: Session ID, tool name, arguments hash or redacted arguments, result summary, status, duration, trace ID, authorization result.
- **WeaverPlan**: Immutable plan version, plan type, target entities, expected impact, validation results, rollback path, risk classification, status.
- **WeaverPlanApproval**: Plan ID, version, approving account, decision, timestamp, permission snapshot, confirmation ID.
- **WeaverPlanExecution**: Plan ID, execution status, linked deployment run/runtime command/audit records, error summary, timestamps.

## Open Questions

- Should Weaver be enabled first for deployments only, or for the whole platform shell?
- Should Elsa Cloud use BYOK by default, GitHub Copilot-backed mode by default, or support both from day one?
- Should Copilot SDK run inside the API process, a separate worker/service, or a per-tenant session runner?
- What is the initial model policy and reasoning-effort policy per workspace tier?
- How long should SDK session state and platform transcripts be retained?
- Should organization admins be able to review all Weaver transcripts, or only audited plan/execution summaries?
- Do we need a separate secret-store registry spec before Weaver can safely operate on secret references?

## MVP Slice

1. Backend Weaver service using .NET Copilot SDK.
2. Session create/resume APIs and streaming event endpoint.
3. Page-aware global drawer wired to backend sessions.
4. Read-only deployment investigation tools.
5. Hooks for prompt enrichment, tool authorization, redaction, and audit.
6. Structured plan drafting for deployment promotion and rollback.
7. Approval-gated execution for one existing safe deployment-run path.
8. Audit and session detail view.

## Implementation Notes

- Start with all built-in shell, filesystem, and file-edit tools disabled or unavailable in hosted production sessions.
- Prefer custom tools over generic API-calling tools so every tool has explicit argument schemas and authorization checks.
- Mark safe read tools as permission-skipping only after server-side authorization and workspace scoping are covered by tests.
- Do not let the model synthesize direct HTTP calls to platform APIs. Tool handlers should call typed domain services.
- Store full tool arguments only when safe; otherwise store hashes plus redacted summaries.
- Use OpenTelemetry trace IDs to connect Copilot SDK events, platform API calls, and audit records.
- Use a feature flag so Weaver can remain visible as a prototype while backend capability rolls out per workspace.

## Success Metrics

- Users can answer "what is wrong on this deployment page?" without manually opening more than one related page.
- Operators can draft a valid promotion or rollback plan in under one minute.
- All Weaver mutations have corresponding approval and audit records.
- Zero raw secret exposures in assistant transcripts and tool-call logs.
- Zero successful cross-workspace data access attempts in authorization tests.
- At least 80% of read-only Weaver answers cite retrieved platform objects rather than unsupported assertions.

## Risks

- Copilot SDK runtime process management may need careful hosting and scaling in a multi-tenant ASP.NET deployment.
- BYOK support avoids Copilot seat dependency but introduces provider configuration, quota, and key-management work.
- Agentic tools can generate too much context unless tools are narrowly scoped and results are summarized.
- Prompt injection risk is high because Weaver reads user-authored platform metadata; hooks and tool boundaries must treat all retrieved content as untrusted.
- Generic built-in tools are powerful but inappropriate for hosted platform operations unless heavily restricted.
