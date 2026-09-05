#!/usr/bin/env python3
"""Compile and inspect the exact subscription bootstrap resource boundary."""

import json
import shutil
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MAIN = ROOT / "infra/azure-customer-subscription/main.bicep"


class CustomerSubscriptionBootstrapTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        az = shutil.which("az")
        if az is None:
            raise RuntimeError("Azure CLI with Bicep is required for bootstrap contract tests")
        result = subprocess.run(
            [az, "bicep", "build", "--file", str(MAIN), "--stdout"],
            capture_output=True, text=True, check=False,
        )
        if result.returncode:
            raise AssertionError(result.stderr)
        cls.template = json.loads(result.stdout)

    def test_bootstrap_contains_only_anchor_identity_and_budget(self):
        resources = self.template["resources"]
        self.assertCountEqual(
            [resource["type"] for resource in resources],
            ["Microsoft.Resources/resourceGroups", "Microsoft.Resources/deployments", "Microsoft.Consumption/budgets"],
        )
        module = next(resource for resource in resources if resource["type"] == "Microsoft.Resources/deployments")
        nested = module["properties"]["template"]["resources"]
        self.assertEqual(["Microsoft.ManagedIdentity/userAssignedIdentities"], [resource["type"] for resource in nested])
        self.assertNotIn("subscriptionId", module)
        self.assertNotIn("tenantId", module)
        self.assertEqual("Incremental", module["properties"]["mode"])

    def test_no_implicit_target_subscription_or_real_identifiers(self):
        self.assertTrue(self.template["$schema"].endswith("subscriptionDeploymentTemplate.json#"))
        serialized = json.dumps(self.template)
        self.assertNotRegex(serialized, r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
        self.assertNotRegex(serialized, r"[\w.+-]+@[\w.-]+\.[a-z]{2,}")
        self.assertNotIn("Microsoft.Authorization/roleAssignments", serialized)
        self.assertNotIn("Microsoft.Web/sites", serialized)

    def test_budget_is_alert_only_and_recipient_is_not_an_output(self):
        parameters = self.template["parameters"]
        self.assertEqual("securestring", parameters["budgetContactEmail"]["type"].lower())
        self.assertNotIn("defaultValue", parameters["budgetContactEmail"])
        self.assertNotIn("defaultValue", parameters["budgetStartDate"])
        self.assertNotIn("defaultValue", parameters["budgetEndDate"])
        self.assertEqual(100, parameters["monthlyBudgetAmount"]["defaultValue"])
        budget = next(resource for resource in self.template["resources"] if resource["type"] == "Microsoft.Consumption/budgets")
        self.assertEqual("Monthly", budget["properties"]["timeGrain"])
        notifications = budget["properties"]["notifications"]
        self.assertEqual({"actual50", "actual80", "actual100", "forecast100"}, set(notifications))
        for name, notification in notifications.items():
            self.assertTrue(notification["enabled"])
            self.assertEqual("GreaterThanOrEqualTo", notification["operator"])
            self.assertEqual("Forecasted" if name.startswith("forecast") else "Actual", notification["thresholdType"])
            self.assertEqual(["[parameters('budgetContactEmail')]"], notification["contactEmails"])
            self.assertNotIn("contactGroups", notification)
        self.assertEqual([50, 80, 100, 100], [notification["threshold"] for notification in notifications.values()])
        self.assertNotIn("budgetContactEmail", json.dumps(self.template.get("outputs", {})))


if __name__ == "__main__":
    unittest.main()
