import { useEffect, useRef, useState } from "react";
import type { ComponentProps } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, ShieldAlert } from "lucide-react";
import { Link, useParams } from "react-router-dom";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { Badge, Button, EmptyState, Input, SecondaryButton, Table, buttonClassName } from "@/components/ui";
import { getDeploymentCredentialReferences } from "@/features/deployments/deploymentApi";
import {
  activateDraftSourceOwnershipBinding,
  createHealingAuthorityProfile,
  createSourceOwnershipBinding,
  createHealingConfirmation,
  getHealingComponentManifests,
  getHealingConfiguration,
  getHealingAuthorityCatalog,
  getSourceOwnershipBindings,
  registerHealingComponentManifest,
  resumeHealing,
  stopHealing,
  transitionHealingComponentManifest,
  transitionHealingProviderConnection,
  transitionSourceOwnershipBinding,
  updateHealingConfiguration,
  validateHealingProviderConnection
} from "@/features/healing/healingApi";
import type { ActivateSourceOwnershipBindingRequest, CreateHealingAuthorityProfileRequest, HealingApplicationConfiguration, UpdateHealingConfigurationRequest } from "@/features/healing/healingModels";
import { queryKeys } from "@/lib/query/queryClient";

export function HealingLandingPage() {
  return (
    <EmptyState
      title="Healing operations"
      description="Review normalized incidents, or open a deployed application to configure exception discovery and approved component ownership. Automatic discovery also requires the Platform OpenTelemetry module."
      action={<div className="flex flex-wrap justify-center gap-2"><Link className={buttonClassName()} to="/admin/healing/incidents">View incidents</Link><Link className={buttonClassName("secondary")} to="/admin/deployments/applications">Open applications</Link></div>}
    />
  );
}

