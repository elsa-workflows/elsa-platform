#!/usr/bin/env bash
set -euo pipefail

subject="${1:-user-123}"
name="${2:-Ada Lovelace}"
email="${3:-ada@example.test}"

issuer="${PLATFORM_IDENTITY_ISSUER:-https://local.elsa-platform.test}"
audience="${PLATFORM_IDENTITY_AUDIENCE:-elsa-platform-dev}"
signing_key="${PLATFORM_IDENTITY_SIGNING_KEY:-local-development-platform-identity-signing-key-change-me}"
ttl_seconds="${PLATFORM_IDENTITY_TOKEN_TTL_SECONDS:-3600}"

now="$(date +%s)"
expires="$((now + ttl_seconds))"

base64url() {
  openssl base64 -A | tr '+/' '-_' | tr -d '='
}

json_escape() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//$'\n'/\\n}"
  printf '%s' "$value"
}

header='{"alg":"HS256","typ":"JWT"}'
payload="$(printf '{"iss":"%s","aud":"%s","sub":"%s","name":"%s","email":"%s","nbf":%s,"iat":%s,"exp":%s}' \
  "$(json_escape "$issuer")" \
  "$(json_escape "$audience")" \
  "$(json_escape "$subject")" \
  "$(json_escape "$name")" \
  "$(json_escape "$email")" \
  "$((now - 60))" \
  "$now" \
  "$expires")"

encoded_header="$(printf '%s' "$header" | base64url)"
encoded_payload="$(printf '%s' "$payload" | base64url)"
signature="$(printf '%s' "$encoded_header.$encoded_payload" \
  | openssl dgst -sha256 -hmac "$signing_key" -binary \
  | base64url)"

printf '%s.%s.%s\n' "$encoded_header" "$encoded_payload" "$signature"
