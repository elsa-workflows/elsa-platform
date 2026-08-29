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
  --sql-bootstrap-ip <IPv4 address>    Exact operator IP for the temporary SQL firewall rule

Optional:
  --subscription <id>                  Azure subscription to use
  --registry-subscription <id>         Subscription containing the existing ACR (default: --subscription)
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
sql_bootstrap_ip=""
subscription_id=""
registry_subscription_id=""
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
    --sql-bootstrap-ip) sql_bootstrap_ip="${2:?Missing value for --sql-bootstrap-ip}"; shift 2 ;;
    --subscription) subscription_id="${2:?Missing value for --subscription}"; shift 2 ;;
    --registry-subscription) registry_subscription_id="${2:?Missing value for --registry-subscription}"; shift 2 ;;
    --expiry-utc) expiry_utc="${2:?Missing value for --expiry-utc}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
proof_dir="$repo_root/infra/azure-workload-proof"
# shellcheck source=scripts/lib/azure-workload-proof.sh
source "$script_dir/lib/azure-workload-proof.sh"

validate_name() {
  [[ "$1" =~ ^[a-z][a-z0-9-]{1,14}[a-z0-9]$ && "$1" != *--* ]] || {
    echo "proof name must be 3-16 lowercase letters, numbers or single hyphens; it must start with a letter and end with a letter or number" >&2
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

validate_ipv4() {
  local ip="$1" octet
  [[ "$ip" =~ ^[0-9]+(\.[0-9]+){3}$ && "$ip" != 0.0.0.0 ]] || {
    echo "--sql-bootstrap-ip must be a non-zero IPv4 address (CIDR and 0.0.0.0 are not accepted)" >&2
    exit 2
  }
  IFS=. read -r -a octets <<<"$ip"
  for octet in "${octets[@]}"; do
    (( octet <= 255 )) || { echo "--sql-bootstrap-ip contains an invalid octet" >&2; exit 2; }
  done
}

sha256_text() {
  if command -v sha256sum >/dev/null; then
    printf '%s' "$1" | sha256sum | awk '{print $1}'
  else
    printf '%s' "$1" | shasum -a 256 | awk '{print $1}'
  fi
}

sha256_stream() {
  if command -v sha256sum >/dev/null; then
    sha256sum | awk '{print $1}'
  else
    shasum -a 256 | awk '{print $1}'
  fi
}

load_revision_names() {
  local app_id="$1"
  local next_url="${app_id}/revisions?api-version=2024-03-01"
  local page page_names
  local revision_names='[]'

  while [[ -n "$next_url" ]]; do
    page="$(az rest --method get --url "$next_url" --only-show-errors)"
    page_names="$(jq -c '[.value[].name]' <<<"$page")"
    revision_names="$(jq -cn --argjson existing "$revision_names" --argjson page "$page_names" '$existing + $page')"
    next_url="$(jq -r '.nextLink // empty' <<<"$page")"
  done
  printf '%s\n' "$revision_names"
}

resolve_workload_revision_suffix() {
  local plan_fingerprint="$1"
  local app_name="${proof_name}-app"
  local app_id="/subscriptions/${proof_subscription_id}/resourceGroups/${resource_group}/providers/Microsoft.App/containerApps/${app_name}"
  local app_count current_revision_suffix revision_names

  app_count="$(az resource list --resource-group "$resource_group" --resource-type Microsoft.App/containerApps --query "[?name=='${app_name}'] | length(@)" --output tsv --only-show-errors)"
  if (( app_count == 0 )); then
    printf '%s\n' "$plan_fingerprint"
    return 0
  fi
  if (( app_count != 1 )); then
    echo "Expected exactly one proof Container App named $app_name" >&2
    return 5
  fi

  current_revision_suffix="$(az resource show --ids "$app_id" --api-version 2024-03-01 --query properties.template.revisionSuffix --output tsv --only-show-errors)"
  revision_names="$(load_revision_names "$app_id")"
  select_workload_revision_suffix "$plan_fingerprint" "$current_revision_suffix" "$app_name" "$revision_names"
}

resolve_stable_traffic_revision() {
  local app_name="${proof_name}-app"
  local app_count stable_revision stable_state

  app_count="$(az resource list --resource-group "$resource_group" --resource-type Microsoft.App/containerApps --query "[?name=='${app_name}'] | length(@)" --output tsv --only-show-errors)"
  if (( app_count == 0 )); then
    return 0
  fi
  if (( app_count != 1 )); then
    echo "Expected exactly one proof Container App named $app_name" >&2
    return 5
  fi

  # shellcheck disable=SC2016 # Backticks are JMESPath numeric literals.
  stable_revision="$(az containerapp show --resource-group "$resource_group" --name "$app_name" --query 'properties.configuration.ingress.traffic[?weight == `100` && revisionName != null].revisionName | [0]' --output tsv --only-show-errors)"
  if [[ -z "$stable_revision" ]]; then
    stable_revision="$(az containerapp show --resource-group "$resource_group" --name "$app_name" --query properties.latestReadyRevisionName --output tsv --only-show-errors)"
  fi
  [[ -n "$stable_revision" ]] || { echo "Existing proof app has no stable revision" >&2; return 5; }
  stable_state="$(az containerapp revision show --resource-group "$resource_group" --name "$app_name" --revision "$stable_revision" --query 'properties.{active:active,health:healthState}' --output json --only-show-errors)"
  [[ "$(jq -r .active <<<"$stable_state")" == true && "$(jq -r .health <<<"$stable_state")" == Healthy ]] || { echo "Refusing rollout because stable revision $stable_revision is not active and healthy" >&2; return 5; }
  printf '%s\n' "$stable_revision"
}

if [[ "$mode" == cleanup ]]; then
  : "${resource_group:?cleanup requires --resource-group}"
  : "${proof_name:?cleanup requires --proof-name so the external ACR role can be removed safely}"
  : "${registry_resource_group:?cleanup requires --registry-resource-group so the external ACR role can be removed safely}"
  validate_name "$proof_name"
  command -v jq >/dev/null || { echo "jq is required for ownership-safe cleanup" >&2; exit 2; }
  [[ -z "$subscription_id" ]] || az account set --subscription "$subscription_id"
  group_tags="$(az group show --name "$resource_group" --query tags --output json --only-show-errors 2>/dev/null || true)"
  [[ "$(jq -r '.proof // empty' <<<"$group_tags")" == 108 && "$(jq -r '.owner // empty' <<<"$group_tags")" == elsa-control && "$(jq -r '."proof-name" // empty' <<<"$group_tags")" == "$proof_name" ]] || {
    echo "Refusing cleanup: resource group is absent or does not belong to this exact proof" >&2
    exit 3
  }
  cleanup_status=0
  identity_principal_id="$(az identity show --resource-group "$resource_group" --name "${proof_name}-identity" --query principalId --output tsv --only-show-errors 2>/dev/null || true)"
  proof_subscription_id="$(az account show --query id --output tsv --only-show-errors)"
  registry_subscription_id="${registry_subscription_id:-$proof_subscription_id}"
  az account set --subscription "$registry_subscription_id"
  if ! registry_id="$(az acr show --resource-group "$registry_resource_group" --name "$registry_name" --query id --output tsv --only-show-errors)"; then
    echo "Refusing resource-group deletion: the requested ACR scope could not be resolved" >&2
    exit 3
  fi
  stored_deployment_name="$(jq -r '.acrDeployment // empty' <<<"$group_tags")"
  stored_principal_id="$(jq -r '.acrPrincipal // empty' <<<"$group_tags")"
  stored_registry_id="$(jq -r '.acrRegistryId // empty' <<<"$group_tags")"
  if [[ -n "$stored_principal_id" && -n "$identity_principal_id" && "$stored_principal_id" != "$identity_principal_id" ]]; then
    echo "Refusing resource-group deletion: stored and live proof identities do not match" >&2
    exit 3
  fi
  if [[ -n "$stored_registry_id" && "$stored_registry_id" != "$registry_id" ]]; then
    echo "Refusing resource-group deletion: stored and requested ACR scopes do not match" >&2
    exit 3
  fi
  cleanup_principal_id="${stored_principal_id:-$identity_principal_id}"
  role_assignment_id=""
  if [[ -n "$stored_deployment_name" ]]; then
    [[ "$cleanup_principal_id" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ && -n "$stored_registry_id" ]] || {
      echo "Refusing resource-group deletion: stored ACR deployment provenance is incomplete" >&2
      exit 3
    }
    external_context="${proof_subscription_id}/${resource_group}/${cleanup_principal_id}/${registry_subscription_id}/${registry_resource_group}/${registry_name}"
    expected_deployment_name="elsa108-${proof_name}-$(sha256_text "$external_context" | cut -c1-12)-acr"
    [[ "$stored_deployment_name" == "$expected_deployment_name" ]] || {
      echo "Refusing resource-group deletion: stored ACR deployment name does not match this exact proof context" >&2
      exit 3
    }
    if ! deployment_list_json="$(az deployment group list --resource-group "$registry_resource_group" --output json --only-show-errors)"; then
      echo "Refusing resource-group deletion: ACR deployment records could not be read" >&2
      exit 3
    fi
    role_assignment_id="$(jq -r --arg name "$stored_deployment_name" '[.[] | select(.name == $name)][0].properties.outputs.roleAssignmentId.value // empty' <<<"$deployment_list_json")"
    valid_role_assignment_id "$registry_id" "$role_assignment_id" || {
      echo "Refusing resource-group deletion: stored ACR deployment has no valid role-assignment output" >&2
      exit 3
    }
  fi
  if [[ -n "$role_assignment_id" ]]; then
    if ! assignment_list_json="$(az role assignment list --scope "$registry_id" --output json --only-show-errors)"; then
      echo "Refusing resource-group deletion: ACR role assignments could not be read" >&2
      exit 3
    fi
    assignment_json="$(jq -c --arg id "$role_assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))][0] // empty' <<<"$assignment_list_json")"
    if [[ -n "$assignment_json" ]]; then
      assignment_principal_id="$(jq -r '.principalId // empty' <<<"$assignment_json")"
      assignment_scope="$(jq -r '.scope // empty' <<<"$assignment_json")"
      assignment_scope_lower="$(printf '%s' "$assignment_scope" | tr '[:upper:]' '[:lower:]')"
      registry_id_lower="$(printf '%s' "$registry_id" | tr '[:upper:]' '[:lower:]')"
      assignment_role_id="$(jq -r '.roleDefinitionId // empty | split("/") | last' <<<"$assignment_json")"
      [[ "$assignment_principal_id" == "$cleanup_principal_id" && "$assignment_scope_lower" == "$registry_id_lower" && "$assignment_role_id" == 7f951dda-4ed3-4680-a7ca-43fe172d538d ]] || {
        echo "Refusing resource-group deletion: stored ACR assignment does not match this proof identity, scope, and role" >&2
        exit 3
      }
    fi
    delete_and_verify_role_assignment "$registry_id" "$role_assignment_id" || cleanup_status=1
    if (( cleanup_status == 0 )); then
      delete_and_verify_group_deployment "$registry_resource_group" "$stored_deployment_name" || cleanup_status=1
    fi
  elif [[ -n "$cleanup_principal_id" && -n "$registry_id" ]]; then
    if ! role_ids="$(az role assignment list --scope "$registry_id" --assignee-object-id "$cleanup_principal_id" --role AcrPull --query '[].id' --output tsv --only-show-errors)"; then
      echo "Refusing resource-group deletion: identity-scoped ACR assignments could not be read" >&2
      exit 3
    fi
    while IFS= read -r role_id; do
      [[ -z "$role_id" ]] || delete_and_verify_role_assignment "$registry_id" "$role_id" || cleanup_status=1
    done <<<"$role_ids"
  else
    echo "Refusing resource-group deletion: external ACR cleanup cannot be proven from the proof identity or deployment record" >&2
    exit 3
  fi
  az account set --subscription "$proof_subscription_id"
  if (( cleanup_status != 0 )); then
    echo "Refusing resource-group deletion because external ACR cleanup was incomplete" >&2
    exit 3
  fi
  az group delete --name "$resource_group" --yes --no-wait --only-show-errors || cleanup_status=1
  wait_for_resource_group_absence "$resource_group" || cleanup_status=1
  vault_name="${proof_name}-kv"
  purge_and_verify_deleted_vault "$vault_name" westeurope || cleanup_status=1
  (( cleanup_status == 0 )) && echo "Proof group deleted, external AcrPull removed, and proof vault purge verified." || echo "Cleanup incomplete; inspect exact proof targets." >&2
  exit "$cleanup_status"
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
validate_ipv4 "$sql_bootstrap_ip"
: "${resource_group:?$mode requires --resource-group}"
: "${registry_resource_group:?$mode requires --registry-resource-group}"
: "${sql_bootstrap_object_id:?$mode requires --sql-bootstrap-object-id}"
: "${sql_bootstrap_login:?$mode requires --sql-bootstrap-login}"
command -v jq >/dev/null || { echo "jq is required to read safe deployment outputs" >&2; exit 2; }