export function HealingConfigurationPage() {
  const { applicationId = "" } = useParams();
  const { selectedWorkspaceId } = useWorkspaceContext();
  const queryClient = useQueryClient();
  const configuration = useQuery({
    queryKey: queryKeys.healingConfiguration(selectedWorkspaceId, applicationId),
    queryFn: () => getHealingConfiguration(selectedWorkspaceId, applicationId),
    enabled: Boolean(selectedWorkspaceId && applicationId)
  });
  const [draft, setDraft] = useState<UpdateHealingConfigurationRequest | null>(null);
  const [confirmStop, setConfirmStop] = useState(false);
  const [confirmResume, setConfirmResume] = useState(false);
  const [confirmAutoMerge, setConfirmAutoMerge] = useState(false);
  const stopTriggerRef = useRef<HTMLButtonElement>(null);
  const saveTriggerRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (configuration.data) setDraft(toUpdateRequest(configuration.data));
  }, [configuration.data]);

  const save = useMutation({
    mutationFn: async (request: UpdateHealingConfigurationRequest) => {
      if (request.automaticMergeEnabled !== configuration.data?.automaticMergeEnabled) {
        const confirmation = await createHealingConfirmation(selectedWorkspaceId, applicationId, "HealingAutomaticMerge", request.automaticMergeEnabled);
        return updateHealingConfiguration(selectedWorkspaceId, applicationId, { ...request, confirmationId: confirmation.id });
      }
      return updateHealingConfiguration(selectedWorkspaceId, applicationId, request);
    },
    onSuccess: (value) => queryClient.setQueryData(queryKeys.healingConfiguration(selectedWorkspaceId, applicationId), value)
  });
  const stop = useMutation({
    mutationFn: () => stopHealing(selectedWorkspaceId, applicationId),
    onSuccess: (value) => {
      queryClient.setQueryData(queryKeys.healingConfiguration(selectedWorkspaceId, applicationId), value);
      setConfirmStop(false);
      requestAnimationFrame(() => stopTriggerRef.current?.focus());
    }
  });
  const resume = useMutation({
    mutationFn: () => resumeHealing(selectedWorkspaceId, applicationId),
    onSuccess: (value) => {
      queryClient.setQueryData(queryKeys.healingConfiguration(selectedWorkspaceId, applicationId), value);
      setConfirmResume(false);
      requestAnimationFrame(() => stopTriggerRef.current?.focus());
    }
  });

  if (configuration.isError)
    return <RequestStateView state="unexpected" title="Healing configuration could not load" />;
  if (configuration.isLoading || !configuration.data || !draft)
    return <RequestStateView state="loading" title="Loading Healing configuration" />;

  const value = configuration.data;
  const canConfigure = value.permissions.includes("healing.configure");
  const canConfigureAutoMerge = value.permissions.includes("healing.automerge.configure");

  return (
    <section className="space-y-6">
      <HealingPageHeader applicationId={applicationId} title="Healing configuration" applicationName={value.applicationName} />
      {value.applicationKillSwitch ? <StopBanner /> : null}
      {!canConfigure ? (
        <p className="rounded-ui border border-border bg-muted p-3 text-sm">
          You can review effective policy, but healing.configure is required to make changes.
        </p>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(16rem,1fr)]">
        <form
          className="space-y-5 rounded-ui border border-border bg-surface p-5"
          onSubmit={(event) => {
            event.preventDefault();
            if (draft.automaticMergeEnabled !== value.automaticMergeEnabled)
              setConfirmAutoMerge(true);
            else
              save.mutate(draft);
          }}
        >
          <fieldset className="space-y-3" disabled={!canConfigure || save.isPending}>
            <legend className="text-base font-semibold">Automation stages</legend>
            <Toggle label="Automatic exception discovery" checked={draft.discoveryEnabled} onChange={(checked) => setDraft({ ...draft, discoveryEnabled: checked })} />
            <Toggle label="Repair dispatch" checked={draft.repairDispatchEnabled} onChange={(checked) => setDraft({ ...draft, repairDispatchEnabled: checked })} />
            <Toggle
              label="Automatic merge"
              checked={draft.automaticMergeEnabled}
              disabled={!canConfigureAutoMerge}
              onChange={(checked) => setDraft({ ...draft, automaticMergeEnabled: checked })}
            />
            {!canConfigureAutoMerge ? <p className="text-xs text-muted-foreground">healing.automerge.configure and target confirmation are required.</p> : null}
          </fieldset>

          <fieldset className="grid gap-4 sm:grid-cols-2" disabled={!canConfigure || save.isPending}>
            <legend className="mb-3 text-base font-semibold">Limits and verification</legend>
            <LabeledInput label="Attempt limit" type="number" min={1} max={2} value={draft.defaultAttemptLimit} onChange={(value) => setDraft({ ...draft, defaultAttemptLimit: Number(value) })} />
            <LabeledInput label="Concurrency budget" type="number" min={1} max={32} value={draft.concurrencyBudget} onChange={(value) => setDraft({ ...draft, concurrencyBudget: Number(value) })} />
            <LabeledInput label="Inference budget" type="number" min={0} max={2000000} value={draft.inferenceBudget} onChange={(value) => setDraft({ ...draft, inferenceBudget: Number(value) })} />
            <LabeledInput label="Repository run budget" type="number" min={0} max={10} value={draft.repositoryRunBudget} onChange={(value) => setDraft({ ...draft, repositoryRunBudget: Number(value) })} />
            <LabeledInput label="Time budget" value={draft.timeBudget} onChange={(value) => setDraft({ ...draft, timeBudget: value })} />
            <LabeledInput label="Verification window" value={draft.verificationWindow} onChange={(value) => setDraft({ ...draft, verificationWindow: value })} />
          </fieldset>

          <fieldset className="space-y-2" disabled={!canConfigure || save.isPending}>
            <legend className="text-base font-semibold">Classification policy</legend>
            <p className="text-xs text-muted-foreground">
              Versioned JSON may set failure-class thresholds, debounceSeconds, and authorized class overrides.
            </p>
            <label className="block text-sm font-medium" htmlFor="healing-classification-policy">Application policy JSON</label>
            <textarea
              id="healing-classification-policy"
              className="min-h-28 w-full rounded-ui border border-border bg-background px-3 py-2 font-mono text-sm"
              value={draft.classificationPolicyJson ?? "{}"}
              onChange={(event) => setDraft({ ...draft, classificationPolicyJson: event.target.value })}
              spellCheck={false}
            />
          </fieldset>

          <fieldset className="space-y-4" disabled={!canConfigure || save.isPending}>
            <legend className="text-base font-semibold">Environment overrides</legend>
            {draft.environments.map((environment, index) => (
              <div key={environment.environmentId} className="space-y-3 rounded-ui border border-border p-4">
                <h3 className="font-medium">{environment.name}</h3>
                <div className="grid gap-3 sm:grid-cols-2">
                  <Toggle label={`${environment.name} discovery`} checked={environment.discoveryEnabled} onChange={(checked) => setDraft(updateEnvironment(draft, index, { discoveryEnabled: checked }))} />
                  <Toggle label={`${environment.name} repair dispatch`} checked={environment.repairDispatchEnabled} onChange={(checked) => setDraft(updateEnvironment(draft, index, { repairDispatchEnabled: checked }))} />
                  <Toggle label={`${environment.name} emergency stop`} checked={environment.environmentKillSwitch} onChange={(checked) => setDraft(updateEnvironment(draft, index, { environmentKillSwitch: checked }))} />
                  <LabeledInput label={`${environment.name} occurrence threshold`} type="number" min={1} value={environment.occurrenceThreshold ?? 1} onChange={(value) => setDraft(updateEnvironment(draft, index, { occurrenceThreshold: Number(value) }))} />
                  <LabeledInput label={`${environment.name} debounce window`} value={environment.debounceWindow ?? "00:00:00"} onChange={(value) => setDraft(updateEnvironment(draft, index, { debounceWindow: value }))} />
                </div>
                <label className="block text-sm font-medium" htmlFor={`healing-environment-policy-${environment.environmentId}`}>
                  {environment.name} classification policy JSON
                </label>
                <textarea
                  id={`healing-environment-policy-${environment.environmentId}`}
                  className="min-h-24 w-full rounded-ui border border-border bg-background px-3 py-2 font-mono text-sm"
                  value={environment.classificationPolicyJson ?? "{}"}
                  onChange={(event) => setDraft(updateEnvironment(draft, index, { classificationPolicyJson: event.target.value }))}
                  spellCheck={false}
                />
              </div>
            ))}
          </fieldset>

          <div className="flex flex-wrap items-center gap-3">
            <Button ref={saveTriggerRef} type="submit" disabled={!canConfigure || save.isPending}>Save configuration</Button>
            {save.isSuccess ? <span role="status" className="text-sm text-muted-foreground">Configuration saved.</span> : null}
            {save.isError ? <span role="alert" className="text-sm text-danger">Configuration could not be saved.</span> : null}
          </div>
        </form>

        <aside className="space-y-4" aria-label="Healing readiness and safety">
          <ReadinessCard label="Component manifest" value={value.manifestReadiness} />
          <ReadinessCard label="Source provider" value={value.providerReadiness} />
          <div className="rounded-ui border border-danger/30 bg-surface p-4">
            <h2 className="font-semibold">Emergency stop</h2>
            <p className="mt-2 text-sm text-muted-foreground">Stops new repair dispatch, publication, and automatic merge without deleting incident history.</p>
            {value.applicationKillSwitch ? (
              <SecondaryButton ref={stopTriggerRef} className="mt-4" type="button" disabled={!canConfigure} onClick={() => setConfirmResume(true)}>
                Resume Healing
              </SecondaryButton>
            ) : (
              <SecondaryButton ref={stopTriggerRef} className="mt-4" type="button" disabled={!canConfigure} onClick={() => setConfirmStop(true)}>
                Activate emergency stop
              </SecondaryButton>
            )}
          </div>
        </aside>
      </div>
      {confirmStop ? <EmergencyStopDialog applicationName={value.applicationName} pending={stop.isPending} error={stop.isError} onCancel={() => { setConfirmStop(false); requestAnimationFrame(() => stopTriggerRef.current?.focus()); }} onConfirm={() => stop.mutate()} /> : null}
      {confirmResume ? <EmergencyResumeDialog applicationName={value.applicationName} pending={resume.isPending} error={resume.isError} onCancel={() => { setConfirmResume(false); requestAnimationFrame(() => stopTriggerRef.current?.focus()); }} onConfirm={() => resume.mutate()} /> : null}
      {confirmAutoMerge ? <AutomaticMergeDialog applicationName={value.applicationName} enabled={draft.automaticMergeEnabled} pending={save.isPending} onCancel={() => { setConfirmAutoMerge(false); requestAnimationFrame(() => saveTriggerRef.current?.focus()); }} onConfirm={() => { setConfirmAutoMerge(false); save.mutate(draft); requestAnimationFrame(() => saveTriggerRef.current?.focus()); }} /> : null}
    </section>
  );
}

