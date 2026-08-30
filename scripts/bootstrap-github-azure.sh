#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

GITHUB_ENVIRONMENT="${GITHUB_ENVIRONMENT:-production}"
AZURE_ENVIRONMENT="${AZURE_ENVIRONMENT:-}"
LOCATION="${AZURE_LOCATION:-westeurope}"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
SUBSCRIPTION_ID="${AZURE_SUBSCRIPTION_ID:-}"
APP_DISPLAY_NAME="${APP_DISPLAY_NAME:-}"
AZURE_CLIENT_ID="${AZURE_CLIENT_ID:-}"
SQL_ADMINISTRATOR_LOGIN="${SQL_ADMINISTRATOR_LOGIN:-elsaadmin}"
DRY_RUN=false
SKIP_ROLE_ASSIGNMENTS=false

usage() {
  cat <<'USAGE'
Usage: scripts/bootstrap-github-azure.sh [options]

Creates or updates a GitHub Actions environment that deploys to a matching
Azure resource group through OpenID Connect.

Options:
  --environment <name>        GitHub environment name. Default: production.
  --azure-environment <name>  Azure/Bicep environment name. Default: GitHub environment,
                              with development mapped to dev.
  --resource-group <name>     Azure resource group. Default: rg-elsa-control-<azure-env>.
  --location <name>           Azure region. Default: westeurope.
  --subscription <id>         Azure subscription ID. Default: current az account.
  --client-id <id>            Existing Entra app registration client ID to use.
  --app-display-name <name>   Entra app display name when creating/reusing OIDC app.
  --skip-role-assignments     Do not create Azure role assignments.
  --dry-run                   Print changes without writing Azure/GitHub state.
  -h, --help                  Show this help.

Optional environment variables used as GitHub environment secrets:
  ADMIN_API_KEY
  SQL_ADMINISTRATOR_PASSWORD
  BUILDER_CLIENT_API_KEY
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --environment)
      GITHUB_ENVIRONMENT="$2"
      shift 2
      ;;
    --azure-environment)
      AZURE_ENVIRONMENT="$2"
      shift 2
      ;;
    --resource-group)
      RESOURCE_GROUP="$2"
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
    --client-id)
      AZURE_CLIENT_ID="$2"
      shift 2
      ;;
    --app-display-name)
      APP_DISPLAY_NAME="$2"
      shift 2
      ;;
    --skip-role-assignments)
      SKIP_ROLE_ASSIGNMENTS=true
      shift
      ;;
    --dry-run)
      DRY_RUN=true
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

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

run() {
  if [[ "$DRY_RUN" == true ]]; then
    printf 'DRY RUN:'
    printf ' %q' "$@"
    printf '\n'
  else
    "$@"
  fi
}

set_github_var() {
  local name="$1"
  local value="$2"

  if [[ -z "$value" ]]; then
    echo "Cannot set $name because its value is empty." >&2
    exit 1
  fi

  echo "Setting GitHub variable $name in environment $GITHUB_ENVIRONMENT."
  run gh variable set "$name" --env "$GITHUB_ENVIRONMENT" --body "$value"
}

set_github_secret_if_present() {
  local name="$1"
  local value="${!name:-}"

  if [[ -z "$value" ]]; then
    echo "Skipping GitHub secret $name because it is not set locally."
    return
  fi

  echo "Setting GitHub secret $name in environment $GITHUB_ENVIRONMENT."
  if [[ "$DRY_RUN" == true ]]; then
    echo "DRY RUN: gh secret set $name --env $GITHUB_ENVIRONMENT --body <redacted>"
  else
    gh secret set "$name" --env "$GITHUB_ENVIRONMENT" --body "$value" >/dev/null
  fi
}

