import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { LoaderCircle, Plus } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Button, Input, SecondaryButton, Select } from "@/components/ui";
import {
  createManagedElsaInstance,
  getManagedElsaOnboardingOptions,
  getManagedElsaOperation,
} from "@/features/managed-elsa/managedElsaApi";
import type { ManagedElsaInstanceIntent, ManagedElsaOnboardingOptions, ManagedElsaOperation } from "@/features/managed-elsa/managedElsaModels";
import { ApiError } from "@/lib/api/httpClient";
import { queryKeys } from "@/lib/query/queryClient";

type PendingOperation = { instanceId: string; operationId: string };

export function ManagedElsaCreatePanel({ workspaceId }: { workspaceId: string }) {
  const queryClient = useQueryClient();
  const options = useQuery({
    queryKey: ["managed-elsa", workspaceId, "onboarding-options"],
    queryFn: () => getManagedElsaOnboardingOptions(workspaceId),
    retry: false
  });
  const choices = useMemo(() => onboardingChoices(options.data), [options.data]);
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [choiceIndex, setChoiceIndex] = useState("0");
  const selected = choices[Number(choiceIndex)];
  const selectionKey = selected ? JSON.stringify([workspaceId, selected]) : null;
  const [consentedSelection, setConsentedSelection] = useState<string | null>(null);
  const previewConsentRequired = selected?.previewManifestDigest !== null && selected?.previewManifestDigest !== undefined;
  const previewConsented = selectionKey !== null && consentedSelection === selectionKey;

  useEffect(() => { setConsentedSelection(null); }, [selectionKey]);
  const [idempotencyKey, setIdempotencyKey] = useState(() => loadOrCreateIdempotencyKey(workspaceId));
  const [pending, setPending] = useState<PendingOperation | null>(() => loadPendingOperation(workspaceId));

  const operation = useQuery({
    queryKey: ["managed-elsa", workspaceId, "operation", pending?.instanceId, pending?.operationId],
    queryFn: () => getManagedElsaOperation(workspaceId, pending!.instanceId, pending!.operationId),
    enabled: pending !== null,
    retry: false,
    refetchInterval: (query) => {
      if (query.state.status === "error") return false;
      const result = query.state.data;
      return result && pending && matchesPendingOperation(pending, result) && isTerminal(result.state) ? false : 2000;
    }
  });

  useEffect(() => {
    if (!operation.data || !pending || !matchesPendingOperation(pending, operation.data) || !isTerminal(operation.data.state)) return;
    clearPendingOperation(workspaceId);
    setIdempotencyKey(resetIdempotencyKey(workspaceId));
    void queryClient.invalidateQueries({ queryKey: queryKeys.managedElsaInstances(workspaceId) });
  }, [operation.data, queryClient, workspaceId]);

  const create = useMutation({
    mutationFn: async () => {
      if (!selected || !options.data) throw new Error("selection-unavailable");
      if (previewConsentRequired && !previewConsented) throw new Error("preview-consent-required");
      const intent: ManagedElsaInstanceIntent = {
        release: {
          distributionId: selected.distributionId,
          releaseLine: selected.releaseLine,
          requestedVersion: selected.version,
          channel: selected.channel,
          patchUpdates: "automatic-within-minor",
          minorUpdates: "explicit-approval",
          majorMigrations: "explicit-migration",
          ...(selected.previewManifestDigest ? { previewManifestDigest: selected.previewManifestDigest } : {})
        },
        application: {
          topologyId: selected.topologyId,
          featurePresetId: null,
          featureOverrides: {},
          packagePolicy: null,
          configurationShapeRevisionId: null
        },
        placement: {
          targetMode: options.data.launchProfile.targetMode,
          regionCode: options.data.launchProfile.regionCode,
          isolationProfile: options.data.launchProfile.isolationProfile,
          capacityProfile: options.data.launchProfile.capacityProfile,
          networkOutcome: options.data.launchProfile.networkOutcome,
          domainOutcome: options.data.launchProfile.domainOutcome
        },
        desiredLifecycle: "Running"
      };
      return createManagedElsaInstance(workspaceId, { name: name.trim(), slug: slug.trim(), intent }, idempotencyKey);
    },
    onSuccess: (accepted) => {
      const next = { instanceId: accepted.instance.instanceId, operationId: accepted.operation.id };
      savePendingOperation(workspaceId, next);
      setPending(next);
      void queryClient.invalidateQueries({ queryKey: queryKeys.managedElsaInstances(workspaceId) });
    }
  });

  const operationIdentityInvalid = operation.data !== undefined && pending !== null && !matchesPendingOperation(pending, operation.data);
  const activeOperation = operation.data && !operationIdentityInvalid ? operation.data : undefined;
  const terminal = activeOperation && isTerminal(activeOperation.state);
  const pollingFailed = operation.isError || operationIdentityInvalid;
  const unavailable = options.isLoading || !selected;

  return (
    <div className="rounded-ui border border-border bg-surface p-4">
      <div className="flex flex-col gap-1">
        <h2 className="font-display text-lg font-semibold">Create managed Elsa</h2>
        <p className="text-sm text-muted-foreground">Choose a governed Elsa release and topology. Hosting defaults are managed by Elsa Control.</p>
      </div>
      {pending ? (
        <div className="mt-4 rounded-ui border border-primary/20 bg-primary/5 p-4" aria-live="polite">
          <p className="font-medium">Provisioning status: {operation.isLoading ? "Checking" : operationLabel(activeOperation)}</p>
          <p className="mt-1 text-sm text-muted-foreground">This status comes from the durable Control operation and can be resumed after refresh.</p>
          {pollingFailed ? (
            <div className="mt-3 space-y-2">
              <p role="alert" className="text-sm text-warning">Provisioning status could not be refreshed.</p>
              <SecondaryButton type="button" disabled={operation.isFetching} onClick={() => void operation.refetch()}>
                Retry status
              </SecondaryButton>
            </div>
          ) : null}
          {terminal ? (
            <Button className="mt-3" type="button" onClick={() => {
              clearPendingOperation(workspaceId);
              setPending(null);
            }}>Create another instance</Button>
          ) : null}
        </div>
      ) : (
        <form className="mt-4 grid gap-4 md:grid-cols-2" onSubmit={(event) => { event.preventDefault(); create.mutate(); }}>
          <label className="space-y-1 text-sm">
            <span className="font-medium">Instance name</span>
            <Input required value={name} onChange={(event) => {
              setName(event.target.value);
              if (!slug || slug === slugify(name)) setSlug(slugify(event.target.value));
            }} placeholder="My Elsa" />
          </label>
          <label className="space-y-1 text-sm">
            <span className="font-medium">Instance address</span>
            <Input required pattern="[a-z0-9]+(?:-[a-z0-9]+)*" value={slug} onChange={(event) => setSlug(slugify(event.target.value))} placeholder="my-elsa" />
          </label>
          <label className="space-y-1 text-sm md:col-span-2">
            <span className="font-medium">Elsa release and topology</span>
            <Select className="w-full" value={choiceIndex} onChange={(event) => {
              setConsentedSelection(null);
              setChoiceIndex(event.target.value);
            }} disabled={unavailable}>
              {choices.map((entry, index) => (
                <option key={choiceKey(entry.distributionId, entry.releaseLine, entry.version, entry.channel, entry.topologyId)} value={index}>
                  Elsa {entry.version} · {entry.releaseLine} · {entry.channel} · {entry.topologyId}{entry.previewManifestDigest ? " · Preview" : ""}
                </option>
              ))}
            </Select>
          </label>
          {previewConsentRequired ? (
            <div className="space-y-3 rounded-ui border border-warning/40 bg-warning/5 p-3 md:col-span-2">
              <p className="text-sm" id="managed-elsa-preview-warning">Preview releases are for evaluation and have no availability SLO. Choose a supported release for production workloads.</p>
              <label className="flex items-start gap-2 text-sm">
                <input type="checkbox" required className="mt-1 h-4 w-4 accent-primary" aria-describedby="managed-elsa-preview-warning"
                  checked={previewConsented} onChange={(event) => setConsentedSelection(event.target.checked ? selectionKey : null)} />
                <span>I agree to use this Preview release for this instance.</span>
              </label>
            </div>
          ) : null}
          <div className="md:col-span-2">
            <Button type="submit" disabled={unavailable || create.isPending || !name.trim() || !slug.trim() || (previewConsentRequired && !previewConsented)}>
              {create.isPending ? <LoaderCircle aria-hidden className="h-4 w-4 animate-spin" /> : <Plus aria-hidden className="h-4 w-4" />}
              {create.isPending ? "Creating…" : "Create instance"}
            </Button>
          </div>
          {options.isError ? <p role="alert" className="text-sm text-warning md:col-span-2">{onboardingOptionsError(options.error)}</p> : null}
          {!options.isLoading && !options.isError && choices.length === 0 ? <p role="status" className="text-sm text-muted-foreground md:col-span-2">No managed Elsa releases are currently available.</p> : null}
          {create.isError ? <p role="alert" className="text-sm text-warning md:col-span-2">{createError(create.error)}</p> : null}
        </form>
      )}
    </div>
  );
}

