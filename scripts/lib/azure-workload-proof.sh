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
