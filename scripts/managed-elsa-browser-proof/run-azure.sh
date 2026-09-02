#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/../.." && pwd)
proof_output=$(mktemp -d "${TMPDIR:-/tmp}/elsa-managed-azure-browser-proof.XXXXXX")

cleanup() {
  local exit_status=$?
  set +e
  if [[ -d "$proof_output" ]]; then
    find "$proof_output" -depth -delete
  fi
  exit "$exit_status"
}
trap cleanup EXIT

: "${ADMIN_UI_BASE_URL:?Set ADMIN_UI_BASE_URL to the public Elsa Control HTTPS origin.}"
: "${MANAGED_ELSA_PROOF_RUNTIME_ORIGIN:?Set MANAGED_ELSA_PROOF_RUNTIME_ORIGIN to the public runtime HTTPS origin.}"
: "${MANAGED_ELSA_PROOF_STATE_LIFETIME_SECONDS:?Set MANAGED_ELSA_PROOF_STATE_LIFETIME_SECONDS to the configured runtime browser-state lifetime.}"
: "${MANAGED_ELSA_PROOF_CONTROL_RESOURCE_GROUP:?Set MANAGED_ELSA_PROOF_CONTROL_RESOURCE_GROUP to the Control resource group.}"
: "${MANAGED_ELSA_PROOF_CONTROL_APP_NAME:?Set MANAGED_ELSA_PROOF_CONTROL_APP_NAME to the Control App Service name.}"
: "${MANAGED_ELSA_PROOF_EXPECTED_CONTROL_IMAGE_ID:?Set MANAGED_ELSA_PROOF_EXPECTED_CONTROL_IMAGE_ID to the deployed Control commit.}"
: "${MANAGED_ELSA_PROOF_EXPECTED_CONTROL_BUILD_NUMBER:?Set MANAGED_ELSA_PROOF_EXPECTED_CONTROL_BUILD_NUMBER to the deployed Control build number.}"
: "${MANAGED_ELSA_PROOF_RUNTIME_RESOURCE_GROUP:?Set MANAGED_ELSA_PROOF_RUNTIME_RESOURCE_GROUP to the runtime resource group.}"
: "${MANAGED_ELSA_PROOF_RUNTIME_APP_NAME:?Set MANAGED_ELSA_PROOF_RUNTIME_APP_NAME to the runtime Container App name.}"
: "${MANAGED_ELSA_PROOF_EXPECTED_IMAGE:?Set MANAGED_ELSA_PROOF_EXPECTED_IMAGE to the immutable admitted runtime image.}"
: "${MANAGED_ELSA_PROOF_INSTANCE_ID:?Set MANAGED_ELSA_PROOF_INSTANCE_ID to the exact Control/runtime instance ID.}"

while [[ "$ADMIN_UI_BASE_URL" == */ ]]; do
  ADMIN_UI_BASE_URL=${ADMIN_UI_BASE_URL%/}
done
while [[ "$MANAGED_ELSA_PROOF_RUNTIME_ORIGIN" == */ ]]; do
  MANAGED_ELSA_PROOF_RUNTIME_ORIGIN=${MANAGED_ELSA_PROOF_RUNTIME_ORIGIN%/}
done

fail_preflight() {
  printf 'Azure browser proof preflight failed: %s\n' "$1" >&2
  exit 1
}

command -v az >/dev/null || fail_preflight "Azure CLI is required."
command -v jq >/dev/null || fail_preflight "jq is required."
command -v curl >/dev/null || fail_preflight "curl is required."

immutable_image_pattern='^[a-z0-9.-]+(:[0-9]+)?(/[a-z0-9._-]+)+@sha256:[0-9a-f]{64}$'
[[ "$MANAGED_ELSA_PROOF_EXPECTED_IMAGE" =~ $immutable_image_pattern ]] ||
  fail_preflight "Expected runtime image must be an immutable repository digest."
[[ "$MANAGED_ELSA_PROOF_EXPECTED_CONTROL_IMAGE_ID" =~ ^[0-9a-f]{40}$ ]] ||
  fail_preflight "Expected Control image ID must be a lowercase commit digest."
[[ "$MANAGED_ELSA_PROOF_EXPECTED_CONTROL_BUILD_NUMBER" =~ ^[1-9][0-9]*$ ]] ||
  fail_preflight "Expected Control build number must be a positive integer."
