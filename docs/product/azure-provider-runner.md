# Azure provider worker composition

The API composes `AzureBicepProviderRunner` only when
`Deployment:AzureProvider:WorkerEnabled=true` and the concrete runner is
explicitly enabled and fully validated. The default API configuration keeps the
provider fail-closed.

An enabled worker host must provide absolute, non-symbolic paths for the pinned
`az`, `sqlcmd`, and `curl` executables, plus the checked-in Bicep/SQL template
root. It must also provide the exact target subscription/resource-group/registry
scope under `Deployment:AzureProvider:Runner:TargetScope`. Startup rejects
partial configuration; it never silently falls back to an unconfigured runner.
When managed-instance lifecycle integration is enabled, its template and
provider-scope fingerprints are derived directly from this validated runner
authority. The host rejects lifecycle enablement without the concrete worker;
there is no duplicated or shipped fallback fingerprint that can become stale.

The worker receives only admitted immutable plan identities and safe secret
references. Secret aliases are configured under
`Deployment:AzureProvider:Secrets:<index>:Name`, `Reference`, and `Value`. The
name binds the safe reference to a required resolved-plan configuration slot;
only the reference crosses into lifecycle resolution and durable provider
records. A missing or ambiguous named alias fails startup closed when managed
instance lifecycle is enabled. Secret values remain runtime-only and never enter
provider contracts, diagnostics, or durable records.
