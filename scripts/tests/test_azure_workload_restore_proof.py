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
DATABASE_TEMPLATE = ROOT / "infra" / "azure-workload-proof" / "recovery-database.bicep"
ACR_ROLE_TEMPLATE = ROOT / "infra" / "azure-workload-proof" / "acr-pull-role.bicep"


class AzureWorkloadRestoreProofRunbookTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = RUNBOOK.read_text()
        cls.target_template = TARGET_TEMPLATE.read_text()
        cls.database_template = DATABASE_TEMPLATE.read_text()
        cls.acr_role_template = ACR_ROLE_TEMPLATE.read_text()

    def test_cutoff_is_provider_observed_after_real_quiescence(self) -> None:
        source = self.source
        for expected in (
            "az containerapp revision deactivate",
            "az containerapp revision list",
            "az containerapp replica list",
            "verify_source_database_drained",
            "sys.dm_exec_requests",
            "sys.dm_exec_sessions",
            "sys.dm_tran_session_transactions",
            'source_quiesced_at="$source_database_drained_at"',
            'recovery_cutoff_utc="$source_quiesced_at"',
            "provider-confirmed zero-active and zero-replica state",
            "resume_source",
        ):
            self.assertIn(expected, source)
        self.assertNotIn('source_quiesced_at="$(utc_now)"', source)
        self.assertNotIn("timedelta", source)
        self.assertNotIn("AZURE_RECOVERY_POINT_SETTLE_SECONDS", source)
        self.assertNotIn("proof-grade-quiescence", source)
        fresh = source[source.index('else\n  [[ "$target_group_exists" == false ]]') :]
        self.assertLess(fresh.index("quiesce_source ||"), fresh.index("recovery manifest could not be sealed"))

    def test_source_resume_is_idempotent_and_waits_for_external_health(self) -> None:
        source = self.source
        resume = source[source.index("resume_source()") : source.index("verify_target_group_inventory()")]
        self.assertIn("load_source_recovery_lock", resume)
        self.assertIn("release_source_recovery_lock", resume)
        self.assertIn("foreign_active_count", resume)
        self.assertLess(resume.index("foreign_active_count"), resume.index("az containerapp revision activate"))
        self.assertIn('revision_active="$(jq -r', resume)
        self.assertIn('if [[ "$revision_active" != true ]]', resume)
        self.assertIn("for _ in {1..180}", resume)
        self.assertIn("active_exact=", resume)
        self.assertIn('curl --fail --silent --show-error --max-time 30 "$source_endpoint/health"', resume)

    def test_source_quiescence_uses_a_durable_exclusive_provider_lock(self) -> None:
        source = self.source
        lock = source[source.index("read_source_recovery_lock()") : source.index("verify_sql_bootstrap_identity()")]
        for expected in (
            "Microsoft.Authorization/locks/elsa-control-recovery",
            "If-None-Match=*",
            'level:"CanNotDelete"',
            "source_recovery_lock_token",
            "az lock delete",
        ):
            self.assertIn(expected, source if "Microsoft.Authorization" in expected else lock)
        cleanup = source[source.index('if [[ "$mode" == cleanup ]]') : source.index('source_endpoint="$(verify_source)"')]
        self.assertIn('source_endpoint="$(verify_source false)"', cleanup)
        self.assertIn("resume_source", cleanup)
        self.assertLess(cleanup.index("resume_source"), cleanup.index("cleanup_target"))
        self.assertIn('if [[ -z "$expected_manifest_digest" ]]', cleanup)
        self.assertIn("verify_no_target_state_without_manifest", cleanup)
        self.assertIn("target state exists; cleanup requires the sealed manifest digest", cleanup)

    def test_sql_drain_and_bootstrap_are_bound_to_the_governed_current_identity(self) -> None:
        source = self.source
        identity = source[source.index("verify_sql_bootstrap_identity()") : source.index("quiesce_source()")]
        for expected in (
            "az sql server ad-admin list",
            'sql_bootstrap_object_id',
            'sql_bootstrap_login',
            "az account get-access-token --resource https://database.windows.net/",
            'value.get("oid", "")',
            "current Azure principal is not the governed SQL bootstrap identity",
        ):
            self.assertIn(expected, identity)
        self.assertGreaterEqual(source.count("verify_sql_bootstrap_identity"), 4)
        self.assertEqual(2, source.count("--authentication-method ActiveDirectoryAzCli"))
        self.assertNotIn("--authentication-method ActiveDirectoryDefault", source)
        self.assertIn("verify_sql_bootstrap_identity\n  create_owned_firewall_rule", source)
        self.assertIn("verify_sql_bootstrap_identity\ncreate_owned_firewall_rule", source)

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

    def test_existing_database_requires_manifest_and_durable_provider_provenance(self) -> None:
        source = self.source
        for expected in (
            "verify_owned_target_database",
            'tags[\"manifest-digest\"] == $manifest',
            "verify_provider_restore_provenance",
            ".properties.outputs.createMode.value",
            ".properties.outputs.sourceDatabaseId.value",
            ".properties.outputs.restorePointUtc.value",
            "PointInTimeRestore",
            'database_restore_deployment="elsa129-db-${target_name}-${recovery_id}"',
            'target_deployment="elsa129-target-${target_name}-${recovery_id}"',
            'acr_deployment="elsa129-${target_name}-${recovery_id}"',
            'delete_and_verify_group_deployment "$subscription_id" "$source_resource_group" "$database_restore_deployment"',
            "restore-started-utc=\"$restore_accepted_at\"",
        ):
            self.assertIn(expected, source)
        self.assertLess(
            source.index('[[ -z "$target_db_json" ]] || fail "target database appeared before the sealed restore request"'),
            source.index("deploy_target false"),
        )
        self.assertIn(".restoreStartedUtc // empty", source)
        self.assertNotIn("az sql db restore", source)
        for expected in (
            "createMode: 'PointInTimeRestore'",
            "sourceDatabaseId: sourceDatabaseId",
            "restorePointInTime: restorePointUtc",
            "output recoveryManifestDigest",
            "output templateFingerprint",
        ):
            self.assertIn(expected, self.database_template)

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
        self.assertIn("verify_no_target_state_without_manifest", source)
        self.assertIn('target_principal_id="$assignment_principal"', source)
        no_manifest = source[source.index("verify_no_target_state_without_manifest()") : source.index("cleanup_target()")]
        self.assertIn("az acr repository list", no_manifest)
        self.assertIn("az acr repository show-tags", no_manifest)
        self.assertIn('manifest-${recovery_id}-', no_manifest)
        self.assertIn('acr_assignment_description', no_manifest)
        self.assertIn('az role assignment list --subscription "$registry_subscription_id" --scope "$registry_id"', no_manifest)
        self.assertNotIn('--all --scope "$registry_id"', source)
        self.assertNotIn('select(startswith($prefix))] == [$expected]', no_manifest)

    def test_acr_assignment_has_a_durable_recovery_identity(self) -> None:
        self.assertIn("param recoveryId string", self.acr_role_template)
        self.assertIn("description: 'elsa-control-recovery|${recoveryId}|${workloadIdentityId}'", self.acr_role_template)
        source = self.source
        self.assertIn('acr_assignment_description="elsa-control-recovery|${recovery_id}|${target_identity_id}"', source)
        self.assertIn('.description == $description', source)
        self.assertIn('recoveryId="$recovery_id"', source)
        lookup = source[source.index("lookup_owned_acr_assignment()") : source.index("verify_no_target_state_without_manifest()")]
        self.assertIn('azure_cli_error_is_not_found "$error_text"', lookup)
        self.assertIn("ACR deployment record lookup is inconsistent", lookup)
        self.assertNotIn("any(.[]; .name == $name)", lookup)

    def test_vault_cleanup_waits_for_an_expected_tombstone_before_claiming_purge(self) -> None:
        source = self.source
        purge = source[source.index("purge_and_verify_target_vault()") : source.index("cleanup_owned_role_assignment()")]
        self.assertIn('expected_tombstone="$3"', purge)
        self.assertIn("absence_observations >= 6", purge)
        self.assertIn("expected_tombstone == 0", purge)
        self.assertIn("continue", purge)
        self.assertIn('purge_and_verify_target_vault "$target_vault" "${vault_location:-westeurope}" "$vault_tombstone_expected"', source)

    def test_manifest_is_safe_and_sealed(self) -> None:
        source = self.source
        manifest = source[
            source.index('jq -n -cS \\\n    --arg sourceProofName "$source_proof_name"'):
            source.index('manifest_reference="$published_oci_reference"')
        ]
        for expected in (
            "jq -n -cS",
            "provider:\"azure-sql-pitr\"",
            "providerConfirmation",
            "providerSnapshotReference",
            "providerSnapshotDigest",
            "desiredState",
            "desiredRevisionId",
            "resolvedPlanReference",
            "releaseManifestReference",
            'artifacts:[{kind:"runtime-image"',
            "requiredSecretReferenceKeys",
            "secretReferences",
            "adminSecretReference",
            "signingSecretReference",
            "restoreStartedUtc",
            "templateFingerprints",
            "chmod 400 \"$manifest_file\"",
            'manifest_digest="$(sha256_stream <"$manifest_file")"',
            '"application/vnd.elsa-control.recovery-manifest.v1+json"',
        ):
            self.assertIn(expected, manifest)
        for forbidden in ("connectionString", "signingKey", "token", "secretValue", "source_password_file"):
            self.assertNotIn(forbidden, manifest)

    def test_provider_restore_evidence_binds_deployment_database_and_workflow_boundary(self) -> None:
        source = self.source
        verifier = source[
            source.index("verify_provider_restore_provenance()"):
            source.index("verify_owned_target_database()")
        ]
        self.assertIn("provider_restore_deployment_record", verifier)
        self.assertIn(".properties.provisioningState == \"Succeeded\"", verifier)
        self.assertIn(".properties.outputs.recoveryManifestDigest.value", verifier)
        self.assertIn(".properties.outputs.templateFingerprint.value", verifier)
        self.assertIn("az deployment operation group list", verifier)
        self.assertIn("providerOperation", verifier)

        evidence = source[
            source.index('>"$temp_dir/provider-restore-evidence.json"') - 500:
            source.index('cutover_eligible_at="$(utc_now)"')
        ]
        for expected in (
            '--argjson deployment "$provider_restore_deployment_record"',
            'database:{id:$databaseId,status:$databaseStatus}',
            'workflowBoundary:{prePoint:$preDefinition,postPointAbsent:$postDefinition,status:"Finished"}',
            'publish_bound_oci_json "$temp_dir/provider-restore-evidence.json"',
            'provider_restore_evidence_digest="$published_oci_digest"',
        ):
            self.assertIn(expected, evidence)
        final_evidence = source[source.index("cleanup_target\npost_cleanup_source_endpoint"):]
        self.assertNotIn("providerSnapshotPlan", final_evidence)
        self.assertNotIn("providerRestoreEvidenceRecord", final_evidence)
        self.assertNotIn("sourceDatabaseId", final_evidence)
        self.assertNotIn("targetDatabaseId", final_evidence)

    def test_retry_loads_the_same_durable_manifest_before_source_mutation(self) -> None:
        source = self.source
        retry_start = source.index('if [[ -n "$expected_manifest_reference" ]]', source.index('target_group_exists="$(az group exists'))
        retry = source[
            retry_start:
            source.index('else\n  [[ "$target_group_exists" == false ]]')
        ]
        for expected in (
            'fetch_bound_oci_json "$expected_manifest_reference" "recovery-manifest.json"',
            '"application/vnd.elsa-control.recovery-manifest.v1+json"',
            '[[ "$(sha256_stream <"$manifest_file")" == "$expected_manifest_digest" ]]',
            'source.secretReferences.adminPassword',
            'source.secretReferences.identitySigningKey',
            'templateFingerprints.target',
            'templateFingerprints.database',
            'provider_snapshot_reference',
            '"${provider_snapshot_reference%@sha256:*}" == "$recovery_evidence_repository"',
            'same_instant "$source_quiesced_at" "$restore_point_utc"',
            'cleanup_target',
            'verify_no_target_state_without_manifest',
            'if ! verify_no_target_state_without_manifest "$manifest_digest"',
        ):
            self.assertIn(expected, retry)
        self.assertNotIn('--mode create', retry)
        self.assertNotIn('quiesce_source', retry)

    def test_retry_reconciles_only_an_exact_terminal_restore_request(self) -> None:
        source = self.source
        verifier = source[
            source.index("verify_provider_restore_request_identity()"):
            source.index("verify_owned_target_database()")
        ]
        for expected in (
            ".properties.parameters.sourceDatabaseId.value",
            ".properties.parameters.targetDatabaseName.value",
            ".properties.parameters.restorePointUtc.value",
            ".properties.parameters.recoveryManifestDigest.value",
            ".properties.parameters.recoveryId.value",
            ".properties.parameters.templateFingerprint.value",
            "wait_for_owned_restore_deployment_terminal",
            "Succeeded|Failed|Canceled",
            "Accepted|Running|Creating|Updating|Canceling",
        ):
            self.assertIn(expected, verifier)
        cleanup_start = source.index("cleanup_target()")
        cleanup = source[cleanup_start : source.index('if [[ "$mode" == cleanup ]]', cleanup_start)]
        self.assertIn('wait_for_owned_restore_deployment_terminal "$cleanup_manifest_tag"', cleanup)
        self.assertIn('if [[ "$provider_restore_cleanup_state" == Succeeded ]]', cleanup)
        cleanup_execution = source.index('if [[ "$mode" == cleanup ]]', cleanup_start)
        self.assertLess(
            source.index('database_template_fingerprint="$(az bicep build'),
            cleanup_execution,
        )

    def test_provider_and_manifest_evidence_are_private_immutable_oci_artifacts(self) -> None:
        source = self.source
        for expected in (
            'recovery_evidence_repository="${registry_name}.azurecr.io/control-proof/recovery-evidence"',
            'publish_bound_oci_json()',
            'oras push --no-tty --artifact-type',
            'fetch_bound_oci_json "$immutable_reference"',
            'application/vnd.elsa-control.recovery-provider-snapshot.v1+json',
            'application/vnd.elsa-control.recovery-provider-restore-evidence.v1+json',
            'recoveryManifest:{reference:$manifestReference,digest:$manifestArtifactDigest,contentDigest:$manifestDigest}',
            'kind:"recovery-manifest-sealed"',
        ):
            self.assertIn(expected, source)
        self.assertNotIn("snapshot://", source)
        self.assertLess(
            source.index('publish_bound_oci_json "$temp_dir/provider-restore-evidence.json"'),
            source.index("cleanup_target\npost_cleanup_source_endpoint"),
        )

    def test_final_source_health_uses_bounded_full_source_verification(self) -> None:
        final = self.source[self.source.index("cleanup_target\npost_cleanup_source_endpoint"):]
        self.assertIn('post_cleanup_source_endpoint="$(verify_source)"', final)
        self.assertIn('[[ "$post_cleanup_source_endpoint" == "$source_endpoint" ]]', final)
        self.assertNotIn('curl --fail --silent --show-error --max-time 30 "$source_endpoint/health"', final)

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
