# Research: BYOC Deployment Targets

## Decision: Azure Container Apps First

Rationale: Best fit for .NET audience and prior template roadmap.

Alternatives considered: Kubernetes first, rejected as broader operational surface.

## Decision: Preview Before Deploy

Rationale: BYOC is security- and cost-sensitive; users need explicit plan review.

## Decision: Adapter Port With Fakes

Rationale: Tests should not require live cloud credentials.