"$repo_root/scripts/validate-azure-workload-proof.sh"

# Bind the deployment/revision identity to the exact compiled IaC, not only to
# its data parameters. This prevents a code-only template change from being
# mistaken for an unchanged plan.
template_fingerprint="$(az bicep build --file "$proof_dir/main.bicep" --stdout | sha256_stream)"

[[ -z "$subscription_id" ]] || az account set --subscription "$subscription_id"
proof_subscription_id="$(az account show --query id --output tsv --only-show-errors)"
registry_subscription_id="${registry_subscription_id:-$proof_subscription_id}"

parameters=(
  "proofName=$proof_name"
  "imageRepository=$image_repository"
  "imageDigest=$image_digest"
  "registryName=$registry_name"
  "registrySubscriptionId=$registry_subscription_id"
  "registryResourceGroupName=$registry_resource_group"
  "sqlBootstrapObjectId=$sql_bootstrap_object_id"
  "sqlBootstrapLogin=$sql_bootstrap_login"
  "expiryUtc=$expiry_utc"
  "templateFingerprint=$template_fingerprint"
)

image_digest_lower="$(printf '%s' "$image_digest" | tr '[:upper:]' '[:lower:]')"
plan_input="proof=108|template=${template_fingerprint}|name=${proof_name}|location=westeurope|image=${image_repository}@sha256:${image_digest_lower}|elsa=3.8|sql-workflow=3.8.0-preview.5413|sql-quartz=3.8.0-preview.342|topology=combined|acr=${registry_subscription_id}/${registry_resource_group}/${registry_name}|sql-bootstrap=${sql_bootstrap_object_id}/${sql_bootstrap_login}|admin=proof-admin|secrets=sql-connection/identity-signing-key/admin-password|expiry=${expiry_utc}"
deployment_suffix="$(sha256_text "$plan_input" | cut -c1-12)"

