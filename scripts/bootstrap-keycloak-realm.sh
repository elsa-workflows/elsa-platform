#!/usr/bin/env bash
set -euo pipefail

KEYCLOAK_URL="${KEYCLOAK_URL:-}"
PLATFORM_API_URL="${PLATFORM_API_URL:-}"
KEYCLOAK_ADMIN_USERNAME="${KEYCLOAK_ADMIN_USERNAME:-keycloak-admin}"
KEYCLOAK_ADMIN_PASSWORD="${KEYCLOAK_ADMIN_PASSWORD:-}"
KEYCLOAK_REALM="${KEYCLOAK_REALM:-elsa-platform}"
KEYCLOAK_CLIENT_ID="${KEYCLOAK_CLIENT_ID:-elsa-platform-console}"
KEYCLOAK_CLIENT_SECRET="${KEYCLOAK_CLIENT_SECRET:-}"
PLATFORM_ADMIN_ROLE="${PLATFORM_ADMIN_ROLE:-platform_admin}"
CREATE_DEV_USER="${CREATE_DEV_USER:-false}"
DEV_USERNAME="${DEV_USERNAME:-ada}"
DEV_PASSWORD="${DEV_PASSWORD:-password}"
DEV_EMAIL="${DEV_EMAIL:-ada@example.local}"

usage() {
  cat <<'USAGE'
Usage: scripts/bootstrap-keycloak-realm.sh

Required environment variables:
  KEYCLOAK_URL
  PLATFORM_API_URL
  KEYCLOAK_ADMIN_PASSWORD
  KEYCLOAK_CLIENT_SECRET

Optional environment variables:
  KEYCLOAK_ADMIN_USERNAME       Default: keycloak-admin
  KEYCLOAK_REALM                Default: elsa-platform
  KEYCLOAK_CLIENT_ID            Default: elsa-platform-console
  PLATFORM_ADMIN_ROLE           Default: platform_admin
  CREATE_DEV_USER               Default: false
  DEV_USERNAME                  Default: ada
  DEV_PASSWORD                  Default: password
  DEV_EMAIL                     Default: ada@example.local
USAGE
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require_value() {
  if [[ -z "${!1:-}" ]]; then
    echo "$1 is required." >&2
    usage >&2
    exit 1
  fi
}

require_command curl
require_command jq
require_value KEYCLOAK_URL
require_value PLATFORM_API_URL
require_value KEYCLOAK_ADMIN_PASSWORD
require_value KEYCLOAK_CLIENT_SECRET

KEYCLOAK_URL="${KEYCLOAK_URL%/}"
PLATFORM_API_URL="${PLATFORM_API_URL%/}"

TOKEN="$(curl -fsS -X POST "$KEYCLOAK_URL/realms/master/protocol/openid-connect/token" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode 'grant_type=password' \
  --data-urlencode 'client_id=admin-cli' \
  --data-urlencode "username=$KEYCLOAK_ADMIN_USERNAME" \
  --data-urlencode "password=$KEYCLOAK_ADMIN_PASSWORD" | jq -r '.access_token')"

realm_status="$(curl -sS -o /tmp/elsa-keycloak-realm-check.out -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" \
  "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM")"

if [[ "$realm_status" == "404" ]]; then
  jq -n --arg realm "$KEYCLOAK_REALM" \
    '{realm:$realm, enabled:true, registrationAllowed:false, loginWithEmailAllowed:true}' \
    > /tmp/elsa-keycloak-realm.json
  curl -fsS -X POST "$KEYCLOAK_URL/admin/realms" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @/tmp/elsa-keycloak-realm.json
  echo "Created realm $KEYCLOAK_REALM."
elif [[ "$realm_status" == "200" ]]; then
  echo "Realm $KEYCLOAK_REALM already exists."
else
  echo "Unexpected realm lookup status $realm_status." >&2
  cat /tmp/elsa-keycloak-realm-check.out >&2
  exit 1
fi

role_status="$(curl -sS -o /tmp/elsa-keycloak-role-check.out -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" \
  "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/roles/$PLATFORM_ADMIN_ROLE")"
if [[ "$role_status" == "404" ]]; then
  jq -n --arg name "$PLATFORM_ADMIN_ROLE" \
    '{name:$name, description:"Grants access to Elsa Platform administration surfaces."}' \
    > /tmp/elsa-keycloak-platform-admin-role.json
  curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/roles" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @/tmp/elsa-keycloak-platform-admin-role.json
  echo "Created realm role $PLATFORM_ADMIN_ROLE."
elif [[ "$role_status" == "200" ]]; then
  echo "Realm role $PLATFORM_ADMIN_ROLE already exists."
else
  echo "Unexpected role lookup status $role_status." >&2
  cat /tmp/elsa-keycloak-role-check.out >&2
  exit 1
fi

client_uuid="$(curl -fsS \
  -H "Authorization: Bearer $TOKEN" \
  "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients?clientId=$KEYCLOAK_CLIENT_ID" \
  | jq -r '.[0].id // empty')"

