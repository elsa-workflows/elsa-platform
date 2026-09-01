#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

usage() {
  cat <<'EOF'
Usage:
  scripts/azure-workload-restore-proof.sh validate [options]
  scripts/azure-workload-restore-proof.sh what-if [options]
  scripts/azure-workload-restore-proof.sh apply [options]
  scripts/azure-workload-restore-proof.sh cleanup [options]

Required options:
  --source-proof-name <name>       Existing disposable #108 proof name
  --source-resource-group <name>   Existing disposable source resource group
  --target-name <name>             New isolated target name (3-16 safe characters)
  --target-resource-group <name>   New disposable target resource group
  --recovery-id <id>               Lowercase deterministic recovery identity
  --image-digest <64 hex>          Exact immutable Elsa image digest
  --registry-resource-group <name> Existing governed ACR resource group
  --sql-bootstrap-object-id <guid> Governed Entra SQL administrator object ID
  --sql-bootstrap-login <name>     Governed Entra SQL administrator login
  --sql-bootstrap-ip <IPv4>        Exact temporary operator firewall address

Optional:
  --subscription <id>
  --registry-subscription <id>
  --registry-name <name>           Default: valenceruntimeimages
  --image-repository <repository>  Default: valenceruntimeimages.azurecr.io/runtime-combined
  --expiry-utc <YYYY-MM-DD>

Apply and cleanup require DISPOSABLE_PROOF_APPLY=YES. Apply performs the live
restore, emits safe evidence, and cleans the target. It never deletes or routes
traffic to/from the source proof.
EOF
}

mode="${1:-}"
case "$mode" in validate|what-if|apply|cleanup) shift ;; *) usage >&2; exit 2 ;; esac

source_proof_name=""
source_resource_group=""
target_name=""
target_resource_group=""
recovery_id=""
image_digest=""
registry_resource_group=""
sql_bootstrap_object_id=""
sql_bootstrap_login=""
sql_bootstrap_ip=""
subscription_id=""
registry_subscription_id=""
registry_name="valenceruntimeimages"
image_repository="valenceruntimeimages.azurecr.io/runtime-combined"
expiry_utc=""

while (($#)); do
  case "$1" in
    --source-proof-name) source_proof_name="${2:?}"; shift 2 ;;
    --source-resource-group) source_resource_group="${2:?}"; shift 2 ;;
    --target-name) target_name="${2:?}"; shift 2 ;;
    --target-resource-group) target_resource_group="${2:?}"; shift 2 ;;
    --recovery-id) recovery_id="${2:?}"; shift 2 ;;
    --image-digest) image_digest="${2:?}"; shift 2 ;;
    --registry-resource-group) registry_resource_group="${2:?}"; shift 2 ;;
    --sql-bootstrap-object-id) sql_bootstrap_object_id="${2:?}"; shift 2 ;;
    --sql-bootstrap-login) sql_bootstrap_login="${2:?}"; shift 2 ;;
    --sql-bootstrap-ip) sql_bootstrap_ip="${2:?}"; shift 2 ;;
    --subscription) subscription_id="${2:?}"; shift 2 ;;
    --registry-subscription) registry_subscription_id="${2:?}"; shift 2 ;;
    --registry-name) registry_name="${2:?}"; shift 2 ;;
    --image-repository) image_repository="${2:?}"; shift 2 ;;
    --expiry-utc) expiry_utc="${2:?}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
proof_dir="$repo_root/infra/azure-workload-proof"
target_template="$proof_dir/recovery-target.bicep"
probe_project="$repo_root/src/Deployment/ElsaControl.Deployment.WorkflowProbeHost/ElsaControl.WorkflowProbeHost.csproj"
# shellcheck source=scripts/lib/azure-workload-proof.sh
source "$script_dir/lib/azure-workload-proof.sh"
expiry_utc="${expiry_utc:-$(default_expiry_utc)}"