[[ -z "$subscription_id" ]] || az account set --subscription "$subscription_id"

if [[ "$mode" == what-if ]]; then
  if ! az group show --name "$resource_group" --only-show-errors >/dev/null 2>&1; then
    echo "what-if requires an existing resource group; no group was created" >&2
    exit 3
  fi
  what_if_parameters=("${parameters[@]}")
  what_if_stable_revision="$(resolve_stable_traffic_revision)"
  [[ -z "$what_if_stable_revision" ]] || what_if_parameters+=("stableTrafficRevisionName=$what_if_stable_revision")
  foundation_deployment_name="elsa108-${proof_name}-${deployment_suffix}-foundation"
  foundation_deployment_count="$(az deployment group list --resource-group "$resource_group" --query "[?name=='${foundation_deployment_name}'] | length(@)" --output tsv --only-show-errors)"
  if (( foundation_deployment_count == 1 )); then
    foundation_plan_fingerprint="$(az deployment group show --resource-group "$resource_group" --name "$foundation_deployment_name" --query properties.outputs.planFingerprint.value --output tsv --only-show-errors)"
    what_if_revision_suffix="$(resolve_workload_revision_suffix "$foundation_plan_fingerprint")"
    what_if_parameters+=("workloadRevisionSuffix=$what_if_revision_suffix")
  elif (( foundation_deployment_count != 0 )); then
    echo "Expected at most one matching foundation deployment" >&2
    exit 5
  fi
  az deployment group what-if \
    --resource-group "$resource_group" \
    --name "elsa108-${proof_name}-${deployment_suffix}-whatif" \
    --template-file "$proof_dir/main.bicep" \
    --parameters "${what_if_parameters[@]}" \
    --only-show-errors
  exit 0
