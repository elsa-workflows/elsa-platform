# Managed lifecycle telemetry sink

This separate resource-group template creates a workspace-based Application
Insights sink for Control's existing managed lifecycle signals. It does not deploy,
restart, convert or configure the API, change its identities, expose the Aspire
dashboard, or enable lifecycle workers. It belongs in the reviewed **Control**
subscription, not in the customer workload subscription or Pay-As-You-Go.

## Security and cost boundary

- The template resolves an existing Control API user-assigned identity and grants
  only Monitoring Metrics Publisher at the exact Application Insights resource.
  Do not use the customer workload provisioner identity.
- Local authentication is disabled on Application Insights and Log Analytics.
  The exporter must use the matching managed identity, with no developer-credential
  or instrumentation-key authentication fallback. Azure calls the role “Metrics
  Publisher”, but it authorizes telemetry publication for all signal types.
- The Azure service ingestion/query endpoints use public TLS endpoints protected
  by Entra authentication and RBAC. This is **not** a private-link/AMPLS deployment.
  The existing Aspire dashboard remains a separate resource with public network
  access disabled. Neither resource deployment nor an anonymous HTTP denial proves
  that the private operator dashboard route works.
- The workspace uses consumption pricing without reserved capacity, 30-day
  retention, and a default 1 GB/day ingestion safety brake. The quota is not a
  guaranteed spend cap and reaching it makes an observation window incomplete;
  missing samples must never count as healthy. Keep normal collection limited to
  managed lifecycle signals, not general request/dependency/log auto-capture.
- Outputs contain resource and identity references only. Retrieve the connection
  metadata privately from the exact deployed component during operator setup; do
  not paste it into issue comments, CI logs, or acceptance evidence.

Microsoft documents [Entra-authenticated ingestion and the required scoped role](https://learn.microsoft.com/en-us/azure/azure-monitor/app/azure-ad-authentication).

## Validation and rollout

1. Run `python3 scripts/tests/test_managed_telemetry_infrastructure.py`. It compiles
   the template and inspects the actual generated resource/role boundary.
2. Resolve and verify the intended Control subscription, resource group, existing
   API identity, supported sink region, and resource names. Pass those explicit
   values to a scoped `az deployment group what-if`; do not rely on the CLI's
   default subscription. Review every proposed change before deployment.
3. Deploy only this reviewed template in Incremental mode. Verify the exact identity
   role, local-auth disablement, workspace linkage and quota/retention settings.
4. Enable the reviewed source exporter only through the existing immutable API
   image promotion and migration-compatibility gates. The live API's classic Docker
   mode must not be converted to site containers to enable observability.
5. Prove positive managed-identity ingestion and negative unauthorized ingestion,
   then positive authorized-operator and negative anonymous/unauthorized dashboard
   access while public dashboard access stays disabled. Verify actual received
   signal names/labels and authorized trace correlation, not merely exporter setup.
6. Run the separate bounded sampler against fresh managed runtime/provider
   observations for at least five minutes. Retained database health is not a fresh
   probe. Retain safe UTC window timestamps and healthy/total/unknown counts; no raw
   credentials, endpoints, customer identifiers or provider responses in evidence.

These live gates remain open under [#266](https://github.com/valence-works/elsa-control/issues/266).
Compilation and local transport tests alone do not satisfy them.
