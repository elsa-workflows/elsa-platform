# Research: Generator Adoption Fixes for Elsa Shell Modules

## Decision: Classify Fatal Diagnostics Separately From Manifest Validation Severity

The generator should distinguish manifest validation findings from
infrastructure and invalid input failures before applying
`ElsaPackageManifestValidationSeverity`.

Rationale: Staged adoption needs required manifest validation findings to be
loggable as warnings when requested, but unreadable assemblies, malformed
override files, oversized inputs, and task exceptions are not valid warning-only
outputs. This also fixes the MSBuild `MSB4181` scenario because task success will
be based on the mapped logged severity and fatal classification rather than the
pre-mapping error count alone.

Alternatives considered:

- Downgrade every diagnostic under warning severity: rejected because broken
  generator inputs could produce successful builds with missing or stale
  manifests.
- Downgrade only recommended metadata: rejected because it does not support the
  requested warning-only rollout for required manifest validation findings.

## Decision: Filter Delegate-Shaped Code Hooks Before Unsupported Setting Validation

Direct delegates and container shapes whose element/value type is delegate-shaped
should be classified as code configuration hooks during setting discovery and
excluded from the deploy-time setting list before schema generation.

Rationale: Elsa shell features use callbacks, service factories, HTTP client
configuration hooks, and delegate-valued collections as application-code
extension points. These values cannot be represented as deployment
configuration. Filtering them before schema generation prevents unsupported-type
failures while keeping normal deploy-time settings visible.

Alternatives considered:

- Treat delegate hooks as unsupported settings with warning severity: rejected
  because `FailOnWarnings=true` would make ordinary shell modules fail for
  expected code hooks.
- Require consumers to annotate every hook with ignore metadata: rejected because
  adoption spans many existing Elsa Core modules and should not require noisy
  per-property workarounds.
- Serialize delegate hooks as opaque settings: rejected because Runtime Builder
  and deployment configuration cannot use them safely.

## Decision: Omit Unsupported Non-Delegate Setting Candidates

Unsupported non-delegate property shapes such as `System.Type` and complex
option objects should be classified as non-manifestable setting candidates
during discovery, excluded from the generated manifest, and reported only as
low-importance non-warning diagnostics.

Rationale: Unsupported CLR-only shapes cannot be represented as deploy-time
configuration. Failing the build for these properties blocks adoption across
otherwise valid shell modules and creates the same operational problem as
delegate-shaped hooks. Omitting them keeps the manifest truthful: it contains
only settings Runtime Builder can configure.

Alternatives considered:

- Keep unsupported non-delegate settings as build errors: rejected because
  ordinary shell-feature modules can expose CLR-only implementation hooks such
  as provider `Type` values.
- Log default warnings for unsupported omissions: rejected because
  `FailOnWarnings=true` would still block builds for omitted non-manifestable
  properties.
- Serialize unsupported shapes as opaque object settings: rejected because the
  manifest contract has no actionable schema for them.

## Decision: Do Not Warn By Default For Ignored Delegate Hooks

Ignored delegate-shaped code hooks should produce no default warnings. Concise
low-importance or verbose diagnostics may identify ignored hooks when diagnostic
verbosity is increased.

Rationale: The normal adoption path should be clean and should not interact with
`ElsaPackageManifestFailOnWarnings`. Verbose diagnostics still help maintainers
explain why a public settable property did not appear as a manifest setting.

Alternatives considered:

- Default warning per ignored hook: rejected because fail-on-warnings builds
  would fail for intentional non-configurable properties.
- No diagnostics ever: rejected because troubleshooting missing settings would be
  harder.

## Decision: Use First Declared Target Framework As Canonical For Equivalent Surfaces

When a multi-targeted project produces equivalent manifest-relevant surfaces, the
first declared target framework supplies the canonical package manifest included
at the package root.

Rationale: The existing generator targets already calculate a first target
framework value, and the rule is deterministic, easy for package authors to
reason about, and straightforward to test through package inspection.

Alternatives considered:

- Last generated target framework: rejected because build scheduling/order is
  less explicit and can be harder to reason about.
- Separate outer-build canonical generation: rejected for this adoption fix
  because it adds coordination complexity beyond the immediate packaging gap.

## Decision: Prefer Existing Test Helpers And Add Focused Coverage

Use the existing sample project builder, package inspector, core tests, MSBuild
tests, and integration tests rather than creating new test infrastructure.

Rationale: The failure modes are localized and already map to current test
projects. Focused tests will cover task return behavior, delegate-shaped
property filtering, required validation failures, and multi-target package
inspection.

Alternatives considered:

- New end-to-end harness for real Elsa Core modules: deferred because it would
  add significant setup cost and belongs after the focused package behavior is
  stable.
