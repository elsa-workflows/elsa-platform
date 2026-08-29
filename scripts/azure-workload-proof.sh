#!/usr/bin/env bash
set -euo pipefail

# Disposable proof runner. Validation is the default and never creates a
# resource group or resource. Apply is deliberately opt-in and all resources
# are scoped to the supplied disposable resource group.

usage() {
  cat <<'EOF'
Usage:
  scripts/azure-workload-proof.sh validate [options]
  scripts/azure-workload-proof.sh what-if [options]
  scripts/azure-workload-proof.sh apply [options]
  scripts/azure-workload-proof.sh cleanup --resource-group <name> --proof-name <suffix> --registry-resource-group <name>

Required apply/what-if options:
  --proof-name <suffix>                 Lowercase unique suffix, e.g. weu-20260829a
  --resource-group <name>              Disposable proof resource group
  --image-repository <repository>      Repository without a tag or digest
  --image-digest <64 hex characters>   Immutable image digest without sha256:
  --registry-resource-group <name>     Resource group containing valenceruntimeimages
  --sql-bootstrap-object-id <GUID>     Entra object ID for temporary SQL administrator
  --sql-bootstrap-login <name>         Entra login/display name for temporary SQL administrator

Optional:
  --subscription <id>                  Azure subscription to use
  --registry-name <name>               Existing ACR (default: valenceruntimeimages)
  --expiry-utc <YYYY-MM-DD>            Proof expiry tag (default: 2026-09-02)
EOF
}

mode="${1:-}"
case "$mode" in
  validate|what-if|apply|cleanup) shift ;;
  *) usage >&2; exit 2 ;;
esac

proof_name=""
resource_group=""
image_repository="valenceruntimeimages.azurecr.io/runtime-combined"
image_digest=""
registry_name="valenceruntimeimages"
registry_resource_group=""
sql_bootstrap_object_id=""
sql_bootstrap_login=""
subscription_id=""
expiry_utc="2026-09-02"

while (($#)); do
  case "$1" in
    --proof-name) proof_name="${2:?Missing value for --proof-name}"; shift 2 ;;
    --resource-group) resource_group="${2:?Missing value for --resource-group}"; shift 2 ;;
    --image-repository) image_repository="${2:?Missing value for --image-repository}"; shift 2 ;;
    --image-digest) image_digest="${2:?Missing value for --image-digest}"; shift 2 ;;
    --registry-name) registry_name="${2:?Missing value for --registry-name}"; shift 2 ;;
    --registry-resource-group) registry_resource_group="${2:?Missing value for --registry-resource-group}"; shift 2 ;;
    --sql-bootstrap-object-id) sql_bootstrap_object_id="${2:?Missing value for --sql-bootstrap-object-id}"; shift 2 ;;
    --sql-bootstrap-login) sql_bootstrap_login="${2:?Missing value for --sql-bootstrap-login}"; shift 2 ;;
    --subscription) subscription_id="${2:?Missing value for --subscription}"; shift 2 ;;
    --expiry-utc) expiry_utc="${2:?Missing value for --expiry-utc}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
proof_dir="$repo_root/infra/azure-workload-proof"

validate_name() {
  [[ "$1" =~ ^[a-z0-9][a-z0-9-]{2,15}$ ]] || {
    echo "proof name must be 3-16 lowercase letters, numbers or hyphens" >&2
    exit 2
  }
}

validate_digest() {
  [[ "$1" =~ ^[a-fA-F0-9]{64}$ ]] || {
    echo "image digest must be exactly 64 hexadecimal characters; tags are not accepted" >&2
    exit 2
  }
}

validate_repository() {
  [[ "$1" =~ ^[a-z0-9./_-]+$ && "$1" != *:* && "$1" != *@* ]] || {
    echo "image repository must not contain a tag or digest" >&2
    exit 2
  }
}