fi

[[ "${DISPOSABLE_PROOF_APPLY:-}" == YES ]] || {
  echo "apply requires DISPOSABLE_PROOF_APPLY=YES; validation and what-if remain non-mutating" >&2
  exit 3
}

group_exists="$(az group exists --name "$resource_group" --output tsv --only-show-errors)"
if [[ "$group_exists" == true ]]; then
  existing_tags="$(az group show --name "$resource_group" --query tags --output json --only-show-errors)"
  [[ "$(jq -r '.proof // empty' <<<"$existing_tags")" == 108 && "$(jq -r '.owner // empty' <<<"$existing_tags")" == elsa-control && "$(jq -r '."proof-name" // empty' <<<"$existing_tags")" == "$proof_name" ]] || { echo "Refusing to adopt unrelated resource group" >&2; exit 3; }
  az tag update --resource-id "/subscriptions/${proof_subscription_id}/resourceGroups/${resource_group}" \
    --operation Merge --tags proof=108 owner=elsa-control proof-name="$proof_name" expiry="$expiry_utc" \
    --only-show-errors >/dev/null
elif [[ "$group_exists" == false ]]; then
  az group create --name "$resource_group" --location westeurope \
    --tags proof=108 owner=elsa-control proof-name="$proof_name" expiry="$expiry_utc" --only-show-errors >/dev/null