function onboardingChoices(options: ManagedElsaOnboardingOptions | undefined) {
  const entries = [
    ...(options?.releases ?? []).map((entry) => ({ ...entry, previewManifestDigest: null as string | null })),
    ...(options?.previewReleases ?? [])
      .filter((entry) => /^sha256:[0-9a-f]{64}$/.test(entry.manifestDigest))
      .map(({ manifestDigest, ...entry }) => ({ ...entry, previewManifestDigest: manifestDigest }))
  ];
  const groups = new Map<string, typeof entries>();
  for (const entry of entries) {
    const key = choiceKey(entry.distributionId, entry.releaseLine, entry.version, entry.channel, entry.topologyId);
    const group = groups.get(key) ?? [];
    group.push(entry);
    groups.set(key, group);
  }
  // Supported discovery retains its existing semantics. A stale Preview row must
  // not hide a Supported choice or grant Preview consent for an ambiguous identity.
  return [...groups.values()].flatMap((group) => {
    const supported = group.find((entry) => entry.previewManifestDigest === null);
    if (supported) return [supported];
    return group.every((entry) => entry.previewManifestDigest === group[0].previewManifestDigest) ? [group[0]] : [];
  });
}

function choiceKey(distributionId: string, releaseLine: string, version: string, channel: string, topologyId: string) {
  return `${distributionId}|${releaseLine}|${version}|${channel}|${topologyId}`;
}

