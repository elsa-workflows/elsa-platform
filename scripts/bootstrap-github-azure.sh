#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

GITHUB_ENVIRONMENT="${GITHUB_ENVIRONMENT:-production}"
REMOTE_NAME="${REMOTE_NAME:-origin}"
RUN_PIPELINE_CONFIG="${RUN_PIPELINE_CONFIG:-true}"
DRY_RUN="${DRY_RUN:-false}"
AZD_ENVIRONMENT="${AZD_ENVIRONMENT:-}"

usage() {
  cat <<'USAGE'
Usage: scripts/bootstrap-github-azure.sh [options]

Configures the GitHub environment used by the Azure Platform API Deploy workflow.

Options:
  --environment <name>       GitHub environment name. Default: production.
  --azd-environment <name>   azd environment name. Default: current azd env.
  --remote-name <name>       Git remote used by azd pipeline config. Default: origin.
  --skip-pipeline-config     Do not run azd pipeline config.
  --dry-run                  Print actions without changing Azure/GitHub.
  -h, --help                 Show this help.

Environment:
  ADMIN_API_KEY              Required unless already present as AZURE_ADMIN_API_KEY
                             in the selected azd environment.
  AZURE_CLIENT_ID            Optional fallback if azd pipeline config has not set
                             the GitHub environment variable yet.
  AZURE_RESOURCE_GROUP       Optional override for the resource group.
  AZURE_WEBAPP_NAME          Optional override for the API App Service name.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --environment)
      GITHUB_ENVIRONMENT="$2"
      shift 2
      ;;
    --azd-environment)
      AZD_ENVIRONMENT="$2"
      shift 2
      ;;
    --remote-name)
      REMOTE_NAME="$2"
      shift 2
      ;;
    --skip-pipeline-config)
      RUN_PIPELINE_CONFIG=false
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

azd_get() {
  local name="$1"
  local value
  if value="$(azd env get-value "$name" -e "$AZD_ENVIRONMENT" 2>/dev/null)"; then
    printf '%s\n' "$value"
  fi
}

gh_env_var() {
  local name="$1"
  gh variable list --env "$GITHUB_ENVIRONMENT" \
    | awk -v key="$name" '$1 == key { print $2; found = 1 } END { if (!found) exit 1 }' 2>/dev/null || true
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

set_github_secret() {
  local name="$1"
  local value="$2"

  if [[ -z "$value" ]]; then
    echo "Cannot set $name because its value is empty." >&2
    exit 1
  fi

  echo "Setting GitHub secret $name in environment $GITHUB_ENVIRONMENT."
  if [[ "$DRY_RUN" == true ]]; then
    echo "DRY RUN: gh secret set $name --env $GITHUB_ENVIRONMENT --body <redacted>"
  else
    gh secret set "$name" --env "$GITHUB_ENVIRONMENT" --body "$value" >/dev/null
  fi
}

require_command az
require_command azd
require_command gh
require_command awk

if [[ -z "$AZD_ENVIRONMENT" ]]; then
  AZD_ENVIRONMENT="$(azd_get AZURE_ENV_NAME)"
fi

if [[ -z "$AZD_ENVIRONMENT" ]]; then
  echo "Could not determine azd environment. Pass --azd-environment <name>." >&2
  exit 1
fi

if [[ "$DRY_RUN" != true ]]; then
  gh auth status >/dev/null
fi

echo "Using azd environment: $AZD_ENVIRONMENT"
echo "Using GitHub environment: $GITHUB_ENVIRONMENT"

run gh api --method PUT "repos/:owner/:repo/environments/$GITHUB_ENVIRONMENT" >/dev/null

if [[ "$RUN_PIPELINE_CONFIG" == true ]]; then
  run azd pipeline config \
    --provider github \
    --auth-type federated \
    --environment "$AZD_ENVIRONMENT" \
    --remote-name "$REMOTE_NAME" \
    --no-prompt
fi

AZURE_SUBSCRIPTION_ID="${AZURE_SUBSCRIPTION_ID:-$(azd_get AZURE_SUBSCRIPTION_ID)}"
AZURE_LOCATION="${AZURE_LOCATION:-$(azd_get AZURE_LOCATION)}"
AZURE_TENANT_ID="${AZURE_TENANT_ID:-$(az account show --query tenantId -o tsv)}"
AZURE_CLIENT_ID="${AZURE_CLIENT_ID:-$(gh_env_var AZURE_CLIENT_ID)}"
AZURE_RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-$(azd_get AZURE_RESOURCE_GROUP)}"
AZURE_RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-rg-$AZD_ENVIRONMENT}"
AZURE_WEBAPP_NAME="${AZURE_WEBAPP_NAME:-$(az webapp list --resource-group "$AZURE_RESOURCE_GROUP" --query "[?starts_with(name, 'api-')].name | [0]" -o tsv)}"

