#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: scripts/validate-api-provider-image.sh <image>" >&2
}

fail() {
  echo "API provider image smoke check failed: $1" >&2
  exit 1
}

if (($# != 1)) || [[ -z "$1" ]]; then
  usage
  exit 2
fi

image="$1"
command -v docker >/dev/null 2>&1 || fail "docker is unavailable"
command -v openssl >/dev/null 2>&1 || fail "openssl is unavailable"

Authentication__ApiKey="$(openssl rand -hex 32 2>/dev/null)" || fail "could not generate an ephemeral API key"
[[ "$Authentication__ApiKey" =~ ^[[:xdigit:]]{64}$ ]] || fail "generated API key had an unexpected format"
export Authentication__ApiKey

container_id=""
# shellcheck disable=SC2329 # cleanup is invoked indirectly by the EXIT trap.
cleanup() {
  if [[ -n "$container_id" ]]; then
    if ! docker rm --force "$container_id" >/dev/null 2>&1; then
      echo "API provider image smoke cleanup failed; the test container was retained." >&2
      exit 1
    fi
  fi
}
trap cleanup EXIT

if ! created_id="$(docker create \
  --network none \
  --env ASPNETCORE_ENVIRONMENT=Production \
  --env Database__Provider=Sqlite \
  --env 'ConnectionStrings__Catalog=Data Source=/tmp/elsa-image-smoke.db' \
  --env DataProtection__KeysPath=/tmp/elsa-image-smoke-keys \
  --env Authentication__ApiKey \
  "$image" 2>/dev/null)"; then
  fail "container could not be created"
fi
[[ "$created_id" =~ ^[a-f0-9]{64}$ ]] || fail "container creation returned an invalid identifier"
container_id="$created_id"
unset Authentication__ApiKey

if ! docker start "$container_id" >/dev/null 2>&1; then
  fail "container failed to start"
fi

deadline=$((SECONDS + 60))
while ((SECONDS < deadline)); do
  state=""
  if ! state="$(docker inspect --format '{{.State.Status}}' "$container_id" 2>/dev/null)"; then
    fail "container state could not be inspected"
  fi

  case "$state" in
    running)
      response=""
      if response="$(docker exec "$container_id" curl --fail --silent --max-time 2 http://127.0.0.1:8080/health 2>/dev/null)" &&
        [[ "$response" =~ \"status\"[[:space:]]*:[[:space:]]*\"ok\" ]]; then
        echo "API provider image smoke check passed."
        exit 0
      fi
      ;;
    created|restarting)
      ;;
    exited|dead|removing)
      fail "container exited before /health became ready"
      ;;
    *)
      fail "container entered an unexpected state"
      ;;
  esac

  sleep 1
done

fail "container did not return a healthy /health response within 60 seconds"
