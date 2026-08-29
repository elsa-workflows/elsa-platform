#!/usr/bin/env bash

# Pure selection logic shared by apply, what-if and offline behavioral tests.
# Revision names are supplied as a JSON array so Azure pagination remains the
# caller's responsibility and the decision itself stays deterministic.
select_workload_revision_suffix() {
  local plan_fingerprint="$1"
  local current_revision_suffix="$2"
  local app_name="$3"
  local revision_names_json="$4"
  local candidate recovery_ordinal

  if [[ "$current_revision_suffix" == "$plan_fingerprint" || "$current_revision_suffix" =~ ^${plan_fingerprint}-r[0-9]+$ ]]; then
    printf '%s\n' "$current_revision_suffix"
    return 0
  fi

  if ! jq -e --arg name "${app_name}--${plan_fingerprint}" 'index($name) != null' <<<"$revision_names_json" >/dev/null; then
    printf '%s\n' "$plan_fingerprint"
    return 0
  fi

  for recovery_ordinal in {1..999}; do
    candidate="${plan_fingerprint}-r${recovery_ordinal}"
    if ! jq -e --arg name "${app_name}--${candidate}" 'index($name) != null' <<<"$revision_names_json" >/dev/null; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  echo "No free deterministic recovery revision suffix was available" >&2
  return 5
}

promote_workload_revision() {
  local resource_group="$1"
  local app_name="$2"
  local stable_revision="$3"
  local candidate_revision="$4"
  local endpoint="$5"
  local promotion_failed=0

  if ! az containerapp ingress traffic set --resource-group "$resource_group" --name "$app_name" \
    --revision-weight "${candidate_revision}=100" --only-show-errors --output none; then
    echo "Candidate traffic promotion failed or returned an uncertain result" >&2
    promotion_failed=1
  elif ! curl --fail --silent --show-error --retry 30 --retry-all-errors --retry-delay 5 --max-time 10 "$endpoint/health" >/dev/null; then
    echo "Candidate failed external health after traffic promotion" >&2
    promotion_failed=1
  fi

  (( promotion_failed == 1 )) || return 0
  if [[ -n "$stable_revision" && "$stable_revision" != "$candidate_revision" ]]; then
    if az containerapp ingress traffic set --resource-group "$resource_group" --name "$app_name" \
      --revision-weight "${stable_revision}=100" "${candidate_revision}=0" --only-show-errors --output none; then
      echo "Restored stable traffic to $stable_revision after failed promotion" >&2
    else
      echo "CRITICAL: failed to restore stable traffic to $stable_revision after failed promotion" >&2
    fi
  else
    echo "No prior stable revision was available for rollback" >&2
  fi
  return 5
}

# Remove only the temporary SQL Entra administrator owned by this proof. The
# caller may invoke this from an EXIT trap, so an unexpected administrator must
# be preserved even when an earlier setup step has already failed closed.
remove_owned_sql_bootstrap_admin() {
  local subscription_id="$1"
  local resource_group="$2"
  local server_name="$3"
  local expected_login="$4"
  local expected_object_id="$5"
  local admin_count admin_state server_count

  server_count="$(az sql server list --subscription "$subscription_id" --resource-group "$resource_group" \
    --query "[?name=='${server_name}'] | length(@)" --output tsv --only-show-errors 2>/dev/null)" || return 1
  (( server_count == 0 )) && return 0
  (( server_count == 1 )) || { echo "Expected at most one proof SQL server named $server_name" >&2; return 1; }

  admin_count="$(az sql server ad-admin list --subscription "$subscription_id" --resource-group "$resource_group" \
    --server "$server_name" --query 'length(@)' --output tsv --only-show-errors 2>/dev/null)" || return 1
  (( admin_count == 0 )) && return 0
  (( admin_count == 1 )) || { echo "Refusing to remove unexpected SQL server administrators" >&2; return 1; }

  admin_state="$(az sql server ad-admin list --subscription "$subscription_id" --resource-group "$resource_group" \
    --server "$server_name" --query '[0].{login:login,sid:sid}' --output json --only-show-errors 2>/dev/null)" || return 1
  if [[ "$(jq -r .login <<<"$admin_state")" != "$expected_login" || "$(jq -r .sid <<<"$admin_state")" != "$expected_object_id" ]]; then
    echo "Refusing to remove an unexpected SQL server administrator" >&2
    return 1
  fi

  az sql server ad-only-auth disable --subscription "$subscription_id" --resource-group "$resource_group" \
    --name "$server_name" --only-show-errors >/dev/null || return 1
  az sql server ad-admin delete --subscription "$subscription_id" --resource-group "$resource_group" \
    --server "$server_name" --only-show-errors >/dev/null || return 1

  for _ in {1..12}; do
    admin_count="$(az sql server ad-admin list --subscription "$subscription_id" --resource-group "$resource_group" \
      --server "$server_name" --query 'length(@)' --output tsv --only-show-errors 2>/dev/null)" || return 1
    (( admin_count == 0 )) && return 0
    sleep 5
  done
  echo "Temporary SQL bootstrap administrator remained configured" >&2
  return 1
}

