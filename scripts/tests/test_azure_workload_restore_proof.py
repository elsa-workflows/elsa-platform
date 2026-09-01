#!/usr/bin/env python3
"""Offline contract checks for the restore runbook's provider safety gates."""

from __future__ import annotations

import re
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RUNBOOK = ROOT / "scripts" / "azure-workload-restore-proof.sh"
TARGET_TEMPLATE = ROOT / "infra" / "azure-workload-proof" / "recovery-target.bicep"


class AzureWorkloadRestoreProofRunbookTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = RUNBOOK.read_text()
        cls.target_template = TARGET_TEMPLATE.read_text()

    def test_cutoff_is_provider_observed_after_real_quiescence(self) -> None:
        source = self.source
        for expected in (
            "az containerapp revision deactivate",
            "az containerapp revision list",
            "az containerapp replica list",
            "source_quiesced_at=\"$(canonical_utc",
            'recovery_cutoff_utc="$source_quiesced_at"',
            "provider-confirmed zero-active and zero-replica state",
            "resume_source",
        ):
            self.assertIn(expected, source)
        self.assertNotIn('source_quiesced_at="$(utc_now)"', source)
        self.assertNotIn("timedelta", source)
        self.assertNotIn("AZURE_RECOVERY_POINT_SETTLE_SECONDS", source)
        self.assertNotIn("proof-grade-quiescence", source)
        self.assertLess(
            source.index("quiesce_source ||"),
            source.index('manifest_file="$temp_dir/recovery-manifest.json"'),
        )

    def test_source_resume_is_idempotent_and_waits_for_external_health(self) -> None:
        source = self.source
        resume = source[source.index("resume_source()") : source.index("verify_target_group_inventory()")]
        self.assertIn('revision_active="$(jq -r', resume)
        self.assertIn('if [[ "$revision_active" != true ]]', resume)
        self.assertIn("for _ in {1..180}", resume)
        self.assertIn("active_exact=", resume)
        self.assertIn('curl --fail --silent --show-error --max-time 30 "$source_endpoint/health"', resume)

    def test_rpo_uses_incident_age_and_provider_restore_point(self) -> None:
        source = self.source
        self.assertIn(
            'non_negative_age_seconds "$post_committed_at" "$provider_restore_point_utc"',
            source,
        )
        self.assertNotIn('rpo_seconds="$(python3 -c', source)
        self.assertNotIn('"$post_committed_at" "$(epoch "$restore_point_utc")"', source)
        self.assertIn('recoveryPointUtc:$restorePoint', source)
        self.assertIn('incidentCutoffUtc:$incidentCutoff', source)

    def test_source_secret_values_are_never_copied_to_target(self) -> None:
        source = self.source
        self.assertNotIn('name identity-signing-key --file "$source_signing_file"', source)
        self.assertNotIn('set_secret_file admin-password "$source_password_file"', source)
        self.assertIn('show_source_secret_reference admin-password', source)
        self.assertIn('show_source_secret_reference identity-signing-key', source)
        self.assertIn('adminPasswordSecretUri="$source_admin_secret_uri"', source)
        self.assertIn('signingKeySecretUri="$source_signing_secret_uri"', source)
        self.assertIn('adminPasswordSecretUri: adminPasswordSecretUri', self.target_template)
        self.assertIn('signingKeySecretUri: signingKeySecretUri', self.target_template)
        self.assertIn('sqlConnectionSecretUri: sqlConnectionSecretUri', self.target_template)
        self.assertNotIn('az containerapp secret set', source)
        self.assertNotIn('target-bootstrap-admin-password', source)
        self.assertNotIn('target-bootstrap-signing-key', source)
        self.assertIn('ensure_source_secret_assignment', source)
        self.assertIn('role 4633458b-17de-408a-b874-0445c86b69e6', source)
        self.assertNotIn('role b86a8fe4-44ce-4948-aee5-eccb2c155cd7', source)
        self.assertIn('(.principalType // "") == "ServicePrincipal"', source)
        self.assertIn('principal identity changed', source)
        self.assertIn("Authentication=Active Directory Managed Identity", source)

        source_probe = re.findall(
            r"--endpoint \"\$source_endpoint\".*?--password-file \"\$([^\"]+)\"",
            source,
        )
        self.assertTrue(source_probe)
        self.assertTrue(all(path == "source_password_file" for path in source_probe))
        self.assertIn(
            '--endpoint "$target_endpoint" --environment "$target_name" --username proof-admin --password-file "$source_password_file"',
            source,
        )

    def test_existing_database_requires_manifest_and_provider_identity(self) -> None:
        source = self.source
        for expected in (
            "verify_owned_target_database",
            'tags[\"manifest-digest\"] == $manifest',
            ".properties.createMode // .createMode",
            ".properties.sourceDatabaseId // .sourceDatabaseId",
            ".properties.restorePointInTime // .restorePointInTime",
            "PointInTimeRestore",
            "Azure provider restore point does not match the selected recovery cutoff",
            "--tags proof=129 owner=elsa-control recovery-id=",
            "recovery-point-utc=\"$restore_point_utc\"",
            "restore-started-utc=\"$restore_accepted_at\"",
        ):
            self.assertIn(expected, source)
        self.assertLess(
            source.index('verify_owned_target_database "$target_db_json" "$manifest_tag"'),
            source.index("deploy_target false"),
        )
        self.assertNotIn('az resource tag --ids "$target_db_id" --tags proof=129 owner=elsa-control', source[: source.index("verify_owned_target_database")])
        self.assertIn('.tags["restore-started-utc"] // empty', source)

    def test_firewall_collision_and_cleanup_are_ownership_checked(self) -> None:
        source = self.source
        create = source[source.index("list_owned_firewall_rules()"):source.index("delete_owned_firewall_rule()")]
        delete = source[source.index("delete_owned_firewall_rule()"):source.index("wait_for_target_group_absence()")]
        self.assertIn("firewall-rule list", create)
        self.assertIn("refusing to reuse an existing or colliding SQL firewall rule", create)
        self.assertIn("firewall_rule_created=1", create)
        self.assertIn("ownership could not be proven after creation", create)
        self.assertIn("startIpAddress", delete)
        self.assertIn("endIpAddress", delete)
        self.assertIn("Refusing to delete SQL firewall rule: ownership or address changed", delete)
        self.assertIn('az sql server firewall-rule delete --subscription "$subscription_id"', delete)
        self.assertNotIn("delete_and_verify_firewall_rule", source)

    def test_partial_target_cleanup_is_armed_and_subscription_explicit(self) -> None:
        source = self.source
        trap = source[source.index("cleanup_local()"):source.index("verify_source()")]
        self.assertIn("resume_source", trap)
        self.assertIn("cleanup_target", trap)
        self.assertIn("target_scope_started", trap)
        self.assertIn("delete_owned_firewall_rule", trap)
        self.assertIn("wait_for_target_group_absence", source)
        self.assertIn("purge_and_verify_target_vault", source)
        self.assertIn('az group exists --subscription "$subscription_id"', source)
        self.assertIn('az keyvault list-deleted --subscription "$subscription_id"', source)
        self.assertIn('az deployment group list --subscription "$registry_subscription_id"', source)
        self.assertNotIn("wait_for_resource_group_absence \"$target_resource_group\"", source)
        self.assertNotIn("purge_and_verify_deleted_vault", source)

    def test_manifest_is_safe_and_sealed(self) -> None:
        source = self.source
        manifest = source[
            source.index('manifest_file="$temp_dir/recovery-manifest.json"'):
            source.index('manifest_tag="sha256:${manifest_digest}"')
        ]
        for expected in (
            "jq -n -cS",
            "provider:\"azure-sql-pitr\"",
            "providerConfirmation",
            "desiredState",
            "desiredRevisionId",
            "resolvedPlanReference",
            "releaseManifestReference",
            'artifacts:[{kind:"runtime-image"',
            "requiredSecretReferenceKeys",
            "chmod 400 \"$manifest_file\"",
            'manifest_digest="$(sha256_stream <"$manifest_file")"',
        ):
            self.assertIn(expected, manifest)
        for forbidden in ("connectionString", "signingKey", "token", "secretValue", "source_password_file"):
            self.assertNotIn(forbidden, manifest)

    def test_oci_subjects_are_content_bound_and_reported(self) -> None:
        source = self.source
        for expected in (
            "fetch_bound_oci_json",
            '"application/vnd.elsa-control.resolved-plan.v1+json"',
            '"application/vnd.valence.release-manifest.v2+json"',
            '"io.elsa-control.desired-revision-id"',
            '"io.elsa-control.desired-revision-digest"',
            '"io.elsa-control.release-manifest-reference"',
            '"io.elsa-control.release-manifest-digest"',
            ".images.paid.reference == $image",
            ".release.releaseManifestReference == $releaseReference",
            "resolvedPlan:{reference:$resolvedPlanReference,digest:$resolvedPlanDigest}",
            "releaseManifest:{reference:$releaseManifestReference,digest:$releaseManifestDigest}",
        ):
            self.assertIn(expected, source)
        self.assertIn('oras blob fetch --output "$payload_file"', source)
        self.assertIn('"sha256:$(sha256_stream <"$payload_file")" == "$layer_digest"', source)
        self.assertNotIn("^(oci://)?", source)

    def test_unknown_option_diagnostic_does_not_echo_the_supplied_value(self) -> None:
        marker = "do-not-echo-this-value"
        result = subprocess.run(
            [str(RUNBOOK), "validate", f"--{marker}"],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(2, result.returncode)
        self.assertNotIn(marker, result.stdout + result.stderr)
        self.assertIn("An unknown option was supplied", result.stderr)


if __name__ == "__main__":
    unittest.main()
