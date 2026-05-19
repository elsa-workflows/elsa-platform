# Data Model: Server-Side Planning

## BuilderIntent

User-selected image, packages, features, settings, infrastructure, and capabilities.

## RuntimePlan

Resolved image, packages, features, infrastructure, settings, auto-added items, and findings.

## AutoAddedItem

Fields: `kind`, `id`, `reason`, `source`.

## PlannerFinding

Fields: `level`, `code`, `message`, `scope`.
