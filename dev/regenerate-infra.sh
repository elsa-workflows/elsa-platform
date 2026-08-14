#!/usr/bin/env bash
# Regenerates the Aspire infrastructure into infra/ and re-applies the one local patch we carry.
#
# Why the patch: Aspire emits an `api-roles-control-sql` deployment script to grant the API's managed
# identity access to Azure SQL. That script installs SqlServer PowerShell 22.3.0 onto Az PowerShell
# 14.0, where Invoke-Sqlcmd throws MissingMethodException before it reaches SQL, failing the whole
# provision. Aspire exposes no opt-out (WithRoleAssignments covers only Container Registry and
# Storage), and the same script is emitted by 13.3.5 and 13.4.6 alike. The contained SQL user is
# created out of band instead -- see docs/deployment/azure-app-service.md.
#
# Run this after changing AppHost.cs, then `azd provision` / `azd deploy`.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "Regenerating infrastructure from the Aspire AppHost..."
rm -rf infra
azd infra generate --force --no-prompt

if [ -d infra/api-roles-control-sql ]; then
    echo "Stripping the broken api-roles-control-sql module..."
    python3 - <<'PY'
marker = "module api_roles_control_sql 'api-roles-control-sql/api-roles-control-sql.module.bicep' = {"
note = (
    "// NOTE: the Aspire-generated api-roles-control-sql module is removed by dev/regenerate-infra.sh.\n"
    "// Its deployment script is broken upstream (SqlServer PowerShell 22.3.0 on Az PowerShell 14.0),\n"
    "// so the API identity's contained SQL user is created out of band instead.\n"
)
with open("infra/main.bicep") as handle:
    content = handle.read()
start = content.index(marker)
end = content.index("\n}\n", start) + len("\n}\n")
with open("infra/main.bicep", "w") as handle:
    handle.write(content[:start] + note + content[end:])
PY
    rm -rf infra/api-roles-control-sql
fi

az bicep build --file infra/main.bicep --stdout > /dev/null
echo "infra/ regenerated and patched."
