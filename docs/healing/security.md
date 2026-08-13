# Valence Control Healing: Security and Operations

This document defines the production trust model for Valence Control Healing and the operator response when a boundary fails. Read [Getting Started](getting-started.md) for configuration and staged enablement.

## Security Invariants

The following rules are non-negotiable:

- Only post-redaction telemetry or an authorized explicit incident report can enter Healing.
- Monitored applications cannot choose repair repositories, workflows, branches, policies, or merge behavior.
- Repair agents never receive GitHub App credentials, installation tokens, deployment credentials, or unrestricted workspace access.
- Repository, telemetry, workflow, provider, and model content is untrusted data, never Valence Control instructions.
- Valence Control independently parses and validates every patch before repository mutation.
- Missing, failed, stale, ambiguous, or unknown automatic-merge evidence denies automatic merge.
- Merge is not healing; per-environment deployment and positive verification are required.
- Every security-sensitive decision is workspace-scoped, idempotent where applicable, and append-only audited.

## Trust Boundaries

| Boundary | Accepted authority | Rejected authority |
| --- | --- | --- |
| Telemetry intake | Authenticated telemetry source scoped to workspace/application/environment; Valence Control redaction and signal profile. | Repository routing, commands, credentials, or unbounded payloads embedded in logs. |
| Component attribution | Verified revision manifest plus deterministic stack/package/assembly evidence. | Repository suggestions in a manifest as authorization. |
| Repair authority | Active, owner-approved source ownership binding and trusted provider/path/evidence/merge policies. | GitHub issue text, labels, source files, agent output, or conflicting bindings. |
| Repair workflow | Signed GitHub OIDC claims bound to repository, workflow, immutable revision, run, attempt, audience, and nonce. | Long-lived workflow secret or bearer token with general Valence Control access. |
| Proposal | Bounded inert result envelope associated with one leased attempt. | Tool calls, natural-language commands, direct Git mutation, or claims not supported by structured evidence. |
| Publication | Trusted Valence Control publisher using a just-in-time repository-scoped GitHub App token. | Agent- or runner-held write token, unsafe diff, stale target, or self-modifying guardrail change. |
| Human command | Verified webhook, repository permission snapshot, linked Valence Control identity, workspace permission, and confirmation when required. | Comment or label text by itself. |
| Deployment | Authenticated deployment identity and idempotent observation. | Agent claim that a revision was deployed or healed. |

## Privacy and Redaction

The OpenTelemetry module redacts before invoking the durable Healing contributor. A request is acknowledged only after the normalized redacted envelope is stored in the Healing inbox. Never add a pre-redaction Healing sink.

Ordinary incident, agent, GitHub, audit, and usage projections must exclude:

- authorization headers, bearer tokens, API keys, client secrets, private keys, connection strings, and credential-bearing URLs;
- raw request/response bodies, unrestricted workflow inputs, prompts, source archives, and protected tenant content;
- local source paths, package-feed credentials, environment dumps, and provider error bodies that may echo secrets;
- unbounded stack, log, trace, or source context.

Evidence bundles are immutable, bounded, redacted, tiered, expiring, and tied to one attempt. They carry provenance, digest, and explicit omitted/truncated markers. Evidence elevation requires `healing.evidence.elevate`, a stated purpose, requested fields/tier, target-bound confirmation, and an explicit host authorization policy. The default authorization policy denies elevation.

Managed proposal source context is a separate untrusted boundary. The repository workflow reads only bounded .NET source and project files from immutable Git blobs, excludes configuration and credential-shaped paths, and never executes collector-time repository code. Valence Control validates the original bundle digest, then omits any file containing credential markers, private-key material, token-shaped values, credential assignments, credential-bearing URLs, or unsafe control characters before constructing an inference prompt. Omission reduces analysis quality but never relaxes the no-secret rule.

If sensitive text appears in an incident, agent input, issue, PR, or ordinary audit view:

1. Activate the appropriate kill switch and stop affected attempts.
2. Treat the exposed value as compromised and rotate it at its source.
3. Restrict or remove the external GitHub content using repository incident-response procedures; preserve necessary audit evidence in a protected system.
4. Identify whether the leak entered before redaction, through an elevation decision, provider response, or projection bug.
5. Add a regression case to the security matrix before resuming.

Do not copy the sensitive value into a ticket, chat, audit reason, or test fixture.

## Credentials and GitHub App Configuration

Valence Control stores stable credential references. Secret values remain in a host-owned provider such as Azure Key Vault, Kubernetes Secrets, a managed identity service, or an equivalent protected store. Follow the broader [runtime transport trust policy](../runtime-transport-trust-policy.md) for rotation and safe diagnostics.

