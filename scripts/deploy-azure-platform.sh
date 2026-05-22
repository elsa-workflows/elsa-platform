#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-dev}"
LOCATION="${LOCATION:-westeurope}"
RESOURCE_GROUP="${RESOURCE_GROUP:-}"
RESOURCE_GROUP_EXPLICIT=false
IMAGE_TAG="${IMAGE_TAG:-$(git rev-parse --short HEAD 2>/dev/null || date +%Y%m%d%H%M%S)}"
SQL_ADMINISTRATOR_LOGIN="${SQL_ADMINISTRATOR_LOGIN:-elsaadmin}"
ADMIN_API_KEY="${ADMIN_API_KEY:-}"
BUILDER_CLIENT_API_KEY="${BUILDER_CLIENT_API_KEY:-}"
SQL_ADMINISTRATOR_PASSWORD="${SQL_ADMINISTRATOR_PASSWORD:-}"
SUBSCRIPTION_ID="${AZURE_SUBSCRIPTION_ID:-}"
WHAT_IF=false

usage() {
  cat <<'USAGE'
Usage: scripts/deploy-azure-platform.sh [options]

Options:
  --environment <name>       Environment name. Default: dev.
  --resource-group <name>    Azure resource group. Default: rg-elsa-platform-<environment>.
  --location <name>          Azure region. Default: westeurope.
  --subscription <id>        Azure subscription ID. Can also use AZURE_SUBSCRIPTION_ID.
  --image-tag <tag>          Container image tag. Default: current git SHA.
  --what-if                  Preview the infrastructure deployment only.
  -h, --help                 Show this help.

Required environment variables:
  ADMIN_API_KEY
  SQL_ADMINISTRATOR_PASSWORD

Optional environment variables:
  BUILDER_CLIENT_API_KEY
  SQL_ADMINISTRATOR_LOGIN
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

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-elsa-platform-$ENVIRONMENT_NAME}"

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

if [[ "$WHAT_IF" != true ]]; then
  require_command docker
fi

require_secret ADMIN_API_KEY
require_secret SQL_ADMINISTRATOR_PASSWORD

if [[ -n "$SUBSCRIPTION_ID" ]]; then
  az account set --subscription "$SUBSCRIPTION_ID"
fi

echo "Ensuring resource group $RESOURCE_GROUP in $LOCATION."
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

COMMON_PARAMETERS=(
  environmentName="$ENVIRONMENT_NAME"
  location="$LOCATION"
  adminApiKey="$ADMIN_API_KEY"
  builderClientApiKey="$BUILDER_CLIENT_API_KEY"
  sqlAdministratorLogin="$SQL_ADMINISTRATOR_LOGIN"
  sqlAdministratorPassword="$SQL_ADMINISTRATOR_PASSWORD"
)

if [[ "$WHAT_IF" == true ]]; then
  az deployment group what-if \
    --resource-group "$RESOURCE_GROUP" \
    --template-file infra/main.bicep \
    --parameters "${COMMON_PARAMETERS[@]}"
  exit 0
fi

echo "Provisioning base infrastructure."
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/main.bicep \
  --parameters "${COMMON_PARAMETERS[@]}" \
  --query properties.outputs \
  --output json

ACR_LOGIN_SERVER="$(az deployment group show \
  --resource-group "$RESOURCE_GROUP" \
  --name main \
  --query properties.outputs.containerRegistryLoginServer.value \
  --output tsv)"

ACR_NAME="${ACR_LOGIN_SERVER%%.azurecr.io}"
IMAGE="$ACR_LOGIN_SERVER/elsa-platform/api:$IMAGE_TAG"

echo "Building $IMAGE."
az acr login --name "$ACR_NAME"
docker build \
  --file src/Elsa.Platform.PackageCatalog.Api/Dockerfile \
  --tag "$IMAGE" \
  .
docker push "$IMAGE"

echo "Deploying Web App image $IMAGE."
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/main.bicep \
  --parameters "${COMMON_PARAMETERS[@]}" containerImage="$IMAGE" \
  --query properties.outputs \
  --output json

WEB_URL="$(az deployment group show \
  --resource-group "$RESOURCE_GROUP" \
  --name main \
  --query properties.outputs.platformApiUrl.value \
  --output tsv)"

echo "Deployment completed: $WEB_URL"
