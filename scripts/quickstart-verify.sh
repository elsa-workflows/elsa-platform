#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

dotnet restore ElsaControl.sln
dotnet test ElsaControl.sln --no-restore

echo "Quickstart verification passed."
