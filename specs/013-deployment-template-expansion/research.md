# Research: Deployment Template Expansion

## Decision: Target Renderer Registry

Rationale: Existing bundle rendering can choose a target and invoke a deterministic renderer set.

Alternatives considered: separate endpoints per target, rejected because target is a property of bundle generation.

## Decision: Stage Azure Before Kubernetes/Helm

Rationale: Azure Container Apps matches the .NET audience and is less broad than generic Kubernetes.

Alternatives considered: implement all targets at once, rejected as too large.