# Azure role-assignment deletion is eventually consistent and may commit even
# when the CLI returns an error. Verify the exact owned assignment is absent
# before its deployment record (the cleanup provenance) can be removed.
valid_role_assignment_id() {
  local registry_id_lower assignment_id_lower expected_prefix assignment_guid
  registry_id_lower="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
  assignment_id_lower="$(printf '%s' "$2" | tr '[:upper:]' '[:lower:]')"
  expected_prefix="${registry_id_lower}/providers/microsoft.authorization/roleassignments/"
  [[ "${assignment_id_lower:0:${#expected_prefix}}" == "$expected_prefix" ]] || return 1
  assignment_guid="${assignment_id_lower:${#expected_prefix}}"
  [[ "$assignment_guid" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
}

delete_and_verify_role_assignment() {
  local registry_id="$1"
  local assignment_id="$2"
  local max_attempts="${3:-24}"
  local delay_seconds="${4:-5}"
  local assignments_json attempt

  az role assignment delete --ids "$assignment_id" --only-show-errors >/dev/null 2>&1 || true
  for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
    if assignments_json="$(az role assignment list --scope "$registry_id" --output json --only-show-errors 2>/dev/null)" &&
      jq -e --arg id "$assignment_id" '[.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | length == 0' <<<"$assignments_json" >/dev/null; then
      return 0
    fi
    (( attempt == max_attempts )) || sleep "$delay_seconds"
  done
  echo "Proof-owned ACR role assignment remained observable after deletion" >&2
  return 1
}

delete_and_verify_group_deployment() {
  local resource_group="$1"
  local deployment_name="$2"
  local max_attempts="${3:-24}"
  local delay_seconds="${4:-5}"
  local deployments_json attempt

  az deployment group delete --resource-group "$resource_group" --name "$deployment_name" --only-show-errors >/dev/null 2>&1 || true
  for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
    if deployments_json="$(az deployment group list --resource-group "$resource_group" --output json --only-show-errors 2>/dev/null)" &&
      jq -e --arg name "$deployment_name" '[.[] | select(.name == $name)] | length == 0' <<<"$deployments_json" >/dev/null; then
      return 0
    fi
    (( attempt == max_attempts )) || sleep "$delay_seconds"
  done
  echo "Proof-owned ACR deployment record remained observable after deletion" >&2
  return 1
}

wait_for_resource_group_absence() {
  local resource_group="$1"
  local max_attempts="${2:-240}"
  local delay_seconds="${3:-5}"
  local group_exists attempt

  for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
    group_exists="$(az group exists --name "$resource_group" --output tsv --only-show-errors 2>/dev/null || echo unknown)"
    [[ "$group_exists" == false ]] && return 0
    (( attempt == max_attempts )) || sleep "$delay_seconds"
  done
  echo "Proof resource group remained observable after the bounded deletion window" >&2
  return 1
}

purge_and_verify_deleted_vault() {
  local vault_name="$1"
  local location="$2"
  local max_attempts="${3:-30}"
  local delay_seconds="${4:-5}"
  local deleted_vaults_json vault_count attempt purge_requested=0

  for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
    if deleted_vaults_json="$(az keyvault list-deleted --resource-type vault --output json --only-show-errors 2>/dev/null)"; then
      vault_count="$(jq -r --arg name "$vault_name" --arg location "$location" \
        '[.[] | select(.name == $name and ((.properties.location // .location // "") | ascii_downcase) == ($location | ascii_downcase))] | length' \
        <<<"$deleted_vaults_json")" || return 1
      (( vault_count == 0 )) && return 0
      (( vault_count == 1 )) || { echo "Expected at most one deleted proof vault" >&2; return 1; }
      if (( purge_requested == 0 )); then
        az keyvault purge --name "$vault_name" --location "$location" --only-show-errors >/dev/null 2>&1 || true
        purge_requested=1
      fi
    fi
    (( attempt == max_attempts )) || sleep "$delay_seconds"
  done
  echo "Deleted proof vault absence could not be verified" >&2
  return 1
}
