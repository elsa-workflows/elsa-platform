# Research: Managed Hosting Control Plane

## Decision: Narrow Managed Shape First

Rationale: Managed hosting is operationally heavy. One region, one persistence provider, and one runtime shape keeps support realistic.

## Decision: Control Plane Records First

Rationale: Provisioning must be auditable and resumable. Environment and event records are required before runtime operations.