export function HealingComponentsPage() {
  const { applicationId = "" } = useParams();
  const { selectedWorkspaceId } = useWorkspaceContext();
  const manifests = useQuery({
    queryKey: queryKeys.healingManifests(selectedWorkspaceId, applicationId),
    queryFn: () => getHealingComponentManifests(selectedWorkspaceId, applicationId),
    enabled: Boolean(selectedWorkspaceId && applicationId)
  });
  const bindings = useQuery({
    queryKey: queryKeys.healingBindings(selectedWorkspaceId, applicationId),
    queryFn: () => getSourceOwnershipBindings(selectedWorkspaceId, applicationId),
    enabled: Boolean(selectedWorkspaceId && applicationId)
  });
  const authority = useQuery({
    queryKey: queryKeys.healingAuthorityCatalog(selectedWorkspaceId, applicationId),
    queryFn: () => getHealingAuthorityCatalog(selectedWorkspaceId, applicationId),
    enabled: Boolean(selectedWorkspaceId && applicationId)
  });
  const credentialReferences = useQuery({
    queryKey: queryKeys.deploymentCredentialReferences(selectedWorkspaceId),
    queryFn: () => getDeploymentCredentialReferences(selectedWorkspaceId),
    enabled: Boolean(selectedWorkspaceId)
  });
  const queryClient = useQueryClient();
  const [revisionId, setRevisionId] = useState("");
  const [manifestJson, setManifestJson] = useState("");
  const [selectedManifestId, setSelectedManifestId] = useState("");
  const [bindingDraft, setBindingDraft] = useState<ActivateSourceOwnershipBindingRequest>(emptyBindingDraft);
  const [authorityDraft, setAuthorityDraft] = useState<CreateHealingAuthorityProfileRequest>(emptyAuthorityDraft);
  const [confirmAuthorityAutoMerge, setConfirmAuthorityAutoMerge] = useState(false);
  const authoritySubmitRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const items = manifests.data?.items ?? [];
    if (items.length > 0 && !items.some((item) => item.id === selectedManifestId))
      setSelectedManifestId(items[0].id);
  }, [manifests.data?.items, selectedManifestId]);
  const registerManifest = useMutation({
    mutationFn: () => registerHealingComponentManifest(selectedWorkspaceId, applicationId, revisionId, manifestJson),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.healingManifests(selectedWorkspaceId, applicationId) })
  });
  const createBinding = useMutation({
    mutationFn: () => createSourceOwnershipBinding(selectedWorkspaceId, applicationId, bindingDraft),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.healingBindings(selectedWorkspaceId, applicationId) })
  });
  const createAuthority = useMutation({
    mutationFn: async () => {
      if (authorityDraft.automaticMergeEnabled) {
        const confirmation = await createHealingConfirmation(
          selectedWorkspaceId, applicationId, "HealingAutomaticMerge", true);
        return createHealingAuthorityProfile(
          selectedWorkspaceId, applicationId, { ...authorityDraft, confirmationId: confirmation.id });
      }
      return createHealingAuthorityProfile(selectedWorkspaceId, applicationId, authorityDraft);
    },
    onSuccess: (profile) => {
      setBindingDraft({
        ...bindingDraft,
        providerConnectionId: "",
        repositoryProviderId: "github",
        repositoryOwner: "",
        repositoryName: "",
        pathPolicyId: profile.pathPolicy.id,
        evidencePolicyId: profile.evidencePolicy.id,
        mergePolicyId: profile.mergePolicy.id
      });
      queryClient.invalidateQueries({ queryKey: queryKeys.healingAuthorityCatalog(selectedWorkspaceId, applicationId) });
    }
  });
  const transitionProvider = useMutation({
    mutationFn: ({ providerConnectionId, transition, version }: { providerConnectionId: string; transition: "suspend" | "revoke"; version: string }) =>
      transitionHealingProviderConnection(selectedWorkspaceId, applicationId, providerConnectionId, transition, version),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.healingAuthorityCatalog(selectedWorkspaceId, applicationId) })
  });
  const validateProvider = useMutation({
    mutationFn: ({ providerConnectionId, version }: { providerConnectionId: string; version: string }) =>
      validateHealingProviderConnection(selectedWorkspaceId, applicationId, providerConnectionId, version),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.healingAuthorityCatalog(selectedWorkspaceId, applicationId) })
  });
  const transitionBinding = useMutation({
    mutationFn: ({ bindingId, transition }: { bindingId: string; transition: "suspend" | "revoke" }) =>
      transitionSourceOwnershipBinding(selectedWorkspaceId, applicationId, bindingId, transition),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.healingBindings(selectedWorkspaceId, applicationId) })
  });
  const activateBinding = useMutation({
    mutationFn: (bindingId: string) => activateDraftSourceOwnershipBinding(selectedWorkspaceId, applicationId, bindingId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.healingBindings(selectedWorkspaceId, applicationId) })
  });
  const transitionManifest = useMutation({
    mutationFn: ({ manifestId, transition }: { manifestId: string; transition: "verify" | "revoke" }) =>
      transitionHealingComponentManifest(selectedWorkspaceId, applicationId, manifestId, transition),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.healingManifests(selectedWorkspaceId, applicationId) })
  });

  if (manifests.isLoading || bindings.isLoading || authority.isLoading || credentialReferences.isLoading)
    return <RequestStateView state="loading" title="Loading Healing components" />;
  if (manifests.isError || bindings.isError || authority.isError || credentialReferences.isError)
    return <RequestStateView state="unexpected" title="Healing components could not load" />;

  const manifest = manifests.data?.items.find((item) => item.id === selectedManifestId) ?? manifests.data?.items[0];
  const canConfigure = bindings.data?.permissions?.includes("healing.configure") ?? false;
  const canConfigureAutoMerge = bindings.data?.permissions?.includes("healing.automerge.configure") ?? false;
  const canApproveOwnership = canConfigure && (bindings.data?.canApproveOwnership ?? manifests.data?.canApproveOwnership ?? false);
  return (
    <section className="space-y-6">
      <HealingPageHeader applicationId={applicationId} title="Components and source ownership" />
      {!manifest ? (
        <RequestStateView state="empty" title="No component manifests registered" description="Register a trusted revision-bound manifest before components can become repairable." />
      ) : <>
        {(manifests.data?.items.length ?? 0) > 1 ? <LabeledSelect
          label="Manifest revision"
          value={manifest.id}
          onChange={setSelectedManifestId}
          options={manifests.data!.items.map((item) => ({ value: item.id, label: `${item.sourceRevision} · ${item.trustState}` }))}
        /> : null}
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Badge>{manifest.automationAuthoritative
            ? "Delivery attested — automation authoritative"
            : manifest.trustState === "Verified"
              ? "Owner verified — observation only"
              : `${manifest.trustState} manifest`}</Badge>
          <span className="text-muted-foreground">Revision {manifest.sourceRevision}</span>
          {canApproveOwnership && manifest.trustState === "Unverified" ? <SecondaryButton type="button" onClick={() => transitionManifest.mutate({ manifestId: manifest.id, transition: "verify" })}>Verify manifest</SecondaryButton> : null}
          {canApproveOwnership && manifest.trustState === "Verified" ? <SecondaryButton type="button" onClick={() => {
            if (window.confirm(`Revoke trust for manifest ${manifest.id} at revision ${manifest.sourceRevision}? Components from this manifest will become observation-only.`))
              transitionManifest.mutate({ manifestId: manifest.id, transition: "revoke" });
          }}>Revoke manifest trust</SecondaryButton> : null}
        </div>
        <Table>
        <table className="w-full min-w-[52rem] text-left text-sm">
          <thead className="bg-muted text-xs uppercase tracking-wide text-muted-foreground">
            <tr><th scope="col" className="p-3">Component</th><th scope="col" className="p-3">Hash</th><th scope="col" className="p-3">Source authority</th><th scope="col" className="p-3">Repair eligibility</th></tr>
          </thead>
          <tbody className="divide-y divide-border">
            {manifest.entries.map((component) => (
              <tr key={component.componentKey} className="align-top">
                <th scope="row" className="p-3 font-medium">{component.name}<span className="block text-xs font-normal text-muted-foreground">{component.kind} · {component.version ?? "Unversioned"}</span></th>
                <td className="p-3 font-mono text-xs">{component.contentHash}</td>
                <td className="p-3">
                  {component.bindingId ? "Authorized binding" : component.repositorySuggestion ? <><span className="block">{component.repositorySuggestion}</span><span className="text-xs font-medium">Suggested—not authorized</span></> : "No source metadata"}
                  {component.matchingBindings?.length ? <ul className="mt-2 space-y-1 text-xs text-muted-foreground">{component.matchingBindings.map((binding) => <li key={binding.id}>{binding.name} · priority {binding.priority} · {binding.repository}@{binding.targetBranch}</li>)}</ul> : null}
                </td>
                <td className="p-3 font-medium">{component.repairEligibility === "Ambiguous" ? "Ambiguous—repair blocked" : component.repairEligibility}{component.reasonCodes?.length ? <span className="mt-1 block text-xs font-normal text-muted-foreground">{component.reasonCodes.join(", ")}</span> : null}</td>
              </tr>
            ))}
          </tbody>
        </table>
        </Table>
      </>}

      {canConfigure ? (
        <div className="grid gap-4 xl:grid-cols-2">
          <form className="space-y-3 rounded-ui border border-border bg-surface p-4" onSubmit={(event) => { event.preventDefault(); registerManifest.mutate(); }}>
            <h2 className="font-semibold">Register component manifest</h2>
            <LabeledInput label="Revision ID" required value={revisionId} onChange={setRevisionId} />
            <label className="block space-y-1 text-sm font-medium" htmlFor="healing-manifest-json">Manifest JSON
              <textarea id="healing-manifest-json" className="min-h-36 w-full rounded-ui border border-input bg-background p-2 font-mono text-xs" required value={manifestJson} onChange={(event) => setManifestJson(event.target.value)} />
            </label>
            <Button type="submit" disabled={registerManifest.isPending}>Register manifest</Button>
            {registerManifest.isError ? <p role="alert" className="text-sm text-danger">Manifest could not be registered.</p> : null}
            {transitionManifest.isError ? <p role="alert" className="text-sm text-danger">Manifest trust could not be changed.</p> : null}
          </form>

          {canApproveOwnership ? <form className="space-y-3 rounded-ui border border-border bg-surface p-4" onSubmit={(event) => { event.preventDefault(); if (authorityDraft.automaticMergeEnabled) setConfirmAuthorityAutoMerge(true); else createAuthority.mutate(); }}>
            <h2 className="font-semibold">Connect a GitHub repair repository</h2>
            <p className="text-xs text-muted-foreground">Connect a GitHub App installation to a protected workspace credential. Store the write-only credential as JSON containing appId and privateKeyPem. The connection remains pending and cannot authorize repair until Platform validates the installation and repository identity.</p>
            <div className="grid gap-3 sm:grid-cols-2">
              <LabeledInput label="Profile name" required value={authorityDraft.name} onChange={(name) => setAuthorityDraft({ ...authorityDraft, name })} />
              <LabeledInput label="GitHub installation ID" required value={authorityDraft.installationId} onChange={(installationId) => setAuthorityDraft({ ...authorityDraft, installationId })} />
              <LabeledInput label="GitHub repository owner" required value={authorityDraft.repositoryOwner} onChange={(repositoryOwner) => setAuthorityDraft({ ...authorityDraft, repositoryOwner })} />
              <LabeledInput label="GitHub repository name" required value={authorityDraft.repositoryName} onChange={(repositoryName) => setAuthorityDraft({ ...authorityDraft, repositoryName })} />
              <LabeledSelect label="GitHub App credential" required value={authorityDraft.credentialReferenceId} onChange={(credentialReferenceId) => setAuthorityDraft({ ...authorityDraft, credentialReferenceId })} options={(credentialReferences.data?.items ?? []).filter((item) => item.status === "Active").map((item) => ({ value: item.id, label: `${item.name} · ${item.secretStoreName}` }))} />
              <LabeledSelect label="GitHub webhook HMAC credential" required value={authorityDraft.webhookSecretCredentialReferenceId ?? ""} onChange={(webhookSecretCredentialReferenceId) => setAuthorityDraft({ ...authorityDraft, webhookSecretCredentialReferenceId })} options={(credentialReferences.data?.items ?? []).filter((item) => item.status === "Active" && item.id !== authorityDraft.credentialReferenceId).map((item) => ({ value: item.id, label: `${item.name} · ${item.secretStoreName}` }))} />
              <LabeledInput label="Allowed source roots" required value={(authorityDraft.allowedRoots ?? []).join(", ")} onChange={(value) => setAuthorityDraft({ ...authorityDraft, allowedRoots: splitList(value) })} />
              <LabeledInput label="Forbidden source roots" value={(authorityDraft.forbiddenRoots ?? []).join(", ")} onChange={(value) => setAuthorityDraft({ ...authorityDraft, forbiddenRoots: splitList(value) })} />
              <LabeledInput label="Required checks" value={(authorityDraft.requiredChecks ?? []).join(", ")} onChange={(value) => setAuthorityDraft({ ...authorityDraft, requiredChecks: splitList(value) })} />
              <LabeledInput label="Forbidden change categories" value={(authorityDraft.forbiddenChangeCategories ?? []).join(", ")} onChange={(value) => setAuthorityDraft({ ...authorityDraft, forbiddenChangeCategories: splitList(value) })} />
              <LabeledInput label="Maximum files" type="number" min={1} max={100} value={authorityDraft.maxFiles ?? 20} onChange={(value) => setAuthorityDraft({ ...authorityDraft, maxFiles: Number(value) })} />
              <LabeledInput label="Maximum changed lines" type="number" min={1} max={10000} value={authorityDraft.maxChangedLines ?? 1000} onChange={(value) => setAuthorityDraft({ ...authorityDraft, maxChangedLines: Number(value) })} />
              <LabeledInput label="Maximum patch bytes" type="number" min={1} max={10000000} value={authorityDraft.maxPatchBytes ?? 1000000} onChange={(value) => setAuthorityDraft({ ...authorityDraft, maxPatchBytes: Number(value) })} />
              <LabeledInput label="Minimum inference confidence" type="number" min={0} max={1} step={0.01} value={authorityDraft.minimumInferenceConfidence ?? 0.9} onChange={(value) => setAuthorityDraft({ ...authorityDraft, minimumInferenceConfidence: Number(value) })} />
              <LabeledInput label="Independent verifier" value={authorityDraft.independentVerifier ?? ""} onChange={(independentVerifier) => setAuthorityDraft({ ...authorityDraft, independentVerifier })} />
            </div>
            <Toggle label="Require reproduction before proposing a repair" checked={authorityDraft.requireReproduction ?? false} onChange={(requireReproduction) => setAuthorityDraft({ ...authorityDraft, requireReproduction })} />
            <Toggle label="Allow high-confidence inference when reproduction is unavailable" checked={authorityDraft.allowHighConfidenceInference ?? true} onChange={(allowHighConfidenceInference) => setAuthorityDraft({ ...authorityDraft, allowHighConfidenceInference })} />
            <Toggle label="Allow automatic merge when all gates pass" checked={authorityDraft.automaticMergeEnabled ?? false} disabled={!canConfigureAutoMerge} onChange={(automaticMergeEnabled) => setAuthorityDraft({ ...authorityDraft, automaticMergeEnabled })} />
            {!canConfigureAutoMerge ? <p className="text-xs text-muted-foreground">healing.automerge.configure and target confirmation are required.</p> : null}
            <Toggle label="Require rollback or emergency-stop capability" checked={authorityDraft.requireRollbackOrStopCapability ?? true} onChange={(requireRollbackOrStopCapability) => setAuthorityDraft({ ...authorityDraft, requireRollbackOrStopCapability })} />
            <Button ref={authoritySubmitRef} type="submit" disabled={createAuthority.isPending}>Create pending connection and policies</Button>
            {createAuthority.isSuccess ? <p role="status" className="text-sm text-muted-foreground">Policy profile created. Validate the provider connection before creating an active binding.</p> : null}
            {createAuthority.isError ? <p role="alert" className="text-sm text-danger">Repository profile could not be created.</p> : null}
          </form> : null}

          <form className="space-y-3 rounded-ui border border-border bg-surface p-4" onSubmit={(event) => { event.preventDefault(); createBinding.mutate(); }}>
            <h2 className="font-semibold">Create source ownership draft</h2>
            <p className="text-xs text-muted-foreground">Suggestions remain non-authoritative until an owner activates a complete provider, repository, workflow, and policy binding.</p>
            <div className="grid gap-3 sm:grid-cols-2">
              <LabeledSelect label="Selector kind" required value={bindingDraft.selectorKind} onChange={(selectorKind) => setBindingDraft({ ...bindingDraft, selectorKind: selectorKind as ActivateSourceOwnershipBindingRequest["selectorKind"] })} options={[
                { value: "Application", label: "Application" },
                { value: "Package", label: "Package" },
                { value: "Assembly", label: "Assembly" },
                { value: "ComponentKey", label: "Component key" }
              ]} />
              <LabeledSelect label="Provider connection" required value={bindingDraft.providerConnectionId} onChange={(providerConnectionId) => {
                const provider = authority.data?.providerConnections.find((item) => item.id === providerConnectionId);
                setBindingDraft({
                  ...bindingDraft,
                  providerConnectionId,
                  repositoryProviderId: provider?.repositoryProviderId ?? "github",
                  repositoryOwner: provider?.repositoryOwner ?? "",
                  repositoryName: provider?.repositoryName ?? ""
                });
              }} options={(authority.data?.providerConnections ?? []).filter((item) => item.status === "Active").map((item) => ({ value: item.id, label: `${item.repositoryOwner}/${item.repositoryName} · installation ${item.installationId}` }))} />
              <LabeledSelect label="Path policy" required value={bindingDraft.pathPolicyId} onChange={(pathPolicyId) => setBindingDraft({ ...bindingDraft, pathPolicyId })} options={(authority.data?.pathPolicies ?? []).map(toPolicyOption)} />
              <LabeledSelect label="Evidence policy" required value={bindingDraft.evidencePolicyId} onChange={(evidencePolicyId) => setBindingDraft({ ...bindingDraft, evidencePolicyId })} options={(authority.data?.evidencePolicies ?? []).map(toPolicyOption)} />
              <LabeledSelect label="Merge policy" required value={bindingDraft.mergePolicyId} onChange={(mergePolicyId) => setBindingDraft({ ...bindingDraft, mergePolicyId })} options={(authority.data?.mergePolicies ?? []).map(toPolicyOption)} />
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <LabeledInput label={`${bindingDraft.selectorKind} selector`} required value={bindingDraft.selectorPattern} onChange={(selectorPattern) => setBindingDraft({ ...bindingDraft, selectorPattern })} />
              {bindingFields.map((field) => (
                <LabeledInput key={field.key} label={field.label} required value={String(bindingDraft[field.key])} onChange={(value) => setBindingDraft({ ...bindingDraft, [field.key]: field.key === "priority" ? Number(value) : value })} />
              ))}
            </div>
            <Button type="submit" disabled={createBinding.isPending}>Create binding draft</Button>
            {createBinding.isError ? <p role="alert" className="text-sm text-danger">Binding draft could not be created.</p> : null}
          </form>
        </div>
      ) : <p className="rounded-ui border border-border bg-muted p-3 text-sm">healing.configure and workspace owner approval are required to register or change source ownership.</p>}

      <section className="space-y-3" aria-labelledby="ownership-bindings-title">
        <h2 id="ownership-bindings-title" className="text-base font-semibold">Approved ownership bindings</h2>
        {bindings.data?.items.length ? bindings.data.items.map((binding) => (
          <article key={binding.id} className="rounded-ui border border-border bg-surface p-4">
            <div className="flex flex-wrap items-center justify-between gap-2"><h3 className="font-medium">{binding.name}</h3><Badge>{binding.status}</Badge></div>
            <p className="mt-2 text-sm text-muted-foreground">{binding.selectorKind} {binding.selectorPattern} → {binding.repository}@{binding.targetBranch}</p>
            {canApproveOwnership && binding.status !== "Revoked" ? <div className="mt-3 flex gap-2">
              {binding.status === "Draft" || binding.status === "Suspended" ? <SecondaryButton type="button" disabled={activateBinding.isPending} onClick={() => activateBinding.mutate(binding.id)}>Activate</SecondaryButton> : null}
              {binding.status === "Active" ? <SecondaryButton type="button" disabled={transitionBinding.isPending} onClick={() => {
                if (window.confirm(`Suspend ${binding.name} for ${binding.repository}? New repair publication and merge will stop.`))
                  transitionBinding.mutate({ bindingId: binding.id, transition: "suspend" });
              }}>Suspend</SecondaryButton> : null}
              <SecondaryButton type="button" disabled={transitionBinding.isPending} onClick={() => {
                if (window.confirm(`Permanently revoke ${binding.name} for ${binding.repository}? It cannot be reactivated.`))
                  transitionBinding.mutate({ bindingId: binding.id, transition: "revoke" });
              }}>Revoke</SecondaryButton>
            </div> : null}
          </article>
        )) : <p className="text-sm text-muted-foreground">No approved source ownership bindings.</p>}
        {activateBinding.isError || transitionBinding.isError ? <p role="alert" className="text-sm text-danger">Ownership binding state could not be changed.</p> : null}
      </section>

      <section className="space-y-3" aria-labelledby="provider-connections-title">
        <h2 id="provider-connections-title" className="text-base font-semibold">Provider connections</h2>
        {authority.data?.providerConnections.length ? authority.data.providerConnections.map((provider) => (
          <article key={provider.id} className="rounded-ui border border-border bg-surface p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div><h3 className="font-medium">{provider.repositoryOwner}/{provider.repositoryName}</h3><p className="text-xs text-muted-foreground">{provider.provider} installation {provider.installationId}</p></div>
              <Badge>{provider.status}</Badge>
            </div>
            {canApproveOwnership && provider.status !== "Revoked" ? <div className="mt-3 flex gap-2">
              {provider.status === "Suspended" ? <SecondaryButton type="button" disabled={validateProvider.isPending} onClick={() => validateProvider.mutate({ providerConnectionId: provider.id, version: provider.version })}>Revalidate and activate</SecondaryButton> : null}
              {provider.status === "PendingValidation" ? <SecondaryButton type="button" disabled={validateProvider.isPending} onClick={() => validateProvider.mutate({ providerConnectionId: provider.id, version: provider.version })}>Validate GitHub connection</SecondaryButton> : null}
              {provider.status === "Active" ? <SecondaryButton type="button" disabled={transitionProvider.isPending} onClick={() => {
                if (window.confirm(`Suspend provider access to ${provider.repositoryOwner}/${provider.repositoryName}? New repair publication and merge will stop.`))
                  transitionProvider.mutate({ providerConnectionId: provider.id, transition: "suspend", version: provider.version });
              }}>Suspend provider</SecondaryButton> : null}
              <SecondaryButton type="button" disabled={transitionProvider.isPending} onClick={() => {
                if (window.confirm(`Permanently revoke provider access to ${provider.repositoryOwner}/${provider.repositoryName}? Existing bindings will no longer authorize repair.`))
                  transitionProvider.mutate({ providerConnectionId: provider.id, transition: "revoke", version: provider.version });
              }}>Revoke provider</SecondaryButton>
            </div> : null}
          </article>
        )) : <p className="text-sm text-muted-foreground">No provider connections have been configured.</p>}
        {authority.data?.providerConnections.some((provider) => provider.status === "PendingValidation") ? <p className="text-sm text-muted-foreground">Pending connections are non-authoritative until GitHub validates the installation and immutable repository identity.</p> : null}
        {transitionProvider.isError || validateProvider.isError ? <p role="alert" className="text-sm text-danger">Provider connection state could not be changed or validated.</p> : null}
      </section>
      {confirmAuthorityAutoMerge ? <AutomaticMergeDialog applicationName="this repair authority" enabled pending={createAuthority.isPending} onCancel={() => { setConfirmAuthorityAutoMerge(false); requestAnimationFrame(() => authoritySubmitRef.current?.focus()); }} onConfirm={() => { setConfirmAuthorityAutoMerge(false); createAuthority.mutate(); requestAnimationFrame(() => authoritySubmitRef.current?.focus()); }} /> : null}
    </section>
  );
}