ensure_role_assignment() {
  local assignee="$1"
  local role="$2"
  local scope="$3"

  echo "Ensuring Azure role '$role' at $scope."
  if [[ "$DRY_RUN" == true ]]; then
    echo "DRY RUN: az role assignment create --assignee $assignee --role $role --scope $scope"
    return
  fi

  local existing
  existing="$(az role assignment list --assignee "$assignee" --role "$role" --all --query "[?scope=='$scope'] | [0].id" -o tsv 2>/dev/null || true)"
  if [[ -n "$existing" ]]; then
    return
  fi

  az role assignment create \
    --assignee "$assignee" \
    --role "$role" \
    --scope "$scope" \
    --only-show-errors \
    --output none || {
      echo "Warning: Could not create Azure role '$role' at $scope. It may already exist, or your account may not have role assignment permissions." >&2
    }
}

require_command az
require_command gh
require_command python3

if [[ "$DRY_RUN" != true ]]; then
  gh auth status >/dev/null
fi

if [[ -z "$SUBSCRIPTION_ID" ]]; then
  SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
fi

az account set --subscription "$SUBSCRIPTION_ID"
TENANT_ID="$(az account show --query tenantId -o tsv)"

if [[ -z "$AZURE_ENVIRONMENT" ]]; then
  if [[ "$GITHUB_ENVIRONMENT" == "development" ]]; then
    AZURE_ENVIRONMENT="dev"
  else
    AZURE_ENVIRONMENT="$GITHUB_ENVIRONMENT"
  fi
fi

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-elsa-control-$AZURE_ENVIRONMENT}"
APP_DISPLAY_NAME="${APP_DISPLAY_NAME:-elsa-control-$GITHUB_ENVIRONMENT-github-actions}"

REPO_FULL_NAME="$(gh repo view --json nameWithOwner --jq .nameWithOwner)"
SUBJECT="repo:$REPO_FULL_NAME:environment:$GITHUB_ENVIRONMENT"
ISSUER="https://token.actions.githubusercontent.com"

echo "Using GitHub environment: $GITHUB_ENVIRONMENT"
echo "Using Azure environment: $AZURE_ENVIRONMENT"
echo "Using resource group: $RESOURCE_GROUP"
echo "Using subscription: $SUBSCRIPTION_ID"

run gh api --method PUT "repos/:owner/:repo/environments/$GITHUB_ENVIRONMENT" >/dev/null

if [[ -z "$AZURE_CLIENT_ID" ]]; then
  AZURE_CLIENT_ID="$(az ad app list --display-name "$APP_DISPLAY_NAME" --query '[0].appId' -o tsv)"
fi

if [[ -z "$AZURE_CLIENT_ID" ]]; then
  echo "Creating Entra app registration $APP_DISPLAY_NAME."
  if [[ "$DRY_RUN" == true ]]; then
    AZURE_CLIENT_ID="00000000-0000-0000-0000-000000000000"
    echo "DRY RUN: az ad app create --display-name $APP_DISPLAY_NAME"
  else
    AZURE_CLIENT_ID="$(az ad app create --display-name "$APP_DISPLAY_NAME" --query appId -o tsv)"
  fi
else
  echo "Using Entra app registration client ID $AZURE_CLIENT_ID."
fi

APP_OBJECT_ID="$(az ad app show --id "$AZURE_CLIENT_ID" --query id -o tsv 2>/dev/null || true)"

if [[ "$DRY_RUN" != true ]]; then
  az ad sp create --id "$AZURE_CLIENT_ID" --only-show-errors --output none 2>/dev/null || true
fi

SERVICE_PRINCIPAL_OBJECT_ID="$(az ad sp show --id "$AZURE_CLIENT_ID" --query id -o tsv 2>/dev/null || true)"

