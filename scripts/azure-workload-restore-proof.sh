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
  --desired-revision-id <id>       Exact safe desired-revision identity
  --desired-revision-digest <hex>  SHA-256 of the immutable desired revision
  --resolved-plan-reference <ref>  Immutable OCI locator for the resolved plan
  --resolved-plan-digest <hex>     SHA-256 embedded in the resolved-plan locator
  --release-manifest-reference <ref> Immutable OCI locator for the admitted release manifest
  --release-manifest-digest <hex>  SHA-256 embedded in the release-manifest locator
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
  --manifest-digest <64 hex>       Existing sealed manifest digest (cleanup only)
  --target-principal-id <guid>     Existing target identity principal (cleanup only)

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
desired_revision_id=""
desired_revision_digest=""
resolved_plan_reference=""
resolved_plan_digest=""
release_manifest_reference=""
release_manifest_digest=""
registry_resource_group=""
sql_bootstrap_object_id=""
sql_bootstrap_login=""
sql_bootstrap_ip=""
expected_manifest_digest=""
target_principal_id=""
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
    --desired-revision-id) desired_revision_id="${2:?}"; shift 2 ;;
    --desired-revision-digest) desired_revision_digest="${2:?}"; shift 2 ;;
    --resolved-plan-reference) resolved_plan_reference="${2:?}"; shift 2 ;;
    --resolved-plan-digest) resolved_plan_digest="${2:?}"; shift 2 ;;
    --release-manifest-reference) release_manifest_reference="${2:?}"; shift 2 ;;
    --release-manifest-digest) release_manifest_digest="${2:?}"; shift 2 ;;
    --registry-resource-group) registry_resource_group="${2:?}"; shift 2 ;;
    --sql-bootstrap-object-id) sql_bootstrap_object_id="${2:?}"; shift 2 ;;
    --sql-bootstrap-login) sql_bootstrap_login="${2:?}"; shift 2 ;;
    --sql-bootstrap-ip) sql_bootstrap_ip="${2:?}"; shift 2 ;;
    --manifest-digest) expected_manifest_digest="${2:?}"; shift 2 ;;
    --target-principal-id) target_principal_id="${2:?}"; shift 2 ;;
    --subscription) subscription_id="${2:?}"; shift 2 ;;
    --registry-subscription) registry_subscription_id="${2:?}"; shift 2 ;;
    --registry-name) registry_name="${2:?}"; shift 2 ;;
    --image-repository) image_repository="${2:?}"; shift 2 ;;
    --expiry-utc) expiry_utc="${2:?}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "An unknown option was supplied" >&2; usage >&2; exit 2 ;;
  esac
done

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
proof_dir="$repo_root/infra/azure-workload-proof"
target_template="$proof_dir/recovery-target.bicep"
database_template="$proof_dir/recovery-database.bicep"
probe_project="$repo_root/src/Deployment/ElsaControl.Deployment.WorkflowProbeHost/ElsaControl.WorkflowProbeHost.csproj"
# shellcheck source=scripts/lib/azure-workload-proof.sh
source "$script_dir/lib/azure-workload-proof.sh"
expiry_utc="${expiry_utc:-$(default_expiry_utc)}"

fail() {
  echo "$1" >&2
  if (( ${cleanup_in_progress:-0} == 1 )); then
    return 2
  fi
  exit 2
}
valid_name() { [[ "$1" =~ ^[a-z][a-z0-9-]{1,14}[a-z0-9]$ && "$1" != *--* ]]; }
valid_group() { [[ "$1" =~ ^[A-Za-z0-9._()-]{1,90}$ ]]; }
valid_guid() { [[ "$1" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; }
valid_digest() { [[ "$1" =~ ^[a-f0-9]{64}$ ]]; }
valid_immutable_oci_reference() {
  local reference="$1" digest="$2"
  # ORAS accepts registry references, not the URI-style oci:// spelling used
  # by some catalog APIs. Keep the executable proof contract unambiguous.
  [[ "$reference" =~ ^[a-z0-9.-]+(/[a-z0-9._-]+)+@sha256:[a-f0-9]{64}$ ]] &&
    [[ "$reference" == *"@sha256:${digest}" ]]
}
valid_ip() {
  local octet
  [[ "$1" =~ ^[0-9]+(\.[0-9]+){3}$ && "$1" != 0.0.0.0 ]] || return 1
  IFS=. read -r -a octets <<<"$1"
  for octet in "${octets[@]}"; do (( octet <= 255 )) || return 1; done
}
sha256_stream() { if command -v sha256sum >/dev/null; then sha256sum | awk '{print $1}'; else shasum -a 256 | awk '{print $1}'; fi; }
sha256_text() { printf '%s' "$1" | sha256_stream; }
utc_now() { date -u '+%Y-%m-%dT%H:%M:%SZ'; }
canonical_utc() {
  python3 - "$1" <<'PY'
from datetime import datetime, timezone
import sys

value = sys.argv[1]
try:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
except ValueError:
    raise SystemExit(1)
if parsed.tzinfo is None:
    raise SystemExit(1)
parsed = parsed.astimezone(timezone.utc)
if parsed.microsecond:
    print(parsed.isoformat(timespec="microseconds").replace("+00:00", "Z"))
else:
    print(parsed.isoformat(timespec="seconds").replace("+00:00", "Z"))
PY
}
same_instant() {
  python3 - "$1" "$2" <<'PY'
from datetime import datetime
import sys

def parse(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))

raise SystemExit(0 if parse(sys.argv[1]) == parse(sys.argv[2]) else 1)
PY
}
not_after() {
  python3 - "$1" "$2" <<'PY'
from datetime import datetime
import sys

def parse(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))

raise SystemExit(0 if parse(sys.argv[1]) <= parse(sys.argv[2]) else 1)
PY
}
non_negative_age_seconds() {
  python3 - "$1" "$2" <<'PY'
from datetime import datetime
import sys

def parse(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))

age = (parse(sys.argv[1]) - parse(sys.argv[2])).total_seconds()
if age < 0:
    raise SystemExit(1)
print(int(age))
PY
}
secure_file() {
  local path="$1"
  [[ -f "$path" && ! -L "$path" ]] || return 1
  chmod 600 "$path"
  if [[ "$(stat -f '%Lp' "$path" 2>/dev/null || stat -c '%a' "$path" 2>/dev/null)" != 600 ]]; then
    return 1
  fi
}