else
  echo "Could not determine whether the proof resource group exists" >&2
  exit 5
fi

sql_server_name="${proof_name}-sql"
temporary_firewall_rule="elsa108-bootstrap"
temporary_firewall_created=0
bootstrap_admin_removed=0
temp_dir=""
ensure_sql_bootstrap_admin_for_reapply() {
  local admin_count admin_state server_count
  server_count="$(az sql server list --subscription "$proof_subscription_id" --resource-group "$resource_group" --query "[?name=='${sql_server_name}'] | length(@)" --output tsv --only-show-errors)" || return 1
  if (( server_count == 0 )); then return 0; fi
  (( server_count == 1 )) || { echo "Expected at most one proof SQL server named $sql_server_name" >&2; return 1; }

  admin_count="$(az sql server ad-admin list --subscription "$proof_subscription_id" --resource-group "$resource_group" --server "$sql_server_name" --query 'length(@)' --output tsv --only-show-errors)" || return 1
  (( admin_count <= 1 )) || { echo "Expected at most one SQL server administrator" >&2; return 1; }
  if (( admin_count == 0 )); then
    az sql server ad-admin create --subscription "$proof_subscription_id" --resource-group "$resource_group" --server "$sql_server_name" \
      --display-name "$sql_bootstrap_login" --object-id "$sql_bootstrap_object_id" --only-show-errors >/dev/null || return 1
  fi
  admin_state="$(az sql server ad-admin list --subscription "$proof_subscription_id" --resource-group "$resource_group" --server "$sql_server_name" --query '[0].{login:login,sid:sid}' --output json --only-show-errors)" || return 1
  [[ "$(jq -r .login <<<"$admin_state")" == "$sql_bootstrap_login" && "$(jq -r .sid <<<"$admin_state")" == "$sql_bootstrap_object_id" ]] || { echo "Refusing to replace an unexpected SQL server administrator" >&2; return 1; }
  az sql server ad-only-auth enable --subscription "$proof_subscription_id" --resource-group "$resource_group" --name "$sql_server_name" --only-show-errors >/dev/null || return 1
  bootstrap_admin_removed=0
}
remove_sql_bootstrap_admin() {
  remove_owned_sql_bootstrap_admin "$proof_subscription_id" "$resource_group" "$sql_server_name" \
    "$sql_bootstrap_login" "$sql_bootstrap_object_id" || return 1
  bootstrap_admin_removed=1
}
cleanup_apply() {
  if (( temporary_firewall_created )); then
    az sql server firewall-rule delete --subscription "$proof_subscription_id" --resource-group "$resource_group" --server "$sql_server_name" --name "$temporary_firewall_rule" --only-show-errors >/dev/null 2>&1 || true
  fi
  if (( bootstrap_admin_removed == 0 )); then
    remove_sql_bootstrap_admin >/dev/null 2>&1 || echo "CRITICAL: temporary SQL bootstrap administrator cleanup could not be verified" >&2
  fi
  [[ -z "$temp_dir" ]] || rm -rf -- "$temp_dir"
}
trap cleanup_apply EXIT

