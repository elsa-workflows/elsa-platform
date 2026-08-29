#!/usr/bin/env python3
"""Offline contract checks for the disposable Azure workload proof."""

from __future__ import annotations

import json
import re
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PROOF = ROOT / "infra" / "azure-workload-proof"
MAIN = PROOF / "main.bicep"
APP = PROOF / "modules" / "container-app.bicep"
ENVIRONMENT = PROOF / "modules" / "container-apps-environment.bicep"
ACR_ROLE = PROOF / "acr-pull-role.bicep"
VAULT = PROOF / "modules" / "key-vault.bicep"
SQL = PROOF / "modules" / "sql.bicep"
RUNBOOK = ROOT / "scripts" / "azure-workload-proof.sh"
RUNBOOK_LIB = ROOT / "scripts" / "lib" / "azure-workload-proof.sh"
REGENERATE_INFRA = ROOT / "dev" / "regenerate-infra.sh"


class AzureWorkloadProofTests(unittest.TestCase):
    def test_checked_in_shape(self) -> None:
        expected = {
            "main.bicep",
            "acr-pull-role.bicep",
            "sql-bootstrap.sql",
            "modules/identity.bicep",
            "modules/key-vault.bicep",
            "modules/observability.bicep",
            "modules/sql.bicep",
            "modules/container-apps-environment.bicep",
            "modules/container-app.bicep",
        }
        actual = {str(path.relative_to(PROOF)) for path in PROOF.rglob("*") if path.is_file()}
        self.assertTrue(expected <= actual)

    def test_image_is_digest_only(self) -> None:
        source = MAIN.read_text()
        self.assertIn("param imageDigest string", source)
        self.assertIn("@minLength(64)", source)
        self.assertIn("@maxLength(64)", source)
        self.assertIn("@sha256:${toLower(imageDigest)}", source)
        self.assertNotRegex(source, r"param\s+imageTag\b")

    def test_sql_is_entra_only_and_bootstrap_is_explicit(self) -> None:
        source = SQL.read_text()
        self.assertIn("azureADOnlyAuthentication: true", source)
        self.assertIn("administratorType: 'ActiveDirectory'", source)
        self.assertIn("bootstrapObjectId", source)
        self.assertNotIn("administratorPassword", source)
        bootstrap = (PROOF / "sql-bootstrap.sql").read_text()
        self.assertIn("TYPE = E", bootstrap)
        self.assertIn("__WORKLOAD_IDENTITY_CLIENT_ID__", bootstrap)
        self.assertIn("DECLARE @clientId UNIQUEIDENTIFIER", bootstrap)
        self.assertIn("CONVERT(VARBINARY(16), @clientId)", bootstrap)
        self.assertNotIn("REPLACE('__WORKLOAD_IDENTITY_CLIENT_ID__'", bootstrap)
        self.assertNotIn("__WORKLOAD_IDENTITY_OBJECT_ID__", bootstrap)
        self.assertNotIn("Directory Readers", bootstrap)
        self.assertIn("db_ddladmin", bootstrap)

    def test_managed_identity_roles_are_narrow(self) -> None:
        acr = ACR_ROLE.read_text()
        vault = VAULT.read_text()
        self.assertIn("targetScope = 'resourceGroup'", acr)
        self.assertIn("7f951dda-4ed3-4680-a7ca-43fe172d538d", acr)
        self.assertIn("4633458b-17de-408a-b874-0445c86b69e6", vault)
        self.assertIn("b86a8fe4-44ce-4948-aee5-eccb2c155cd7", vault)
        self.assertIn("enableRbacAuthorization: true", vault)

    def test_app_has_safe_ingress_revision_and_probes(self) -> None:
        source = APP.read_text()
        for expected in (
            "activeRevisionsMode: 'Multiple'",
            "targetPort: 8080",
            "allowInsecure: false",
            "latestRevision: true",
            "minReplicas: 0",
            "type: 'Startup'",
            "type: 'Readiness'",
            "type: 'Liveness'",
            "path: '/health'",
            "path: '/alive'",
            "identity: workloadIdentityId",
            "keyVaultUrl:",
        ):
            self.assertIn(expected, source)
        self.assertIn("workloadProfileType: 'Consumption'", ENVIRONMENT.read_text())
        self.assertIn("Nuplane__Setup__Feeds__0__Name", source)
        self.assertIn("Nuplane__Setup__Feeds__1__ServiceIndex", source)
        self.assertIn("Nuplane__Setup__Feeds__2__IncludePatterns__0", source)
        self.assertIn("Nuplane__Setup__Feeds__2__IncludePatterns__1", source)
        self.assertIn("CShells__Shells__Default__Features__DefaultAdminUser__AdminUsername", source)
        self.assertIn("CShells__Shells__Default__Features__DefaultAdminUser__AdminPassword", source)
        self.assertIn("secretRef: adminCredentialRef", source)
        self.assertIn("stableTrafficRevisionName", source)
        self.assertIn("revisionName: stableTrafficRevisionName", source)
        self.assertIn("3.8.0-preview.5413", MAIN.read_text())
        self.assertIn("3.8.0-preview.342", MAIN.read_text())

    def test_deterministic_fingerprint_and_required_tags(self) -> None:
        source = MAIN.read_text()
        self.assertIn("var planInput =", source)
        self.assertIn("template=${toLower(templateFingerprint)}", source)
        self.assertIn("var planFingerprint = uniqueString(planInput)", source)
        self.assertIn("empty(workloadRevisionSuffix)", source)
        self.assertIn("'plan-fingerprint': planFingerprint", source)
        self.assertIn("deploymentName string = take('elsa108-${proofName}-${take(planFingerprint, 12)}', 64)", source)
        self.assertIn("proof: '108'", source)
        self.assertIn("owner: owner", source)
        self.assertIn("expiry: expiryUtc", source)

    def test_outputs_are_secret_safe(self) -> None:
        source = MAIN.read_text()
        output_lines = [line.lower() for line in source.splitlines() if line.lstrip().startswith("output ")]
        self.assertTrue(output_lines)
        for line in output_lines:
            self.assertFalse(any(token in line for token in ("secret", "password", "sharedkey", "connectionstring")))
        self.assertNotIn("@secure()", source)

    def test_no_edge_or_network_expansion(self) -> None:
        source = "\n".join(path.read_text().lower() for path in PROOF.rglob("*.bicep"))
        self.assertNotIn("frontdoor", source)
        self.assertNotIn("virtualnetwork", source)
        self.assertNotIn("microsoft.network/virtualnetworks", source)

    def test_aspire_regeneration_preserves_manual_proof(self) -> None:
        source = REGENERATE_INFRA.read_text()
        self.assertIn("preserved_proof", source)
        self.assertIn("mv infra/azure-workload-proof", source)
        self.assertIn("trap restore_preserved_proof EXIT", source)
        self.assertLess(source.index("mv infra/azure-workload-proof"), source.index("rm -rf infra"))

    def test_runbook_is_fail_closed(self) -> None:
        source = RUNBOOK.read_text()
        self.assertIn("DISPOSABLE_PROOF_APPLY:-", source)
        self.assertIn("what-if requires an existing resource group", source)
        self.assertIn("az group delete", source)
        self.assertIn("--sql-bootstrap-ip", source)
        self.assertIn("--authentication-method ActiveDirectoryDefault", source)
        self.assertIn("sqlcmd '-?'", source)
        self.assertIn("temporary_firewall_rule", source)
        self.assertIn("openssl rand -base64 48 | tr -d '\\r\\n'", source)
        self.assertIn('seed_secret_if_missing admin-password "$temp_dir/admin-password"', source)
        firewall_deletes = [
            line for line in source.splitlines()
            if "az sql server firewall-rule delete" in line
        ]
        self.assertEqual(2, len(firewall_deletes))
        self.assertTrue(all("--yes" not in line for line in firewall_deletes))
        self.assertIn("keyvault purge", source)
        self.assertIn("Refusing to adopt unrelated resource group", source)
        self.assertIn("external ACR cleanup cannot be proven", source)
        self.assertIn("external ACR cleanup was incomplete", source)
        self.assertIn("registry-subscription", source)
        self.assertIn('proof-name="$proof_name"', source)
        self.assertIn("acr_role_ready", source)
        self.assertIn("external_context=", source)
        self.assertIn('external_deployment_suffix="$(sha256_text', source)
        self.assertIn("tags.acrDeployment", source)
        self.assertIn("tags.acrPrincipal", source)
        self.assertIn("tags.acrRegistryId", source)
        self.assertIn("properties.outputs.roleAssignmentId.value", source)
        self.assertIn("stored ACR assignment does not match this proof identity, scope, and role", source)
        self.assertIn("ACR deployment records could not be read", source)
        self.assertIn("ACR role assignments could not be read", source)
        self.assertIn("identity-scoped ACR assignments could not be read", source)
        self.assertIn("az deployment group delete", source)
        self.assertIn("az group exists", source)
        self.assertIn("show-deleted", source)
        self.assertRegex(source, r"\[\[ \"\$\{DISPOSABLE_PROOF_APPLY:-\}\" == YES \]\]")
        validation = (ROOT / "scripts" / "validate-azure-workload-proof.sh").read_text()
        self.assertIn("Compiled main template SHA-256", validation)
        self.assertIn('templateFingerprint="$compiled_fingerprint"', validation)
        self.assertIn('template_fingerprint="$(az bicep build', source)
        self.assertIn('"templateFingerprint=$template_fingerprint"', source)
        self.assertIn('workloadRevisionSuffix="$workload_revision_suffix"', source)
        self.assertIn("resolve_workload_revision_suffix", source)
        self.assertIn("resolve_stable_traffic_revision", source)
        self.assertIn("candidate_healthy", source)
        self.assertIn("stable traffic was preserved", source)
        library = RUNBOOK_LIB.read_text()
        self.assertIn("promote_workload_revision", library)
        self.assertIn("--retry-all-errors", library)
        self.assertIn("/revisions?api-version=2024-03-01", source)
        self.assertIn(".nextLink // empty", source)
        self.assertIn("az tag update", source)
        self.assertIn("--operation Merge", source)
        self.assertIn("tags.acrDeployment", source)

    def test_revision_suffix_selection_is_deterministic(self) -> None:
        def select(current: str, revisions: list[str]) -> subprocess.CompletedProcess[str]:
            return subprocess.run(
                [
                    "bash",
                    "-c",
                    'source "$1"; select_workload_revision_suffix plan123 "$2" proof-app "$3"',
                    "test",
                    str(RUNBOOK_LIB),
                    current,
                    json.dumps(revisions),
                ],
                capture_output=True,
                text=True,
                check=False,
            )

        self.assertEqual("plan123", select("bad", []).stdout.strip())
        occupied = ["proof-app--plan123", "proof-app--plan123-r1"]
        self.assertEqual("plan123-r2", select("bad", occupied).stdout.strip())
        self.assertEqual("plan123-r2", select("plan123-r2", occupied).stdout.strip())
        self.assertEqual("plan123-r2", select("plan123-r2", occupied).stdout.strip())

        exhausted = ["proof-app--plan123", *(f"proof-app--plan123-r{i}" for i in range(1, 1000))]
        result = select("bad", exhausted)
        self.assertEqual(5, result.returncode)
        self.assertIn("No free deterministic recovery revision suffix", result.stderr)

    def test_failed_promotion_restores_stable_traffic(self) -> None:
        script = r'''
source "$1"
az() {
  printf 'az:%s\n' "$*"
  return 0
}
curl() { return 1; }
promote_workload_revision proof-rg proof-app stable-revision candidate-revision https://proof.invalid
'''
        result = subprocess.run(
            ["bash", "-c", script, "test", str(RUNBOOK_LIB)],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(5, result.returncode)
        calls = [line for line in result.stdout.splitlines() if line.startswith("az:")]
        self.assertEqual(2, len(calls))
        self.assertIn("candidate-revision=100", calls[0])
        self.assertIn("stable-revision=100 candidate-revision=0", calls[1])
        self.assertIn("Restored stable traffic", result.stderr)

    def test_uncertain_traffic_set_result_also_rolls_back(self) -> None:
        script = r'''
source "$1"
attempt=0
az() {
  attempt=$((attempt + 1))
  printf 'az:%s\n' "$*"
  (( attempt > 1 ))
}
curl() { echo 'curl must not run' >&2; return 99; }
promote_workload_revision proof-rg proof-app stable-revision candidate-revision https://proof.invalid
'''
        result = subprocess.run(
            ["bash", "-c", script, "test", str(RUNBOOK_LIB)],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(5, result.returncode)
        calls = [line for line in result.stdout.splitlines() if line.startswith("az:")]
        self.assertEqual(2, len(calls))
        self.assertNotIn("curl must not run", result.stderr)
        self.assertIn("uncertain result", result.stderr)
        self.assertIn("Restored stable traffic", result.stderr)

    def test_invalid_vault_derived_proof_names_fail_before_azure(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            fake_az = Path(temp_dir) / "az"
            marker = Path(temp_dir) / "azure-was-called"
            fake_az.write_text(f"#!/usr/bin/env bash\ntouch '{marker}'\nexit 99\n")
            fake_az.chmod(0o700)
            env = os.environ.copy()
            env["PATH"] = f"{temp_dir}:{env['PATH']}"
            common = [
                str(RUNBOOK),
                "apply",
                "--resource-group", "rg-proof",
                "--image-digest", "a" * 64,
                "--registry-resource-group", "rg-acr",
                "--sql-bootstrap-object-id", "00000000-0000-0000-0000-000000000001",
                "--sql-bootstrap-login", "proof-user",
                "--sql-bootstrap-ip", "203.0.113.10",
            ]
            for invalid_name in ("1abc", "abc-", "ab--cd"):
                result = subprocess.run(
                    [*common, "--proof-name", invalid_name],
                    env=env,
                    capture_output=True,
                    text=True,
                    check=False,
                )
                self.assertEqual(2, result.returncode)
                self.assertIn("proof name must be", result.stderr)
                self.assertFalse(marker.exists())


def valid_image_reference(repository: str, digest: str) -> bool:
    return bool(re.fullmatch(r"[a-z0-9./_-]+@sha256:[0-9a-f]{64}", f"{repository}@sha256:{digest}"))


class ImageReferenceTests(unittest.TestCase):
    def test_tags_and_short_digests_are_rejected_by_contract(self) -> None:
        digest = "a" * 64
        self.assertTrue(valid_image_reference("valenceruntimeimages.azurecr.io/runtime-combined", digest))
        self.assertFalse(valid_image_reference("valenceruntimeimages.azurecr.io/runtime-combined:latest", digest))
        self.assertFalse(valid_image_reference("valenceruntimeimages.azurecr.io/runtime-combined", "a" * 63))


if __name__ == "__main__":
    unittest.main()
