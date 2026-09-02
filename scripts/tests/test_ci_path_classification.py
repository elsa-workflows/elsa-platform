#!/usr/bin/env python3
"""Contract checks for the repository-owned pull-request CI classifier."""

from __future__ import annotations

import sys
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

from ci.classify_dotnet_changes import (  # noqa: E402
    DotnetGateDecision,
    _write_github_output,
    main,
    classify_paths,
)


class CiPathClassificationTests(unittest.TestCase):
    def test_documentation_and_browser_harness_changes_skip_dotnet_gate(self) -> None:
        for event_name in ("pull_request", "pull_request_target"):
            with self.subTest(event_name=event_name):
                decision = classify_paths(
                    event_name,
                    [
                        "docs/evidence/managed-elsa-browser-proof.md",
                        "README.md",
                        "infra/azure-workload-proof/main.bicep",
                        "scripts/managed-elsa-browser-proof/run-azure.sh",
                        "src/Hosting/ElsaControl.Console/src/main.tsx",
                        "tests/Hosting/ElsaControl.Console.E2E/managed-elsa-browser-proof.spec.ts",
                    ],
                )

                self.assertFalse(decision.run_dotnet)
                self.assertIn("unrelated-path allowlist", decision.reason)

    def test_mixed_safe_and_dotnet_paths_run_full_gate(self) -> None:
        decision = classify_paths(
            "pull_request",
            ["docs/architecture.md", "src/Deployment/ElsaControl.Deployment.Core/Service.cs"],
        )

        self.assertTrue(decision.run_dotnet)

    def test_dotnet_source_project_test_and_build_paths_run_full_gate(self) -> None:
        for changed_path in (
            "src/Hosting/ElsaControl.Api/Program.cs",
            "tests/Hosting/ElsaControl.Api.Tests/Fixtures/workflow.json",
            "tests/Hosting/ElsaControl.Api.Tests/Fixtures/prompt.md",
            "src/Hosting/ElsaControl.Api/ElsaControl.Api.csproj",
            "ElsaControl.sln",
            "Directory.Packages.props",
            "global.json",
            "Directory.Build.props",
            ".github/workflows/ci.yml",
            "scripts/ci/classify_dotnet_changes.py",
            "scripts/tests/test_ci_path_classification.py",
        ):
            with self.subTest(changed_path=changed_path):
                decision = classify_paths("pull_request", [changed_path])

                self.assertTrue(decision.run_dotnet)
                self.assertIn(changed_path, decision.reason)

    def test_unknown_paths_fail_closed_to_full_gate(self) -> None:
        decision = classify_paths("pull_request", ["new/unknown-build-input.txt"])

        self.assertTrue(decision.run_dotnet)
        self.assertIn("conservative", decision.reason)

    def test_path_normalization_does_not_broaden_unrelated_allowlist(self) -> None:
        decision = classify_paths("pull_request", [" docs/../src/Program.cs"])

        self.assertTrue(decision.run_dotnet)

    def test_non_pull_request_events_always_run_full_gate(self) -> None:
        unrelated_paths = ["README.md", "docs/architecture.md"]

        for event_name in ("push", "workflow_dispatch"):
            with self.subTest(event_name=event_name):
                decision = classify_paths(event_name, unrelated_paths)

                self.assertTrue(decision.run_dotnet)
                self.assertIn(event_name, decision.reason)

    def test_unknown_events_fail_closed_to_full_gate(self) -> None:
        decision = classify_paths("schedule", ["README.md"])

        self.assertTrue(decision.run_dotnet)
        self.assertIn("unsupported", decision.reason)

    def test_empty_pull_request_path_set_fails_closed_to_full_gate(self) -> None:
        decision = classify_paths("pull_request", [])

        self.assertTrue(decision.run_dotnet)
        self.assertIn("no changed paths", decision.reason)

    def test_ci_workflow_wires_classifier_and_preserves_required_job(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text()

        self.assertIn("name: Build and Test", workflow)
        self.assertIn("pull_request_target:", workflow)
        self.assertNotIn("  pull_request:\n", workflow)
        self.assertEqual(
            2,
            workflow.count("ref: ${{ github.event.pull_request.head.sha }}"),
        )
        self.assertEqual(2, workflow.count("persist-credentials: false"))
        self.assertIn("fetch-depth: 0", workflow)
        self.assertIn('"$CI_EVENT_NAME" == "pull_request_target"', workflow)
        self.assertIn('git show "$CI_BASE_SHA:$classifier"', workflow)
        self.assertIn("base branch does not yet contain the trusted classifier", workflow)
        self.assertIn("scripts/ci/classify_dotnet_changes.py", workflow)
        self.assertIn("name: Validate CI path classification contract", workflow)
        self.assertIn("name: Validate Azure deployment workflow contract", workflow)
        self.assertIn("name: Check Azure workload proof shell scripts", workflow)
        self.assertIn("name: Compile Azure workload proof Bicep templates", workflow)
        self.assertEqual(workflow.count("steps.dotnet-gate.outputs.run_dotnet == 'true'"), 5)

    def test_cli_reads_git_diff_and_writes_skip_decision(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            self._git(repository, "init")
            self._git(repository, "config", "user.email", "ci-contract@example.invalid")
            self._git(repository, "config", "user.name", "CI contract")
            (repository / "README.md").write_text("before\n", encoding="utf-8")
            self._git(repository, "add", "README.md")
            self._git(repository, "commit", "-m", "baseline")
            base_sha = self._git(repository, "rev-parse", "HEAD").strip()
            (repository / "README.md").write_text("after\n", encoding="utf-8")
            self._git(repository, "commit", "-am", "docs")
            head_sha = self._git(repository, "rev-parse", "HEAD").strip()
            output_path = repository / "github-output.txt"

            for event_name in ("pull_request", "pull_request_target"):
                with self.subTest(event_name=event_name):
                    output_path.unlink(missing_ok=True)
                    exit_code = main([
                        "--event-name", event_name,
                        "--base-sha", base_sha,
                        "--head-sha", head_sha,
                        "--repo-root", str(repository),
                        "--github-output", str(output_path),
                    ])

                    self.assertEqual(0, exit_code)
                    outputs = output_path.read_text(encoding="utf-8")
                    self.assertIn("run_dotnet=false\n", outputs)
                    self.assertIn("impacting_path_count=0\n", outputs)

    def test_github_output_flattens_diagnostic_newlines(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output_path = Path(directory) / "github-output.txt"

            _write_github_output(
                output_path,
                DotnetGateDecision(True, "stable\r\nmessage", ("src/Program.cs",)),
            )

            outputs = output_path.read_text(encoding="utf-8").splitlines()
            self.assertEqual("run_dotnet=true", outputs[0])
            self.assertEqual("reason=stable  message", outputs[1])
            self.assertEqual("impacting_path_count=1", outputs[2])

    @staticmethod
    def _git(repository: Path, *arguments: str) -> str:
        result = subprocess.run(
            ["git", *arguments],
            cwd=repository,
            check=True,
            capture_output=True,
            text=True,
        )
        return result.stdout


if __name__ == "__main__":
    unittest.main()
