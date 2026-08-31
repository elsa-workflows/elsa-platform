#!/usr/bin/env python3
"""Offline contract checks for the Azure API deployment workflow."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "azure-api-deploy.yml"


class AzureApiDeployWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = WORKFLOW.read_text()

    def test_capture_supports_classic_and_sitecontainer_runtime_images(self) -> None:
        self.assertIn(r"^DOCKER\|[[:alnum:]][[:alnum:]._:/@=-]*$", self.source)
        self.assertIn('elif [ "$linux_fx_version" = "SITECONTAINERS" ]', self.source)
        self.assertIn("az webapp sitecontainers show", self.source)
        self.assertIn("--query properties.image", self.source)
        self.assertIn(r"^[[:alnum:]][[:alnum:]._:/@=-]*$", self.source)
        self.assertNotIn("--registry-password", self.source)
        self.assertNotIn("--registry-username", self.source)

    def test_deploy_and_rollback_use_the_captured_runtime_mode(self) -> None:
        self.assertIn(
            'current_deployment_mode="${{ steps.current-deployment.outputs.deployment_mode }}"',
            self.source,
        )
        self.assertIn('if [ "$current_deployment_mode" = "sitecontainers" ]', self.source)
        self.assertIn('elif [ "$current_deployment_mode" = "classic" ]', self.source)
        self.assertIn("PREVIOUS_SITECONTAINER_IMAGE", self.source)
        self.assertIn('--image "$PREVIOUS_SITECONTAINER_IMAGE"', self.source)
        self.assertIn("steps.deploy-api.outcome == 'failure'", self.source)
        self.assertIn("steps.health-gate.outcome == 'failure'", self.source)

    def test_health_gates_require_exact_http_200(self) -> None:
        self.assertGreaterEqual(
            self.source.count('if [ "$http_status" = "200" ]'), 2
        )


if __name__ == "__main__":
    unittest.main()
