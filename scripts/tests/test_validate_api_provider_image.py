#!/usr/bin/env python3
"""Offline contract and fake-Docker checks for the API image smoke gate."""

from __future__ import annotations

import os
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "validate-api-provider-image.sh"


class ValidateApiProviderImageTests(unittest.TestCase):
    def test_script_uses_a_private_bounded_container_probe(self) -> None:
        source = SCRIPT.read_text()
        self.assertIn("docker create", source)
        self.assertIn("--network none", source)
        self.assertIn("ASPNETCORE_ENVIRONMENT=Production", source)
        self.assertIn("ConnectionStrings__Catalog=Data Source=/tmp/elsa-image-smoke/catalog.db", source)
        self.assertIn("DataProtection__KeysPath=/tmp/elsa-image-smoke/keys", source)
        self.assertIn("Authentication__ApiKey=$api_key", source)
        self.assertIn("docker exec", source)
        self.assertIn("curl --fail --silent --max-time 2", source)
        self.assertIn("deadline=$((SECONDS + 60))", source)
        self.assertIn('docker rm --force "$container_id"', source)
        self.assertNotIn("--publish", source)
        self.assertNotIn("--volume", source)
        self.assertNotIn("docker logs", source)

    def test_fake_docker_success_does_not_leak_key_and_cleans_exact_container(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp = Path(temp_dir)
            docker_log = temp / "docker.log"
            fake_docker = temp / "docker"
            fake_docker.write_text(
                """#!/usr/bin/env bash
set -euo pipefail
log_file="${FAKE_DOCKER_LOG:?}"
printf '%s\n' "$*" >> "$log_file"
case "$1" in
  create) printf '%s\n' fake-container ;;
  start) ;;
  inspect) printf '%s\n' running ;;
  exec) printf '%s\n' '{"status":"ok"}' ;;
  rm) ;;
  *) exit 91 ;;
esac
"""
            )
            fake_docker.chmod(0o755)
            environment = os.environ.copy()
            environment.update(
                {
                    "PATH": f"{temp}:{environment['PATH']}",
                    "FAKE_DOCKER_LOG": str(docker_log),
                }
            )

            result = subprocess.run(
                ["bash", str(SCRIPT), "api-image:test"],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual("API provider image smoke check passed.\n", result.stdout)
            self.assertNotRegex(result.stdout + result.stderr, r"[0-9a-f]{64}")
            docker_calls = docker_log.read_text().splitlines()
            self.assertRegex(docker_calls[0], r"--network none")
            self.assertRegex(docker_calls[0], r"Authentication__ApiKey=[0-9a-f]{64}")
            self.assertNotRegex(docker_calls[0], r"--(?:publish|volume)")
            self.assertIn("exec fake-container curl --fail --silent --max-time 2", docker_calls[3])
            self.assertEqual("rm --force fake-container", docker_calls[-1])

    def test_fake_docker_crash_fails_without_emitting_container_output(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp = Path(temp_dir)
            fake_docker = temp / "docker"
            fake_docker.write_text(
                """#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  create) printf '%s\n' fake-container ;;
  start) ;;
  inspect) printf '%s\n' exited; echo 'untrusted raw container detail' >&2 ;;
  rm) ;;
  *) exit 91 ;;
esac
"""
            )
            fake_docker.chmod(0o755)
            environment = os.environ.copy()
            environment["PATH"] = f"{temp}:{environment['PATH']}"

            result = subprocess.run(
                ["bash", str(SCRIPT), "api-image:test"],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )

            self.assertNotEqual(0, result.returncode)
            self.assertIn("container exited before /health became ready", result.stderr)
            self.assertNotIn("untrusted raw container detail", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
