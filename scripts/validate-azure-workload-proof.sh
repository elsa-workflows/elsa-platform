#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
proof_dir="$repo_root/infra/azure-workload-proof"

compiled_template="$(mktemp)"
compiled_template_again="$(mktemp)"
trap 'rm -f "$compiled_template" "$compiled_template_again"' EXIT
az bicep build --file "$proof_dir/main.bicep" --stdout >"$compiled_template"
az bicep build --file "$proof_dir/main.bicep" --stdout >"$compiled_template_again"
cmp -- "$compiled_template" "$compiled_template_again"
az bicep build --file "$proof_dir/acr-pull-role.bicep" --stdout >/dev/null
python3 "$repo_root/scripts/tests/test_azure_workload_proof.py"

if command -v sha256sum >/dev/null; then
  compiled_fingerprint="$(sha256sum "$compiled_template" | awk '{print $1}')"
else
  compiled_fingerprint="$(shasum -a 256 "$compiled_template" | awk '{print $1}')"
fi
echo "Compiled main template SHA-256: $compiled_fingerprint"

if [[ "${AZURE_WORKLOAD_PROOF_WHAT_IF:-}" == "1" ]]; then
  : "${AZURE_WORKLOAD_PROOF_RESOURCE_GROUP:?Set AZURE_WORKLOAD_PROOF_RESOURCE_GROUP for what-if}"
  : "${AZURE_WORKLOAD_PROOF_IMAGE_DIGEST:?Set AZURE_WORKLOAD_PROOF_IMAGE_DIGEST for what-if}"
  : "${AZURE_WORKLOAD_PROOF_ACR_RESOURCE_GROUP:?Set AZURE_WORKLOAD_PROOF_ACR_RESOURCE_GROUP for what-if}"
  : "${AZURE_WORKLOAD_PROOF_SQL_BOOTSTRAP_OBJECT_ID:?Set AZURE_WORKLOAD_PROOF_SQL_BOOTSTRAP_OBJECT_ID for what-if}"
  : "${AZURE_WORKLOAD_PROOF_SQL_BOOTSTRAP_LOGIN:?Set AZURE_WORKLOAD_PROOF_SQL_BOOTSTRAP_LOGIN for what-if}"
  : "${AZURE_WORKLOAD_PROOF_NAME:?Set AZURE_WORKLOAD_PROOF_NAME for what-if}"

  # what-if is read-only but the resource group must already exist. This script
  # never creates it in validation mode.
  az deployment group what-if \
    --resource-group "$AZURE_WORKLOAD_PROOF_RESOURCE_GROUP" \
    --name "elsa108-${AZURE_WORKLOAD_PROOF_NAME}-whatif" \
    --template-file "$proof_dir/main.bicep" \
    --parameters \
      proofName="$AZURE_WORKLOAD_PROOF_NAME" \
      imageDigest="$AZURE_WORKLOAD_PROOF_IMAGE_DIGEST" \
      registryResourceGroupName="$AZURE_WORKLOAD_PROOF_ACR_RESOURCE_GROUP" \
      sqlBootstrapObjectId="$AZURE_WORKLOAD_PROOF_SQL_BOOTSTRAP_OBJECT_ID" \
      sqlBootstrapLogin="$AZURE_WORKLOAD_PROOF_SQL_BOOTSTRAP_LOGIN" \
    --only-show-errors
fi
