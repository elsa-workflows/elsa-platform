#!/usr/bin/env bash
set -euo pipefail

KEYCLOAK_URL="${KEYCLOAK_URL:-}"
ELSA_CONTROL_API_URL="${ELSA_CONTROL_API_URL:-}"
KEYCLOAK_ADMIN_USERNAME="${KEYCLOAK_ADMIN_USERNAME:-keycloak-admin}"
KEYCLOAK_ADMIN_PASSWORD="${KEYCLOAK_ADMIN_PASSWORD:-}"
KEYCLOAK_REALM="${KEYCLOAK_REALM:-elsa-control}"
KEYCLOAK_CLIENT_ID="${KEYCLOAK_CLIENT_ID:-elsa-control-console}"
KEYCLOAK_CLIENT_SECRET="${KEYCLOAK_CLIENT_SECRET:-}"
ELSA_CONTROL_ADMIN_ROLE="${ELSA_CONTROL_ADMIN_ROLE:-control_admin}"
CREATE_DEV_USER="${CREATE_DEV_USER:-false}"
DEV_USERNAME="${DEV_USERNAME:-ada}"
DEV_PASSWORD="${DEV_PASSWORD:-password}"
DEV_EMAIL="${DEV_EMAIL:-ada@example.local}"

usage() {
  cat <<'USAGE'
Usage: scripts/bootstrap-keycloak-realm.sh

Required environment variables:
  KEYCLOAK_URL
  ELSA_CONTROL_API_URL
  KEYCLOAK_ADMIN_PASSWORD
  KEYCLOAK_CLIENT_SECRET

Optional environment variables:
  KEYCLOAK_ADMIN_USERNAME       Default: keycloak-admin
  KEYCLOAK_REALM                Default: elsa-control
  KEYCLOAK_CLIENT_ID            Default: elsa-control-console
  ELSA_CONTROL_ADMIN_ROLE           Default: control_admin
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
require_value ELSA_CONTROL_API_URL
require_value KEYCLOAK_ADMIN_PASSWORD
require_value KEYCLOAK_CLIENT_SECRET

KEYCLOAK_URL="${KEYCLOAK_URL%/}"
ELSA_CONTROL_API_URL="${ELSA_CONTROL_API_URL%/}"

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

TOKEN="$(curl -fsS -X POST "$KEYCLOAK_URL/realms/master/protocol/openid-connect/token" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode 'grant_type=password' \
  --data-urlencode 'client_id=admin-cli' \
  --data-urlencode "username=$KEYCLOAK_ADMIN_USERNAME" \
  --data-urlencode "password=$KEYCLOAK_ADMIN_PASSWORD" | jq -r '.access_token')"

realm_check_file="$tmp_dir/realm-check.out"
realm_payload_file="$tmp_dir/realm.json"
role_check_file="$tmp_dir/role-check.out"
role_payload_file="$tmp_dir/elsa-control-admin-role.json"
client_payload_file="$tmp_dir/client.json"
role_mapper_payload_file="$tmp_dir/role-mapper.json"
user_payload_file="$tmp_dir/user.json"
password_payload_file="$tmp_dir/password.json"
dev_user_role_payload_file="$tmp_dir/dev-user-role.json"

realm_status="$(curl -sS -o "$realm_check_file" -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" \
  "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM")"

if [[ "$realm_status" == "404" ]]; then
  jq -n --arg realm "$KEYCLOAK_REALM" \
    '{realm:$realm, enabled:true, registrationAllowed:false, loginWithEmailAllowed:true}' \
    > "$realm_payload_file"
  curl -fsS -X POST "$KEYCLOAK_URL/admin/realms" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @"$realm_payload_file"
  echo "Created realm $KEYCLOAK_REALM."
elif [[ "$realm_status" == "200" ]]; then
  echo "Realm $KEYCLOAK_REALM already exists."
else
  echo "Unexpected realm lookup status $realm_status." >&2
  cat "$realm_check_file" >&2
  exit 1
fi

role_status="$(curl -sS -o "$role_check_file" -w '%{http_code}' \
  -H "Authorization: Bearer $TOKEN" \
  "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/roles/$ELSA_CONTROL_ADMIN_ROLE")"
if [[ "$role_status" == "404" ]]; then
  jq -n --arg name "$ELSA_CONTROL_ADMIN_ROLE" \
    '{name:$name, description:"Grants access to Elsa Control administration surfaces."}' \
    > "$role_payload_file"
  curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/roles" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @"$role_payload_file"
  echo "Created realm role $ELSA_CONTROL_ADMIN_ROLE."
elif [[ "$role_status" == "200" ]]; then
  echo "Realm role $ELSA_CONTROL_ADMIN_ROLE already exists."
else
  echo "Unexpected role lookup status $role_status." >&2
  cat "$role_check_file" >&2
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
  --arg apiUrl "$ELSA_CONTROL_API_URL" \
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
  > "$client_payload_file"

if [[ -z "$client_uuid" ]]; then
  curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @"$client_payload_file"
  echo "Created client $KEYCLOAK_CLIENT_ID."
else
  curl -fsS -X PUT "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients/$client_uuid" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @"$client_payload_file"
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
  }' > "$role_mapper_payload_file"

if [[ -z "$role_mapper_id" ]]; then
  curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients/$client_uuid/protocol-mappers/models" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @"$role_mapper_payload_file"
  echo "Created realm role claim mapper."
else
  curl -fsS -X PUT "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/clients/$client_uuid/protocol-mappers/models/$role_mapper_id" \
    -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    --data @"$role_mapper_payload_file"
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
      > "$user_payload_file"
    curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users" \
      -H "Authorization: Bearer $TOKEN" \
      -H 'Content-Type: application/json' \
      --data @"$user_payload_file"
    user_id="$(curl -fsS \
      -H "Authorization: Bearer $TOKEN" \
      "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users?username=$DEV_USERNAME&exact=true" \
      | jq -r '.[0].id')"
    jq -n --arg password "$DEV_PASSWORD" \
      '{type:"password", temporary:false, value:$password}' \
      > "$password_payload_file"
    curl -fsS -X PUT "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users/$user_id/reset-password" \
      -H "Authorization: Bearer $TOKEN" \
      -H 'Content-Type: application/json' \
      --data @"$password_payload_file"
    echo "Created dev user $DEV_USERNAME."
  else
    echo "Dev user $DEV_USERNAME already exists."
  fi

  role="$(curl -fsS \
    -H "Authorization: Bearer $TOKEN" \
    "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/roles/$ELSA_CONTROL_ADMIN_ROLE")"
  has_role="$(curl -fsS \
    -H "Authorization: Bearer $TOKEN" \
    "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users/$user_id/role-mappings/realm" \
    | jq -r --arg role "$ELSA_CONTROL_ADMIN_ROLE" 'any(.[]; .name == $role)')"
  if [[ "$has_role" == "true" ]]; then
    echo "$DEV_USERNAME already has $ELSA_CONTROL_ADMIN_ROLE."
  else
    printf '[%s]' "$role" > "$dev_user_role_payload_file"
    curl -fsS -X POST "$KEYCLOAK_URL/admin/realms/$KEYCLOAK_REALM/users/$user_id/role-mappings/realm" \
      -H "Authorization: Bearer $TOKEN" \
      -H 'Content-Type: application/json' \
      --data @"$dev_user_role_payload_file"
    echo "Assigned $ELSA_CONTROL_ADMIN_ROLE to $DEV_USERNAME."
  fi
fi

curl -fsS "$KEYCLOAK_URL/realms/$KEYCLOAK_REALM/.well-known/openid-configuration" \
  | jq -r '.issuer'