ensure_sql_bootstrap_admin_for_reapply || { echo "Temporary SQL bootstrap administrator could not be established safely" >&2; exit 5; }

deployment_name="elsa108-${proof_name}-${deployment_suffix}"
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
plan_fingerprint="$(jq -r '.planFingerprint.value' <<<"$foundation_outputs")"

workload_revision_suffix="$(resolve_workload_revision_suffix "$plan_fingerprint")"
stable_traffic_revision="$(resolve_stable_traffic_revision)"

# The ACR is intentionally outside the disposable group. This idempotent role
# assignment is the only proof mutation outside the supplied proof group.
az account set --subscription "$registry_subscription_id"
external_context="${proof_subscription_id}/${resource_group}/${identity_principal_id}/${registry_subscription_id}/${registry_resource_group}/${registry_name}"
external_deployment_suffix="$(sha256_text "$external_context" | cut -c1-12)"
external_deployment_name="elsa108-${proof_name}-${external_deployment_suffix}-acr"
az deployment group create \
  --resource-group "$registry_resource_group" \
  --name "$external_deployment_name" \
  --template-file "$proof_dir/acr-pull-role.bicep" \
  --parameters registryName="$registry_name" workloadIdentityId="$identity_id" workloadPrincipalId="$identity_principal_id" \
  --query properties.outputs --output none --only-show-errors
acr_role_ready=0
registry_id="$(az acr show --resource-group "$registry_resource_group" --name "$registry_name" --query id --output tsv --only-show-errors)"
for _ in {1..12}; do
  if [[ "$(az role assignment list --scope "$registry_id" --assignee-object-id "$identity_principal_id" --role AcrPull --query 'length(@)' --output tsv --only-show-errors 2>/dev/null || echo 0)" -gt 0 ]]; then
    acr_role_ready=1
    break
  fi
  sleep 5
done
(( acr_role_ready == 1 )) || { echo "AcrPull role assignment did not become observable" >&2; exit 5; }
az account set --subscription "$proof_subscription_id"
az group update --name "$resource_group" \
  --set tags.acrDeployment="$external_deployment_name" tags.acrPrincipal="$identity_principal_id" tags.acrRegistryId="$registry_id" \
  --only-show-errors >/dev/null

command -v sqlcmd >/dev/null || {
  echo "Go sqlcmd is required; install github.com/microsoft/go-sqlcmd and ensure --authentication-method is supported" >&2
  exit 4
}
sqlcmd_compat_help="$(sqlcmd '-?' 2>&1)" || {
  echo "sqlcmd compatibility help could not be read" >&2
  exit 4
}
grep -q -- '--authentication-method' <<<"$sqlcmd_compat_help" || {
  echo "sqlcmd must be the Go sqlcmd with --authentication-method support; ODBC sqlcmd is not supported" >&2
  exit 4
}