const emptyBindingDraft: ActivateSourceOwnershipBindingRequest = {
  name: "",
  selectorKind: "Package",
  selectorPattern: "",
  providerConnectionId: "",
  repositoryProviderId: "github",
  repositoryOwner: "",
  repositoryName: "",
  targetBranch: "main",
  workflowIdentity: "",
  workflowReference: "",
  workflowRevision: "",
  pathPolicyId: "",
  evidencePolicyId: "",
  mergePolicyId: "",
  priority: 0
};

const emptyAuthorityDraft: CreateHealingAuthorityProfileRequest = {
  name: "Default repair authority",
  installationId: "",
  repositoryOwner: "",
  repositoryName: "",
  credentialReferenceId: "",
  webhookSecretCredentialReferenceId: "",
  allowedRoots: ["src", "tests"],
  forbiddenRoots: [".github", ".azure", "eng", "scripts"],
  maxFiles: 20,
  maxChangedLines: 1000,
  maxPatchBytes: 1000000,
  requireReproduction: false,
  allowHighConfidenceInference: true,
  minimumInferenceConfidence: 0.9,
  automaticMergeEnabled: false,
  requiredChecks: [],
  forbiddenChangeCategories: ["workflow", "build-infrastructure", "credentials"],
  requireRollbackOrStopCapability: true
};

