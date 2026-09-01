#!/usr/bin/env bash

# Shared lifecycle helpers used by apply, what-if and offline behavioral tests.
# Revision names are supplied as a JSON array so Azure pagination remains the
# caller's responsibility and the decision itself stays deterministic.
default_expiry_utc() {
  local expiry
  if expiry="$(date -u -v+1d '+%Y-%m-%d' 2>/dev/null)"; then
    printf '%s\n' "$expiry"
  elif expiry="$(date -u -d '+1 day' '+%Y-%m-%d' 2>/dev/null)"; then
    printf '%s\n' "$expiry"
  else
    echo "A UTC expiry date could not be derived" >&2
    return 1
  fi
}

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

resolve_stable_traffic_revision() {
  local resource_group="$1"
  local app_name="$2"
  local app_count stable_revision stable_state traffic_json

  app_count="$(az resource list --resource-group "$resource_group" --resource-type Microsoft.App/containerApps --query "[?name=='${app_name}'] | length(@)" --output tsv --only-show-errors)"
  if (( app_count == 0 )); then
    return 0
  fi
  if (( app_count != 1 )); then
    echo "Expected exactly one proof Container App named $app_name" >&2
    return 5
  fi

  traffic_json="$(az containerapp show --resource-group "$resource_group" --name "$app_name" \
    --query properties.configuration.ingress.traffic --output json --only-show-errors)" || return 1
  stable_revision="$(jq -r '
    if type != "array" then
      empty
    else
      ([.[] | ((.weight // 0) | tonumber)] | add) as $total_weight |
      ([.[] | select(.revisionName != null and ((.weight // 0) | tonumber) == 100)] | .) as $stable |
      if $total_weight == 100 and ($stable | length) == 1 then $stable[0].revisionName else empty end
    end
  ' <<<"$traffic_json")" || return 1
  [[ -n "$stable_revision" ]] || {
    echo "Refusing rollout because existing app traffic has no single healthy 100% revision" >&2
    return 5
  }
  stable_state="$(az containerapp revision show --resource-group "$resource_group" --name "$app_name" --revision "$stable_revision" --query 'properties.{active:active,health:healthState}' --output json --only-show-errors)" || return 1
  [[ "$(jq -r .active <<<"$stable_state")" == true && "$(jq -r .health <<<"$stable_state")" == Healthy ]] || {
    echo "Refusing rollout because stable revision $stable_revision is not active and healthy" >&2
    return 5
  }
  printf '%s\n' "$stable_revision"
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
  elif ! verify_single_revision_traffic "$resource_group" "$app_name" "$candidate_revision"; then
    echo "Candidate traffic promotion did not reach the required 100% postcondition" >&2
    promotion_failed=1
  elif ! curl --fail --silent --show-error --retry 30 --retry-all-errors --retry-delay 5 --max-time 10 "$endpoint/health" >/dev/null; then
    echo "Candidate failed external health after traffic promotion" >&2
    promotion_failed=1
  fi

  (( promotion_failed == 1 )) || return 0
  if [[ -n "$stable_revision" && "$stable_revision" != "$candidate_revision" ]]; then
    if az containerapp ingress traffic set --resource-group "$resource_group" --name "$app_name" \
      --revision-weight "$stable_revision=100" "$candidate_revision=0" --only-show-errors --output none &&
      verify_workload_traffic "$resource_group" "$app_name" "$stable_revision" "$candidate_revision"; then
      echo "Restored stable traffic to $stable_revision after failed promotion" >&2
    else
      echo "CRITICAL: failed to restore stable traffic to $stable_revision after failed promotion" >&2
    fi
  else
    echo "No prior stable revision was available for rollback" >&2
  fi
  return 5
}

verify_single_revision_traffic() {
  local resource_group="$1"
  local app_name="$2"
  local desired_revision="$3"
  local traffic_json

  traffic_json="$(az containerapp show --resource-group "$resource_group" --name "$app_name" \
    --query properties.configuration.ingress.traffic --output json --only-show-errors)" || return 1
  jq -e --arg desired "$desired_revision" '
    type == "array" and
    ([.[] | select(.revisionName == $desired and ((.weight // 0) | tonumber) == 100)] | length) == 1 and
    (([.[] | ((.weight // 0) | tonumber)] | add) == 100)
  ' <<<"$traffic_json" >/dev/null
}

verify_workload_traffic() {
  local resource_group="$1"
  local app_name="$2"
  local stable_revision="$3"
  local candidate_revision="$4"
  local traffic_json

  traffic_json="$(az containerapp show --resource-group "$resource_group" --name "$app_name" \
    --query properties.configuration.ingress.traffic --output json --only-show-errors)" || return 1
  jq -e --arg stable "$stable_revision" --arg candidate "$candidate_revision" '
    type == "array" and
    ([.[] | select(.revisionName == $stable and ((.weight // 0) | tonumber) == 100)] | length) == 1 and
    ([.[] | select(.revisionName == $candidate and ((.weight // 0) | tonumber) != 0)] | length) == 0 and
    (([.[] | ((.weight // 0) | tonumber)] | add) == 100)
  ' <<<"$traffic_json" >/dev/null
}

# Azure role-assignment deletion is eventually consistent and may commit even
# when the CLI returns an error. Verify the exact owned assignment is absent
# before its deployment record (the cleanup provenance) can be removed.
azure_cli_error_is_not_found() {
  local error_text
  error_text="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
  [[ "$error_text" =~ (notfound|not[[:space:]-]*found|does[[:space:]]+not[[:space:]]+exist|could[[:space:]]+not[[:space:]]+be[[:space:]]+found) ]]
}

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
  local subscription_id="$1"
  local registry_id="$2"
  local assignment_id="$3"
  local max_attempts="${4:-24}"
  local delay_seconds="${5:-5}"
  local assignments_json attempt error_file error_text

  valid_role_assignment_id "$registry_id" "$assignment_id" || {
    echo "Refusing to delete an ACR role assignment outside the governed registry" >&2
    return 1
  }
  error_file="$(mktemp)"
  if ! az role assignment delete --subscription "$subscription_id" --ids "$assignment_id" --only-show-errors >/dev/null 2>"$error_file"; then
    error_text="$(<"$error_file")"
    if azure_cli_error_is_not_found "$error_text"; then
      : # The desired postcondition already holds.
    else
      echo "Role assignment deletion returned an uncertain result; verifying absence" >&2
    fi
  fi
  rm -f -- "$error_file"
  for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
    error_file="$(mktemp)"
    if assignments_json="$(az role assignment list --subscription "$subscription_id" --all --output json --only-show-errors 2>"$error_file")"; then
      rm -f -- "$error_file"
    else
      error_text="$(<"$error_file")"
      rm -f -- "$error_file"
      echo "Could not verify ACR role assignment absence: Azure CLI read failed" >&2
      return 2
    fi
    if jq -e --arg id "$assignment_id" 'type == "array" and ([.[] | select((.id | ascii_downcase) == ($id | ascii_downcase))] | length == 0)' <<<"$assignments_json" >/dev/null; then
      return 0
    fi
    (( attempt == max_attempts )) || sleep "$delay_seconds"
  done
  echo "Proof-owned ACR role assignment remained observable after deletion" >&2
  return 1
}

validate_direct_acr_pull_assignment() {
  local registry_id="$1"
  local assignment_json="$2"
  local expected_principal_id="${3:-}"
  local assignment_id assignment_scope assignment_role_id assignment_principal_id registry_id_lower assignment_scope_lower

  assignment_id="$(jq -r '.id // empty' <<<"$assignment_json")"
  assignment_scope="$(jq -r '.scope // empty' <<<"$assignment_json")"
  assignment_role_id="$(jq -r '.roleDefinitionId // empty | split("/") | last' <<<"$assignment_json")"
  assignment_principal_id="$(jq -r '.principalId // empty' <<<"$assignment_json")"
  registry_id_lower="$(printf '%s' "$registry_id" | tr '[:upper:]' '[:lower:]')"
  assignment_scope_lower="$(printf '%s' "$assignment_scope" | tr '[:upper:]' '[:lower:]')"
  [[ "$assignment_scope_lower" == "$registry_id_lower" ]] || return 1
  [[ "$(printf '%s' "$assignment_role_id" | tr '[:upper:]' '[:lower:]')" == 7f951dda-4ed3-4680-a7ca-43fe172d538d ]] || return 1
  if [[ -n "$expected_principal_id" ]]; then
    [[ "$(printf '%s' "$assignment_principal_id" | tr '[:upper:]' '[:lower:]')" == "$(printf '%s' "$expected_principal_id" | tr '[:upper:]' '[:lower:]')" ]] || return 1
  fi
  valid_role_assignment_id "$registry_id" "$assignment_id"
}

has_direct_acr_pull_assignment() {
  local registry_id="$1"
  local principal_id="$2"
  local assignments_json="$3"

  jq -e --arg scope "$registry_id" --arg principal "$principal_id" '
    type == "array" and
    any(.[]; ((.scope // "") | ascii_downcase) == ($scope | ascii_downcase) and
      ((.principalId // "") | ascii_downcase) == ($principal | ascii_downcase) and
      (((.roleDefinitionId // "") | split("/") | last) | ascii_downcase) == "7f951dda-4ed3-4680-a7ca-43fe172d538d")
  ' <<<"$assignments_json" >/dev/null
}

delete_and_verify_firewall_rule() {
  local subscription_id="$1"
  local resource_group="$2"
  local server_name="$3"
  local rule_name="$4"
  local max_attempts="${5:-24}"
  local delay_seconds="${6:-5}"
  local firewall_rules_json attempt

  az sql server firewall-rule delete --subscription "$subscription_id" --resource-group "$resource_group" \
    --server "$server_name" --name "$rule_name" --only-show-errors >/dev/null 2>&1 || true
  for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
    if firewall_rules_json="$(az sql server firewall-rule list --subscription "$subscription_id" --resource-group "$resource_group" \
      --server "$server_name" --output json --only-show-errors 2>/dev/null)" &&
      jq -e --arg name "$rule_name" '[.[] | select(.name == $name)] | length == 0' <<<"$firewall_rules_json" >/dev/null; then
      return 0
    fi
    (( attempt == max_attempts )) || sleep "$delay_seconds"
  done
  echo "Temporary SQL firewall rule remained observable after deletion" >&2
  return 1
}

delete_and_verify_group_deployment() {
  local subscription_id="$1"
  local resource_group="$2"
  local deployment_name="$3"
  local max_attempts="${4:-24}"
  local delay_seconds="${5:-5}"
  local deployments_json attempt error_file error_text

  error_file="$(mktemp)"
  if ! az deployment group delete --subscription "$subscription_id" --resource-group "$resource_group" --name "$deployment_name" --only-show-errors >/dev/null 2>"$error_file"; then
    error_text="$(<"$error_file")"
    if azure_cli_error_is_not_found "$error_text"; then
      : # The desired postcondition already holds.
    else
      echo "Deployment deletion returned an uncertain result; verifying absence" >&2
    fi
  fi
  rm -f -- "$error_file"
  for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
    error_file="$(mktemp)"
    if deployments_json="$(az deployment group list --subscription "$subscription_id" --resource-group "$resource_group" --output json --only-show-errors 2>"$error_file")"; then
      rm -f -- "$error_file"
    else
      error_text="$(<"$error_file")"
      rm -f -- "$error_file"
      echo "Could not verify deployment absence: Azure CLI read failed" >&2
      return 2
    fi
    if jq -e --arg name "$deployment_name" 'type == "array" and ([.[] | select(.name == $name)] | length == 0)' <<<"$deployments_json" >/dev/null; then
      return 0
    fi
    (( attempt == max_attempts )) || sleep "$delay_seconds"
  done
  echo "Proof-owned deployment record remained observable after deletion" >&2
  return 1
}

wait_for_resource_group_absence() {
  local subscription_id="$1"
  local resource_group="$2"
  # Container Apps managed-environment deletion has exceeded twenty minutes in
  # live proof runs. Keep the wait bounded while allowing the provider's
  # observed tail latency to complete before reporting cleanup failure.
  local max_attempts="${3:-300}"
  local delay_seconds="${4:-5}"
  local group_exists attempt error_file error_text

  for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
    error_file="$(mktemp)"
    if group_exists="$(az group exists --subscription "$subscription_id" --name "$resource_group" --output tsv --only-show-errors 2>"$error_file")"; then
      rm -f -- "$error_file"
      case "$group_exists" in
        false) return 0 ;;
        true) ;;
        *)
          echo "Could not verify resource group absence: Azure CLI returned an unexpected value" >&2
          return 2
          ;;
      esac
    else
      error_text="$(<"$error_file")"
      rm -f -- "$error_file"
      echo "Could not verify resource group absence: Azure CLI read failed" >&2
      return 2
    fi
    (( attempt == max_attempts )) || sleep "$delay_seconds"
  done
  echo "Proof resource group remained observable after the bounded deletion window" >&2
  return 1
}

# The proof group is disposable, but its ownership must still be exact. A
# matching tag is not sufficient: every live resource must be rooted in one of
# the resources emitted by the checked-in proof template, and the vault may
# contain only the two expected direct RBAC assignments. Mixed groups are
# rejected before any external assignment or group deletion is attempted.
verify_proof_resource_inventory() {
  local subscription_id="$1"
  local resource_group="$2"
  local proof_name="$3"
  local identity_principal_id="$4"
  local bootstrap_object_id="$5"
  local group_id resource_json vault_id assignments_json direct_group_assignments

  group_id="/subscriptions/$subscription_id/resourceGroups/$resource_group"
  resource_json="$(az resource list --subscription "$subscription_id" --resource-group "$resource_group" \
    --output json --only-show-errors)" || return 1
  jq -e --arg base "$group_id" --arg proof "$proof_name" '
    def root($provider; $type; $name):
      ($base + "/providers/" + $provider + "/" + $type + "/" + $name);
    def owned_root($id):
      ($id | ascii_downcase) as $lower_id |
      any([
        root("Microsoft.ManagedIdentity"; "userAssignedIdentities"; ($proof + "-identity")),
        root("Microsoft.KeyVault"; "vaults"; ($proof + "-kv")),
        root("Microsoft.Sql"; "servers"; ($proof + "-sql")),
        root("Microsoft.OperationalInsights"; "workspaces"; ($proof + "-logs")),
        root("Microsoft.App"; "managedEnvironments"; ($proof + "-aca")),
        root("Microsoft.App"; "containerApps"; ($proof + "-app"))
      ][]; . as $root | ($lower_id == ($root | ascii_downcase) or
        ($lower_id | startswith(($root | ascii_downcase) + "/"))));
    def owned_vault_role($id):
      ($id | ascii_downcase | startswith(
        (root("Microsoft.KeyVault"; "vaults"; ($proof + "-kv")) |
          ascii_downcase) + "/providers/microsoft.authorization/roleassignments/"));
    all(.[]; ((.type | ascii_downcase) == "microsoft.authorization/roleassignments"
      and owned_vault_role(.id))
      or ((.type | ascii_downcase) != "microsoft.authorization/roleassignments"
        and owned_root(.id)))
  ' <<<"$resource_json" >/dev/null || {
    echo "Refusing cleanup: resource inventory contains an unowned resource" >&2
    return 1
  }

  vault_id="$group_id/providers/Microsoft.KeyVault/vaults/$proof_name-kv"
  assignments_json="$(az role assignment list --subscription "$subscription_id" --all --output json --only-show-errors)" || {
    echo "Could not read proof vault role assignments: Azure CLI read failed" >&2
    return 1
  }
  jq -e --arg scope "$vault_id" --arg workload "$identity_principal_id" --arg bootstrap "$bootstrap_object_id" '
    ($workload | ascii_downcase) as $workload_lower |
    ($bootstrap | ascii_downcase) as $bootstrap_lower |
    ($scope | ascii_downcase) as $scope_lower |
    [.[] | select(
      ((.scope // "") | ascii_downcase) == $scope_lower or
      ((.scope // "") | ascii_downcase | startswith($scope_lower + "/"))
    )] as $owned |
    ($owned | length) == 2 and
    (all($owned[]; ((.scope // "") | ascii_downcase) == $scope_lower)) and
    (any($owned[]; ((.principalId // "") | ascii_downcase) == $workload_lower and ((.roleDefinitionId // "" | split("/") | last) | ascii_downcase) == "4633458b-17de-408a-b874-0445c86b69e6")) and
    (any($owned[]; ((.principalId // "") | ascii_downcase) == $bootstrap_lower and ((.roleDefinitionId // "" | split("/") | last) | ascii_downcase) == "b86a8fe4-44ce-4948-aee5-eccb2c155cd7"))
  ' <<<"$assignments_json" >/dev/null || {
    echo "Refusing cleanup: proof vault RBAC inventory is not exact" >&2
    return 1
  }

  direct_group_assignments="$(az role assignment list --subscription "$subscription_id" --all --output json --only-show-errors)" || {
    echo "Could not read resource-group role assignments: Azure CLI read failed" >&2
    return 1
  }
  jq -e --arg scope "$group_id" '[.[] | select((.scope // "" | ascii_downcase) == ($scope | ascii_downcase))] | length == 0' <<<"$direct_group_assignments" >/dev/null || {
    echo "Refusing cleanup: resource group has an unexpected direct role assignment" >&2
    return 1
  }
}

purge_and_verify_deleted_vault() {
  local subscription_id="$1"
  local vault_name="$2"
  local location="$3"
  local max_attempts="${4:-30}"
  local delay_seconds="${5:-5}"
  local deleted_vaults_json vault_count attempt purge_requested=0 error_file error_text

  for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
    error_file="$(mktemp)"
    if deleted_vaults_json="$(az keyvault list-deleted --subscription "$subscription_id" --resource-type vault --output json --only-show-errors 2>"$error_file")"; then
      rm -f -- "$error_file"
    else
      error_text="$(<"$error_file")"
      rm -f -- "$error_file"
      echo "Could not verify deleted proof vault absence: Azure CLI read failed" >&2
      return 2
    fi
    vault_count="$(jq -r --arg name "$vault_name" --arg location "$location" \
      'if type != "array" then error("expected an array") else ([.[] | select(.name == $name and ((.properties.location // .location // "") | ascii_downcase) == ($location | ascii_downcase))] | length) end' \
      <<<"$deleted_vaults_json")" || {
      echo "Could not verify deleted proof vault absence: Azure CLI returned invalid JSON" >&2
      return 2
    }
    [[ "$vault_count" =~ ^[0-9]+$ ]] || {
      echo "Could not verify deleted proof vault absence: Azure CLI returned an unexpected JSON shape" >&2
      return 2
    }
    (( vault_count == 0 )) && return 0
    (( vault_count == 1 )) || { echo "Expected at most one deleted proof vault" >&2; return 1; }
    if (( purge_requested == 0 )); then
      error_file="$(mktemp)"
      if ! az keyvault purge --subscription "$subscription_id" --name "$vault_name" --location "$location" --only-show-errors >/dev/null 2>"$error_file"; then
        error_text="$(<"$error_file")"
        if ! azure_cli_error_is_not_found "$error_text"; then
          echo "Deleted proof vault purge returned an uncertain result; verifying absence" >&2
        fi
      fi
      rm -f -- "$error_file"
      purge_requested=1
    fi
    (( attempt == max_attempts )) || sleep "$delay_seconds"
  done
  echo "Deleted proof vault absence could not be verified" >&2
  return 1
}