validate_guid() {
  [[ "$1" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] || {
    echo "SQL bootstrap object ID must be a GUID" >&2
    exit 2
  }
}

validate_login() {
  [[ "$1" =~ ^[a-zA-Z0-9._@-]{1,128}$ ]] || {
    echo "SQL bootstrap login contains unsupported characters" >&2
    exit 2
  }
}

if [[ "$mode" == cleanup ]]; then
  : "${resource_group:?cleanup requires --resource-group}"
  : "${proof_name:?cleanup requires --proof-name so the external ACR role can be removed safely}"
  : "${registry_resource_group:?cleanup requires --registry-resource-group so the external ACR role can be removed safely}"
  validate_name "$proof_name"
  [[ -z "$subscription_id" ]] || az account set --subscription "$subscription_id"
  if az group show --name "$resource_group" --only-show-errors >/dev/null 2>&1; then
    identity_principal_id="$(az identity show --resource-group "$resource_group" --name "${proof_name}-identity" --query principalId --output tsv --only-show-errors)"
    registry_id="$(az acr show --resource-group "$registry_resource_group" --name "$registry_name" --query id --output tsv --only-show-errors)"
    role_ids="$(az role assignment list --scope "$registry_id" --assignee-object-id "$identity_principal_id" --role AcrPull --query '[].id' --output tsv --only-show-errors)"
    while IFS= read -r role_id; do
      [[ -z "$role_id" ]] || az role assignment delete --ids "$role_id" --only-show-errors
    done <<<"$role_ids"
  fi
  az group delete --name "$resource_group" --yes --no-wait --only-show-errors
  echo "Deletion requested for $resource_group; verify with: az group exists --name $resource_group"
  exit 0
fi

if [[ "$mode" == validate ]]; then
  "$repo_root/scripts/validate-azure-workload-proof.sh"
  echo "Bicep and static checks passed; no Azure resource was created or changed."
  exit 0
fi

validate_name "$proof_name"
validate_digest "$image_digest"
validate_repository "$image_repository"
validate_guid "$sql_bootstrap_object_id"
validate_login "$sql_bootstrap_login"
: "${resource_group:?$mode requires --resource-group}"
: "${registry_resource_group:?$mode requires --registry-resource-group}"
: "${sql_bootstrap_object_id:?$mode requires --sql-bootstrap-object-id}"
: "${sql_bootstrap_login:?$mode requires --sql-bootstrap-login}"
command -v jq >/dev/null || { echo "jq is required to read safe deployment outputs" >&2; exit 2; }

"$repo_root/scripts/validate-azure-workload-proof.sh"

parameters=(
  "proofName=$proof_name"
  "imageRepository=$image_repository"
  "imageDigest=$image_digest"
  "registryName=$registry_name"
  "registryResourceGroupName=$registry_resource_group"
  "sqlBootstrapObjectId=$sql_bootstrap_object_id"
  "sqlBootstrapLogin=$sql_bootstrap_login"
  "expiryUtc=$expiry_utc"
)

[[ -z "$subscription_id" ]] || az account set --subscription "$subscription_id"

if [[ "$mode" == what-if ]]; then
  if ! az group show --name "$resource_group" --only-show-errors >/dev/null 2>&1; then
    echo "what-if requires an existing resource group; no group was created" >&2
    exit 3
  fi
  az deployment group what-if \
    --resource-group "$resource_group" \
    --name "elsa108-${proof_name}-whatif" \
    --template-file "$proof_dir/main.bicep" \
    --parameters "${parameters[@]}" \
    --only-show-errors
  exit 0
fi

[[ "${DISPOSABLE_PROOF_APPLY:-}" == YES ]] || {
  echo "apply requires DISPOSABLE_PROOF_APPLY=YES; validation and what-if remain non-mutating" >&2
  exit 3
}

az group create --name "$resource_group" --location westeurope \
  --tags proof=108 owner=elsa-control expiry="$expiry_utc" --only-show-errors >/dev/null

deployment_name="elsa108-${proof_name}"
foundation_outputs="$(az deployment group create \
  --resource-group "$resource_group" \
  --name "${deployment_name}-foundation" \
  --template-file "$proof_dir/main.bicep" \
  --parameters "${parameters[@]}" deployWorkload=false \
  --query properties.outputs --output json --only-show-errors)"

identity_id="$(jq -r '.workloadIdentityId.value' <<<"$foundation_outputs")"
identity_client_id="$(jq -r '.workloadIdentityClientId.value' <<<"$foundation_outputs")"
identity_principal_id="$(jq -r '.workloadIdentityPrincipalId.value' <<<"$foundation_outputs")"
key_vault_name="${proof_name}-kv"
sql_fqdn="$(jq -r '.sqlServerFqdn.value' <<<"$foundation_outputs")"

# The ACR is intentionally outside the disposable group. This idempotent role
# assignment is the only proof mutation outside the supplied proof group.
az deployment group create \
  --resource-group "$registry_resource_group" \
  --name "${deployment_name}-acr-pull" \
  --template-file "$proof_dir/acr-pull-role.bicep" \
  --parameters registryName="$registry_name" workloadIdentityId="$identity_id" workloadPrincipalId="$identity_principal_id" \
  --query properties.outputs --output none --only-show-errors

command -v sqlcmd >/dev/null || {
  echo "sqlcmd is required for the controlled Entra contained-user bootstrap; install go-sqlcmd or the Microsoft ODBC sqlcmd client" >&2
  exit 4
}

temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT
umask 077

sql_connection="Server=tcp:${sql_fqdn},1433;Initial Catalog=Elsa;Encrypt=True;Authentication=\"Active Directory Managed Identity\";User Id=${identity_client_id};TrustServerCertificate=False;Connection Timeout=30;"
printf '%s' "$sql_connection" >"$temp_dir/sql-connection"
openssl rand -base64 48 >"$temp_dir/identity-signing-key"
if ! az keyvault secret show --vault-name "$key_vault_name" --name sql-connection --only-show-errors >/dev/null 2>&1; then
  az keyvault secret set --vault-name "$key_vault_name" --name sql-connection --file "$temp_dir/sql-connection" --only-show-errors >/dev/null
fi
if ! az keyvault secret show --vault-name "$key_vault_name" --name identity-signing-key --only-show-errors >/dev/null 2>&1; then
  az keyvault secret set --vault-name "$key_vault_name" --name identity-signing-key --file "$temp_dir/identity-signing-key" --only-show-errors >/dev/null
fi

sed \
  -e "s/__WORKLOAD_IDENTITY_NAME__/${proof_name}-identity/g" \
  -e "s/__WORKLOAD_IDENTITY_OBJECT_ID__/${identity_principal_id}/g" \
  "$proof_dir/sql-bootstrap.sql" >"$temp_dir/sql-bootstrap.sql"
sqlcmd -S "tcp:${sql_fqdn},1433" -d Elsa -G -i "$temp_dir/sql-bootstrap.sql"

az deployment group create \
  --resource-group "$resource_group" \
  --name "${deployment_name}-workload" \
  --template-file "$proof_dir/main.bicep" \
  --parameters "${parameters[@]}" deployWorkload=true \
  --query properties.outputs --output none --only-show-errors

endpoint="$(az deployment group show --resource-group "$resource_group" --name "${deployment_name}-workload" --query 'properties.outputs.containerAppEndpoint.value' --output tsv --only-show-errors)"
curl --fail --silent --show-error --retry 20 --retry-delay 10 "$endpoint/health" >/dev/null
echo "Workload is healthy at $endpoint; capture only redacted IDs, endpoint and immutable digest as evidence."
