# Azure workload-plan adapter

This project is the pure boundary between a governed
`ResolvedElsaApplicationPlan` and Azure workload realization. It performs no
Azure calls, starts no processes and owns no persistence. Checked-in Bicep owns
the resource model; later provider lifecycle code consumes the accepted intent.

The first admitted provider capability is deliberately narrow:

- West Europe (`westeurope`)
- Dedicated isolation
- Combined topology with one component
- Elsa release line 3.8
- Paid images from the governed `valenceruntimeimages.azurecr.io` authority
- Public HTTPS/TLS endpoints with unrestricted egress and no private connectivity

Release line and exact version remain strings in the provider-neutral schema.
The capability check rejects an unsupported later line with a provider finding;
it does not introduce a closed Elsa-version enum or change the schema.

Translation fails closed unless the resolved plan is valid, image identity is
immutable, and the admitted release carries matching `release-manifest` plus
safe `release-manifest-signature` evidence. Output contains only immutable
identities, non-secret placement facts and `secret://` references. It never
contains secret values, credentials, manifest payloads or signer identities.

The fingerprint is SHA-256 over a versioned, canonical projection of the typed
workload intent and normalized Azure target facts. Equivalent plans therefore
produce the same fingerprint, and changes to resource-affecting governed inputs
produce a different one. The unhashed canonical input is not exposed.

See [ADR-0004](../../../docs/adr/0004-deployment-engine-typed-reconciliation-hybrid.md),
[ADR-0007](../../../docs/adr/0007-provider-neutral-elsa-application-desired-state.md)
and [ADR-0010](../../../docs/adr/0010-initial-azure-workload-platform.md).