export function slugify(value: string) {
  return value.toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 63);
}

function isTerminal(state: ManagedElsaOperation["state"] | undefined) {
  return state === "Succeeded" || state === "Failed" || state === "RecoveryRequired" || state === "Cancelled";
}

function matchesPendingOperation(pending: PendingOperation, operation: ManagedElsaOperation) {
  return pending.instanceId.toLowerCase() === operation.instanceId.toLowerCase() &&
    pending.operationId.toLowerCase() === operation.id.toLowerCase();
}

function operationLabel(operation: ManagedElsaOperation | undefined) {
  if (!operation) return "Checking";
  if (operation.state === "Failed") return `Failed (${safeFailureCode(operation.failureCode)})`;
  if (operation.state === "RecoveryRequired") return "Needs recovery";
  return operation.state;
}

function safeFailureCode(value: string | null) {
  return value && /^[a-z0-9][a-z0-9._-]{0,127}$/i.test(value) ? value : "operation-failed";
}

function storageKey(workspaceId: string) { return `managed-elsa-operation:${workspaceId}`; }
function loadPendingOperation(workspaceId: string): PendingOperation | null {
  try {
    const parsed = JSON.parse(sessionStorage.getItem(storageKey(workspaceId)) ?? "null") as PendingOperation | null;
    return parsed && isUuid(parsed.instanceId) && isUuid(parsed.operationId) ? parsed : null;
  } catch { return null; }
}
function savePendingOperation(workspaceId: string, value: PendingOperation) {
  try { sessionStorage.setItem(storageKey(workspaceId), JSON.stringify(value)); } catch { /* polling still works until navigation */ }
}
function clearPendingOperation(workspaceId: string) {
  try { sessionStorage.removeItem(storageKey(workspaceId)); } catch { /* no-op */ }
}
function newIdempotencyKey() { return crypto.randomUUID(); }
function idempotencyStorageKey(workspaceId: string) { return `managed-elsa-idempotency:${workspaceId}`; }
function loadOrCreateIdempotencyKey(workspaceId: string) {
  try {
    const existing = sessionStorage.getItem(idempotencyStorageKey(workspaceId));
    if (existing && isUuid(existing)) return existing;
    return resetIdempotencyKey(workspaceId);
  } catch { return newIdempotencyKey(); }
}
function resetIdempotencyKey(workspaceId: string) {
  const key = newIdempotencyKey();
  try { sessionStorage.setItem(idempotencyStorageKey(workspaceId), key); } catch { /* the in-memory key still protects this page */ }
  return key;
}
function isUuid(value: string) { return /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value); }

function createError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 403) return "You do not have permission to create a managed instance.";
    if (error.status === 409) return "That instance address is already in use.";
    if (error.status === 422) {
      const code = problemCode(error.details);
      if (code === "instance.entitlement-required") return "Managed hosting is not enabled for this organization.";
      if (code === "instance.catalog-selection-unavailable") return "The selected release is not currently available for managed hosting.";
      if (code === "instance.shape-invalid") return "The instance name, address, or selection is invalid.";
      return "The instance request was not accepted.";
    }
  }
  return "The instance could not be created. Try again shortly.";
}

function problemCode(details: unknown) {
  if (!details || typeof details !== "object" || !("code" in details) || typeof details.code !== "string") return null;
  return /^[a-z0-9][a-z0-9.-]{0,127}$/i.test(details.code) ? details.code : null;
}

function onboardingOptionsError(error: unknown) {
  if (error instanceof ApiError && error.status === 422)
    return "Managed hosting is not enabled for this organization.";
  if (error instanceof ApiError && error.status === 403)
    return "You do not have permission to view managed hosting options.";
  return "Provisioning choices are unavailable. Try again shortly.";
}
