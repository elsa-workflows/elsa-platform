# Research: Runtime Operations

## Decision: Adapter Ports For Logs, Metrics, Backups, And Upgrades

Rationale: Managed environments may run on different infrastructure later. Ports keep provider details outside API contracts.

## Decision: Secret Redaction As A First-Class Policy

Rationale: Logs and operational outputs are high-risk for accidental secret leakage.
