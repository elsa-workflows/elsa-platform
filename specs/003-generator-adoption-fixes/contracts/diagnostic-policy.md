# Contract: Diagnostic Policy And MSBuild Task Result

## Purpose

Define how generator diagnostics map to MSBuild log severity and task success for
the adoption hardening work.

## Inputs

- `ElsaPackageManifestValidationSeverity`
- `ElsaPackageManifestFailOnWarnings`
- `ElsaPackageManifestStrict`
- Generator diagnostics emitted by discovery, validation, package inclusion, and
  task execution.

## Diagnostic Categories

| Category | Validation severity mapping | Fatal by default | Notes |
|----------|-----------------------------|------------------|-------|
| Manifest validation | Yes | Error policy only | Required schema findings can map to warning under warning severity. |
| Recommended metadata | Already warning by default | No | Fails only with fail-on-warnings or stricter policy. |
| Delegate-shaped code hook ignored | No | No | No default warning; verbose/low-importance diagnostics only. |
| Non-delegate unsupported setting omitted | No | No | Low-importance diagnostic only; property is excluded from manifest settings. |
| Infrastructure failure | No | Yes | Examples: unreadable assembly, task exception, missing required build input. |
| Invalid input failure | No | Yes | Examples: malformed override JSON, oversized override file, invalid package identity override. |
| Package inclusion failure | No | Yes | Examples: unable to include canonical manifest when inclusion is enabled. |

## Task Result Rules

- The task succeeds when no fatal diagnostics exist, no logged errors remain, and
  fail-on-warnings is false or no warnings are present after mapping.
- The task fails when any fatal diagnostic exists.
- The task fails when any diagnostic is logged as an error.
- The task fails when `ElsaPackageManifestFailOnWarnings=true` and any
  diagnostic is logged as a warning.
- The task must not return false after logging only warnings.

## Acceptance Tests

- Warning severity plus fail-on-warnings false logs mapped validation findings as
  warnings and returns success.
- Warning severity plus fail-on-warnings true logs warnings and returns failure.
- Default error severity logs required manifest validation findings as errors and
  returns failure.
- Warning severity does not downgrade infrastructure or invalid input failures.
- Ignored delegate-shaped hooks do not create default warnings and do not cause
  fail-on-warnings failure.
- Omitted unsupported non-delegate settings do not create warnings or errors and
  do not cause fail-on-warnings failure.
