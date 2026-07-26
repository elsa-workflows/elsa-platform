#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

dotnet restore ValenceControl.sln
dotnet test ValenceControl.sln --no-restore

echo "Quickstart verification passed."
