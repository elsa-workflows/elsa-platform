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
