# Console UX Contract: Elsa Control Healing

## Navigation and routes

- Navigation group: `Operations`
- Navigation label: `Healing`
- Overview: `/workspace/healing`
- Configuration: `/workspace/healing/applications/{applicationId}/configuration`
- Components and ownership: `/workspace/healing/applications/{applicationId}/components`
- Incident detail: `/workspace/healing/incidents/{incidentId}`
- Audit: `/workspace/healing/audit`

All pages operate in the selected workspace and reuse the existing workspace context. Unauthorized cross-workspace IDs render the same not-found/forbidden-safe state.

## Overview

Show:

- enabled/disabled/stopped applications and environments
- open incidents by severity/state
- repairable versus observation-only incidents
- active/blocked repair attempts and PRs
- environments pending deployment/verification
- healed, failed-verification, superseded, and waived outcomes
- provider/inference usage and current budgets

Filters include application, environment, severity, incident state, repairability, and time window. Empty state explains that automatic discovery requires the Elsa Control OpenTelemetry module and application configuration.

## Configuration

Authorized users can independently configure:

- discovery
- repair dispatch
- automatic merge
- classification thresholds/debounce
- attempt/time/concurrency/inference/repository budgets
- verification window
- application/environment kill switches
- evidence, path, and merge policies

Dangerous changes show the exact affected application/repository and require confirmation. Read-only users see effective policy and blockers but no mutation controls.

## Components and source ownership

Show component manifests by revision and trust state. The component table includes package/assembly/application identity, version, content hash status, source metadata suggestion, binding, ambiguity, and repair eligibility.

Repository metadata suggestions are visually marked `Suggested—not authorized`. Activating a binding requires selecting the authorized GitHub installation/repository, target branch, immutable workflow identity, path policy, evidence policy, and merge policy.

Overlaps show every conflicting rule and keep repair disabled until resolved.

## Incident detail

Header shows normalized problem, severity, status, first/last seen, count, affected environments/revisions, component attribution, and whether producing revision is verified.

Tabs:

- `Overview`: safe causal summary, classification, threshold, work item, current blockers.
- `Occurrences`: bounded occurrence metadata; no raw protected payloads.
- `Attribution`: component candidates, evidence basis, confidence, binding decision.
- `Repair`: attempts, evidence tier, reproduction class, agent result, PR, checks, merge eligibility.
- `Environments`: deployment observations and verification timeline per affected environment.
- `Audit`: safe chronological decision history.

The UI must state prominently when a PR is unreproduced or revision-unverified and therefore human-merge-only.

## Commands

Authorized controls:

- retry repair
- stop repair
- request elevated evidence
- waive one environment verification
- activate emergency stop

Each control displays current eligibility, required permission, target, consequences, and confirmation where required. A successful request shows the durable command/decision state rather than assuming immediate provider completion.

## Safety states

- Never render raw provider tokens, app private keys, connection strings, protected tenant inputs, or unrestricted request/workflow payloads.
- Truncated/omitted evidence is explicitly labeled.
- `Merged`, `Deployed`, `Deployed—unverified`, and `Healed` use distinct labels and copy.
- Failed, stale, unknown, or ambiguous auto-merge gates display as blockers, never warnings that can be ignored.
- Provider outages and worker delays preserve the last authoritative Elsa Control state with a stale timestamp.

## Accessibility and responsiveness

- All statuses have text in addition to color/icon.
- Tables support keyboard navigation and meaningful headings.
- Confirmation dialogs restore focus and identify the target.
- Timelines and gate matrices have screen-reader summaries.
- Narrow layouts convert wide incident tables to labeled cards without hiding safety state.
