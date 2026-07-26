# Contract: Deployment Abstractions

## Purpose

`ValenceControl.Deployment.Abstractions` defines the deployment language shared by manifest, artifact, engine, CLI, API, operator, and extension packages. It does not implement parsing, IO, reconciliation, persistence, transport, or runtime adapters.

## Value Contracts

The package exposes value contracts for:

- Resource identity and desired resource metadata.
- Artifact identity and artifact metadata.
- Target descriptors.
- Diagnostics.
- Deployment plans and changes.
- Deployment results and per-resource operation results.
- Deployment history records.

## Extension Contracts

The package exposes minimal extension contracts for:

- Resource handlers.
- Resource state readers.
- Resource validators.
- Artifact readers.
- Artifact writers.
- Deployment engine entry points.
- Deployment targets.
- Deployment history stores.

## Behavioral Contract

- Resource handlers own resource-specific validation, diff, dry-run, and apply semantics.
- Artifact readers and writers expose artifact metadata and content through abstractions; they do not commit the engine to folder, ZIP, OCI, NuGet, or any other storage format.
- History stores record deployment attempts and per-resource outcomes; this slice does not choose a persistence provider.
- Targets describe destination context without storing credentials or raw secrets.
- Diagnostics are structured and can be attached to resources or whole deployments.

## Deferred Contracts

- Manifest schema and normalization contracts.
- Artifact folder and ZIP layout contracts.
- CLI command contract.
- HTTP API contract.
- Workflow definition and variable runtime adapter contracts.
- Package, feature, and recipe descriptor validation implementation.
- Signing, OCI, GitOps, operator, policy, Kubernetes, and multi-tenant contracts.
