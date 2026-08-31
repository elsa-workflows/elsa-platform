#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-dev}"
LOCATION="${LOCATION:-westeurope}"
RESOURCE_GROUP="${RESOURCE_GROUP:-}"
RESOURCE_GROUP_EXPLICIT=false
IMAGE_TAG="${IMAGE_TAG:-$(git rev-parse --short HEAD 2>/dev/null || date +%Y%m%d%H%M%S)}"
DOCKER_PLATFORM="${DOCKER_PLATFORM:-linux/amd64}"
SQL_ADMINISTRATOR_LOGIN="${SQL_ADMINISTRATOR_LOGIN:-elsaadmin}"
ADMIN_API_KEY="${ADMIN_API_KEY:-}"
BUILDER_CLIENT_API_KEY="${BUILDER_CLIENT_API_KEY:-}"
SQL_ADMINISTRATOR_PASSWORD="${SQL_ADMINISTRATOR_PASSWORD:-}"
DEPLOY_KEYCLOAK="${DEPLOY_KEYCLOAK:-false}"
KEYCLOAK_ADMIN_PASSWORD="${KEYCLOAK_ADMIN_PASSWORD:-}"
KEYCLOAK_POSTGRES_ADMINISTRATOR_PASSWORD="${KEYCLOAK_POSTGRES_ADMINISTRATOR_PASSWORD:-}"
KEYCLOAK_CLIENT_SECRET="${KEYCLOAK_CLIENT_SECRET:-}"
KEYCLOAK_REALM="${KEYCLOAK_REALM:-elsa-control}"
KEYCLOAK_CLIENT_ID="${KEYCLOAK_CLIENT_ID:-elsa-control-console}"
KEYCLOAK_START_COMMAND="${KEYCLOAK_START_COMMAND:-}"
SUBSCRIPTION_ID="${AZURE_SUBSCRIPTION_ID:-}"
WHAT_IF=false

usage() {
  cat <<'USAGE'
Usage: scripts/deploy-azure-elsa-control.sh [options]

Options:
  --environment <name>       Environment name. Default: dev.
  --resource-group <name>    Azure resource group. Default: rg-elsa-control-<environment>.
  --location <name>          Azure region. Default: westeurope.
  --subscription <id>        Azure subscription ID. Can also use AZURE_SUBSCRIPTION_ID.
  --image-tag <tag>          Container image tag. Default: current git SHA.
  --docker-platform <value>  Docker target platform. Default: linux/amd64.
  --deploy-keycloak          Provision/update the optional Keycloak identity stack.
  --what-if                  Preview the infrastructure deployment only.
  -h, --help                 Show this help.

Required environment variables:
  ADMIN_API_KEY
  SQL_ADMINISTRATOR_PASSWORD

Optional environment variables:
  BUILDER_CLIENT_API_KEY
  DEPLOY_KEYCLOAK
  KEYCLOAK_ADMIN_PASSWORD
  KEYCLOAK_POSTGRES_ADMINISTRATOR_PASSWORD
  KEYCLOAK_CLIENT_SECRET
  KEYCLOAK_REALM
  KEYCLOAK_CLIENT_ID
  KEYCLOAK_START_COMMAND
  SQL_ADMINISTRATOR_LOGIN
  DOCKER_PLATFORM
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --environment)
      ENVIRONMENT_NAME="$2"
      if [[ "$RESOURCE_GROUP_EXPLICIT" != true ]]; then
        RESOURCE_GROUP=""
      fi
      shift 2
      ;;
    --resource-group)
      RESOURCE_GROUP="$2"
      RESOURCE_GROUP_EXPLICIT=true
      shift 2
      ;;
    --location)
      LOCATION="$2"
      shift 2
      ;;
    --subscription)
      SUBSCRIPTION_ID="$2"
      shift 2
      ;;
    --image-tag)
      IMAGE_TAG="$2"
      shift 2
      ;;
    --docker-platform)
      DOCKER_PLATFORM="$2"
      shift 2
      ;;
    --deploy-keycloak)
      DEPLOY_KEYCLOAK=true
      shift
      ;;
    --what-if)
      WHAT_IF=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-elsa-control-$ENVIRONMENT_NAME}"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require_secret() {
  if [[ -z "${!1:-}" ]]; then
    echo "$1 is required." >&2
    exit 1
  fi
}

require_command az
require_command python3

if [[ "$WHAT_IF" != true ]]; then
  require_command docker
fi

require_secret ADMIN_API_KEY
require_secret SQL_ADMINISTRATOR_PASSWORD
if [[ "$DEPLOY_KEYCLOAK" == true ]]; then
  require_secret KEYCLOAK_ADMIN_PASSWORD
  require_secret KEYCLOAK_POSTGRES_ADMINISTRATOR_PASSWORD
  require_secret KEYCLOAK_CLIENT_SECRET
fi

if [[ -n "$SUBSCRIPTION_ID" ]]; then
  az account set --subscription "$SUBSCRIPTION_ID"
fi

echo "Ensuring resource group $RESOURCE_GROUP in $LOCATION."
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

PARAMETERS_FILE="$(mktemp)"
IMAGE_PARAMETERS_FILE=""

