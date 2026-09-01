#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/../.." && pwd)
proof_output=$(mktemp -d "${TMPDIR:-/tmp}/elsa-managed-azure-browser-proof.XXXXXX")

cleanup() {
  find "$proof_output" -depth -delete
}
trap cleanup EXIT

: "${ADMIN_UI_BASE_URL:?Set ADMIN_UI_BASE_URL to the public Elsa Control HTTPS origin.}"
: "${MANAGED_ELSA_PROOF_RUNTIME_ORIGIN:?Set MANAGED_ELSA_PROOF_RUNTIME_ORIGIN to the public runtime HTTPS origin.}"

MANAGED_ELSA_AZURE_BROWSER_PROOF=1 \
  npm --prefix "$repo_root/tests/Hosting/ElsaControl.Console.E2E" run e2e -- \
    managed-elsa-azure-browser-proof.spec.ts \
    --project=chromium \
    --headed \
    --reporter=line \
    --output="$proof_output"
