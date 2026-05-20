# Research: Runtime Image Metadata API

## Decision: Seed Runtime Image Metadata In Backend Configuration/Source

Rationale: The first slice needs a backend source of deployment truth without adding admin CRUD, migrations, or registry synchronization. Strongly typed seed data is simple, reviewable, and easy to replace with persistence later.

Alternatives considered: database-backed image records, rejected as premature; continued Lovable ownership, rejected because bundle generation needs backend-owned metadata.

## Decision: Extend Builder Catalog Before Adding Separate Public Image Routes

Rationale: Lovable already consumes `/api/builder/catalog`; adding `images` minimizes frontend churn. Dedicated `/api/runtime-images` routes can be added later when non-builder clients need them.

Alternatives considered: separate image-only API first, rejected because it creates an extra rollout step without immediate product value.

## Decision: Classify Fields By Deployment Impact

Rationale: Image references, ports, env vars, capabilities, and companion rules affect generated files and must be backend-owned. Icons, marketing highlights, and static docs can remain frontend-owned unless builder UI requires them.

Alternatives considered: move all marketing data backend-side immediately, rejected to avoid coupling the platform API to visual presentation.

## Decision: Validate Metadata At Startup/Test Time

Rationale: Broken image metadata can generate broken bundles. Validation for unique slugs, valid tags, env var uniqueness, and companion references catches errors before rollout.

Alternatives considered: validate only on request, rejected because configuration errors should be caught early.