const bindingFields: ReadonlyArray<{ key: "name" | "targetBranch" | "workflowIdentity" | "workflowReference" | "workflowRevision" | "priority"; label: string }> = [
  { key: "name", label: "Binding name" },
  { key: "targetBranch", label: "Target branch" },
  { key: "workflowIdentity", label: "Workflow identity" },
  { key: "workflowReference", label: "Workflow branch or tag" },
  { key: "workflowRevision", label: "Workflow revision" },
  { key: "priority", label: "Priority" }
];

function HealingPageHeader({ applicationId, title, applicationName }: { applicationId: string; title: string; applicationName?: string }) {
  return <header className="space-y-3"><div><h1 className="font-display text-xl font-semibold">{title}</h1>{applicationName ? <p className="mt-1 text-sm text-muted-foreground">{applicationName}</p> : null}</div><nav className="flex flex-wrap gap-4" aria-label="Healing application"><Link className="text-sm text-primary" to={`/admin/healing/applications/${applicationId}/configuration`}>Configuration</Link><Link className="text-sm text-primary" to={`/admin/healing/applications/${applicationId}/components`}>Components</Link><Link className="text-sm text-primary" to={`/admin/healing/incidents?applicationId=${encodeURIComponent(applicationId)}`}>Incidents</Link></nav></header>;
}

