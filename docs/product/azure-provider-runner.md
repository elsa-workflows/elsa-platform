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
`Deployment:AzureProvider:Secrets:<index>:Name` and `Reference`. The
name binds the safe reference to a required resolved-plan configuration slot;
only the reference crosses into lifecycle resolution and durable provider
records. Names must already be canonical lower-case keys, and no two names may
collapse to the same Azure secret name after `:` and `_` are mapped to `-`. A
missing, unsafe, or ambiguous named alias fails startup closed when managed
instance lifecycle is enabled. External production references must be absolute, versioned
Key Vault HTTPS secret locators without credentials, query strings, or fragments.
An enabled production worker rejects configured `Value` entries and disposable
proof mode. Its managed identity resolves values only after checking the durable
workspace, organization, instance, assignment and running-operation authorization.
Secret values remain runtime-only and never enter provider contracts, diagnostics,
or durable records. The local configured-value resolver is not the production
composition.

For a normal production workload, the canonical `database:connectionstring`
slot may use the fixed provider-owned reference
`secret://azure-managed/sql-connection`. This is an internal instruction, not a
Key Vault locator: during the post-Foundation `SeedSecrets` step the managed
identity resolver authorizes the current durable assignment and materializes a
passwordless SQL connection from its persisted SQL server and workload identity
resource references. The caller-provided resource snapshot is not trusted. The
identity-signing and admin-password slots remain immutable, versioned Key Vault
references; the internal reference is rejected for every other slot.
