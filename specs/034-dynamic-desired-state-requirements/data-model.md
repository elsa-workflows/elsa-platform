# Data Model: Dynamic Desired-State Requirements

## DesiredStateRequirement

Valence Control-defined metadata describing a desired-state record expected by an environment tier or contextual validation action.

Fields:

- `Id`: stable requirement identifier.
- `CapabilityId`: optional tier capability that activates the requirement.
- `RecordKind`: desired-state record kind produced by the requirement editor.
- `Label`: user-facing requirement label.
- `Description`: short explanation for display.
- `ValidationId`: validation result identifier associated with the requirement.
- `Required`: whether the current applicability requires the record.
- `Applicability`: why the requirement is shown.

Validation:

- Requirement IDs are stable.
- Metadata contains no raw secrets or credential values.
- Unknown future requirements can be represented but unsupported editors must not submit malformed records.

## RequirementApplicability

Reason a requirement appears on the revision form.

Values:

- `CurrentTier`: required by the current environment tier.
- `ContextualFix`: requested by a validation action or deep link.
- `Optional`: available as an advanced record but not required.

Validation:

- `CurrentTier` requirements are required.
- `ContextualFix` requirements explain the target validation reason.
- Unsupported contextual requests are reported as unsupported.

## ObservabilityBindingRequirement

Specialized desired-state requirement for the existing `ObservabilityBinding` record.

Fields:

- Signal kind: Logs, Metrics, Traces, or Console.
- Provider: telemetry provider or binding source.
- Scope: environment/runtime scope covered by the binding.
- Sample or note: safe display note.

Validation:

- Provider and scope are required when the observability editor is shown as required or contextually requested.
- Signal kind must be one of the supported observability binding kinds.
