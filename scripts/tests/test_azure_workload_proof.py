#!/usr/bin/env python3
"""Offline contract checks for the disposable Azure workload proof."""

from __future__ import annotations

import re
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
        self.assertIn("3.8.0-preview.5413", MAIN.read_text())
        self.assertIn("3.8.0-preview.342", MAIN.read_text())

    def test_deterministic_fingerprint_and_required_tags(self) -> None:
        source = MAIN.read_text()
        self.assertIn("var planInput =", source)
        self.assertIn("var planFingerprint = uniqueString(planInput)", source)
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

    def test_runbook_is_fail_closed(self) -> None:
        source = RUNBOOK.read_text()
        self.assertIn("DISPOSABLE_PROOF_APPLY:-", source)
        self.assertIn("what-if requires an existing resource group", source)
        self.assertIn("az group delete", source)
        self.assertIn("--sql-bootstrap-ip", source)
        self.assertIn("--authentication-method ActiveDirectoryDefault", source)
        self.assertIn("temporary_firewall_rule", source)
        self.assertIn("keyvault purge", source)
        self.assertIn("Refusing to adopt unrelated resource group", source)
        self.assertIn("registry-subscription", source)
        self.assertIn('proof-name="$proof_name"', source)
        self.assertIn("acr_role_ready", source)
        self.assertIn("az group exists", source)
        self.assertIn("show-deleted", source)
        self.assertRegex(source, r"\[\[ \"\$\{DISPOSABLE_PROOF_APPLY:-\}\" == YES \]\]")
        validation = (ROOT / "scripts" / "validate-azure-workload-proof.sh").read_text()
        self.assertIn("Compiled main template SHA-256", validation)


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
