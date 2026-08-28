# Contract: GitHub Repair Protocol v1

GitHub is the only v1 provider. The Healing core uses provider-neutral contracts; this document defines the GitHub projection and workflow adapter.

## GitHub App permissions

Request only the permissions needed for enabled capabilities:

- Metadata: read
- Contents: read/write for trusted publisher only
- Issues: read/write
- Pull requests: read/write
- Actions: read/write for approved workflow dispatch
- Checks/statuses: read
- Administration/branch protection: read when available

Installation tokens are minted just in time, narrowed to the bound repository and permissions, held only for one provider operation, and never exposed to the repair workflow or agent.

## Work item projection

Elsa Control creates one issue per active incident episode/repository with:

- Stable machine marker containing Elsa Control incident/episode IDs.
- Human-readable title based on normalized exception/operation, never raw secrets.
- One machine-owned summary section updated in place.
- Impact, affected environments/revisions, attribution, evidence tier, attempts, current blockers, repair/PR/verification state, and Elsa Control deep link.
- GitHub-specific labels such as `elsa-control-healing`, severity, state, and `needs-human` as projections only.

Occurrence updates do not create one comment per signal. Manual edits outside the machine-owned section are preserved.

## Workflow dispatch

Elsa Control dispatches the approved workflow identity at its approved immutable revision with a minimal payload:

- protocol version
- Elsa Control base URL
- incident ID
- episode ID
- attempt ID
- one-time nonce
- expected repository immutable ID
- producing revision status
- target branch and expected target SHA

No exception evidence, prompt, provider token, or protected secret appears in the dispatch payload.

## Workload authentication

The workflow requests a GitHub OIDC token with the Elsa Control audience and exchanges it with the one-time nonce. Elsa Control validates the signed claims against the source ownership binding and attempt before returning a capability token.

The capability permits only:

- read one evidence bundle
- create one bounded, inert managed proposal
- heartbeat one attempt lease
- finalize that exact proposal after repository validation
- upload one bounded result envelope

It cannot create branches, commits, issues, pull requests, checks, labels, or merges.

The capability is not an authority snapshot. Exchange, evidence, proposal, heartbeat, finalization exchange, and result upload each revalidate the current Elsa Control/workspace/application/environment repair gates, active incident episode, provider connection, and source binding. Revocation immediately invalidates and durably revokes outstanding capability exchanges.

Before managed inference, Elsa Control commits an at-most-once leased reservation for the attempt and its full inference-unit allowance. Concurrent callers cannot invoke the provider twice. If Elsa Control crashes after invocation but before the atomic proposal/audit commit, an expired reservation is treated as indeterminate and is not reacquired unless a future provider contract supplies durable provider-side idempotency; the attempt is released to an audited `NeedsHuman` outcome instead.

## Repair result

The workflow uploads:

- base/target SHA
- reproduction classification and structured evidence
- confidence and causal summary
- unified diff
- regression test/change evidence
- build/test/analysis results
- changed-path/risk suggestions
- rollback guidance
- usage and timing summary

Repository/source/log/test content remains untrusted. Result text is never interpreted as a Elsa Control command.

## Trusted publication

Before provider mutation, Elsa Control independently:

1. Confirms attempt lease, configuration, binding, policy versions, and kill switches.
2. Confirms current target SHA and detects an already-fixed target.
3. Parses the unified diff and recomputes paths, file modes, sizes, lines, and digest.
4. Rejects absolute/traversal paths, binary patches, symlinks, submodules, forbidden renames, and self-protection paths.
5. Evaluates reproduction/evidence/change-category/publication policy.
6. Mints a repository-scoped GitHub App installation token.
7. Creates or updates a deterministic repair branch without force-pushing unrelated history.
8. Opens or updates one pull request and records provider IDs/SHAs.
9. Revokes/discards the token after the provider operation.

Forbidden automated changes always include Elsa Control-supplied Healing workflow files, publisher/permission/validation policy files, CODEOWNERS/branch-protection automation, and paths configured as self-protecting guardrails.

## Pull request body

Every PR states:

- canonical incident and issue links
- producing revision and whether it was verified
- target revision
- `Reproduced`, `Inferred—unreproduced`, `Revision-unverified`, or `Insufficient confidence`
- before/after regression evidence
- validation commands/results
- changed paths and policy classification
- risk and rollback guidance
- whether auto-merge is eligible and every blocker
- explicit statement when reproduction was not possible

Unreproduced or revision-unverified fixes are always draft and human-merge-only.

## Merge observation and request

Elsa Control observes PR/check/branch-protection changes through verified webhooks and provider refreshes. Auto-merge is requested only when a fresh complete policy evaluation passes. GitHub branch protection and repository policy remain final authority.

Elsa Control records the merged SHA but does not mark the incident healed until deployment verification succeeds.

### Repository validation harness

If `.elsa/healing/validate` exists and is executable, the isolated validation job sets
`ELSA_CONTROL_HEALING_VALIDATION_OUTPUT` to a runner-owned file. The harness may replace that file with only this
bounded boolean contract:

```json
{"protocolVersion":"1.0","reproduction":{"wasAttempted":true,"wasReproduced":true},"regression":{"wasAdded":true,"failedBeforePatch":true,"passedAfterPatch":true}}
```

Unknown properties, non-boolean evidence, symlinks, oversized output, and internally inconsistent reproduction
claims fail validation. Repository text is never copied from this file into Elsa Control or the pull request. Missing
evidence remains false and therefore human-merge-only.

## Human commands

Supported normalized commands are `retry`, `stop`, `request-evidence`, and `waive-environment`. A GitHub comment/label is only a request. Elsa Control verifies webhook, repository permission, linked Elsa Control identity, workspace permission, and any required confirmation before recording/executing the command.

## Idempotency

Deterministic keys cover issue creation, summary update, workflow dispatch, branch publication, PR creation/update, and merge request. Duplicate webhook deliveries and provider retries return the previously recorded outcome.
