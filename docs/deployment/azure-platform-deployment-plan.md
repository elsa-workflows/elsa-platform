# Azure Platform Deployment Plan

## Objective

Deploy Elsa Platform repeatably into any Azure subscription from repository-owned infrastructure as code. The first production deployment unit is the Elsa Platform API container, including the built Console assets under `/admin`.

The deployment must be reproducible without depending on an existing `azd` environment. Aspire/azd remains useful for developer-driven deployments, but the subscription-neutral path is Bicep plus an explicit container image build/push step.

## Target Architecture

- Azure Resource Group per environment.
- Azure Container Registry for the platform API image.
- Linux Azure App Service Plan.
- Linux Web App running the Elsa Platform API image built from `src/Elsa.Platform.PackageCatalog.Api/Dockerfile`.
- Azure SQL logical server and catalog database.
- Application Insights and Log Analytics for runtime telemetry.
- System-assigned Web App identity with `AcrPull` on the registry.

The API runs with:

- `ASPNETCORE_ENVIRONMENT=Production`
- `Database__Provider=SqlServer`
- `ConnectionStrings__Catalog=<Azure SQL connection string>`
- `Authentication__ApiKey=<strong deployment secret>`
- optional `Authentication__BuilderClientApiKey=<strong deployment secret>`

The API applies EF Core SQL Server migrations at startup outside the `Testing` environment.

## Deployment Flow

1. Select target subscription, resource group, location, and environment name.
2. Provision or update Azure infrastructure from `infra/main.bicep`.
3. Build the API container with the Console baked in.
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

Use one resource group per environment:

| Environment | Example name | Notes |
| --- | --- | --- |
| Development | `rg-elsa-platform-dev` | Lower SKU, disposable data. |
| Test | `rg-elsa-platform-test` | Production-like config for release validation. |
| Production | `rg-elsa-platform-prod` | Strong secrets, backups, access review. |

Every environment should set a distinct `environmentName` parameter. Resource names are derived from that value plus subscription/resource-group uniqueness.

## Secret Handling

Initial IaC uses secure Bicep parameters for the API key and SQL administrator password. Do not commit real parameter files. For CI/CD, pass these values from the target environment secret store.

Later hardening should move the connection string and application credentials into Key Vault references and replace SQL password auth with Microsoft Entra database users once database principal provisioning is part of the deployment pipeline.

## Operational Checks

After deployment:

```bash
curl https://<web-app-name>.azurewebsites.net/health
curl -I https://<web-app-name>.azurewebsites.net/admin
```

Then sign into `/admin/login` with the configured admin API key.

## Known Follow-Ups

- Add custom domain, managed certificate, and front-door/WAF integration when public production DNS is known.
- Add backup, restore, and retention policy decisions for Azure SQL before production data is material.
- Add private networking once the platform has stable network boundaries.
- Add Key Vault references and Entra-only SQL auth as a hardening slice.