Install the GitHub App only on explicitly repairable repositories. Request only the permissions required by enabled capabilities. Valence Control narrows installation tokens to one repository and provider operation, holds them only in memory for that operation, and discards them afterward.

Operational requirements:

- Use separate credential references for the App private key and repository webhook secret.
- Rotate secrets without changing incident, binding, or provider identity records.
- Validate the immutable GitHub repository identity after installation and after any ownership transfer.
- Verify webhook HMAC before parsing JSON and reject duplicate delivery IDs.
- Keep OIDC audience unique to the Valence Control Healing workload exchange.
- Bind workflow identity, workflow reference, and workflow revision; do not authorize a mutable default branch alone.
- Never put evidence, prompts, GitHub tokens, or secrets in the workflow-dispatch payload.
- Link GitHub actor IDs to Valence Control accounts explicitly before accepting provider-originated human commands; revoke the link when membership or provider access changes.

## Package, Path, and Policy Isolation

Source ownership bindings determine which components Valence Control may repair. Prefer exact component/package identifiers or narrow globs. A binding is active only when its provider connection is authorized, immutable repository identity matches, referenced policies are trusted, owner approval is present, and no conflicting authority overlaps it.

Path policy is an allow-list plus independent deny rules and limits. Even an allowed root cannot override hard safety checks. The publisher rejects:

- absolute paths and `..` traversal;
- binary patches, symlinks, submodules, and unsafe file modes;
- forbidden renames and excessive files, lines, or bytes;
- changes outside allowed roots or within forbidden roots;
- Valence Control-supplied Healing workflows;
- publisher, permission, evidence, validation, CODEOWNERS, and branch-protection guardrails;
- stale base/target revisions or a revoked/suspended authority.

Treat package/source metadata from manifests and repositories as attribution evidence only. A repository cannot authorize itself by declaring package ownership in source.

Policy definitions are versioned. An attempt captures its policy versions, while publication and merge re-evaluate current kill switches, target freshness, provider state, and binding validity. Policy changes must not silently relax an already-reviewed attempt.

## Automatic-Merge Safety

Automatic merge is disabled by default at both platform and application scope. Enabling it requires `healing.automerge.configure` and a short-lived target-bound confirmation.

Every configured gate must be freshly and positively established:

- exact producing revision verified;
- failure reproduced;
- before/after regression evidence present;
- independent verification passed;
- required repository checks passed and current;
- branch protection known and satisfied;
- changed paths and size classified low risk;
- no forbidden or sensitive change category;
- trusted rollout-stop or rollback capability available;
- repository explicitly opted in;
- no platform, workspace, application, or environment kill switch active.

Unreproduced and revision-unverified repairs remain draft and human-merge-only even when the causal analysis is high confidence. Human merge remains subject to GitHub branch protection and repository policy.

## Kill-Switch Semantics

Stops are hierarchical:

1. `Healing:ControlKillSwitch` stops Healing mutation across the Valence Control host.
2. Workspace emergency stop applies to all Healing applications in that workspace.
3. Application emergency stop applies to one application.
4. Environment kill switch applies to the selected application environment.

The first active stop wins. Stage enablement at a narrower scope cannot bypass it. Mutation gates re-check stop state before repair dispatch, publication, and automatic merge.

Emergency stops preserve the durable inbox, incident history, attempts, provider journal, evidence metadata, and audit events. They do not delete GitHub resources, cancel a deployment, or roll back code. Operators must coordinate those actions in the authoritative external system.

## Operational Decision Guide

| Condition | Operator action |
| --- | --- |
| Excluded or handled exception | Leave observation-only; correct signal classification only if the profile is wrong. |
| Duplicate occurrences | Inspect the canonical incident and environment impact; do not create one issue per occurrence. |
| Unknown/ambiguous component | Verify revision manifest and resolve attribution; keep repair blocked. |
| No ownership binding | Add a narrowly scoped draft binding, validate it, obtain owner approval, then activate. |
| Conflicting bindings | Suspend or narrow conflicting authorities; never choose a repository from incident text. |
| Provider/workflow unavailable | Keep the durable operation pending/failed, repair provider configuration, then use idempotent retry. |
| Unreproduced or revision-unverified proposal | Review as draft and human-merge-only. |
| Unsafe/self-protecting patch | Reject and stop the attempt; do not waive publisher path policy. |
| Missing/stale check or merge gate | Refresh authoritative provider state; automatic merge remains denied. |
| Merged but not deployed | Wait for an authenticated deployment observation; do not close the incident. |
| Deployed without positive evidence | Keep `Deployed—unverified`; investigate instrumentation and affected-operation coverage. |
| Exception recurs after repaired deployment | Treat as failed verification, use the deployment system's rollout-stop/rollback process, and inspect the trusted failure signal. |
| Environment intentionally excluded | Use an authorized, reasoned, confirmed per-environment waiver; do not mark it positively verified. |

