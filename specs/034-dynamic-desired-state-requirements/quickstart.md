# Quickstart: Dynamic Desired-State Requirements

## Scenario 1: Dev Revision Has No Production Observability Prompt

1. Open an application environment assigned to a Dev/development-like tier.
2. Start a new desired-state revision.
3. Verify the form shows artifact, revision label, and commit fields.
4. Verify the form does not show the observability binding editor.
5. Verify the requirements section states that no additional records are required for the environment.
6. Create the revision and verify only the artifact record is submitted.

## Scenario 2: Production Revision Requires Observability

1. Open an application environment assigned to a tier with `deployment.observability.required`.
2. Start a new desired-state revision.
3. Verify the observability binding requirement appears and is marked required by the environment tier.
4. Try submitting with provider or scope empty and verify the form blocks submission.
5. Complete provider and scope, submit, and verify an `ObservabilityBinding` record is included.

## Scenario 3: Contextual Fix Opens Observability On Dev

1. Open a Dev new revision page with `?includeRequirement=observability-binding`.
2. Verify the observability editor appears.
3. Verify the copy explains that the record is included to satisfy a contextual validation fix, not because Dev requires it.
4. Complete the fields and create the revision.
