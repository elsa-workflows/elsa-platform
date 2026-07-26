# Contract: Healing Signal Profile v1

The profile defines when ordinary OpenTelemetry exception evidence is eligible for Healing. Conforming producers do not need the Valence Control client package.

## Transport

- Automatic discovery uses OTLP/HTTP protobuf logs and traces accepted by the Valence Control OpenTelemetry module.
- The OpenTelemetry module redacts first and invokes the durable Healing contributor before returning success.
- Signals remain valid observability data when Healing is disabled or when they are ineligible for repair.

## Required standard evidence

At least one exception log or span exception event must provide:

- `exception.type`
- `exception.stacktrace` or an equivalent set of exception frames
- `service.name`
- event/span timestamp
- severity or error span status
- trace/span correlation when available

`exception.message` is optional and is never required for fingerprint stability.

## Profile attributes

All product-specific names use the `valence.control.healing` namespace.

| Attribute | Type | Requirement | Meaning |
|---|---|---|---|
| `valence.control.healing.profile.version` | string | Required for explicit conformance | `1.0` |
| `valence.control.healing.application.id` | string/GUID | Required | Valence Control application identity |
| `valence.control.healing.environment.id` | string/GUID | Required | Valence Control environment identity |
| `valence.control.healing.revision.id` | string/GUID | Required when known | Valence Control revision identity |
| `valence.control.healing.source.revision` | string | Recommended | Producing source commit SHA |
| `valence.control.healing.component_manifest.digest` | string | Required for full automation | Revision manifest SHA-256 |
| `valence.control.healing.occurrence.id` | string | Recommended | Producer-stable retry idempotency key |
| `valence.control.healing.operation.name` | string | Required | Stable affected operation identity |
| `valence.control.healing.failure.class` | string | Recommended | Curated classification |
| `valence.control.healing.retry.state` | string | Conditional | `none`, `retrying`, `exhausted` |
| `valence.control.healing.explicit` | bool | Optional | Authorized explicit incident intent |
| `valence.control.healing.component.key` | string | Optional | Direct manifest component hint |
| `valence.control.healing.workflow.definition.id` | string | Optional | Safe Elsa workflow operation context |
| `valence.control.healing.workflow.activity.type` | string | Optional | Safe activity operation context |

Attributes supplied by a monitored application are evidence, not authority. They cannot select a repository, workflow, branch, provider connection, evidence policy, or merge policy.

## Failure classes

V1 recognizes:

- `unhandled_request`
- `fatal_startup`
- `fatal_background`
- `unexpected_workflow`
- `unexpected_activity`
- `transient_exhausted`
- `explicit_incident`
- `validation`
- `authorization`
- `cancellation`
- `handled`
- `transient_retrying`
- `unknown`

The first seven are eligible by default. Validation, authorization, cancellation, handled, and retrying failures are excluded unless an authorized Valence Control policy overrides the classification.

## Idempotency

1. Prefer `valence.control.healing.occurrence.id` scoped to application.
2. Otherwise derive the occurrence key from stable trace ID, span ID, timestamp, resource identity, operation, exception type, and normalized causal frames.
3. Never use the receiver-generated log record ID as an idempotency key.

## Redaction and limits

- Valence Control applies configured redaction before any Healing contributor.
- Secret/credential/token/connection-string attributes and configured protected keys are removed or replaced.
- Exception messages and stack traces are bounded.
- Request bodies, workflow inputs/outputs, locals, environment variables, and source file contents are excluded by default.
- Truncation and omitted fields are represented explicitly in the evidence metadata.

## Explicit incident API mapping

The explicit incident API produces the same normalized envelope and inbox item as OTLP. It exists for domain failures and testing, not as a second general exception pipeline.

## Versioning

- Producers send a major/minor version.
- Valence Control accepts supported minor versions within a major version.
- Unknown major versions remain observable but are not automatically repairable.
- Attribute removals or semantic changes require a new major version.