cleanup() {
  rm -f "$PARAMETERS_FILE"
  if [[ -n "$IMAGE_PARAMETERS_FILE" ]]; then
    rm -f "$IMAGE_PARAMETERS_FILE"
  fi
}

trap cleanup EXIT

write_parameters_file() {
  local file_path="$1"
  local container_image="${2:-}"

  ENVIRONMENT_NAME="$ENVIRONMENT_NAME" \
  LOCATION="$LOCATION" \
  ADMIN_API_KEY="$ADMIN_API_KEY" \
  BUILDER_CLIENT_API_KEY="$BUILDER_CLIENT_API_KEY" \
  SQL_ADMINISTRATOR_LOGIN="$SQL_ADMINISTRATOR_LOGIN" \
  SQL_ADMINISTRATOR_PASSWORD="$SQL_ADMINISTRATOR_PASSWORD" \
  DEPLOY_KEYCLOAK="$DEPLOY_KEYCLOAK" \
  KEYCLOAK_ADMIN_PASSWORD="$KEYCLOAK_ADMIN_PASSWORD" \
  KEYCLOAK_POSTGRES_ADMINISTRATOR_PASSWORD="$KEYCLOAK_POSTGRES_ADMINISTRATOR_PASSWORD" \
  KEYCLOAK_CLIENT_SECRET="$KEYCLOAK_CLIENT_SECRET" \
  KEYCLOAK_REALM="$KEYCLOAK_REALM" \
  KEYCLOAK_CLIENT_ID="$KEYCLOAK_CLIENT_ID" \
  KEYCLOAK_START_COMMAND="$KEYCLOAK_START_COMMAND" \
  CONTAINER_IMAGE="$container_image" \
  python3 - "$file_path" <<'PY'
import json
import os
import sys

parameters = {
    "environmentName": os.environ["ENVIRONMENT_NAME"],
    "location": os.environ["LOCATION"],
    "adminApiKey": os.environ["ADMIN_API_KEY"],
    "builderClientApiKey": os.environ["BUILDER_CLIENT_API_KEY"],
    "sqlAdministratorLogin": os.environ["SQL_ADMINISTRATOR_LOGIN"],
    "sqlAdministratorPassword": os.environ["SQL_ADMINISTRATOR_PASSWORD"],
    "deployKeycloak": os.environ["DEPLOY_KEYCLOAK"].lower() == "true",
}

if parameters["deployKeycloak"]:
    parameters.update({
        "keycloakAdminPassword": os.environ["KEYCLOAK_ADMIN_PASSWORD"],
        "keycloakPostgresAdministratorPassword": os.environ["KEYCLOAK_POSTGRES_ADMINISTRATOR_PASSWORD"],
        "keycloakClientSecret": os.environ["KEYCLOAK_CLIENT_SECRET"],
        "keycloakRealm": os.environ["KEYCLOAK_REALM"],
        "keycloakClientId": os.environ["KEYCLOAK_CLIENT_ID"],
    })
    keycloak_start_command = os.environ["KEYCLOAK_START_COMMAND"]
    if keycloak_start_command:
        parameters["keycloakStartCommand"] = keycloak_start_command

container_image = os.environ["CONTAINER_IMAGE"]
if container_image:
    parameters["containerImage"] = container_image

with open(sys.argv[1], "w", encoding="utf-8") as parameters_file:
    json.dump({"parameters": {key: {"value": value} for key, value in parameters.items()}}, parameters_file)
PY

  chmod 600 "$file_path"
}

write_parameters_file "$PARAMETERS_FILE"

if [[ "$WHAT_IF" == true ]]; then
  az deployment group what-if \
    --resource-group "$RESOURCE_GROUP" \
    --template-file infra/main.bicep \
    --parameters "@$PARAMETERS_FILE"
  exit 0
fi

echo "Provisioning base infrastructure."
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/main.bicep \
  --parameters "@$PARAMETERS_FILE" \
  --query properties.outputs \
  --output json

ACR_LOGIN_SERVER="$(az deployment group show \
  --resource-group "$RESOURCE_GROUP" \
  --name main \
  --query properties.outputs.containerRegistryLoginServer.value \
  --output tsv)"

ACR_NAME="${ACR_LOGIN_SERVER%%.azurecr.io}"
IMAGE="$ACR_LOGIN_SERVER/elsa-control/api:$IMAGE_TAG"

echo "Building $IMAGE."
az acr login --name "$ACR_NAME"
docker build \
  --platform "$DOCKER_PLATFORM" \
  --build-arg ELSA_CONTROL_IMAGE_ID="$IMAGE_TAG" \
  --file src/Hosting/ElsaControl.Api/Dockerfile \
  --tag "$IMAGE" \
  .
docker push "$IMAGE"

IMAGE_PARAMETERS_FILE="$(mktemp)"
write_parameters_file "$IMAGE_PARAMETERS_FILE" "$IMAGE"

echo "Deploying Web App image $IMAGE."
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/main.bicep \
  --parameters "@$IMAGE_PARAMETERS_FILE" \
  --query properties.outputs \
  --output json

WEB_URL="$(az deployment group show \
  --resource-group "$RESOURCE_GROUP" \
  --name main \
  --query properties.outputs.controlApiUrl.value \
  --output tsv)"

echo "Deployment completed: $WEB_URL"