[[ "$MANAGED_ELSA_PROOF_INSTANCE_ID" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]] ||
  fail_preflight "Expected instance ID must be a lowercase canonical identifier."

if [[ ! "$MANAGED_ELSA_PROOF_STATE_LIFETIME_SECONDS" =~ ^[0-9]+$ ]]; then
  fail_preflight "State lifetime must be an integer from 5 through 300."
fi
state_lifetime_seconds=$((10#$MANAGED_ELSA_PROOF_STATE_LIFETIME_SECONDS))
if (( state_lifetime_seconds < 5 || state_lifetime_seconds > 300 )); then
  fail_preflight "State lifetime must be an integer from 5 through 300."
fi

expected_state_lifetime=$(printf '%02d:%02d:%02d' \
  $((state_lifetime_seconds / 3600)) \
  $(((state_lifetime_seconds % 3600) / 60)) \
  $((state_lifetime_seconds % 60)))

# The backticks are JMESPath string delimiters and must remain literal.
# shellcheck disable=SC2016
runtime_state=$(az containerapp show \
  --resource-group "$MANAGED_ELSA_PROOF_RUNTIME_RESOURCE_GROUP" \
  --name "$MANAGED_ELSA_PROOF_RUNTIME_APP_NAME" \
  --query '{fqdn:properties.configuration.ingress.fqdn,revision:properties.latestRevisionName,image:properties.template.containers[0].image,stateLifetime:properties.template.containers[0].env[?name==`ManagedElsa__Handoff__StateLifetime`].value|[0],instanceId:properties.template.containers[0].env[?name==`ManagedElsa__Handoff__InstanceId`].value|[0],audience:properties.template.containers[0].env[?name==`ManagedElsa__Handoff__Audience`].value|[0],controlBaseUrl:properties.template.containers[0].env[?name==`ManagedElsa__Handoff__ControlBaseUrl`].value|[0],controlContinuationUrl:properties.template.containers[0].env[?name==`ManagedElsa__Handoff__ControlContinuationUrl`].value|[0],callbackUri:properties.template.containers[0].env[?name==`ManagedElsa__Handoff__CallbackUri`].value|[0],backendUrl:properties.template.containers[0].env[?name==`Backend__Url`].value|[0],forwardedHeadersEnabled:properties.template.containers[0].env[?name==`ASPNETCORE_FORWARDEDHEADERS_ENABLED`].value|[0],minReplicas:properties.template.scale.minReplicas,maxReplicas:properties.template.scale.maxReplicas}' \
  --output json \
  --only-show-errors) || fail_preflight "Runtime state could not be read."

[[ "$(jq -r '.image // empty' <<<"$runtime_state")" == "$MANAGED_ELSA_PROOF_EXPECTED_IMAGE" ]] ||
  fail_preflight "Runtime image does not match the expected immutable image."
[[ "$(jq -r '.stateLifetime // empty' <<<"$runtime_state")" == "$expected_state_lifetime" ]] ||
  fail_preflight "Runtime state lifetime does not match the proof input."
[[ "$(jq -r '.instanceId // empty' <<<"$runtime_state")" == "$MANAGED_ELSA_PROOF_INSTANCE_ID" ]] ||
  fail_preflight "Runtime instance identity does not match the proof input."
[[ "$(jq -r '.audience // empty' <<<"$runtime_state")" == "urn:elsa:instance:$MANAGED_ELSA_PROOF_INSTANCE_ID" ]] ||
  fail_preflight "Runtime audience does not match the proof instance identity."
[[ "$(jq -r '.controlBaseUrl // empty' <<<"$runtime_state")" == "$ADMIN_UI_BASE_URL" ]] ||
  fail_preflight "Runtime Control base URL does not match the proof origin."
[[ "$(jq -r '.controlContinuationUrl // empty' <<<"$runtime_state")" == "$ADMIN_UI_BASE_URL/admin/runtimes" ]] ||
  fail_preflight "Runtime Control continuation does not match the managed-instance page."
[[ "$(jq -r '.callbackUri // empty' <<<"$runtime_state")" == "$MANAGED_ELSA_PROOF_RUNTIME_ORIGIN/managed-elsa/handoff/callback" ]] ||
  fail_preflight "Runtime callback URI does not match the proof origin."
[[ "$(jq -r '.backendUrl // empty' <<<"$runtime_state")" == "http://localhost:8080/elsa/api" ]] ||
  fail_preflight "Combined runtime backend URL does not use its internal HTTP listener."
[[ "$(jq -r '.forwardedHeadersEnabled // empty' <<<"$runtime_state")" == "true" ]] ||
  fail_preflight "Runtime forwarded-header processing is not enabled for TLS termination."
[[ "$(jq -r '.minReplicas // empty' <<<"$runtime_state")" == "1" && "$(jq -r '.maxReplicas // empty' <<<"$runtime_state")" == "1" ]] ||
  fail_preflight "Runtime scale does not enforce exactly one replica."
[[ "$MANAGED_ELSA_PROOF_RUNTIME_ORIGIN" == "https://$(jq -r '.fqdn // empty' <<<"$runtime_state")" ]] ||
  fail_preflight "Runtime origin does not match the Container App ingress."

runtime_revisions=$(az containerapp revision list \
  --resource-group "$MANAGED_ELSA_PROOF_RUNTIME_RESOURCE_GROUP" \
  --name "$MANAGED_ELSA_PROOF_RUNTIME_APP_NAME" \
  --query '[?properties.active].{name:name,health:properties.healthState,replicas:properties.replicas,traffic:properties.trafficWeight}' \
  --output json \
  --only-show-errors) || fail_preflight "Runtime revision state could not be read."

jq -e --arg revision "$(jq -r '.revision // empty' <<<"$runtime_state")" \
  'length == 1 and .[0].name == $revision and .[0].health == "Healthy" and .[0].replicas == 1 and .[0].traffic == 100' \
  >/dev/null <<<"$runtime_revisions" || fail_preflight "Runtime revision is not exclusively active and healthy."

control_state=$(az webapp show \
  --resource-group "$MANAGED_ELSA_PROOF_CONTROL_RESOURCE_GROUP" \
  --name "$MANAGED_ELSA_PROOF_CONTROL_APP_NAME" \
  --query '{host:defaultHostName,httpsOnly:httpsOnly,state:state}' \
  --output json \
  --only-show-errors) || fail_preflight "Control state could not be read."

[[ "$(jq -r '.httpsOnly' <<<"$control_state")" == "true" && "$(jq -r '.state // empty' <<<"$control_state")" == "Running" ]] ||
  fail_preflight "Control is not running with HTTPS-only enforcement."
[[ "$ADMIN_UI_BASE_URL" == "https://$(jq -r '.host // empty' <<<"$control_state")" ]] ||
  fail_preflight "Control origin does not match the App Service host."

control_health_status=$(curl --silent --show-error --max-time 20 \
  --output "$proof_output/control-health.json" \
  --write-out '%{http_code}' \
  "$ADMIN_UI_BASE_URL/health") ||
  fail_preflight "Control health probe failed."
[[ "$control_health_status" == "200" ]] || fail_preflight "Control health probe did not return HTTP 200."
jq -e \
  --arg image_id "$MANAGED_ELSA_PROOF_EXPECTED_CONTROL_IMAGE_ID" \
  --arg build_number "$MANAGED_ELSA_PROOF_EXPECTED_CONTROL_BUILD_NUMBER" \
  '.status == "ok" and .imageId == $image_id and .buildNumber == $build_number' \
  "$proof_output/control-health.json" >/dev/null || fail_preflight "Control health identity does not match the expected build."

runtime_health_status=$(curl --silent --show-error --max-time 20 \
  --output "$proof_output/runtime-health.txt" \
  --write-out '%{http_code}' \
  "$MANAGED_ELSA_PROOF_RUNTIME_ORIGIN/health") ||
  fail_preflight "Runtime health probe failed."
[[ "$runtime_health_status" == "200" ]] || fail_preflight "Runtime health probe did not return HTTP 200."
[[ "$(tr -d '\r\n' <"$proof_output/runtime-health.txt")" == "Healthy" ]] ||
  fail_preflight "Runtime health response was not Healthy."

MANAGED_ELSA_AZURE_BROWSER_PROOF=1 \
  MANAGED_ELSA_PROOF_STATE_LIFETIME_SECONDS="$MANAGED_ELSA_PROOF_STATE_LIFETIME_SECONDS" \
  npm --prefix "$repo_root/tests/Hosting/ElsaControl.Console.E2E" run e2e -- \
    managed-elsa-azure-browser-proof.spec.ts \
    --project=chromium \
    --headed \
    --reporter=line \
    --output="$proof_output"
