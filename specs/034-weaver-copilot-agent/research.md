# Research: Weaver Copilot Agent

## Decision: Use GitHub Copilot SDK .NET as the Weaver runtime

**Rationale**: GitHub Copilot SDK is generally available as of 2026-06-02 and provides the agent loop, custom tools, custom agents, streaming, hooks, telemetry, session resume, and BYOK support. The .NET SDK matches Elsa Control's backend stack and avoids building a custom orchestration layer.

**Alternatives considered**:

- Direct OpenAI/Azure chat completions: simpler transport, but would require implementing the agent loop, tool orchestration, permission model, streaming protocol, and session resume.
- Browser-only assistant: easier UI, but cannot safely enforce workspace authorization, audit, or approved execution.
- Custom LangChain/Semantic Kernel orchestration: flexible, but duplicates agent runtime concerns already provided by Copilot SDK.

## Decision: Keep Weaver backend-hosted, not browser-hosted

**Rationale**: Workspace authorization, provider credentials, tool calls, redaction, and audit must be enforced on the server. The browser drawer should only send prompts/context and render streamed events/plans.

**Alternatives considered**:

- Direct browser-to-model calls: rejected because provider keys and workspace tool permissions would be exposed or unenforceable.
- Browser-hosted MCP/tool execution: rejected for hosted production because it cannot be trusted as the authorization boundary.

## Decision: Disable generic shell, filesystem, and file-edit tools in hosted sessions

**Rationale**: Weaver is a platform operations assistant, not a code editing agent inside the console. Hosted sessions should expose only typed Elsa Control tools with explicit schemas and authorization.

**Alternatives considered**:

- Allow built-in Copilot CLI tools: useful for developer agents, but too broad for a customer-facing control plane.
- Allow shell tools only to admins: still risky because model-generated commands can affect host infrastructure outside workspace scope.

## Decision: Use custom tools first; MCP only for optional external integrations

**Rationale**: First-party Elsa Control data can be exposed more safely through in-process typed tools that use existing services and permission checks. MCP remains useful later for GitHub, documentation, or customer-support systems.

**Alternatives considered**:

- Internal MCP for all platform tools: portable, but adds process and protocol overhead before there is a need.
- Generic HTTP API tool: rejected because it lets the model compose API calls outside narrowly approved schemas.

## Decision: Persist platform-owned session/audit records separately from SDK session state

**Rationale**: Copilot SDK persistence is useful for resume, but platform audit and compliance need durable, queryable records with redaction status, actor, workspace, plan version, approval, execution, and trace references.

**Alternatives considered**:

- Store only SDK session ID: insufficient for audit and review.
- Store complete raw transcripts and tool payloads: rejected because tool results may include sensitive or high-volume data.

## Decision: Support both GitHub Copilot-backed and BYOK provider modes

**Rationale**: Self-hosted/developer deployments may use GitHub Copilot authentication, while enterprise or cloud deployments often need provider-owned billing, policy, and API-key management. BYOK supports OpenAI-compatible, Azure AI Foundry, and Anthropic-style providers through the SDK.

**Alternatives considered**:

- GitHub Copilot-only: simpler but couples product capability to individual/user Copilot access.
- BYOK-only: safer for enterprise hosting but less convenient for development and evaluation.

## Decision: Start with deployment investigation and plan drafting

**Rationale**: Deployment pages already have rich workspace-scoped state, the current placeholder mentions deployment planning, and operational plans provide a clear path from read-only assistant to agentic behavior without unsafe immediate mutation.

**Alternatives considered**:

- Whole-platform assistant in one slice: too broad to verify thoroughly.
- Configuration-only first: necessary but not enough user value.
