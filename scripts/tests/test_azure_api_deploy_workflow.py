#!/usr/bin/env python3
"""Offline contract checks for the Azure API deployment workflow."""

from __future__ import annotations

import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from textwrap import dedent


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "azure-api-deploy.yml"


class AzureApiDeployWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = WORKFLOW.read_text()

    def test_capture_supports_classic_and_sitecontainer_runtime_images(self) -> None:
        self.assertIn("image_reference_pattern=", self.source)
        self.assertIn("(@sha256:[[:xdigit:]]{64})?", self.source)
        self.assertIn(
            'if [[ "$linux_fx_version" =~ ^DOCKER\\|$image_reference_pattern$ ]]; then',
            self.source,
        )
        self.assertIn('elif [ "$linux_fx_version" = "SITECONTAINERS" ]', self.source)
        self.assertIn("az webapp sitecontainers show", self.source)
        self.assertIn("--query properties.image", self.source)
        self.assertIn(
            'if [[ ! "$sitecontainer_image" =~ ^$image_reference_pattern$ ]]; then',
            self.source,
        )
        self.assertNotIn("--registry-password", self.source)
        self.assertNotIn("--registry-username", self.source)

    def test_deploy_and_rollback_use_the_captured_runtime_mode(self) -> None:
        self.assertEqual(
            self.source.count(
                "if: ${{ success() && steps.deployment-config.outputs.deploy_configured == 'true' }}"
            ),
            4,
        )
        self.assertIn("capture_succeeded=false", self.source)
        self.assertIn("capture_succeeded=true", self.source)
        self.assertIn("steps.current-deployment.outputs.capture_succeeded == 'true'", self.source)
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
        self.assertIn(
            "Restore previous API deployment after deployment or health failure",
            self.source,
        )

    def test_health_gates_require_exact_http_200(self) -> None:
        self.assertGreaterEqual(
            self.source.count('if [ "$http_status" = "200" ]'), 2
        )
        self.assertIn('.buildNumber == $expected_build_number', self.source)
        self.assertIn('.imageId == $expected_image_id', self.source)
        self.assertIn('expected_previous_image_id=""', self.source)
        self.assertIn('restored_runtime_image=', self.source)
        self.assertIn(
            "expected_previous_health_query='.status == \"ok\" and ((.buildNumber // $expected_build_number) == $expected_build_number) and .imageId == $expected_image_id'",
            self.source,
        )
        self.assertIn('legacy-image compatibility path', self.source)
        self.assertIn(
            'Azure is not configured with the captured previous main sitecontainer image',
            self.source,
        )
        self.assertIn('if [ "$stable_health_probes" -ge 2 ]; then', self.source)
        self.assertIn('if [ "$stable_rollback_health_probes" -ge 2 ]; then', self.source)

    def test_infra_deploy_uses_the_checked_in_api_dockerfile(self) -> None:
        deploy_script = (ROOT / "scripts" / "deploy-azure-elsa-control.sh").read_text()
        self.assertIn("--build-arg ELSA_CONTROL_IMAGE_ID=", deploy_script)
        self.assertIn("--file src/Hosting/ElsaControl.Api/Dockerfile", deploy_script)
        self.assertNotIn("--file src/ElsaControl.Api/Dockerfile", deploy_script)

    def test_capture_rejects_credential_bearing_and_scheme_based_images(self) -> None:
        capture_start = self.source.index(
            "        run: |\n",
            self.source.index("      - name: Capture current API deployment"),
        )
        capture_end = self.source.index("\n      - name:", capture_start)
        capture_script = dedent(self.source[capture_start + len("        run: |\n") : capture_end])

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            fake_az = temp_path / "az"
            fake_az.write_text(
                """#!/usr/bin/env bash
set -euo pipefail
case "$*" in
  *"webapp show"*)
    if [ "${WEBAPP_MISSING:-false}" = true ]; then
      printf '%s\\n' "(ResourceNotFound) Web App was not found." >&2
      exit 1
    fi
    printf '%s\\n' "test-api"
    ;;
  *"webapp config show"*) printf '%s\\n' "${LINUX_FX_VERSION}" ;;
  *"webapp sitecontainers show"*)
    if [ "${FAIL_SITECONTAINER_LOOKUP:-false}" = true ]; then exit 1; fi
    printf '%s\\n' "${SITECONTAINER_IMAGE}"
    ;;
  *"webapp config appsettings list"*) printf '%s\\n' "${APPLICATION_BUILD_NUMBER}" ;;
  *) exit 1 ;;
esac
"""
            )
            fake_az.chmod(0o755)

            def run_capture(
                linux_fx_version: str,
                sitecontainer_image: str,
                fail_sitecontainer_lookup: bool = False,
                webapp_missing: bool = False,
                deploy_mode: str = "app",
            ) -> subprocess.CompletedProcess[str]:
                output_file = temp_path / "github-output"
                environment = os.environ.copy()
                environment.update(
                    {
                        "PATH": f"{temp_path}:{environment['PATH']}",
                        "GITHUB_OUTPUT": str(output_file),
                        "LINUX_FX_VERSION": linux_fx_version,
                        "SITECONTAINER_IMAGE": sitecontainer_image,
                        "APPLICATION_BUILD_NUMBER": "1786839398",
                        "FAIL_SITECONTAINER_LOOKUP": str(fail_sitecontainer_lookup).lower(),
                        "WEBAPP_MISSING": str(webapp_missing).lower(),
                        "DEPLOY_MODE": deploy_mode,
                        "AZURE_RESOURCE_GROUP": "test-rg",
                        "AZURE_WEBAPP_NAME": "test-api",
                    }
                )
                return subprocess.run(
                    ["bash", "-c", capture_script],
                    env=environment,
                    capture_output=True,
                    text=True,
                    check=False,
                )

            unsafe_images = (
                "https://user:pass@acr.azurecr.io/elsa-control/api:latest",
                "acr.azurecr.io/elsa-control/api?secret=1",
                "acr.azurecr.io/elsa-control/api#fragment",
            )
            for image in unsafe_images:
                for runtime, captured_image in (
                    (f"DOCKER|{image}", ""),
                    ("SITECONTAINERS", image),
                ):
                    result = run_capture(runtime, captured_image)
                    self.assertNotEqual(result.returncode, 0, image)
                    if runtime == "SITECONTAINERS":
                        self.assertIn(
                            "unexpected or unsafe format",
                            result.stdout + result.stderr,
                        )

            lookup_failure = run_capture(
                "SITECONTAINERS",
                "acr.azurecr.io/elsa-control/api:latest",
                fail_sitecontainer_lookup=True,
            )
            self.assertNotEqual(lookup_failure.returncode, 0)

            fresh_infra = run_capture(
                "SITECONTAINERS",
                "",
                webapp_missing=True,
                deploy_mode="infra",
            )
            self.assertEqual(fresh_infra.returncode, 0, fresh_infra.stderr)
            self.assertIn(
                "capture_succeeded=false",
                (temp_path / "github-output").read_text(),
            )

            fresh_app = run_capture(
                "SITECONTAINERS",
                "",
                webapp_missing=True,
                deploy_mode="app",
            )
            self.assertNotEqual(fresh_app.returncode, 0)


if __name__ == "__main__":
    unittest.main()
