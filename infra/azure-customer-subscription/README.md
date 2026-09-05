# Customer workload subscription bootstrap

This subscription-scoped template implements the initial resource boundary in [ADR-0016](../../docs/adr/0016-customer-workload-subscription-boundary.md). Run it only against the explicitly selected **Elsa Cloud — Customer Workloads** subscription, never the existing Pay-As-You-Go or Control subscription. It does not create the subscription or change billing ownership.

It creates an anchor resource group and a dedicated provisioner user-assigned managed identity in that subscription, and manages a monthly alert budget. It creates no role assignments, customer compute, databases, secrets, App Service attachments or runtime settings. The anchor group is platform bootstrap infrastructure; per-instance groups remain separate siblings owned by the existing provider assignment model.

The default names are `rg-elsa-cloud-workloads-platform-prod-weu`, `mi-elsa-cloud-provisioner-prod-weu` and `elsa-cloud-customer-workloads-monthly`. West Europe follows the governed provider profile. Actual subscription/tenant/identity IDs and contact addresses belong in the private operational record, not this template.

## Deployment

Before mutation, verify the subscription ID, display name and tenant against the operational record, inspect existing resources, and review an exact-template what-if. Never rely on the CLI default subscription. Supply a protected parameters file outside the checkout with `budgetStartDate` (the first day of the month, e.g. `2026-09-01T00:00:00Z`), `budgetEndDate` and `budgetContactEmail`. Keep the existing budget start/end dates on redeployment; do not reset them implicitly each month. The contact parameter is secure to avoid retaining it in deployment history, but the configured address remains visible to authorized budget readers.

```sh
az deployment sub what-if --subscription <verified-customer-subscription-id> \
  --location westeurope --name elsa-cloud-customer-bootstrap \
  --template-file infra/azure-customer-subscription/main.bicep \
  --parameters @<protected-parameters-file>

az deployment sub create --subscription <verified-customer-subscription-id> \
  --location westeurope --name elsa-cloud-customer-bootstrap \
  --template-file infra/azure-customer-subscription/main.bicep \
  --parameters @<protected-parameters-file>
```

Require successful deployment and read back the exact group, identity tenant/client/principal IDs, and budget amount/notifications. A compile or what-if alone is not deployment proof. Redeployments are incremental; removing a resource from the template does not delete it. Do not delete the anchor or identity after it is in use without reviewing attachments, grants and retained provider assignments.

## Gates that remain separate

- Register required workload resource providers and check regional support, quota and capacity.
- Review and grant the runner's workload-subscription permissions and separately scoped shared-registry/secret-reader rights. This identity is initially unprivileged.
- Attach the identity to the API host while preserving its existing identities, `AZURE_CLIENT_ID`, SQL and release-verifier credentials, container mode and image. Identity attachment can restart App Service and needs a staged health/readback check.
- Set explicit runner identity/target authority, pass production preflight, and separately enable lifecycle workers. No existing assignment may be silently retargeted.
- Prove real create/reconcile/recovery/delete behavior and the authenticated customer browser journey.

The default budget is 100 units in the subscription's billing currency, with actual-spend alerts at 50/80/100 percent and forecast at 100 percent. It is **not a cap or automatic shutdown**. Cost reporting and alerts can lag. Confirm EUR for the current subscription, and tune thresholds with usage; no fixed commercial spending ceiling is introduced.

Sources: [subscription-scoped Bicep](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deploy-to-subscription), [budget resource contract](https://learn.microsoft.com/en-us/azure/templates/microsoft.consumption/2024-08-01/budgets), [budget behavior](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets), [App Service managed identities](https://learn.microsoft.com/en-us/azure/app-service/overview-managed-identity).

Local contract gate: `python3 -m unittest scripts.tests.test_customer_subscription_bootstrap -v` (Azure CLI with Bicep required). The compiled-resource allowlist prevents this bootstrap from silently gaining role grants or production-host mutations.