function Toggle({ label, checked, disabled, onChange }: { label: string; checked: boolean; disabled?: boolean; onChange: (checked: boolean) => void }) {
  return <label className="flex items-start gap-3"><input className="mt-1 h-4 w-4" type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} /><span className="text-sm font-medium">{label}</span></label>;
}

function LabeledInput({ label, onChange, ...props }: Omit<ComponentProps<typeof Input>, "onChange"> & { label: string; onChange: (value: string) => void }) {
  const id = `healing-${label.toLowerCase().replaceAll(" ", "-")}`;
  return <label htmlFor={id} className="space-y-1 text-sm font-medium">{label}<Input id={id} {...props} onChange={(event) => onChange(event.target.value)} /></label>;
}

function LabeledSelect({ label, options, onChange, ...props }: Omit<ComponentProps<"select">, "onChange"> & { label: string; options: Array<{ value: string; label: string }>; onChange: (value: string) => void }) {
  const id = `healing-${label.toLowerCase().replaceAll(" ", "-")}`;
  return <label htmlFor={id} className="space-y-1 text-sm font-medium">{label}<select id={id} className="h-9 w-full rounded-ui border border-input bg-background px-3 text-sm" {...props} onChange={(event) => onChange(event.target.value)}><option value="">Select…</option>{options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label>;
}

function toPolicyOption(policy: { id: string; name: string; policyVersion: string }) {
  return { value: policy.id, label: `${policy.name} · v${policy.policyVersion}` };
}

function splitList(value: string) {
  return value.split(",").map((item) => item.trim()).filter(Boolean);
}

function ReadinessCard({ label, value }: { label: string; value: string }) {
  const ready = value === "Ready";
  const Icon = ready ? CheckCircle2 : AlertTriangle;
  return <div className="rounded-ui border border-border bg-surface p-4"><div className="flex items-center gap-2"><Icon aria-hidden className="h-4 w-4" /><h2 className="font-semibold">{label}</h2></div><p className="mt-2 text-sm">{value}</p></div>;
}

function StopBanner() {
  return <div role="status" className="flex items-start gap-3 rounded-ui border border-danger/30 bg-danger/5 p-4"><ShieldAlert aria-hidden className="mt-0.5 h-5 w-5" /><div><p className="font-semibold">Emergency stop active</p><p className="text-sm text-muted-foreground">Observability remains available; new repair work and automatic merge are blocked.</p></div></div>;
}

function EmergencyStopDialog({ applicationName, pending, error, onCancel, onConfirm }: { applicationName: string; pending: boolean; error: boolean; onCancel: () => void; onConfirm: () => void }) {
  const confirmRef = useRef<HTMLButtonElement>(null);
  useEffect(() => { confirmRef.current?.focus(); }, []);
  return <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 p-4" role="dialog" aria-modal="true" aria-labelledby="healing-stop-title"><div className="w-full max-w-lg rounded-ui border border-border bg-background p-5 shadow-sm"><h2 id="healing-stop-title" className="text-lg font-semibold">Stop Healing for {applicationName}</h2><p className="mt-3 text-sm text-muted-foreground">New repair dispatch, publication, and automatic merge will stop immediately. Existing incidents and observability history are retained.</p>{error ? <p role="alert" className="mt-3 text-sm text-danger">Healing could not be stopped. Review the current state and retry.</p> : null}<div className="mt-5 flex justify-end gap-2"><SecondaryButton type="button" onClick={onCancel}>Cancel</SecondaryButton><Button ref={confirmRef} type="button" disabled={pending} onClick={onConfirm}>Stop Healing now</Button></div></div></div>;
}

function EmergencyResumeDialog({ applicationName, pending, error, onCancel, onConfirm }: { applicationName: string; pending: boolean; error: boolean; onCancel: () => void; onConfirm: () => void }) {
  const confirmRef = useRef<HTMLButtonElement>(null);
  useEffect(() => { confirmRef.current?.focus(); }, []);
  return <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 p-4" role="dialog" aria-modal="true" aria-labelledby="healing-resume-title"><div className="w-full max-w-lg rounded-ui border border-border bg-background p-5 shadow-sm"><h2 id="healing-resume-title" className="text-lg font-semibold">Resume Healing for {applicationName}</h2><p className="mt-3 text-sm text-muted-foreground">This clears the application emergency stop. New repair work can be dispatched again when the configured readiness and policy gates pass.</p>{error ? <p role="alert" className="mt-3 text-sm text-danger">Healing could not be resumed. Review the current state and retry.</p> : null}<div className="mt-5 flex justify-end gap-2"><SecondaryButton type="button" onClick={onCancel}>Cancel</SecondaryButton><Button ref={confirmRef} type="button" disabled={pending} onClick={onConfirm}>Resume Healing</Button></div></div></div>;
}

function AutomaticMergeDialog({ applicationName, enabled, pending, onCancel, onConfirm }: { applicationName: string; enabled: boolean; pending: boolean; onCancel: () => void; onConfirm: () => void }) {
  const confirmRef = useRef<HTMLButtonElement>(null);
  useEffect(() => { confirmRef.current?.focus(); }, []);
  return <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 p-4" role="dialog" aria-modal="true" aria-labelledby="healing-automerge-title"><div className="w-full max-w-lg rounded-ui border border-border bg-background p-5 shadow-sm"><h2 id="healing-automerge-title" className="text-lg font-semibold">{enabled ? "Enable" : "Disable"} automatic merge for {applicationName}</h2><p className="mt-3 text-sm text-muted-foreground">This changes whether eligible repairs for this exact application may merge without a human merge action. A server-issued one-use confirmation will bind the change to this target and value.</p><div className="mt-5 flex justify-end gap-2"><SecondaryButton type="button" onClick={onCancel}>Cancel</SecondaryButton><Button ref={confirmRef} type="button" disabled={pending} onClick={onConfirm}>Confirm automatic merge change</Button></div></div></div>;
}

function toUpdateRequest(value: HealingApplicationConfiguration): UpdateHealingConfigurationRequest {
  return {
    discoveryEnabled: value.discoveryEnabled,
    repairDispatchEnabled: value.repairDispatchEnabled,
    automaticMergeEnabled: value.automaticMergeEnabled,
    signalProfileVersion: value.signalProfileVersion,
    defaultAttemptLimit: value.defaultAttemptLimit,
    verificationWindow: value.verificationWindow,
    timeBudget: value.timeBudget,
    concurrencyBudget: value.concurrencyBudget,
    inferenceBudget: value.inferenceBudget,
    repositoryRunBudget: value.repositoryRunBudget,
    classificationPolicyJson: value.classificationPolicyJson,
    version: value.version,
    environments: value.environments
  };
}

function updateEnvironment(
  draft: UpdateHealingConfigurationRequest,
  index: number,
  changes: Partial<UpdateHealingConfigurationRequest["environments"][number]>
): UpdateHealingConfigurationRequest {
  return {
    ...draft,
    environments: draft.environments.map((environment, environmentIndex) =>
      environmentIndex === index ? { ...environment, ...changes } : environment)
  };
}
