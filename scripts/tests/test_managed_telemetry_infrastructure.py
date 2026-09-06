#!/usr/bin/env python3
"""Compile and inspect the managed lifecycle telemetry sink boundary."""

import json
import shutil
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MAIN = ROOT / "infra/managed-telemetry/main.bicep"


class ManagedTelemetryInfrastructureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not MAIN.is_file():
            raise AssertionError("Managed telemetry infrastructure is missing")
        az = shutil.which("az")
        if az is None:
            raise RuntimeError("Azure CLI with Bicep is required for telemetry contract tests")
        result = subprocess.run(
            [az, "bicep", "build", "--file", str(MAIN), "--stdout"],
            capture_output=True, text=True, check=False, timeout=60,
        )
        if result.returncode:
            raise AssertionError(result.stderr)
        cls.template = json.loads(result.stdout)
        cls.resources = {resource["type"]: resource for resource in cls.template["resources"]}

    def test_only_sink_and_exact_publisher_role_are_created(self):
        self.assertEqual(3, len(self.template["resources"]))
        self.assertEqual({
            "Microsoft.OperationalInsights/workspaces",
            "Microsoft.Insights/components",
            "Microsoft.Authorization/roleAssignments",
        }, set(self.resources))

    def test_local_authentication_is_disabled_on_both_ingestion_surfaces(self):
        workspace = self.resources["Microsoft.OperationalInsights/workspaces"]["properties"]
        component = self.resources["Microsoft.Insights/components"]["properties"]
        self.assertTrue(workspace["features"]["disableLocalAuth"])
        self.assertTrue(component["DisableLocalAuth"])
        self.assertFalse(component["DisableIpMasking"])
        self.assertEqual("[resourceId('Microsoft.OperationalInsights/workspaces', parameters('workspaceName'))]",
                         component["WorkspaceResourceId"])

    def test_retention_and_ingestion_cost_controls_are_bounded(self):
        workspace = self.resources["Microsoft.OperationalInsights/workspaces"]["properties"]
        self.assertEqual("PerGB2018", workspace["sku"]["name"])
        self.assertNotIn("capacityReservationLevel", workspace["sku"])
        self.assertEqual(30, workspace["retentionInDays"])
        self.assertEqual("[parameters('dailyQuotaGb')]", workspace["workspaceCapping"]["dailyQuotaGb"])
        quota = self.template["parameters"]["dailyQuotaGb"]
        self.assertEqual(1, quota["defaultValue"])
        self.assertEqual(1, quota["minValue"])
        self.assertEqual(5, quota["maxValue"])

    def test_publisher_is_bound_to_existing_identity_at_exact_component_scope(self):
        role = self.resources["Microsoft.Authorization/roleAssignments"]
        self.assertEqual("[resourceId('Microsoft.Insights/components', parameters('applicationInsightsName'))]", role["scope"])
        props = role["properties"]
        self.assertEqual("ServicePrincipal", props["principalType"])
        self.assertIn("MonitoringMetricsPublisherRoleId", props["roleDefinitionId"])
        self.assertEqual("3913510d-42f4-4e42-8a64-420c390055eb",
                         self.template["variables"]["MonitoringMetricsPublisherRoleId"])
        self.assertIn("Microsoft.ManagedIdentity/userAssignedIdentities", props["principalId"])
        self.assertNotIn("principalId", self.template["parameters"])

    def test_no_app_mutation_key_output_or_anonymous_dashboard_is_introduced(self):
        serialized = json.dumps(self.template)
        self.assertNotIn("Microsoft.Web/sites", serialized)
        self.assertNotIn("Microsoft.Resources/deploymentScripts", serialized)
        self.assertNotIn("listKeys", serialized)
        outputs = json.dumps(self.template.get("outputs", {})).lower()
        self.assertNotIn("connectionstring", outputs)
        self.assertNotIn("instrumentationkey", outputs)
        self.assertNotIn("unsecured", serialized.lower())


if __name__ == "__main__":
    unittest.main()