if [[ -n "$APP_OBJECT_ID" ]]; then
  EXISTING_CREDENTIAL="$(az ad app federated-credential list --id "$APP_OBJECT_ID" --query "[?subject=='$SUBJECT'].id | [0]" -o tsv 2>/dev/null || true)"
  if [[ -z "$EXISTING_CREDENTIAL" ]]; then
    echo "Creating GitHub environment federated credential."
    if [[ "$DRY_RUN" == true ]]; then
      echo "DRY RUN: az ad app federated-credential create --id $APP_OBJECT_ID --subject $SUBJECT"
    else
      CREDENTIAL_FILE="$(mktemp)"
      trap 'rm -f "$CREDENTIAL_FILE"' EXIT
      python3 - "$CREDENTIAL_FILE" "$GITHUB_ENVIRONMENT" "$ISSUER" "$SUBJECT" <<'PY'
import json
import sys

path, environment, issuer, subject = sys.argv[1:]
credential = {
    "name": f"github-{environment}",
    "issuer": issuer,
    "subject": subject,
    "audiences": ["api://AzureADTokenExchange"],
}

with open(path, "w", encoding="utf-8") as handle:
    json.dump(credential, handle)
PY
      az ad app federated-credential create --id "$APP_OBJECT_ID" --parameters "@$CREDENTIAL_FILE" --only-show-errors --output none
    fi
  else
    echo "GitHub environment federated credential already exists."
  fi
fi

OUTPUTS="$(az deployment group show --resource-group "$RESOURCE_GROUP" --name main --query properties.outputs -o json 2>/dev/null || true)"
if [[ -z "$OUTPUTS" || "$OUTPUTS" == "null" ]]; then
  echo "Could not read deployment outputs from $RESOURCE_GROUP/main. Run an infra deployment first or create the resource group with scripts/deploy-azure-elsa-control.sh." >&2
  exit 1
fi

WEBAPP_NAME="$(python3 -c 'import json,sys; print(json.load(sys.stdin)["webAppName"]["value"])' <<<"$OUTPUTS")"
ACR_ENDPOINT="$(python3 -c 'import json,sys; print(json.load(sys.stdin)["containerRegistryLoginServer"]["value"])' <<<"$OUTPUTS")"
ACR_NAME="$(python3 -c 'import json,sys; print(json.load(sys.stdin)["containerRegistryName"]["value"])' <<<"$OUTPUTS")"

RESOURCE_GROUP_ID="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"
ACR_ID="$(az acr show --resource-group "$RESOURCE_GROUP" --name "$ACR_NAME" --query id -o tsv)"

if [[ "$SKIP_ROLE_ASSIGNMENTS" != true ]]; then
  ROLE_ASSIGNEE="${SERVICE_PRINCIPAL_OBJECT_ID:-$AZURE_CLIENT_ID}"
  ensure_role_assignment "$ROLE_ASSIGNEE" Contributor "$RESOURCE_GROUP_ID"
  ensure_role_assignment "$ROLE_ASSIGNEE" AcrPush "$ACR_ID"
fi

set_github_var AZURE_CLIENT_ID "$AZURE_CLIENT_ID"
set_github_var AZURE_TENANT_ID "$TENANT_ID"
set_github_var AZURE_SUBSCRIPTION_ID "$SUBSCRIPTION_ID"
set_github_var AZURE_ENV_NAME "$AZURE_ENVIRONMENT"
set_github_var AZURE_LOCATION "$LOCATION"
set_github_var AZURE_RESOURCE_GROUP "$RESOURCE_GROUP"
set_github_var AZURE_WEBAPP_NAME "$WEBAPP_NAME"
set_github_var AZURE_CONTAINER_REGISTRY_ENDPOINT "$ACR_ENDPOINT"
set_github_var SQL_ADMINISTRATOR_LOGIN "$SQL_ADMINISTRATOR_LOGIN"

set_github_secret_if_present ADMIN_API_KEY
set_github_secret_if_present SQL_ADMINISTRATOR_PASSWORD
set_github_secret_if_present BUILDER_CLIENT_API_KEY

echo "GitHub environment $GITHUB_ENVIRONMENT is configured for $RESOURCE_GROUP."
