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

`Deployment:AzureProvider:Runner:ReleaseFeedServiceIndex` selects the trusted
server-governed NuGet service index used for the admitted release's exact optional
SQL package versions. It defaults to `https://api.nuget.org/v3/index.json`; a host
using producer packages from another feed must explicitly configure that feed.
The locator must be safe HTTPS without credentials, query strings, fragments, or
non-default ports. It is passed to both production deployment phases and bound
into provider execution authority, so it must not change during an operation.
This host setting does not admit packages, infer versions, or replace release
signature verification. The standalone disposable-proof template keeps its
separate feed contract.

SQL bootstrap invokes the pinned `sqlcmd` with `-b` in both production and
disposable-proof modes. SQL batch errors must produce a nonzero process result;
printed SQL output is never interpreted as success or retained as diagnostics.
An unconfirmed bootstrap remains uncertain. After confirmed firewall creation,
the temporary rule must still be deleted and verified absent when SQL execution
fails. An uncertain firewall-create result remains recovery-owned; it does not
establish that the rule is absent.

The worker receives only admitted immutable plan identities and governed secret
references. Secret aliases are configured under
`Deployment:AzureProvider:Secrets:<index>:Name` and `Reference`. The
name binds the governed reference to a required resolved-plan configuration slot;
only the reference crosses into lifecycle resolution and durable provider
records. External names bind immutable Key Vault locators; the provider-owned
SQL instruction is the sole internal exception. Names must already be canonical
lower-case keys, and no two names may collapse to the same Azure secret name
after `:` and `_` are mapped to `-`. A
missing, unsafe, or ambiguous named alias fails startup closed when managed
instance lifecycle is enabled. External configuration accepts absolute, versioned
Key Vault HTTPS secret locators without credentials, query strings, or fragments.
They are normalized to `secret://<vault>.vault.azure.net/secrets/<name>/<version>`
before entering plans and durable operations; that exact canonical form is also
accepted in configuration. HTTPS/canonical aliases of the same locator cannot be
assigned to two names. The managed resolver requires the exact persisted canonical
reference, and converts it to the fixed HTTPS vault origin only when reading the secret.
The external source secret name must match the slot's governed Azure name, using the
same case-insensitive binding at startup and during durable authorization. In particular,
`identity:signingkey` binds `identity-signing-key`, `admin:password` binds `admin-password`,
and an external `database:connectionstring` binds `sql-connection`. A mismatched source
name fails startup before provisioning; it is not silently remapped or authorized later.
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
