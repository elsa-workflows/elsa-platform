# Azure Platform Deployment Plan

## Objective

Deploy Elsa Platform repeatably into any Azure subscription from repository-owned infrastructure as code. The first production deployment unit is the Elsa Platform API container, including the built Console assets under `/admin`.

The deployment must be reproducible without depending on an existing `azd` environment. Aspire/azd remains useful for developer-driven deployments, but the subscription-neutral path is Bicep plus an explicit container image build/push step.

## Target Architecture

- Azure Resource Group per environment.
- Azure Container Registry for the platform API image.
- Linux Azure App Service Plan.
- Linux Web App running the Elsa Platform API image built from `src/Elsa.Platform.Api/Dockerfile`.
- Azure SQL logical server and catalog database.
- Application Insights and Log Analytics for runtime telemetry.
- System-assigned Web App identity with `AcrPull` on the registry.

The API runs with:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so ASP.NET Core uses App Service forwarded scheme/host headers for HTTPS-aware auth, redirects, and same-origin checks
- `Database__Provider=SqlServer`
- `ConnectionStrings__Catalog=<Azure SQL connection string>`
- `Authentication__ApiKey=<strong deployment secret>`
- optional `Authentication__BuilderClientApiKey=<strong deployment secret>`

For SaaS customer login, enable the optional Keycloak stack in `infra/main.bicep`
with `deployKeycloak=true`. This provisions a separate Keycloak Web App,
PostgreSQL Flexible Server, and API OIDC app settings. The production runbook is
documented in [keycloak-saas.md](keycloak-saas.md).

The API applies EF Core SQL Server migrations at startup outside the `Testing` environment.

## Deployment Flow

1. Select target subscription, resource group, location, and environment name.
2. Provision or update Azure infrastructure from `infra/main.bicep`.
3. Build the API container with the Console baked in. The helper script targets `linux/amd64` by default so local Apple Silicon builds run correctly on Linux App Service.
4. Push the image to the provisioned Azure Container Registry.
5. Re-run the Bicep deployment with the pushed image reference.
6. Verify `/health` and `/admin`.

Use the helper script for the full flow:

```bash
AZURE_SUBSCRIPTION_ID=<subscription-id> \
ADMIN_API_KEY='<strong-secret>' \
SQL_ADMINISTRATOR_PASSWORD='<strong-sql-password>' \
scripts/deploy-azure-platform.sh \
  --environment prod \
  --resource-group rg-elsa-platform-prod \
  --location westeurope
```

For an infrastructure preview:

```bash
az deployment group what-if \
  --resource-group rg-elsa-platform-prod \
  --template-file infra/main.bicep \
  --parameters @infra/parameters/prod.example.json \
  --parameters adminApiKey='<strong-secret>' sqlAdministratorPassword='<strong-sql-password>'
```

## Environment Strategy

Use one GitHub Actions environment per Azure resource group:

| GitHub environment | Azure environment | Example resource group | Notes |
| --- | --- | --- |
| `development` | `dev` | `rg-elsa-platform-dev` | Lower SKU, disposable data. |
| `test` | `test` | `rg-elsa-platform-test` | Production-like config for release validation. |
| `production` | `production` or `prod` | `rg-elsa-platform-prod` | Strong secrets, backups, access review. |

Every environment should set a distinct `environmentName` parameter. Resource names are derived from that value plus subscription/resource-group uniqueness.

After a resource group has been provisioned once, bootstrap the matching GitHub environment from the Azure deployment outputs:

```bash
scripts/bootstrap-github-azure.sh \
  --environment development \
  --azure-environment dev \
  --resource-group rg-elsa-platform-dev \
  --location westeurope
```

The bootstrap script creates or reuses an Entra app registration for GitHub Actions OIDC, adds a federated credential scoped to the selected GitHub environment, assigns Azure roles to the target resource group and registry, and writes the environment variables consumed by `.github/workflows/azure-api-deploy.yml`.

For infrastructure deployments from GitHub Actions, also set these GitHub environment secrets:

- `ADMIN_API_KEY`
- `SQL_ADMINISTRATOR_PASSWORD`
- optionally `BUILDER_CLIENT_API_KEY`

For app-only deployments, the workflow needs only the OIDC and Azure resource variables written by the bootstrap script.

## Secret Handling

Initial IaC uses secure Bicep parameters for the API key and SQL administrator password. Do not commit real parameter files. For CI/CD, pass these values from the target environment secret store.

The admin API key is for machine-to-machine administration only. Browser users
sign in through the platform OIDC provider and receive a platform session cookie.
Admin UI/API access is granted by the `platform_admin` role.

Later hardening should move the connection string and application credentials into Key Vault references and replace SQL password auth with Microsoft Entra database users once database principal provisioning is part of the deployment pipeline.

## Operational Checks

After deployment:

```bash
curl https://<web-app-name>.azurewebsites.net/health
curl -I https://<web-app-name>.azurewebsites.net/admin
```

Then open `/admin`. Anonymous browser requests redirect to the platform OIDC
sign-in flow.

## Known Follow-Ups

- Add custom domain, managed certificate, and front-door/WAF integration when public production DNS is known.
- Add backup, restore, and retention policy decisions for Azure SQL before production data is material.
- Add private networking once the platform has stable network boundaries.
- Add Key Vault references and Entra-only SQL auth as a hardening slice.
- Add Key Vault references and private networking for the optional Keycloak
  PostgreSQL database before public SaaS launch.