valid_name "$source_proof_name" || fail "source proof name is invalid"
valid_name "$target_name" || fail "target name is invalid"
[[ "$recovery_id" =~ ^[a-z0-9]{3,12}$ ]] || fail "recovery ID is invalid"
valid_group "$source_resource_group" || fail "source resource group is invalid"
valid_group "$target_resource_group" || fail "target resource group is invalid"
[[ "$source_resource_group" != "$target_resource_group" ]] || fail "source and target resource groups must differ"
[[ "$image_digest" =~ ^[a-fA-F0-9]{64}$ ]] || fail "image digest must be 64 hexadecimal characters"
image_digest="$(printf '%s' "$image_digest" | tr '[:upper:]' '[:lower:]')"
[[ "$desired_revision_id" =~ ^[a-z0-9][a-z0-9._-]{2,127}$ ]] || fail "desired revision identity is invalid"
desired_revision_digest="$(printf '%s' "$desired_revision_digest" | tr '[:upper:]' '[:lower:]')"
resolved_plan_digest="$(printf '%s' "$resolved_plan_digest" | tr '[:upper:]' '[:lower:]')"
release_manifest_digest="$(printf '%s' "$release_manifest_digest" | tr '[:upper:]' '[:lower:]')"
valid_digest "$desired_revision_digest" || fail "desired revision digest is invalid"
valid_digest "$resolved_plan_digest" || fail "resolved plan digest is invalid"
valid_digest "$release_manifest_digest" || fail "release manifest digest is invalid"
valid_immutable_oci_reference "$resolved_plan_reference" "$resolved_plan_digest" || fail "resolved plan reference is not immutable"
valid_immutable_oci_reference "$release_manifest_reference" "$release_manifest_digest" || fail "release manifest reference is not immutable"
[[ "$registry_name" == valenceruntimeimages ]] || fail "registry must be valenceruntimeimages"
[[ "$image_repository" =~ ^valenceruntimeimages\.azurecr\.io/[a-z0-9._/-]+$ && "$image_repository" != *:* && "$image_repository" != *@* ]] || fail "image repository is invalid"
valid_group "$registry_resource_group" || fail "registry resource group is invalid"
valid_guid "$sql_bootstrap_object_id" || fail "SQL bootstrap object ID is invalid"
[[ "$sql_bootstrap_login" =~ ^[a-zA-Z0-9._@-]{1,128}$ ]] || fail "SQL bootstrap login is invalid"
valid_ip "$sql_bootstrap_ip" || fail "SQL bootstrap IP is invalid"
[[ "$expiry_utc" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || fail "expiry date is invalid"
if [[ "$mode" == cleanup ]]; then
  [[ "$expected_manifest_digest" =~ ^[a-fA-F0-9]{64}$ ]] || fail "cleanup requires the sealed manifest digest"
  expected_manifest_digest="$(printf '%s' "$expected_manifest_digest" | tr '[:upper:]' '[:lower:]')"
  valid_guid "$target_principal_id" || fail "cleanup requires the exact target principal ID"
elif [[ -n "$target_principal_id" ]]; then
  fail "target principal ID is accepted only for cleanup"
fi
for command_name in az jq python3 dotnet sqlcmd curl sed stat; do
  command -v "$command_name" >/dev/null || fail "$command_name is required"
done
if [[ "$mode" == apply ]]; then
  command -v oras >/dev/null || fail "oras is required"
fi
[[ -f "$target_template" && -f "$database_template" && -f "$probe_project" && -f "$proof_dir/sql-bootstrap.sql" ]] || fail "checked-in recovery proof artifacts are missing"
az bicep build --file "$target_template" --stdout >/dev/null
az bicep build --file "$database_template" --stdout >/dev/null

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
database_restore_deployment="elsa129-db-${target_name}"
source_db_id="/subscriptions/${subscription_id}/resourceGroups/${source_resource_group}/providers/Microsoft.Sql/servers/${source_server}/databases/${source_database}"
source_vault_id="/subscriptions/${subscription_id}/resourceGroups/${source_resource_group}/providers/Microsoft.KeyVault/vaults/${source_vault}"
target_db_id="/subscriptions/${subscription_id}/resourceGroups/${source_resource_group}/providers/Microsoft.Sql/servers/${source_server}/databases/${target_database}"
registry_id="/subscriptions/${registry_subscription_id}/resourceGroups/${registry_resource_group}/providers/Microsoft.ContainerRegistry/registries/${registry_name}"
firewall_rule="elsa129-${recovery_id}"
firewall_rule_created=0
target_scope_started=0
cleanup_in_progress=0
source_quiesced=0
source_revisions_json=""
target_db_existing=0
source_secret_assignment_id="${source_vault_id}/providers/Microsoft.Authorization/roleAssignments/$(sha256_text "elsa129-source-vault|${target_name}-identity" | awk '{print substr($0,1,8) "-" substr($0,9,4) "-" substr($0,13,4) "-" substr($0,17,4) "-" substr($0,21,12)}')"
temp_dir="$(mktemp -d "${TMPDIR:-/tmp}/elsa129.XXXXXX")"

cleanup_local() {
  local prior_status="$?" cleanup_status
  trap - EXIT HUP INT TERM
  cleanup_in_progress=1
  set +e
  if (( source_quiesced == 1 )); then
    resume_source || prior_status=2
  fi
  if (( target_scope_started == 1 )); then
    (cleanup_in_progress=0; cleanup_target)
    cleanup_status="$?"
    if (( cleanup_status != 0 )); then
      echo "CRITICAL: target cleanup did not reach a verified absence state" >&2
      prior_status=2
    fi
  fi
  if (( firewall_rule_created == 1 )); then
    delete_owned_firewall_rule || prior_status=2
  fi
  find "$temp_dir" -type f -exec chmod 600 {} + 2>/dev/null || true
  rm -rf -- "$temp_dir"
  exit "$prior_status"
}
trap cleanup_local EXIT HUP INT TERM

verify_source() {
  local group_json source_image source_template source_template_digest endpoint source_healthy=0
  group_json="$(az group show --subscription "$subscription_id" --name "$source_resource_group" --output json --only-show-errors)"
  jq -e --arg proof "$source_proof_name" '.tags.proof == "108" and .tags.owner == "elsa-control" and (.name | length) > 0' <<<"$group_json" >/dev/null || fail "source group ownership is invalid"
  source_image="$(az containerapp show --subscription "$subscription_id" --resource-group "$source_resource_group" --name "$source_app" --query 'properties.template.containers[0].image' --output tsv --only-show-errors)"
  [[ "$source_image" == "${image_repository}@sha256:${image_digest}" ]] || fail "source image does not match the admitted digest"
  source_template="$(az containerapp show --subscription "$subscription_id" --resource-group "$source_resource_group" --name "$source_app" \
    --query properties.template --output json --only-show-errors)" || fail "source desired state could not be read"
  source_template_digest="$(jq -cS . <<<"$source_template" | sha256_stream)" || fail "source desired state could not be hashed"
  [[ "$source_template_digest" == "$desired_revision_digest" ]] || fail "source desired revision digest does not match"
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

quiesce_source() {
  local revisions_json active_revisions active_count replicas_json replicas_empty revision
  revisions_json="$(az containerapp revision list --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --name "$source_app" --output json --only-show-errors)" || fail "source revisions could not be read"
  jq -e 'type == "array"' <<<"$revisions_json" >/dev/null || fail "source revision inventory is invalid"
  active_revisions="$(jq -r '.[] | select(.properties.active == true) | .name' <<<"$revisions_json")"
  [[ "$(printf '%s\n' "$active_revisions" | sed '/^$/d' | wc -l | tr -d ' ')" == 1 ]] || fail "source must have exactly one active revision to quiesce"
  [[ "$active_revisions" == "$desired_revision_id" ]] || fail "source active revision does not match the desired revision identity"
  for revision in $active_revisions; do
    [[ "$revision" =~ ^[A-Za-z0-9-]+$ ]] || fail "source revision identity is invalid"
  done
  source_revisions_json="$active_revisions"
  source_quiesced=1
  for revision in $source_revisions_json; do
    az containerapp revision deactivate --subscription "$subscription_id" --resource-group "$source_resource_group" \
      --name "$source_app" --revision "$revision" --only-show-errors >/dev/null || {
      echo "a source revision could not be deactivated; refusing to select a recovery point" >&2
      return 1
    }
  done

  for _ in {1..60}; do
    revisions_json="$(az containerapp revision list --subscription "$subscription_id" --resource-group "$source_resource_group" \
      --name "$source_app" --output json --only-show-errors)" || return 1
    active_count="$(jq -r '[.[] | select(.properties.active == true)] | length' <<<"$revisions_json")" || return 1
    replicas_empty=1
    for revision in $source_revisions_json; do
      replicas_json="$(az containerapp replica list --subscription "$subscription_id" --resource-group "$source_resource_group" \
        --name "$source_app" --revision "$revision" --output json --only-show-errors)" || return 1
      if ! jq -e 'type == "array" and length == 0' <<<"$replicas_json" >/dev/null; then
        replicas_empty=0
      fi
    done
    if (( active_count == 0 && replicas_empty == 1 )); then
      break
    fi
    sleep 5
  done
  (( active_count == 0 && replicas_empty == 1 )) || {
    echo "source revisions did not reach provider-confirmed zero-active and zero-replica state" >&2
    return 1
  }

  # Azure has confirmed the blocking state and all replicas are gone. Capture
  # the cutoff immediately after that provider observation; never invent a
  # point by subtracting an arbitrary safety interval.
  source_quiesced_at="$(canonical_utc "$(utc_now)")" || return 1
  recovery_cutoff_utc="$source_quiesced_at"
}

resume_source() {
  local revision revisions_json expected_revisions_json active_exact source_healthy=0 revision_active
  (( source_quiesced == 1 )) || return 0
  expected_revisions_json="$(printf '%s\n' "$source_revisions_json" | jq -Rsc 'split("\n") | map(select(length > 0)) | sort')" || return 1
  revisions_json="$(az containerapp revision list --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --name "$source_app" --output json --only-show-errors)" || return 1
  for revision in $source_revisions_json; do
    revision_active="$(jq -r --arg revision "$revision" \
      '[.[] | select(.name == $revision and .properties.active == true)] | length == 1' <<<"$revisions_json")" || return 1
    if [[ "$revision_active" != true ]]; then
      az containerapp revision activate --subscription "$subscription_id" --resource-group "$source_resource_group" \
        --name "$source_app" --revision "$revision" --only-show-errors >/dev/null || {
        echo "CRITICAL: a source revision could not be reactivated" >&2
        return 1
      }
    fi
  done
  # Managed-environment reactivation has an observed multi-minute provider tail.
  # Keep this bounded but long enough to restore the source before any target work.
  for _ in {1..180}; do
    revisions_json="$(az containerapp revision list --subscription "$subscription_id" --resource-group "$source_resource_group" \
      --name "$source_app" --output json --only-show-errors)" || return 1
    active_exact="$(jq -r --argjson names "$expected_revisions_json" \
      '([.[] | select(.properties.active == true) | .name] | sort) == $names' <<<"$revisions_json")" || return 1
    if [[ "$active_exact" == true ]]; then
      if curl --fail --silent --show-error --max-time 30 "$source_endpoint/health" >/dev/null 2>&1; then
        source_healthy=1
        break
      fi
    fi
    sleep 5
  done
  if (( source_healthy == 0 )); then
    echo "CRITICAL: source was not restored to active healthy state" >&2
    return 1
  fi
  source_quiesced=0
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

query_target_database() {
  local listed_databases
  target_db_json=""
  if target_db_json="$(az sql db show --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --server "$source_server" --name "$target_database" --output json --only-show-errors 2>/dev/null)"; then
    return 0
  fi
  listed_databases="$(az sql db list --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --server "$source_server" --output json --only-show-errors 2>/dev/null)" || {
    echo "Azure target database existence could not be determined" >&2
    return 1
  }
  jq -e 'type == "array"' <<<"$listed_databases" >/dev/null || return 1
  target_db_json="$(jq -c --arg name "$target_database" '[.[] | select(.name == $name)] | first // empty' <<<"$listed_databases")"
}

verify_provider_restore_provenance() {
  local deployment_json provider_point
  deployment_json="$(az deployment group show --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --name "$database_restore_deployment" --output json --only-show-errors 2>/dev/null)" || {
    echo "Azure restore deployment provenance could not be read" >&2
    return 1
  }
  provider_point="$(jq -r '.properties.outputs.restorePointUtc.value // empty' <<<"$deployment_json")"
  provider_point="$(canonical_utc "$provider_point")" || return 1
  if [[ -n "${recovery_cutoff_utc:-}" ]]; then
    same_instant "$provider_point" "$recovery_cutoff_utc" || return 1
  else
    recovery_cutoff_utc="$provider_point"
  fi
  jq -e --arg source "$source_db_id" --arg target "$target_database" --arg point "$provider_point" \
    --arg manifest "$manifest_tag" --arg targetId "$target_db_id" '
    .properties.provisioningState == "Succeeded" and
    ((.properties.parameters.sourceDatabaseId.value // "") | ascii_downcase) == ($source | ascii_downcase) and
    .properties.parameters.targetDatabaseName.value == $target and
    .properties.parameters.restorePointUtc.value == $point and
    .properties.outputs.createMode.value == "PointInTimeRestore" and
    ((.properties.outputs.sourceDatabaseId.value // "") | ascii_downcase) == ($source | ascii_downcase) and
    ((.properties.outputs.restoredDatabaseId.value // "") | ascii_downcase) == ($targetId | ascii_downcase) and
    .properties.outputs.restorePointUtc.value == $point and
    .properties.outputs.recoveryManifestDigest.value == $manifest
  ' <<<"$deployment_json" >/dev/null || {
    echo "Azure restore deployment provenance is not exact" >&2
    return 1
  }
  provider_restore_point_utc="$provider_point"
}

verify_owned_target_database() {
  local database_json="$1" manifest_tag="$2"
  jq -e --arg id "$target_db_id" --arg recovery "$recovery_id" --arg manifest "$manifest_tag" '
    (.id | ascii_downcase) == ($id | ascii_downcase) and
    .tags.proof == "129" and .tags.owner == "elsa-control" and
    .tags["recovery-id"] == $recovery and .tags["target-role"] == "restore" and
    .tags["managed-by"] == "elsa-control-recovery" and
    .tags["manifest-digest"] == $manifest
  ' <<<"$database_json" >/dev/null
}

list_owned_firewall_rules() {
  az sql server firewall-rule list --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --server "$source_server" --output json --only-show-errors
}

create_owned_firewall_rule() {
  local rules
  rules="$(list_owned_firewall_rules)" || fail "SQL firewall inventory could not be read before creation"
  jq -e 'type == "array"' <<<"$rules" >/dev/null || fail "SQL firewall inventory is invalid"
  if jq -e --arg name "$firewall_rule" 'any(.[]; .name == $name)' <<<"$rules" >/dev/null; then
    fail "refusing to reuse an existing or colliding SQL firewall rule"
  fi
  firewall_rule_created=1
  az sql server firewall-rule create --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --server "$source_server" --name "$firewall_rule" --start-ip-address "$sql_bootstrap_ip" \
    --end-ip-address "$sql_bootstrap_ip" --only-show-errors >/dev/null || fail "SQL firewall rule creation failed"
  rules="$(list_owned_firewall_rules)" || fail "SQL firewall rule could not be verified after creation"
  jq -e --arg name "$firewall_rule" --arg ip "$sql_bootstrap_ip" '
    ([.[] | select(.name == $name)] | length) == 1 and
    ([.[] | select(.name == $name)][0].startIpAddress) == $ip and
    ([.[] | select(.name == $name)][0].endIpAddress) == $ip
  ' <<<"$rules" >/dev/null || {
    fail "SQL firewall rule ownership could not be proven after creation"
  }
}

delete_owned_firewall_rule() {
  local rules attempt matching_count
  rules="$(list_owned_firewall_rules)" || {
    echo "Refusing to delete SQL firewall rule: inventory could not be read" >&2
    return 1
  }
  matching_count="$(jq -r --arg name "$firewall_rule" '[.[] | select(.name == $name)] | length' <<<"$rules")" || return 1
  if (( matching_count == 0 )); then
    firewall_rule_created=0
    return 0
  fi
  if ! jq -e --arg name "$firewall_rule" --arg ip "$sql_bootstrap_ip" '
    ([.[] | select(.name == $name)] | length) == 1 and
    ([.[] | select(.name == $name)][0].startIpAddress) == $ip and
    ([.[] | select(.name == $name)][0].endIpAddress) == $ip
  ' <<<"$rules" >/dev/null; then
    echo "Refusing to delete SQL firewall rule: ownership or address changed" >&2
    return 1
  fi
  az sql server firewall-rule delete --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --server "$source_server" --name "$firewall_rule" --only-show-errors >/dev/null 2>&1 || true
  for attempt in {1..24}; do
    if rules="$(list_owned_firewall_rules 2>/dev/null)" &&
      jq -e --arg name "$firewall_rule" '[.[] | select(.name == $name)] | length == 0' <<<"$rules" >/dev/null; then
      firewall_rule_created=0
      return 0
    fi
    (( attempt == 24 )) || sleep 5
  done
  echo "SQL firewall rule remained observable after owned deletion" >&2
  return 1
}

wait_for_target_group_absence() {
  local attempt group_exists
  for attempt in {1..300}; do
    group_exists="$(az group exists --subscription "$subscription_id" --name "$target_resource_group" --output tsv --only-show-errors 2>/dev/null || echo unknown)"
    [[ "$group_exists" == false ]] && return 0
    (( attempt == 300 )) || sleep 5
  done
  echo "Target resource group remained observable after deletion" >&2
  return 1
}

purge_and_verify_target_vault() {
  local vault_name="$1" location="$2" deleted_vaults_json vault_count attempt purge_requested=0 vault_id
  vault_id="/subscriptions/${subscription_id}/resourceGroups/${target_resource_group}/providers/Microsoft.KeyVault/vaults/${vault_name}"
  for attempt in {1..30}; do
    if deleted_vaults_json="$(az keyvault list-deleted --subscription "$subscription_id" --resource-type vault \
      --output json --only-show-errors 2>/dev/null)"; then
      vault_count="$(jq -r --arg name "$vault_name" --arg location "$location" --arg id "$vault_id" \
        '[.[] | select(.name == $name and ((.properties.location // .location // "") | ascii_downcase) == ($location | ascii_downcase) and
          ((.properties.vaultId // .id // "") | ascii_downcase) == ($id | ascii_downcase))] | length' \
        <<<"$deleted_vaults_json")" || return 1
      (( vault_count == 0 )) && return 0
      (( vault_count == 1 )) || { echo "Expected at most one deleted target vault" >&2; return 1; }
      if (( purge_requested == 0 )); then
        az keyvault purge --subscription "$subscription_id" --name "$vault_name" --location "$location" \
          --only-show-errors >/dev/null 2>&1 || true
        purge_requested=1
      fi
    fi
    (( attempt == 30 )) || sleep 5
  done
  echo "Deleted target vault absence could not be verified" >&2
  return 1
}

cleanup_owned_role_assignment() {
  local assignment_id="$1" expected_principal="$2" assignments_json assignment_json attempt
  assignments_json="$(az role assignment list --all --subscription "$registry_subscription_id" \
    --output json --only-show-errors 2>/dev/null)" || return 1
  assignment_json="$(jq -c --arg id "$assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | first // empty' \
    <<<"$assignments_json")" || return 1
  [[ -z "$assignment_json" ]] && return 0
  valid_guid "$expected_principal" || return 1
  validate_direct_acr_pull_assignment "$registry_id" "$assignment_json" "$expected_principal" || return 1
  az role assignment delete --subscription "$registry_subscription_id" --ids "$assignment_id" --only-show-errors >/dev/null 2>&1 || true
  for attempt in {1..24}; do
    if assignments_json="$(az role assignment list --all --subscription "$registry_subscription_id" \
      --output json --only-show-errors 2>/dev/null)" &&
      jq -e --arg id "$assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | length == 0' <<<"$assignments_json" >/dev/null; then
      return 0
    fi
    (( attempt == 24 )) || sleep 5
  done
  echo "Proof-owned ACR role assignment remained observable after deletion" >&2
  return 1
}

cleanup_owned_acr_deployment() {
  local deployment_json deployments_json attempt
  az deployment group delete --subscription "$registry_subscription_id" --resource-group "$registry_resource_group" \
    --name "$acr_deployment" --only-show-errors >/dev/null 2>&1 || true
  for attempt in {1..24}; do
    if deployments_json="$(az deployment group list --subscription "$registry_subscription_id" \
      --resource-group "$registry_resource_group" --output json --only-show-errors 2>/dev/null)" &&
      jq -e --arg name "$acr_deployment" '[.[] | select(.name == $name)] | length == 0' <<<"$deployments_json" >/dev/null; then
      return 0
    fi
    (( attempt == 24 )) || sleep 5
  done
  echo "Proof-owned ACR deployment record remained observable after deletion" >&2
  return 1
}

cleanup_source_secret_assignment() {
  local assignments_json assignment_json attempt
  assignments_json="$(az role assignment list --all --subscription "$subscription_id" --output json --only-show-errors 2>/dev/null)" || {
    echo "Refusing to delete source-vault access: role-assignment inventory could not be read" >&2
    return 1
  }
  assignment_json="$(jq -c --arg id "$source_secret_assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | first // empty' <<<"$assignments_json")"
  if [[ -z "$assignment_json" ]]; then
    return 0
  fi
  jq -e --arg scope "$source_vault_id" --arg role "4633458b-17de-408a-b874-0445c86b69e6" \
    '((.scope // "") | ascii_downcase) == ($scope | ascii_downcase) and
     (((.roleDefinitionId // "") | split("/") | last) | ascii_downcase) == $role and
     (.principalType // "") == "ServicePrincipal"' <<<"$assignment_json" >/dev/null || {
    echo "Refusing to delete source-vault access: assignment ownership is not exact" >&2
    return 1
  }
  if [[ -n "${target_principal_id:-}" ]]; then
    jq -e --arg principal "$target_principal_id" '((.principalId // "") | ascii_downcase) == ($principal | ascii_downcase)' \
      <<<"$assignment_json" >/dev/null || {
      echo "Refusing to delete source-vault access: principal identity changed" >&2
      return 1
    }
  else
    echo "Refusing to delete source-vault access: target principal is unavailable" >&2
    return 1
  fi
  az role assignment delete --subscription "$subscription_id" --ids "$source_secret_assignment_id" --only-show-errors >/dev/null 2>&1 || true
  for attempt in {1..24}; do
    if assignments_json="$(az role assignment list --all --subscription "$subscription_id" --output json --only-show-errors 2>/dev/null)" &&
      jq -e --arg id "$source_secret_assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | length == 0' <<<"$assignments_json" >/dev/null; then
      return 0
    fi
    (( attempt == 24 )) || sleep 5
  done
  echo "Source-vault access assignment remained observable after deletion" >&2
  return 1
}

ensure_source_secret_assignment() {
  local assignments_json existing_json assignment_json
  assignments_json="$(az role assignment list --all --subscription "$subscription_id" --output json --only-show-errors 2>/dev/null)" || fail "source-vault role-assignment inventory could not be read"
  existing_json="$(jq -c --arg id "$source_secret_assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | first // empty' <<<"$assignments_json")"
  [[ -z "$existing_json" ]] || fail "refusing to reuse a pre-existing source-vault role assignment"
  az role assignment create --subscription "$subscription_id" --name "${source_secret_assignment_id##*/}" \
    --assignee-object-id "$target_principal_id" --assignee-principal-type ServicePrincipal \
    --role 4633458b-17de-408a-b874-0445c86b69e6 --scope "$source_vault_id" \
    --only-show-errors --output json >/dev/null || fail "target source-vault access could not be granted"
  assignment_json="$(az role assignment list --all --subscription "$subscription_id" --output json --only-show-errors 2>/dev/null | \
    jq -c --arg id "$source_secret_assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | first // empty')" || fail "source-vault role assignment could not be verified"
  [[ -n "$assignment_json" ]] || fail "source-vault role assignment did not become observable"
  jq -e --arg scope "$source_vault_id" --arg principal "$target_principal_id" --arg role "4633458b-17de-408a-b874-0445c86b69e6" \
    '((.scope // "") | ascii_downcase) == ($scope | ascii_downcase) and
     ((.principalId // "") | ascii_downcase) == ($principal | ascii_downcase) and
     (((.roleDefinitionId // "") | split("/") | last) | ascii_downcase) == $role and
     (.principalType // "") == "ServicePrincipal"' <<<"$assignment_json" >/dev/null || {
      fail "source-vault role assignment identity is invalid"
    }
}

show_source_secret_reference() {
  local secret_name="$1" secret_id
  secret_id="$(az keyvault secret show --subscription "$subscription_id" --vault-name "$source_vault" --name "$secret_name" \
    --query id --output tsv --only-show-errors)" || fail "source secret reference could not be read"
  [[ "$secret_id" =~ ^https://[a-z0-9-]+\.vault\.azure\.net/secrets/[a-z0-9-]+/[a-f0-9]{32}$ ]] || fail "source secret reference is not immutable"
  printf '%s\n' "$secret_id"
}

fetch_bound_oci_json() {
  local reference="$1" expected_title="$2" expected_artifact_type="$3" payload_file="$4" manifest_file="$5"
  local repository layer_digest
  repository="${reference%@sha256:*}"
  oras manifest fetch "$reference" >"$manifest_file" 2>/dev/null || fail "an admitted OCI artifact could not be fetched"
  jq -e --arg artifactType "$expected_artifact_type" '
    .schemaVersion == 2 and .artifactType == $artifactType and (.layers | type == "array")
  ' "$manifest_file" >/dev/null || fail "an admitted OCI artifact manifest is invalid"
  layer_digest="$(jq -r --arg title "$expected_title" '
    [.layers[] | select(.mediaType == "application/json" and .annotations["org.opencontainers.image.title"] == $title)] |
    if length == 1 then .[0].digest else empty end
  ' "$manifest_file")" || fail "an admitted OCI artifact layer is invalid"
  [[ "$layer_digest" =~ ^sha256:[a-f0-9]{64}$ ]] || fail "an admitted OCI artifact layer is missing"
  oras blob fetch --output "$payload_file" "${repository}@${layer_digest}" >/dev/null 2>&1 || fail "an admitted OCI artifact payload could not be fetched"
  [[ "sha256:$(sha256_stream <"$payload_file")" == "$layer_digest" ]] || fail "an admitted OCI artifact payload digest does not match"
  jq -e 'type == "object"' "$payload_file" >/dev/null || fail "an admitted OCI artifact payload is invalid"
}

lookup_owned_acr_assignment() {
  local deployment_json deployments_json assignments_json matching_json
  acr_deployment_present=0
  if deployment_json="$(az deployment group show --subscription "$registry_subscription_id" \
    --resource-group "$registry_resource_group" --name "$acr_deployment" --output json --only-show-errors 2>/dev/null)"; then
    acr_deployment_present=1
    assignment_id="$(jq -r '.properties.outputs.roleAssignmentId.value // empty' <<<"$deployment_json")"
  else
    deployments_json="$(az deployment group list --subscription "$registry_subscription_id" --resource-group "$registry_resource_group" \
      --output json --only-show-errors 2>/dev/null)" || fail "ACR deployment records could not be read"
    if jq -e --arg name "$acr_deployment" 'any(.[]; .name == $name)' <<<"$deployments_json" >/dev/null; then
      acr_deployment_present=1
    fi
  fi
  # A failed or externally-pruned ARM deployment can leave the exact role
  # assignment behind without a readable deployment output/record. Reconcile
  # it from the disposable target principal plus the governed scope and role.
  if [[ -z "$assignment_id" && -n "${target_principal_id:-}" ]]; then
    valid_guid "$target_principal_id" || fail "target principal is unavailable for ACR reconciliation"
    assignments_json="$(az role assignment list --subscription "$registry_subscription_id" --all \
      --assignee-object-id "$target_principal_id" --output json --only-show-errors 2>/dev/null)" || fail "target ACR assignment inventory could not be read"
    matching_json="$(jq -c --arg scope "$registry_id" --arg role "7f951dda-4ed3-4680-a7ca-43fe172d538d" '
      [.[] | select(((.scope // "") | ascii_downcase) == ($scope | ascii_downcase) and
        (((.roleDefinitionId // "") | split("/") | last) | ascii_downcase) == $role)]' <<<"$assignments_json")" || \
      fail "target ACR assignment inventory is invalid"
    [[ "$(jq -r 'length' <<<"$matching_json")" -le 1 ]] || fail "target ACR assignment inventory is ambiguous"
    assignment_id="$(jq -r '.[0].id // empty' <<<"$matching_json")"
  fi
}

cleanup_target() {
  local group_exists group_json assignment_id="" vault_location cleanup_manifest_tag observed_target_principal identities_json vaults_json vault_count
  local restore_deployments_json restore_deployment_count
  cleanup_manifest_tag="${manifest_tag:-sha256:${expected_manifest_digest:-}}"
  group_exists="$(az group exists --name "$target_resource_group" --subscription "$subscription_id" --output tsv --only-show-errors)"
  [[ "$group_exists" == true || "$group_exists" == false ]] || fail "target resource group existence could not be determined"
  if [[ "$group_exists" == true ]]; then
    group_json="$(az group show --subscription "$subscription_id" --name "$target_resource_group" --output json --only-show-errors)"
    jq -e --arg recovery "$recovery_id" --arg manifest "$cleanup_manifest_tag" \
      '.tags.proof == "129" and .tags.owner == "elsa-control" and .tags["recovery-id"] == $recovery and
       .tags["target-role"] == "restore" and .tags["managed-by"] == "elsa-control-recovery" and
       .tags["manifest-digest"] == $manifest' <<<"$group_json" >/dev/null || fail "target group ownership is invalid"
    verify_target_group_inventory
    identities_json="$(az identity list --subscription "$subscription_id" --resource-group "$target_resource_group" \
      --output json --only-show-errors)" || fail "target identity inventory could not be read for cleanup"
    jq -e 'type == "array"' <<<"$identities_json" >/dev/null || fail "target identity inventory is invalid"
    observed_target_principal="$(jq -r --arg name "$target_identity" \
      'if ([.[] | select(.name == $name)] | length) > 1 then error("duplicate identity") else ([.[] | select(.name == $name)][0].principalId // empty) end' \
      <<<"$identities_json")" || fail "target identity inventory is ambiguous"
    if [[ -n "$observed_target_principal" ]]; then
      valid_guid "$observed_target_principal" || fail "target identity principal is invalid"
      if [[ -z "$target_principal_id" ]]; then
        target_principal_id="$observed_target_principal"
      elif [[ "$(printf '%s' "$observed_target_principal" | tr '[:upper:]' '[:lower:]')" != "$(printf '%s' "$target_principal_id" | tr '[:upper:]' '[:lower:]')" ]]; then
        fail "target principal does not match the owned target identity"
      fi
    fi
  fi

  lookup_owned_acr_assignment
  if [[ -n "$assignment_id" ]]; then
    valid_role_assignment_id "$registry_id" "$assignment_id" || fail "target ACR role assignment identity is invalid"
    cleanup_owned_role_assignment "$assignment_id" "$target_principal_id" || fail "target ACR role assignment cleanup could not be verified"
  fi
  if [[ "$acr_deployment_present" == 1 ]]; then
    cleanup_owned_acr_deployment || fail "target ACR deployment cleanup could not be verified"
  fi

  restore_deployments_json="$(az deployment group list --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --output json --only-show-errors)" || fail "restore deployment inventory could not be read during cleanup"
  restore_deployment_count="$(jq -r --arg name "$database_restore_deployment" \
    '[.[] | select(.name == $name)] | length' <<<"$restore_deployments_json")" || fail "restore deployment inventory is invalid"
  (( restore_deployment_count <= 1 )) || fail "restore deployment inventory is ambiguous"
  if (( restore_deployment_count == 1 )); then
    verify_provider_restore_provenance || fail "restore deployment provenance is invalid during cleanup"
  fi

  query_target_database || fail "target database existence could not be determined during cleanup"
  if [[ -n "$target_db_json" ]]; then
    (( restore_deployment_count == 1 )) || fail "target database has no exact restore deployment provenance"
    verify_owned_target_database "$target_db_json" "$cleanup_manifest_tag" || fail "target database ownership or restore identity is invalid"
    az sql db delete --subscription "$subscription_id" --resource-group "$source_resource_group" --server "$source_server" \
      --name "$target_database" --yes --no-wait --only-show-errors >/dev/null || fail "target database deletion was not accepted"
    for _ in {1..180}; do
      query_target_database || fail "target database absence could not be determined"
      [[ -z "$target_db_json" ]] && break
      sleep 5
    done
    query_target_database || fail "target database absence could not be determined"
    [[ -z "$target_db_json" ]] || fail "target database absence was not verified"
  fi
  if (( restore_deployment_count == 1 )); then
    delete_and_verify_group_deployment "$subscription_id" "$source_resource_group" "$database_restore_deployment" || \
      fail "restore deployment record cleanup could not be verified"
  fi

  if [[ "$group_exists" == true ]]; then
    vaults_json="$(az keyvault list --subscription "$subscription_id" --resource-group "$target_resource_group" \
      --output json --only-show-errors)" || fail "target vault inventory could not be read for cleanup"
    jq -e 'type == "array"' <<<"$vaults_json" >/dev/null || fail "target vault inventory is invalid"
    vault_count="$(jq -r --arg name "$target_vault" '[.[] | select(.name == $name)] | length' <<<"$vaults_json")" || \
      fail "target vault inventory is invalid"
    (( vault_count <= 1 )) || fail "target vault inventory is ambiguous"
    if (( vault_count == 1 )); then
      vault_location="$(jq -r --arg name "$target_vault" '[.[] | select(.name == $name)][0].location // empty' <<<"$vaults_json")"
      [[ -n "$vault_location" ]] || fail "target vault location is invalid"
    fi
    az group delete --subscription "$subscription_id" --name "$target_resource_group" --yes --no-wait --only-show-errors >/dev/null || \
      fail "target resource group deletion was not accepted"
    wait_for_target_group_absence || fail "target resource group absence was not verified"
  fi
  purge_and_verify_target_vault "$target_vault" "${vault_location:-westeurope}" || fail "target vault purge was not verified"
  cleanup_source_secret_assignment || fail "source-vault access cleanup was not verified"
  target_scope_started=0
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

fetch_bound_oci_json "$resolved_plan_reference" "resolved-plan.json" \
  "application/vnd.elsa-control.resolved-plan.v1+json" "$temp_dir/resolved-plan.json" "$temp_dir/resolved-plan-oci.json"
fetch_bound_oci_json "$release_manifest_reference" "release/release-manifest.json" \
  "application/vnd.valence.release-manifest.v2+json" "$temp_dir/release-manifest.json" "$temp_dir/release-manifest-oci.json"

jq -e --arg revisionId "$desired_revision_id" --arg revisionDigest "sha256:${desired_revision_digest}" \
  --arg releaseReference "$release_manifest_reference" --arg releaseDigest "sha256:${release_manifest_digest}" '
  .annotations["io.elsa-control.desired-revision-id"] == $revisionId and
  .annotations["io.elsa-control.desired-revision-digest"] == $revisionDigest and
  .annotations["io.elsa-control.release-manifest-reference"] == $releaseReference and
  .annotations["io.elsa-control.release-manifest-digest"] == $releaseDigest
' "$temp_dir/resolved-plan-oci.json" >/dev/null || fail "resolved plan subject does not bind the admitted desired revision and release manifest"

jq -e --arg image "${image_repository}@sha256:${image_digest}" --arg digest "sha256:${image_digest}" '
  .schemaVersion == "2.0.0" and
  any(.distributions[]?; .images.paid.reference == $image and
    .images.paid.digest == $digest and .images.paid.indexDigest == $digest)
' "$temp_dir/release-manifest.json" >/dev/null || fail "release manifest does not bind the admitted runtime image"

jq -e --arg releaseReference "$release_manifest_reference" --arg releaseDigest "sha256:${release_manifest_digest}" \
  --arg image "${image_repository}@sha256:${image_digest}" --arg digest "sha256:${image_digest}" '
  .schemaVersion == "1" and
  .release.releaseManifestReference == $releaseReference and
  .release.releaseManifestDigest == $releaseDigest and
  any(.topology.components[]?; .image.reference == $image and .image.digest == $digest)
' "$temp_dir/resolved-plan.json" >/dev/null || fail "resolved plan payload does not bind the admitted release manifest and runtime image"

source_password_file="$temp_dir/admin-password"
az keyvault secret download --subscription "$subscription_id" --vault-name "$source_vault" --name admin-password --file "$source_password_file" --encoding utf-8 --only-show-errors >/dev/null
secure_file "$source_password_file" || fail "source admin credential file is not private"
source_admin_secret_uri="$(show_source_secret_reference admin-password)"
source_signing_secret_uri="$(show_source_secret_reference identity-signing-key)"
pre_definition="elsa-recovery-pre-${source_proof_name}-${recovery_id}"
post_definition="elsa-recovery-post-${source_proof_name}-${recovery_id}"
dotnet run --project "$probe_project" --no-restore -- \
  --endpoint "$source_endpoint" --environment "$source_proof_name" --username proof-admin --password-file "$source_password_file" \
  --workflow-id "$pre_definition" --mode create >"$temp_dir/pre.json"
jq -e '.outcome == "passed" and .result == "Finished"' "$temp_dir/pre.json" >/dev/null || fail "source pre-point workflow did not complete"

quiesce_source || fail "source could not reach provider-confirmed quiescence"
restore_point_utc="$recovery_cutoff_utc"
pre_committed_at="$(jq -r '.evidence.finishedAt // empty' "$temp_dir/pre.json")"
[[ -n "$pre_committed_at" ]] || fail "pre-point workflow timestamp is missing"
not_after "$pre_committed_at" "$restore_point_utc" || fail "provider-confirmed recovery cutoff does not contain the pre-point workflow"
earliest_restore_date="$(az rest --method get --url "https://management.azure.com${source_db_id}?api-version=2023-08-01" --query properties.earliestRestoreDate --output tsv --only-show-errors)"
[[ -n "$earliest_restore_date" ]] || fail "Azure did not return an earliest restore date"
not_after "$earliest_restore_date" "$restore_point_utc" || fail "provider-confirmed recovery cutoff is outside the Azure retention window"

resume_source || fail "source could not be restored after the consistency point"
dotnet run --project "$probe_project" --no-restore -- \
  --endpoint "$source_endpoint" --environment "$source_proof_name" --username proof-admin --password-file "$source_password_file" \
  --workflow-id "$post_definition" --mode create >"$temp_dir/post.json"
jq -e '.outcome == "passed" and .result == "Finished"' "$temp_dir/post.json" >/dev/null || fail "source post-point workflow did not complete"
post_committed_at="$(jq -r '.evidence.finishedAt // empty' "$temp_dir/post.json")"
[[ -n "$post_committed_at" ]] || fail "post-point workflow timestamp is missing"
not_after "$restore_point_utc" "$post_committed_at" || fail "post-point marker did not follow the selected point"

manifest_file="$temp_dir/recovery-manifest.json"
jq -n -cS \
  --arg sourceProofName "$source_proof_name" --arg sourceResourceGroup "$source_resource_group" \
  --arg sourceDatabaseId "$source_db_id" --arg recoveryId "$recovery_id" \
  --arg recoveryCutoffUtc "$restore_point_utc" --arg incidentCutoffUtc "$post_committed_at" \
  --arg sourceQuiescedAtUtc "$source_quiesced_at" --arg providerConfirmation "azure-container-apps-zero-active-zero-replica" \
  --arg targetDatabaseId "$target_db_id" --arg image "${image_repository}@sha256:${image_digest}" --arg imageDigest "sha256:${image_digest}" \
  --arg desiredRevisionId "$desired_revision_id" --arg desiredRevisionDigest "sha256:${desired_revision_digest}" \
  --arg resolvedPlanReference "$resolved_plan_reference" --arg resolvedPlanDigest "sha256:${resolved_plan_digest}" \
  --arg releaseManifestReference "$release_manifest_reference" --arg releaseManifestDigest "sha256:${release_manifest_digest}" \
  --arg preDefinition "$pre_definition" --arg postDefinition "$post_definition" \
  '{schemaVersion:1,source:{proofName:$sourceProofName,resourceGroup:$sourceResourceGroup,databaseId:$sourceDatabaseId},
    recoveryId:$recoveryId,sourceQuiescedAtUtc:$sourceQuiescedAtUtc,recoveryCutoffUtc:$recoveryCutoffUtc,
    incidentCutoffUtc:$incidentCutoffUtc,provider:"azure-sql-pitr",providerConfirmation:$providerConfirmation,
    providerSnapshotReference:$targetDatabaseId,
    desiredState:{revisionId:$desiredRevisionId,revisionDigest:$desiredRevisionDigest,
      resolvedPlan:{reference:$resolvedPlanReference,digest:$resolvedPlanDigest},
      releaseManifest:{reference:$releaseManifestReference,digest:$releaseManifestDigest},
      artifacts:[{kind:"runtime-image",reference:$image,digest:$imageDigest}]},
    immutableImage:$image,prePointWorkflow:$preDefinition,postPointWorkflow:$postDefinition,
    requiredSecretReferenceKeys:["admin-password","identity-signing-key","sql-connection"]}' >"$manifest_file" || fail "recovery manifest could not be sealed"
chmod 400 "$manifest_file"
[[ "$(stat -f '%Lp' "$manifest_file" 2>/dev/null || stat -c '%a' "$manifest_file" 2>/dev/null)" == 400 ]] || fail "recovery manifest is not immutable"
manifest_digest="$(sha256_stream <"$manifest_file")"
[[ "$manifest_digest" =~ ^[a-f0-9]{64}$ ]] || fail "recovery manifest digest is invalid"
manifest_tag="sha256:${manifest_digest}"
target_sql_secret_uri=""

restore_accepted_at="$(utc_now)"
if [[ "$(az group exists --subscription "$subscription_id" --name "$target_resource_group" --output tsv --only-show-errors)" == true ]]; then
  existing_target_group="$(az group show --subscription "$subscription_id" --name "$target_resource_group" --output json --only-show-errors)"
  jq -e --arg recovery "$recovery_id" --arg manifest "$manifest_tag" --arg point "$restore_point_utc" \
    '.tags.proof == "129" and .tags.owner == "elsa-control" and .tags["recovery-id"] == $recovery and
     .tags["target-role"] == "restore" and .tags["managed-by"] == "elsa-control-recovery" and
     .tags["manifest-digest"] == $manifest and .tags["recovery-point-utc"] == $point' <<<"$existing_target_group" >/dev/null || fail "refusing to adopt an unrelated target resource group"
  restore_accepted_at="$(jq -r '.tags["restore-started-utc"] // empty' <<<"$existing_target_group")"
  restore_accepted_at="$(canonical_utc "$restore_accepted_at")" || fail "existing target restore start is invalid"
  not_after "$restore_accepted_at" "$(utc_now)" || fail "existing target restore start is invalid"
else
  target_scope_started=1
  az group create --subscription "$subscription_id" --name "$target_resource_group" --location westeurope \
    --tags proof=129 owner=elsa-control recovery-id="$recovery_id" target-role=restore managed-by=elsa-control-recovery \
      manifest-digest="$manifest_tag" recovery-point-utc="$restore_point_utc" restore-started-utc="$restore_accepted_at" \
      expiry="$expiry_utc" --only-show-errors >/dev/null
fi
target_scope_started=1
query_target_database || fail "target database existence could not be determined before target creation"
if [[ -n "$target_db_json" ]]; then
  verify_owned_target_database "$target_db_json" "$manifest_tag" || fail "refusing to adopt a stale or unrelated restored database"
  target_db_existing=1
fi
template_fingerprint="$(az bicep build --file "$target_template" --stdout | sha256_stream)"
deploy_target() {
  local deploy_workload="$1"
  az deployment group create --subscription "$subscription_id" --resource-group "$target_resource_group" --name "elsa129-${target_name}" \
    --template-file "$target_template" --parameters targetName="$target_name" imageRepository="$image_repository" imageDigest="$image_digest" \
    registryName="$registry_name" registrySubscriptionId="$registry_subscription_id" registryResourceGroupName="$registry_resource_group" \
    bootstrapObjectId="$sql_bootstrap_object_id" restoredDatabaseId="$target_db_id" recoveryPointDigest="$manifest_digest" \
    adminPasswordSecretUri="$source_admin_secret_uri" signingKeySecretUri="$source_signing_secret_uri" sqlConnectionSecretUri="$target_sql_secret_uri" \
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
ensure_source_secret_assignment

az deployment group create --subscription "$registry_subscription_id" --resource-group "$registry_resource_group" --name "$acr_deployment" \
  --template-file "$proof_dir/acr-pull-role.bicep" --parameters registryName="$registry_name" workloadIdentityId="$target_identity_id" workloadPrincipalId="$target_principal_id" \
  --only-show-errors >"$temp_dir/acr.json"
assignment_id="$(jq -r '.properties.outputs.roleAssignmentId.value' "$temp_dir/acr.json")"
valid_role_assignment_id "$registry_id" "$assignment_id" || fail "target ACR assignment identity is invalid"
assignment_json=""
for _ in {1..12}; do
  assignment_json="$(az role assignment list --all --subscription "$registry_subscription_id" --assignee-object-id "$target_principal_id" --output json --only-show-errors 2>/dev/null | jq -c --arg id "$assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | if length == 1 then .[0] else empty end' 2>/dev/null || true)"
  [[ -n "$assignment_json" ]] && break
  sleep 5
done
[[ -n "$assignment_json" ]] || fail "target ACR assignment did not become observable"
validate_direct_acr_pull_assignment "$registry_id" "$assignment_json" "$target_principal_id" || fail "target ACR assignment is invalid"

query_target_database || fail "target database existence could not be determined"
if (( target_db_existing == 0 )); then
  existing_restore_deployments="$(az deployment group list --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --output json --only-show-errors)" || fail "restore deployment inventory could not be read"
  jq -e --arg name "$database_restore_deployment" '[.[] | select(.name == $name)] | length == 0' \
    <<<"$existing_restore_deployments" >/dev/null || fail "refusing to reuse an existing restore deployment record"
  az deployment group create --subscription "$subscription_id" --resource-group "$source_resource_group" \
    --name "$database_restore_deployment" --template-file "$database_template" \
    --parameters serverName="$source_server" sourceDatabaseId="$source_db_id" targetDatabaseName="$target_database" \
      restorePointUtc="$restore_point_utc" recoveryManifestDigest="$manifest_digest" recoveryId="$recovery_id" expiryUtc="$expiry_utc" \
    --only-show-errors --output json >"$temp_dir/database-restore.json" || fail "Azure point-in-time restore deployment failed"
else
  verify_owned_target_database "$target_db_json" "$manifest_tag" || fail "refusing to adopt a stale or unrelated restored database"
fi
for _ in {1..180}; do
  query_target_database || fail "target database state could not be determined"
  [[ -n "$target_db_json" ]] && jq -e '
    ((.status // .properties.status) == "Online") and
    (((.provisioningState // .properties.provisioningState) == null) or ((.provisioningState // .properties.provisioningState) == "Succeeded"))
  ' <<<"$target_db_json" >/dev/null && break
  sleep 10
done
if [[ -z "${target_db_json:-}" ]] || ! jq -e --arg id "$target_db_id" '(.id | ascii_downcase) == ($id | ascii_downcase) and ((.status // .properties.status) == "Online")' <<<"$target_db_json" >/dev/null; then
  fail "target database restore was not verified"
fi
verify_owned_target_database "$target_db_json" "$manifest_tag" || fail "restored database ownership is invalid"
verify_provider_restore_provenance || fail "Azure provider restore provenance was not verified"

sql_connection_file="$temp_dir/sql-connection"
printf 'Server=tcp:%s.database.windows.net,1433;Initial Catalog=%s;Authentication=Active Directory Managed Identity;User Id=%s;Encrypt=True;Trust Server Certificate=False;' "$source_server" "$target_database" "$target_client_id" >"$sql_connection_file"
secure_file "$sql_connection_file" || fail "target SQL reference file is not private"
set_secret_file() {
  local name="$1" file="$2"
  for _ in {1..24}; do
    az keyvault secret set --subscription "$subscription_id" --vault-name "$target_vault" --name "$name" --file "$file" --only-show-errors >/dev/null 2>&1 && return 0
    sleep 5
  done
  return 1
}
set_secret_file sql-connection "$sql_connection_file" || fail "target SQL secret could not be rebound"
target_sql_secret_uri="$(az keyvault secret show --subscription "$subscription_id" --vault-name "$target_vault" --name sql-connection \
  --query id --output tsv --only-show-errors)" || fail "target SQL secret reference could not be read"
[[ "$target_sql_secret_uri" =~ ^https://[a-z0-9-]+\.vault\.azure\.net/secrets/sql-connection/[a-f0-9]{32}$ ]] || fail "target SQL secret reference is not immutable"

sed -e "s/__WORKLOAD_IDENTITY_NAME__/${target_identity}/g" -e "s/__WORKLOAD_IDENTITY_CLIENT_ID__/${target_client_id}/g" "$proof_dir/sql-bootstrap.sql" >"$temp_dir/sql-bootstrap.sql"
create_owned_firewall_rule
sqlcmd -S "tcp:${source_server}.database.windows.net,1433" -d "$target_database" --authentication-method ActiveDirectoryDefault -i "$temp_dir/sql-bootstrap.sql" >/dev/null
delete_owned_firewall_rule || fail "temporary SQL firewall rule cleanup was not verified"

deploy_target true >"$temp_dir/target-workload.json"
target_secret_refs="$(az containerapp show --subscription "$subscription_id" --resource-group "$target_resource_group" --name "$target_app" \
  --query properties.configuration.secrets --output json --only-show-errors)" || fail "target secret references could not be verified"
jq -e --arg admin "$source_admin_secret_uri" --arg signing "$source_signing_secret_uri" --arg sql "$target_sql_secret_uri" --arg identity "$target_identity_id" '
  type == "array" and length == 3 and
  ([.[].name] | sort) == ["admin-password","identity-signing-key","sql-connection"] and
  ([.[] | select(.name == "admin-password" and .keyVaultUrl == $admin and .identity == $identity)] | length) == 1 and
  ([.[] | select(.name == "identity-signing-key" and .keyVaultUrl == $signing and .identity == $identity)] | length) == 1 and
  ([.[] | select(.name == "sql-connection" and .keyVaultUrl == $sql and .identity == $identity)] | length) == 1
' <<<"$target_secret_refs" >/dev/null || fail "target secret references are not exact"
target_endpoint="$(jq -r '.properties.outputs.containerAppEndpoint.value' "$temp_dir/target-workload.json")"
[[ "$target_endpoint" == "https://${target_app}."*.azurecontainerapps.io ]] || fail "target endpoint output is invalid"
for _ in {1..60}; do curl --fail --silent --show-error --max-time 30 "$target_endpoint/health" >/dev/null 2>&1 && break; sleep 10; done
curl --fail --silent --show-error --max-time 30 "$target_endpoint/health" >/dev/null || fail "target health was not verified"
dotnet run --project "$probe_project" --no-restore -- \
  --endpoint "$target_endpoint" --environment "$target_name" --username proof-admin --password-file "$source_password_file" \
  --workflow-id "$pre_definition" --mode verify --absent-workflow-id "$post_definition" >"$temp_dir/target-workflow.json"
jq -e '.outcome == "passed" and .result == "Finished"' "$temp_dir/target-workflow.json" >/dev/null || fail "restored workflow verification failed"
cutover_eligible_at="$(utc_now)"
rpo_seconds="$(non_negative_age_seconds "$post_committed_at" "$provider_restore_point_utc")" || fail "incident/recovery-point age could not be measured"
rto_seconds="$(non_negative_age_seconds "$cutover_eligible_at" "$restore_accepted_at")" || fail "restore duration could not be measured"
(( rpo_seconds <= 86400 && rto_seconds <= 14400 )) || fail "recovery objectives were exceeded"

cleanup_target
curl --fail --silent --show-error --max-time 30 "$source_endpoint/health" >/dev/null || fail "source was not preserved after target cleanup"
jq -n \
  --arg recoveryId "$recovery_id" --arg source "$source_proof_name" --arg target "$target_name" \
  --arg sourceQuiescedAt "$source_quiesced_at" --arg earliestRestoreDate "$earliest_restore_date" \
  --arg restorePoint "$provider_restore_point_utc" --arg incidentCutoff "$post_committed_at" \
  --arg manifestDigest "$manifest_tag" \
  --arg desiredRevisionId "$desired_revision_id" --arg desiredRevisionDigest "sha256:${desired_revision_digest}" \
  --arg resolvedPlanReference "$resolved_plan_reference" --arg resolvedPlanDigest "sha256:${resolved_plan_digest}" \
  --arg releaseManifestReference "$release_manifest_reference" --arg releaseManifestDigest "sha256:${release_manifest_digest}" \
  --arg image "${image_repository}@sha256:${image_digest}" --arg preDefinition "$pre_definition" --arg postDefinition "$post_definition" \
  --argjson rpoSeconds "$rpo_seconds" --argjson rtoSeconds "$rto_seconds" \
  '{schemaVersion:1,outcome:"passed",recoveryId:$recoveryId,sourceInstance:$source,targetInstance:$target,
    sourceQuiescedAtUtc:$sourceQuiescedAt,recoveryPointUtc:$restorePoint,incidentCutoffUtc:$incidentCutoff,
    earliestRestoreDateUtc:$earliestRestoreDate,manifestDigest:$manifestDigest,immutableImage:$image,
    desiredState:{revisionId:$desiredRevisionId,revisionDigest:$desiredRevisionDigest,
      resolvedPlan:{reference:$resolvedPlanReference,digest:$resolvedPlanDigest},
      releaseManifest:{reference:$releaseManifestReference,digest:$releaseManifestDigest}},
    quiescence:{providerConfirmed:true,activeRevisionCount:0,replicaCount:0,workloadDrainScope:"container-app"},
    workflow:{prePoint:$preDefinition,postPointAbsent:$postDefinition,status:"Finished"},rpoSeconds:$rpoSeconds,
    rtoSeconds:$rtoSeconds,healthBeforeEligibility:true,cutoverEligible:true,trafficMutated:false,
    targetResourcesAbsent:true,sourcePreserved:true,
    limitations:["same-region","same-logical-sql-server","no-automatic-cutover","disposable-provider-confirmed-zero-active-zero-replica-quiescence","quiescence-does-not-prove-database-drain"]}'
