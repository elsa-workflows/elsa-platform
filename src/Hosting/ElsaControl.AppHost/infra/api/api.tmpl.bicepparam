using './api-website.module.bicep'

param adminapikey_value = '{{ securedParameter "adminApiKey" }}'
param api_containerimage = '{{ .Image }}'
param api_containerport = '{{ targetPortOrDefault 8080 }}'
param api_identity_outputs_clientid = '{{ .Env.API_IDENTITY_CLIENTID }}'
param api_identity_outputs_id = '{{ .Env.API_IDENTITY_ID }}'
{{ if index .Env "AZURE_PROVISIONER_IDENTITY_ID" }}
param provisioner_identity_outputs_id = '{{ .Env.AZURE_PROVISIONER_IDENTITY_ID }}'
{{ else }}
param provisioner_identity_outputs_id = ''
{{ end }}
param builderclientapikey_value = '{{ securedParameter "builderClientApiKey" }}'
param control_sql_outputs_sqlserverfqdn = '{{ .Env.CONTROL_SQL_SQLSERVERFQDN }}'
param entraclientid_value = '{{ parameter "entraClientId" }}'
param entraclientsecret_value = '{{ securedParameter "entraClientSecret" }}'
param entratenantid_value = '{{ parameter "entraTenantId" }}'
param elsa_control_outputs_azure_app_service_dashboard_uri = '{{ .Env.ELSA_CONTROL_AZURE_APP_SERVICE_DASHBOARD_URI }}'
param elsa_control_outputs_azure_container_registry_endpoint = '{{ .Env.ELSA_CONTROL_AZURE_CONTAINER_REGISTRY_ENDPOINT }}'
param elsa_control_outputs_azure_container_registry_managed_identity_client_id = '{{ .Env.ELSA_CONTROL_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_CLIENT_ID }}'
param elsa_control_outputs_azure_container_registry_managed_identity_id = '{{ .Env.ELSA_CONTROL_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID }}'
param elsa_control_outputs_azure_website_contributor_managed_identity_id = '{{ .Env.ELSA_CONTROL_AZURE_WEBSITE_CONTRIBUTOR_MANAGED_IDENTITY_ID }}'
param elsa_control_outputs_azure_website_contributor_managed_identity_principal_id = '{{ .Env.ELSA_CONTROL_AZURE_WEBSITE_CONTRIBUTOR_MANAGED_IDENTITY_PRINCIPAL_ID }}'
param elsa_control_outputs_planid = '{{ .Env.ELSA_CONTROL_PLANID }}'