az sql server firewall-rule create --resource-group "$resource_group" --server "$sql_server_name" --name "$temporary_firewall_rule" \
  --start-ip-address "$sql_bootstrap_ip" --end-ip-address "$sql_bootstrap_ip" --only-show-errors >/dev/null
temporary_firewall_created=1

temp_dir="$(mktemp -d)"
umask 077

sql_connection="Server=tcp:${sql_fqdn},1433;Initial Catalog=Elsa;Encrypt=True;Authentication=\"Active Directory Managed Identity\";User Id=${identity_client_id};TrustServerCertificate=False;Connection Timeout=30;"
printf '%s' "$sql_connection" >"$temp_dir/sql-connection"
openssl rand -base64 48 | tr -d '\r\n' >"$temp_dir/identity-signing-key"
openssl rand -base64 48 | tr -d '\r\n' >"$temp_dir/admin-password"
seed_secret_if_missing() {
  local name="$1" file="$2"
  if az keyvault secret show --vault-name "$key_vault_name" --name "$name" --only-show-errors >/dev/null 2>&1; then return 0; fi
  for _ in {1..12}; do
    if az keyvault secret set --vault-name "$key_vault_name" --name "$name" --file "$file" --only-show-errors >/dev/null; then return 0; fi
    sleep 5
  done
  echo "Key Vault RBAC propagation did not complete for secret $name" >&2
  return 1
}
seed_secret_if_missing sql-connection "$temp_dir/sql-connection"
seed_secret_if_missing identity-signing-key "$temp_dir/identity-signing-key"
seed_secret_if_missing admin-password "$temp_dir/admin-password"

sed \
  -e "s/__WORKLOAD_IDENTITY_NAME__/${proof_name}-identity/g" \
  -e "s/__WORKLOAD_IDENTITY_CLIENT_ID__/${identity_client_id}/g" \
  "$proof_dir/sql-bootstrap.sql" >"$temp_dir/sql-bootstrap.sql"
bootstrap_ok=0
for _ in {1..12}; do
  if sqlcmd -S "tcp:${sql_fqdn},1433" -d Elsa --authentication-method ActiveDirectoryDefault -i "$temp_dir/sql-bootstrap.sql"; then bootstrap_ok=1; break; fi
  sleep 10
done
(( bootstrap_ok == 1 )) || { echo "SQL bootstrap did not become ready" >&2; exit 5; }
az sql server firewall-rule delete --resource-group "$resource_group" --server "$sql_server_name" --name "$temporary_firewall_rule" --only-show-errors >/dev/null
temporary_firewall_created=0

az deployment group create \
  --resource-group "$resource_group" \
  --name "${deployment_name}-workload" \
  --template-file "$proof_dir/main.bicep" \
  --parameters "${parameters[@]}" deployWorkload=true workloadRevisionSuffix="$workload_revision_suffix" stableTrafficRevisionName="$stable_traffic_revision" \
  --query properties.outputs --output none --only-show-errors

remove_sql_bootstrap_admin || { echo "Temporary SQL bootstrap administrator cleanup failed" >&2; exit 5; }

endpoint="$(az deployment group show --resource-group "$resource_group" --name "${deployment_name}-workload" --query 'properties.outputs.containerAppEndpoint.value' --output tsv --only-show-errors)"
candidate_revision="${proof_name}-app--${workload_revision_suffix}"
candidate_healthy=0
for _ in {1..60}; do
  candidate_state="$(az containerapp revision show --resource-group "$resource_group" --name "${proof_name}-app" --revision "$candidate_revision" --query properties.healthState --output tsv --only-show-errors)"
  if [[ "$candidate_state" == Healthy ]]; then candidate_healthy=1; break; fi
  sleep 5
done
(( candidate_healthy == 1 )) || { echo "Candidate revision $candidate_revision did not become healthy; stable traffic was preserved" >&2; exit 5; }

promote_workload_revision "$resource_group" "${proof_name}-app" "$stable_traffic_revision" "$candidate_revision" "$endpoint"
echo "Workload is healthy at $endpoint; capture only redacted IDs, endpoint and immutable digest as evidence."
