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

The worker receives only admitted immutable plan identities and safe secret
references. Secret aliases are configured under
`Deployment:AzureProvider:Secrets:<index>:Reference` and `Value`; only exact
safe references are accepted. A missing alias fails a workload step closed, and
secret values never enter provider contracts, diagnostics, or durable records.