## Audit Operations

Healing audit events are append-only and use safe structured details. Audit coverage includes configuration, candidate classification, deduplication, attribution, work-item projection, evidence access, agent activity, provider mutation, publication, merge evaluation, human commands, deployment, verification, failure, waiver, and closure.

For an investigation:

1. Record the workspace, application, incident, episode, attempt, provider operation, deployment observation, and correlation IDs.
2. Export safe events through the Healing audit API with the narrowest application/incident and time filters.
3. Correlate them with protected GitHub App, GitHub Actions, identity-provider, secret-store, and deployment-system audit logs.
4. Verify actor type and ID, policy/version hashes, reason codes, idempotency keys, target revisions, and timestamps.
5. Keep protected evidence in its owning system. Do not enrich the ordinary Healing audit record with raw payloads.

Authorization failures deliberately do not reveal whether a cross-workspace resource exists. Workspace isolation tests must remain part of every release gate.

## Incident Response and Recovery

### Suspected credential compromise

1. Activate the platform or affected application stop.
2. Suspend the provider connection and source ownership bindings.
3. Revoke/rotate the credential in the secret provider and GitHub App settings.
4. Invalidate affected workflow runs and review provider-operation and webhook replay journals.
5. Revalidate repository identity, permissions, webhook delivery, and actor links.
6. Resume in observation-only mode, then restore repair dispatch after a sandbox check.

### Malicious or unsafe proposal

1. Stop the attempt and preserve its digest and safe audit metadata.
2. Confirm the trusted publisher did not create or update a branch/PR.
3. Inspect evidence/source boundaries and policy classification without executing repository content.
4. Tighten package/path/evidence policy and add a negative regression test.
5. Retry only if the underlying incident still qualifies and attempt budget remains.

### Incorrect or harmful merged repair

1. Use the deployment system and repository's normal rollout-stop, revert, or rollback process; Healing has no rollback authority.
2. Activate the relevant Healing stop to prevent additional publication/merge.
3. Record deployment observations and recurrence evidence accurately; do not rewrite the incident to `Healed`.
4. Revoke or suspend the binding if authority/policy was wrong.
5. Resume only after branch protection, checks, policy, and verification coverage are corrected.

### Worker or provider outage

Durable inbox leases and provider-operation idempotency allow safe restart. Restore the dependency, confirm expired leases can be reclaimed, inspect retry/dead-letter state and budgets, then restart workers. Avoid manually inserting/deleting Healing rows or recreating GitHub resources. The last durable Valence Control state is authoritative; UI projections may show a stale timestamp until refreshed.

## Rollout and Release Gate

Use progressive exposure with explicit rollback criteria:

1. Local/fake-provider tests with workers off by default.
2. Observation-only in a non-production workspace.
3. Discovery and incident review for one application/environment.
4. Draft repair dispatch for one narrow package binding.
5. Human publication/merge in a dedicated GitHub sandbox.
6. Deployment verification in one non-production environment.
7. Limited production discovery, then human-merge repair.
8. Automatic merge only for low-risk paths after all fresh gates are proven.

At each stage, measure acceptance-to-projection latency, deduplication, exclusions, redaction failures, attribution ambiguity, provider retries, budgets, stale projections, verification recurrence, and operator response time. Stop progression on any secret leak, cross-workspace disclosure, duplicate mutation, ambiguous authority, unsafe publisher acceptance, auto-merge fail-open result, or unverifiable environment closure.

Before each production release, run the [security negative matrix and real GitHub sandbox gate](../../specs/039-valence-control-self-healing/quickstart.md#scenario-5-security-negative-matrix). Preserve evidence that kill switches, credential rotation, provider suspension, idempotent retry, and confirmed resume were exercised.

## Known Limitations

- GitHub is the only v1 provider; provider-neutral core contracts do not imply another adapter is production-ready.
- Valence Control does not ingest from arbitrary third-party log stores in v1.
- Exact producing revision and deterministic reproduction are not always available. Those cases cannot auto-merge.
- Agent analysis is evidence, not authority. The trusted publisher and merge policy recompute safety decisions.
- Valence Control does not deploy, stop rollouts, or roll back.
- Default evidence elevation denies all requests until the host supplies an explicit authorizer.
- An external provider outage can delay projection even though Valence Control has durably accepted the operation.
- Safe audit views are intentionally insufficient for raw-forensics use; protected telemetry/provider systems retain that responsibility.