ADMIN_API_KEY="${ADMIN_API_KEY:-$(azd_get AZURE_ADMIN_API_KEY)}"
ADMIN_API_KEY="${ADMIN_API_KEY:-$(azd_get adminApiKey)}"

if [[ -z "$AZURE_CLIENT_ID" ]]; then
  echo "AZURE_CLIENT_ID is empty. Run without --skip-pipeline-config or export AZURE_CLIENT_ID." >&2
  exit 1
fi

declare -A values=(
  [AZURE_CLIENT_ID]="$AZURE_CLIENT_ID"
  [AZURE_TENANT_ID]="$AZURE_TENANT_ID"
  [AZURE_SUBSCRIPTION_ID]="$AZURE_SUBSCRIPTION_ID"
  [AZURE_ENV_NAME]="$AZD_ENVIRONMENT"
  [AZURE_LOCATION]="$AZURE_LOCATION"
  [AZURE_RESOURCE_GROUP]="$AZURE_RESOURCE_GROUP"
  [AZURE_WEBAPP_NAME]="$AZURE_WEBAPP_NAME"
  [AZURE_APP_SERVICE_DASHBOARD_URI]="$(azd_get AZURE_APP_SERVICE_DASHBOARD_URI)"
  [AZURE_CONTAINER_REGISTRY_ENDPOINT]="$(azd_get AZURE_CONTAINER_REGISTRY_ENDPOINT)"
  [API_IDENTITY_CLIENTID]="$(azd_get API_IDENTITY_CLIENTID)"
  [API_IDENTITY_ID]="$(azd_get API_IDENTITY_ID)"
  [PLATFORM_SQL_SQLSERVERFQDN]="$(azd_get PLATFORM_SQL_SQLSERVERFQDN)"
  [ELSA_PLATFORM_AZURE_APP_SERVICE_DASHBOARD_URI]="$(azd_get ELSA_PLATFORM_AZURE_APP_SERVICE_DASHBOARD_URI)"
  [ELSA_PLATFORM_AZURE_CONTAINER_REGISTRY_ENDPOINT]="$(azd_get ELSA_PLATFORM_AZURE_CONTAINER_REGISTRY_ENDPOINT)"
  [ELSA_PLATFORM_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_CLIENT_ID]="$(azd_get ELSA_PLATFORM_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_CLIENT_ID)"
  [ELSA_PLATFORM_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID]="$(azd_get ELSA_PLATFORM_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID)"
  [ELSA_PLATFORM_AZURE_WEBSITE_CONTRIBUTOR_MANAGED_IDENTITY_ID]="$(azd_get ELSA_PLATFORM_AZURE_WEBSITE_CONTRIBUTOR_MANAGED_IDENTITY_ID)"
  [ELSA_PLATFORM_AZURE_WEBSITE_CONTRIBUTOR_MANAGED_IDENTITY_PRINCIPAL_ID]="$(azd_get ELSA_PLATFORM_AZURE_WEBSITE_CONTRIBUTOR_MANAGED_IDENTITY_PRINCIPAL_ID)"
  [ELSA_PLATFORM_PLANID]="$(azd_get ELSA_PLATFORM_PLANID)"
)

for name in "${!values[@]}"; do
  set_github_var "$name" "${values[$name]}"
done

set_github_secret ADMIN_API_KEY "$ADMIN_API_KEY"

echo "GitHub Azure deployment bootstrap completed."