jq -n \
  --arg id "$client_uuid" \
  --arg clientId "$KEYCLOAK_CLIENT_ID" \
  --arg secret "$KEYCLOAK_CLIENT_SECRET" \
  --arg apiUrl "$PLATFORM_API_URL" \
  '{
    id: (if $id == "" then null else $id end),
    clientId: $clientId,
    enabled: true,
    protocol: "openid-connect",
    publicClient: false,
    serviceAccountsEnabled: false,
    standardFlowEnabled: true,
    directAccessGrantsEnabled: false,
    secret: $secret,
    redirectUris: [($apiUrl + "/api/auth/callback")],
    webOrigins: [$apiUrl],
    attributes: {
      "pkce.code.challenge.method": "S256",
      "post.logout.redirect.uris": ($apiUrl + "/admin/*")
    }
  } | with_entries(select(.value != null))' \
  > /tmp/elsa-keycloak-client.json

if [[ -z "$client_uuid" ]]; then
  curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @/tmp/elsa-keycloak-client.json
  echo "Created client $KEYCLOAK_CLIENT_ID."
else
  curl -fsS -X PUT "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients/$client_uuid" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @/tmp/elsa-keycloak-client.json
  echo "Updated client $KEYCLOAK_CLIENT_ID."
fi

client_uuid="$(curl -fsS \
  -H "Authorization: Bearer $TOKEN" \
  "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients?clientId=$KEYCLOAK_CLIENT_ID" \
  | jq -r '.[0].id')"

role_mapper_id="$(curl -fsS \
  -H "Authorization: Bearer $TOKEN" \
  "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients/$client_uuid/protocol-mappers/models" \
  | jq -r '.[] | select(.name == "realm roles as role claim") | .id')"

jq -n \
  '{
    name: "realm roles as role claim",
    protocol: "openid-connect",
    protocolMapper: "oidc-usermodel-realm-role-mapper",
    consentRequired: false,
    config: {
      "multivalued": "true",
      "userinfo.token.claim": "true",
      "id.token.claim": "true",
      "access.token.claim": "true",
      "claim.name": "role",
      "jsonType.label": "String"
    }
  }' > /tmp/elsa-keycloak-role-mapper.json

if [[ -z "$role_mapper_id" ]]; then
  curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients/$client_uuid/protocol-mappers/models" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @/tmp/elsa-keycloak-role-mapper.json
  echo "Created realm role claim mapper."
else
  curl -fsS -X PUT "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients/$client_uuid/protocol-mappers/models/$role_mapper_id" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @/tmp/elsa-keycloak-role-mapper.json
  echo "Updated realm role claim mapper."
fi

if [[ "$CREATE_DEV_USER" == "true" ]]; then
  user_id="$(curl -fsS \
    -H "Authorization: Bearer $TOKEN" \
    "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users?username=$DEV_USERNAME&exact=true" \
    | jq -r '.[0].id // empty')"

  if [[ -z "$user_id" ]]; then
    jq -n \
      --arg username "$DEV_USERNAME" \
      --arg email "$DEV_EMAIL" \
      '{username:$username, email:$email, enabled:true, emailVerified:true}' \
      > /tmp/elsa-keycloak-user.json
    curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users" \
      -H "Authorization: Bearer $TOKEN" \
      -H 'Content-Type: application/json' \
      --data @/tmp/elsa-keycloak-user.json
    user_id="$(curl -fsS \
      -H "Authorization: Bearer $TOKEN" \
      "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users?username=$DEV_USERNAME&exact=true" \
      | jq -r '.[0].id')"
    jq -n --arg password "$DEV_PASSWORD" \
      '{type:"password", temporary:false, value:$password}' \
      > /tmp/elsa-keycloak-password.json
    curl -fsS -X PUT "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users/$user_id/reset-password" \
      -H "Authorization: Bearer $TOKEN" \
      -H 'Content-Type: application/json' \
      --data @/tmp/elsa-keycloak-password.json
    echo "Created dev user $DEV_USERNAME."
  else
    echo "Dev user $DEV_USERNAME already exists."
  fi

  role="$(curl -fsS \
    -H "Authorization: Bearer $TOKEN" \
    "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/roles/$PLATFORM_ADMIN_ROLE")"
  has_role="$(curl -fsS \
    -H "Authorization: Bearer $TOKEN" \
    "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users/$user_id/role-mappings/realm" \
    | jq -r --arg role "$PLATFORM_ADMIN_ROLE" 'any(.[]; .name == $role)')"
  if [[ "$has_role" == "true" ]]; then
    echo "$DEV_USERNAME already has $PLATFORM_ADMIN_ROLE."
  else
    printf '[%s]' "$role" > /tmp/elsa-keycloak-dev-user-role.json
    curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users/$user_id/role-mappings/realm" \
      -H "Authorization: Bearer $TOKEN" \
      -H 'Content-Type: application/json' \
      --data @/tmp/elsa-keycloak-dev-user-role.json
    echo "Assigned $PLATFORM_ADMIN_ROLE to $DEV_USERNAME."
  fi
fi

curl -fsS "$KEYCLOAK_URL/realms/$KEYCLOAK_REALM/.well-known/openid-configuration" \
  | jq -r '.issuer'