fail() { echo "$1" >&2; exit 2; }
valid_name() { [[ "$1" =~ ^[a-z][a-z0-9-]{1,14}[a-z0-9]$ && "$1" != *--* ]]; }
valid_group() { [[ "$1" =~ ^[A-Za-z0-9._()-]{1,90}$ ]]; }
valid_guid() { [[ "$1" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; }
valid_ip() {
  local octet
  [[ "$1" =~ ^[0-9]+(\.[0-9]+){3}$ && "$1" != 0.0.0.0 ]] || return 1
  IFS=. read -r -a octets <<<"$1"
  for octet in "${octets[@]}"; do (( octet <= 255 )) || return 1; done
}
sha256_stream() { if command -v sha256sum >/dev/null; then sha256sum | awk '{print $1}'; else shasum -a 256 | awk '{print $1}'; fi; }
sha256_text() { printf '%s' "$1" | sha256_stream; }
epoch() { python3 -c 'from datetime import datetime; import sys; print(datetime.fromisoformat(sys.argv[1].replace("Z", "+00:00")).timestamp())' "$1"; }
utc_now() { date -u '+%Y-%m-%dT%H:%M:%SZ'; }

valid_name "$source_proof_name" || fail "source proof name is invalid"
valid_name "$target_name" || fail "target name is invalid"
[[ "$recovery_id" =~ ^[a-z0-9]{3,12}$ ]] || fail "recovery ID is invalid"
valid_group "$source_resource_group" || fail "source resource group is invalid"
valid_group "$target_resource_group" || fail "target resource group is invalid"
[[ "$source_resource_group" != "$target_resource_group" ]] || fail "source and target resource groups must differ"
[[ "$image_digest" =~ ^[a-fA-F0-9]{64}$ ]] || fail "image digest must be 64 hexadecimal characters"
image_digest="$(printf '%s' "$image_digest" | tr '[:upper:]' '[:lower:]')"
[[ "$registry_name" == valenceruntimeimages ]] || fail "registry must be valenceruntimeimages"
[[ "$image_repository" =~ ^valenceruntimeimages\.azurecr\.io/[a-z0-9._/-]+$ && "$image_repository" != *:* && "$image_repository" != *@* ]] || fail "image repository is invalid"
valid_group "$registry_resource_group" || fail "registry resource group is invalid"
valid_guid "$sql_bootstrap_object_id" || fail "SQL bootstrap object ID is invalid"
[[ "$sql_bootstrap_login" =~ ^[a-zA-Z0-9._@-]{1,128}$ ]] || fail "SQL bootstrap login is invalid"
valid_ip "$sql_bootstrap_ip" || fail "SQL bootstrap IP is invalid"
[[ "$expiry_utc" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || fail "expiry date is invalid"

for command_name in az jq python3 dotnet sqlcmd curl sed; do
  command -v "$command_name" >/dev/null || fail "$command_name is required"
done
[[ -f "$target_template" && -f "$probe_project" && -f "$proof_dir/sql-bootstrap.sql" ]] || fail "checked-in recovery proof artifacts are missing"
az bicep build --file "$target_template" --stdout >/dev/null

if [[ "$mode" == validate ]]; then
  echo '{"outcome":"passed","mode":"validate","proof":"129"}'
  exit 0
fi

if [[ "$mode" == apply || "$mode" == cleanup ]]; then
  [[ "${DISPOSABLE_PROOF_APPLY:-}" == YES ]] || fail "DISPOSABLE_PROOF_APPLY=YES is required for Azure mutation"
fi
subscription_id="${subscription_id:-$(az account show --query id --output tsv --only-show-errors)}"
registry_subscription_id="${registry_subscription_id:-$subscription_id}"
valid_guid "$subscription_id" || fail "subscription ID is invalid"
valid_guid "$registry_subscription_id" || fail "registry subscription ID is invalid"

source_server="${source_proof_name}-sql"
source_database="Elsa"
source_vault="${source_proof_name}-kv"
source_app="${source_proof_name}-app"
target_database="ElsaRestore${recovery_id}"
target_vault="${target_name}-kv"
target_identity="${target_name}-identity"
target_app="${target_name}-app"
acr_deployment="elsa129-${target_name}"
target_db_id="/subscriptions/${subscription_id}/resourceGroups/${source_resource_group}/providers/Microsoft.Sql/servers/${source_server}/databases/${target_database}"
registry_id="/subscriptions/${registry_subscription_id}/resourceGroups/${registry_resource_group}/providers/Microsoft.ContainerRegistry/registries/${registry_name}"
firewall_rule="elsa129-${recovery_id}"
firewall_rule_created=0
temp_dir="$(mktemp -d "${TMPDIR:-/tmp}/elsa129.XXXXXX")"

cleanup_local() {
  if (( firewall_rule_created == 1 )); then
    delete_and_verify_firewall_rule "$subscription_id" "$source_resource_group" "$source_server" "$firewall_rule" 12 5 || true
  fi
  find "$temp_dir" -type f -exec chmod 600 {} + 2>/dev/null || true
  rm -rf -- "$temp_dir"
}
trap cleanup_local EXIT HUP INT TERM

verify_source() {
  local group_json source_image endpoint source_healthy=0
  group_json="$(az group show --subscription "$subscription_id" --name "$source_resource_group" --output json --only-show-errors)"
  jq -e --arg proof "$source_proof_name" '.tags.proof == "108" and .tags.owner == "elsa-control" and (.name | length) > 0' <<<"$group_json" >/dev/null || fail "source group ownership is invalid"
  source_image="$(az containerapp show --subscription "$subscription_id" --resource-group "$source_resource_group" --name "$source_app" --query 'properties.template.containers[0].image' --output tsv --only-show-errors)"
  [[ "$source_image" == "${image_repository}@sha256:${image_digest}" ]] || fail "source image does not match the admitted digest"
  endpoint="$(az containerapp show --subscription "$subscription_id" --resource-group "$source_resource_group" --name "$source_app" --query properties.configuration.ingress.fqdn --output tsv --only-show-errors)"
  [[ "$endpoint" == "${source_app}."*.azurecontainerapps.io ]] || fail "source endpoint is invalid"
  for _ in {1..12}; do
    if curl --fail --silent --show-error --max-time 30 "https://${endpoint}/health" >/dev/null 2>&1; then
      source_healthy=1
      break
    fi
    sleep 10
  done
  (( source_healthy == 1 )) || fail "source health could not be verified"
  printf '%s\n' "https://${endpoint}"
}

verify_target_group_inventory() {
  local resources group_id
  group_id="/subscriptions/${subscription_id}/resourceGroups/${target_resource_group}"
  resources="$(az resource list --subscription "$subscription_id" --resource-group "$target_resource_group" --output json --only-show-errors)"
  jq -e --arg base "$group_id" --arg target "$target_name" '
    def root($provider; $type; $name): ($base + "/providers/" + $provider + "/" + $type + "/" + $name);
    def starts($root): (.id | ascii_downcase) == ($root | ascii_downcase) or (.id | ascii_downcase | startswith(($root | ascii_downcase) + "/"));
    all(.[];
      starts(root("Microsoft.ManagedIdentity"; "userAssignedIdentities"; ($target + "-identity"))) or
      starts(root("Microsoft.KeyVault"; "vaults"; ($target + "-kv"))) or
      starts(root("Microsoft.OperationalInsights"; "workspaces"; ($target + "-logs"))) or
      starts(root("Microsoft.App"; "managedEnvironments"; ($target + "-aca"))) or
      starts(root("Microsoft.App"; "containerApps"; ($target + "-app"))))
  ' <<<"$resources" >/dev/null || fail "target resource inventory is not exact"
}

cleanup_target() {
  local group_exists group_json target_db_json assignment_id vault_location
  group_exists="$(az group exists --name "$target_resource_group" --subscription "$subscription_id" --output tsv --only-show-errors)"
  if [[ "$group_exists" == true ]]; then
    group_json="$(az group show --subscription "$subscription_id" --name "$target_resource_group" --output json --only-show-errors)"
    jq -e --arg recovery "$recovery_id" '.tags.proof == "129" and .tags.owner == "elsa-control" and .tags["recovery-id"] == $recovery and .tags["target-role"] == "restore"' <<<"$group_json" >/dev/null || fail "target group ownership is invalid"
    verify_target_group_inventory
  fi

  assignment_id="$(az deployment group show --subscription "$registry_subscription_id" --resource-group "$registry_resource_group" --name "$acr_deployment" --query properties.outputs.roleAssignmentId.value --output tsv --only-show-errors 2>/dev/null || true)"
  if [[ -n "$assignment_id" ]]; then
    valid_role_assignment_id "$registry_id" "$assignment_id" || fail "target ACR role assignment identity is invalid"
    delete_and_verify_role_assignment "$registry_id" "$assignment_id"
    delete_and_verify_group_deployment "$registry_resource_group" "$acr_deployment"
  fi

  target_db_json="$(az sql db show --subscription "$subscription_id" --resource-group "$source_resource_group" --server "$source_server" --name "$target_database" --output json --only-show-errors 2>/dev/null || true)"
  if [[ -n "$target_db_json" ]]; then
    jq -e --arg recovery "$recovery_id" --arg id "$target_db_id" '(.id | ascii_downcase) == ($id | ascii_downcase) and .tags.proof == "129" and .tags["recovery-id"] == $recovery and .tags["target-role"] == "restore"' <<<"$target_db_json" >/dev/null || fail "target database ownership is invalid"
    az sql db delete --subscription "$subscription_id" --resource-group "$source_resource_group" --server "$source_server" --name "$target_database" --yes --no-wait --only-show-errors >/dev/null
    for _ in {1..180}; do
      az sql db show --subscription "$subscription_id" --resource-group "$source_resource_group" --server "$source_server" --name "$target_database" --only-show-errors >/dev/null 2>&1 || break
      sleep 5
    done
    az sql db show --subscription "$subscription_id" --resource-group "$source_resource_group" --server "$source_server" --name "$target_database" --only-show-errors >/dev/null 2>&1 && fail "target database absence was not verified"
  fi

  if [[ "$group_exists" == true ]]; then
    vault_location="$(az keyvault show --subscription "$subscription_id" --resource-group "$target_resource_group" --name "$target_vault" --query location --output tsv --only-show-errors)"
    az group delete --subscription "$subscription_id" --name "$target_resource_group" --yes --no-wait --only-show-errors >/dev/null
    wait_for_resource_group_absence "$target_resource_group"
    purge_and_verify_deleted_vault "$target_vault" "$vault_location"
  fi
}

if [[ "$mode" == cleanup ]]; then
  cleanup_target
  echo '{"outcome":"passed","mode":"cleanup","targetResourcesAbsent":true}'
  exit 0
fi

source_endpoint="$(verify_source)"

if [[ "$mode" == what-if ]]; then
  [[ "$(az group exists --subscription "$subscription_id" --name "$target_resource_group" --output tsv --only-show-errors)" == true ]] || fail "what-if requires an existing target resource group"
  template_fingerprint="$(az bicep build --file "$target_template" --stdout | sha256_stream)"
  az deployment group what-if --subscription "$subscription_id" --resource-group "$target_resource_group" --template-file "$target_template" \
    --parameters targetName="$target_name" imageRepository="$image_repository" imageDigest="$image_digest" \
    registryName="$registry_name" registrySubscriptionId="$registry_subscription_id" registryResourceGroupName="$registry_resource_group" \
    bootstrapObjectId="$sql_bootstrap_object_id" restoredDatabaseId="$target_db_id" recoveryPointDigest="$(printf '0%.0s' {1..64})" \
    templateFingerprint="$template_fingerprint" deployWorkload=false expiryUtc="$expiry_utc" --only-show-errors
  exit 0
fi

source_password_file="$temp_dir/admin-password"
source_signing_file="$temp_dir/identity-signing-key"
az keyvault secret download --subscription "$subscription_id" --vault-name "$source_vault" --name admin-password --file "$source_password_file" --encoding utf-8 --only-show-errors >/dev/null
chmod 600 "$source_password_file"
pre_definition="elsa-recovery-pre-${source_proof_name}-${recovery_id}"
post_definition="elsa-recovery-post-${source_proof_name}-${recovery_id}"
dotnet run --project "$probe_project" --no-restore -- \
  --endpoint "$source_endpoint" --environment "$source_proof_name" --username proof-admin --password-file "$source_password_file" \
  --workflow-id "$pre_definition" --mode create >"$temp_dir/pre.json"
jq -e '.outcome == "passed" and .result == "Finished"' "$temp_dir/pre.json" >/dev/null || fail "source pre-point workflow did not complete"

source_quiesced_at="$(utc_now)"
settle_seconds="${AZURE_RECOVERY_POINT_SETTLE_SECONDS:-120}"
[[ "$settle_seconds" =~ ^[1-9][0-9]*$ ]] || fail "recovery-point settle configuration is invalid"
sleep "$settle_seconds"
restore_point_utc="$(python3 -c 'from datetime import datetime, timezone, timedelta; print((datetime.now(timezone.utc)-timedelta(seconds=30)).replace(microsecond=0).isoformat().replace("+00:00", "Z"))')"
pre_committed_at="$(jq -r '.evidence.finishedAt // empty' "$temp_dir/pre.json")"
[[ -n "$pre_committed_at" ]] || fail "pre-point workflow timestamp is missing"
python3 -c 'import sys; raise SystemExit(0 if float(sys.argv[1]) <= float(sys.argv[2]) else 1)' "$(epoch "$pre_committed_at")" "$(epoch "$restore_point_utc")" || fail "selected recovery point does not contain the pre-point workflow"
source_db_id="/subscriptions/${subscription_id}/resourceGroups/${source_resource_group}/providers/Microsoft.Sql/servers/${source_server}/databases/${source_database}"
earliest_restore_date="$(az rest --method get --url "https://management.azure.com${source_db_id}?api-version=2023-08-01" --query properties.earliestRestoreDate --output tsv --only-show-errors)"
[[ -n "$earliest_restore_date" ]] || fail "Azure did not return an earliest restore date"
python3 -c 'import sys; raise SystemExit(0 if float(sys.argv[1]) <= float(sys.argv[2]) else 1)' "$(epoch "$earliest_restore_date")" "$(epoch "$restore_point_utc")" || fail "selected recovery point is outside the Azure retention window"

manifest_digest="$(sha256_text "schema=1|source=${source_proof_name}|point=${restore_point_utc}|image=${image_repository}@sha256:${image_digest}|pre=${pre_definition}|secrets=admin-password,identity-signing-key,sql-connection")"
dotnet run --project "$probe_project" --no-restore -- \
  --endpoint "$source_endpoint" --environment "$source_proof_name" --username proof-admin --password-file "$source_password_file" \
  --workflow-id "$post_definition" --mode create >"$temp_dir/post.json"
jq -e '.outcome == "passed" and .result == "Finished"' "$temp_dir/post.json" >/dev/null || fail "source post-point workflow did not complete"
post_committed_at="$(jq -r '.evidence.finishedAt // empty' "$temp_dir/post.json")"
[[ -n "$post_committed_at" ]] || fail "post-point workflow timestamp is missing"
python3 -c 'import sys; raise SystemExit(0 if float(sys.argv[1]) > float(sys.argv[2]) else 1)' "$(epoch "$post_committed_at")" "$(epoch "$restore_point_utc")" || fail "post-point marker did not follow the selected point"

restore_accepted_at="$(utc_now)"
if [[ "$(az group exists --subscription "$subscription_id" --name "$target_resource_group" --output tsv --only-show-errors)" == true ]]; then
  existing_target_group="$(az group show --subscription "$subscription_id" --name "$target_resource_group" --output json --only-show-errors)"
  jq -e --arg recovery "$recovery_id" '.tags.proof == "129" and .tags.owner == "elsa-control" and .tags["recovery-id"] == $recovery and .tags["target-role"] == "restore" and .tags["managed-by"] == "elsa-control-recovery"' <<<"$existing_target_group" >/dev/null || fail "refusing to adopt an unrelated target resource group"
else
  az group create --subscription "$subscription_id" --name "$target_resource_group" --location westeurope \
    --tags proof=129 owner=elsa-control recovery-id="$recovery_id" target-role=restore managed-by=elsa-control-recovery expiry="$expiry_utc" --only-show-errors >/dev/null
fi
template_fingerprint="$(az bicep build --file "$target_template" --stdout | sha256_stream)"
deploy_target() {
  local deploy_workload="$1"
  az deployment group create --subscription "$subscription_id" --resource-group "$target_resource_group" --name "elsa129-${target_name}" \
    --template-file "$target_template" --parameters targetName="$target_name" imageRepository="$image_repository" imageDigest="$image_digest" \
    registryName="$registry_name" registrySubscriptionId="$registry_subscription_id" registryResourceGroupName="$registry_resource_group" \
    bootstrapObjectId="$sql_bootstrap_object_id" restoredDatabaseId="$target_db_id" recoveryPointDigest="$manifest_digest" \
    templateFingerprint="$template_fingerprint" deployWorkload="$deploy_workload" expiryUtc="$expiry_utc" --only-show-errors --output json
}
deploy_target false >"$temp_dir/target-foundation.json"
target_identity_id="$(jq -r '.properties.outputs.workloadIdentityId.value' "$temp_dir/target-foundation.json")"
target_client_id="$(jq -r '.properties.outputs.workloadIdentityClientId.value' "$temp_dir/target-foundation.json")"
target_principal_id="$(jq -r '.properties.outputs.workloadIdentityPrincipalId.value' "$temp_dir/target-foundation.json")"
[[ "$target_identity_id" == "/subscriptions/${subscription_id}/resourceGroups/${target_resource_group}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/${target_identity}" ]] || fail "target identity output is invalid"
if ! valid_guid "$target_client_id" || ! valid_guid "$target_principal_id"; then
  fail "target identity properties are invalid"
fi

az deployment group create --subscription "$registry_subscription_id" --resource-group "$registry_resource_group" --name "$acr_deployment" \
  --template-file "$proof_dir/acr-pull-role.bicep" --parameters registryName="$registry_name" workloadIdentityId="$target_identity_id" workloadPrincipalId="$target_principal_id" \
  --only-show-errors >"$temp_dir/acr.json"
assignment_id="$(jq -r '.properties.outputs.roleAssignmentId.value' "$temp_dir/acr.json")"
valid_role_assignment_id "$registry_id" "$assignment_id" || fail "target ACR assignment identity is invalid"
assignment_json=""
for _ in {1..12}; do
  assignment_json="$(az role assignment list --all --assignee-object-id "$target_principal_id" --output json --only-show-errors 2>/dev/null | jq -c --arg id "$assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | if length == 1 then .[0] else empty end' 2>/dev/null || true)"
  [[ -n "$assignment_json" ]] && break
  sleep 5
done
[[ -n "$assignment_json" ]] || fail "target ACR assignment did not become observable"
validate_direct_acr_pull_assignment "$registry_id" "$assignment_json" "$target_principal_id" || fail "target ACR assignment is invalid"

existing_target="$(az sql db show --subscription "$subscription_id" --resource-group "$source_resource_group" --server "$source_server" --name "$target_database" --output json --only-show-errors 2>/dev/null || true)"
if [[ -z "$existing_target" ]]; then
  az sql db restore --subscription "$subscription_id" --resource-group "$source_resource_group" --server "$source_server" \
    --name "$source_database" --dest-name "$target_database" --time "$restore_point_utc" \
    --tags proof=129 owner=elsa-control recovery-id="$recovery_id" target-role=restore managed-by=elsa-control-recovery manifest-digest="$manifest_digest" expiry="$expiry_utc" \
    --no-wait --only-show-errors >/dev/null
else
  jq -e --arg id "$target_db_id" --arg recovery "$recovery_id" '(.id | ascii_downcase) == ($id | ascii_downcase) and .tags.proof == "129" and .tags.owner == "elsa-control" and .tags["recovery-id"] == $recovery and .tags["target-role"] == "restore"' <<<"$existing_target" >/dev/null || fail "refusing to adopt an unrelated restored database"
fi
for _ in {1..180}; do
  target_db_json="$(az sql db show --subscription "$subscription_id" --resource-group "$source_resource_group" --server "$source_server" --name "$target_database" --output json --only-show-errors 2>/dev/null || true)"
  [[ -n "$target_db_json" ]] && jq -e '
    ((.status // .properties.status) == "Online") and
    (((.provisioningState // .properties.provisioningState) == null) or ((.provisioningState // .properties.provisioningState) == "Succeeded"))
  ' <<<"$target_db_json" >/dev/null && break
  sleep 10
done
if [[ -z "${target_db_json:-}" ]] || ! jq -e --arg id "$target_db_id" '(.id | ascii_downcase) == ($id | ascii_downcase) and ((.status // .properties.status) == "Online")' <<<"$target_db_json" >/dev/null; then
  fail "target database restore was not verified"
fi
az resource tag --ids "$target_db_id" --tags proof=129 owner=elsa-control recovery-id="$recovery_id" target-role=restore managed-by=elsa-control-recovery manifest-digest="$manifest_digest" expiry="$expiry_utc" --only-show-errors >/dev/null

az keyvault secret download --subscription "$subscription_id" --vault-name "$source_vault" --name identity-signing-key --file "$source_signing_file" --encoding utf-8 --only-show-errors >/dev/null
chmod 600 "$source_signing_file"
sql_connection_file="$temp_dir/sql-connection"
printf 'Server=tcp:%s.database.windows.net,1433;Initial Catalog=%s;Authentication=Active Directory Managed Identity;User Id=%s;Encrypt=True;Trust Server Certificate=False;' "$source_server" "$target_database" "$target_client_id" >"$sql_connection_file"
chmod 600 "$sql_connection_file"
set_secret_file() {
  local name="$1" file="$2"
  for _ in {1..24}; do
    az keyvault secret set --subscription "$subscription_id" --vault-name "$target_vault" --name "$name" --file "$file" --only-show-errors >/dev/null 2>&1 && return 0
    sleep 5
  done
  return 1
}
set_secret_file admin-password "$source_password_file" || fail "target admin secret could not be rebound"
set_secret_file identity-signing-key "$source_signing_file" || fail "target signing secret could not be rebound"
set_secret_file sql-connection "$sql_connection_file" || fail "target SQL secret could not be rebound"
secret_names="$(az keyvault secret list --subscription "$subscription_id" --vault-name "$target_vault" --query '[].name' --output json --only-show-errors)"
jq -e 'sort == ["admin-password","identity-signing-key","sql-connection"]' <<<"$secret_names" >/dev/null || fail "target secret reference set is not exact"

sed -e "s/__WORKLOAD_IDENTITY_NAME__/${target_identity}/g" -e "s/__WORKLOAD_IDENTITY_CLIENT_ID__/${target_client_id}/g" "$proof_dir/sql-bootstrap.sql" >"$temp_dir/sql-bootstrap.sql"
az sql server firewall-rule create --subscription "$subscription_id" --resource-group "$source_resource_group" --server "$source_server" \
  --name "$firewall_rule" --start-ip-address "$sql_bootstrap_ip" --end-ip-address "$sql_bootstrap_ip" --only-show-errors >/dev/null
firewall_rule_created=1
sqlcmd -S "tcp:${source_server}.database.windows.net,1433" -d "$target_database" --authentication-method ActiveDirectoryDefault -i "$temp_dir/sql-bootstrap.sql" >/dev/null
delete_and_verify_firewall_rule "$subscription_id" "$source_resource_group" "$source_server" "$firewall_rule"
firewall_rule_created=0

deploy_target true >"$temp_dir/target-workload.json"
target_endpoint="$(jq -r '.properties.outputs.containerAppEndpoint.value' "$temp_dir/target-workload.json")"
[[ "$target_endpoint" == "https://${target_app}."*.azurecontainerapps.io ]] || fail "target endpoint output is invalid"
for _ in {1..60}; do curl --fail --silent --show-error --max-time 30 "$target_endpoint/health" >/dev/null 2>&1 && break; sleep 10; done
curl --fail --silent --show-error --max-time 30 "$target_endpoint/health" >/dev/null || fail "target health was not verified"
dotnet run --project "$probe_project" --no-restore -- \
  --endpoint "$target_endpoint" --environment "$target_name" --username proof-admin --password-file "$source_password_file" \
  --workflow-id "$pre_definition" --mode verify --absent-workflow-id "$post_definition" >"$temp_dir/target-workflow.json"
jq -e '.outcome == "passed" and .result == "Finished"' "$temp_dir/target-workflow.json" >/dev/null || fail "restored workflow verification failed"
cutover_eligible_at="$(utc_now)"
rpo_seconds="$(python3 -c 'import sys; print(max(0, int(float(sys.argv[1]) - float(sys.argv[2]))))' "$(epoch "$post_committed_at")" "$(epoch "$restore_point_utc")")"
rto_seconds="$(python3 -c 'import sys; print(max(0, int(float(sys.argv[1]) - float(sys.argv[2]))))' "$(epoch "$cutover_eligible_at")" "$(epoch "$restore_accepted_at")")"
(( rpo_seconds <= 86400 && rto_seconds <= 14400 )) || fail "recovery objectives were exceeded"

cleanup_target
curl --fail --silent --show-error --max-time 30 "$source_endpoint/health" >/dev/null || fail "source was not preserved after target cleanup"
jq -n \
  --arg recoveryId "$recovery_id" --arg source "$source_proof_name" --arg target "$target_name" \
  --arg sourceQuiescedAt "$source_quiesced_at" --arg earliestRestoreDate "$earliest_restore_date" \
  --arg restorePoint "$restore_point_utc" --arg manifestDigest "sha256:${manifest_digest}" \
  --arg image "${image_repository}@sha256:${image_digest}" --arg preDefinition "$pre_definition" --arg postDefinition "$post_definition" \
  --argjson rpoSeconds "$rpo_seconds" --argjson rtoSeconds "$rto_seconds" \
  '{schemaVersion:1,outcome:"passed",recoveryId:$recoveryId,sourceInstance:$source,targetInstance:$target,sourceQuiescedAtUtc:$sourceQuiescedAt,earliestRestoreDateUtc:$earliestRestoreDate,restorePointUtc:$restorePoint,manifestDigest:$manifestDigest,immutableImage:$image,workflow:{prePoint:$preDefinition,postPointAbsent:$postDefinition,status:"Finished"},rpoSeconds:$rpoSeconds,rtoSeconds:$rtoSeconds,healthBeforeEligibility:true,cutoverEligible:true,trafficMutated:false,targetResourcesAbsent:true,sourcePreserved:true,limitations:["same-region","same-logical-sql-server","no-automatic-cutover","proof-grade-quiescence"]}'
