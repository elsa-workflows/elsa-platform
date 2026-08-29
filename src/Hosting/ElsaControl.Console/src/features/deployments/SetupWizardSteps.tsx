import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, FileArchive, Loader2, Plus, Rocket, ShieldPlus, Trash2, Upload, X } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge, Button, buttonClassName, EmptyState, Input, SecondaryButton, Select } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import {
  completeArtifactUpload,
  createArtifactUpload,
  listWorkspaceArtifacts,
  uploadArtifactContent
} from "@/features/artifacts/artifactApi";
import type { WorkspaceArtifact, WorkspaceArtifactDiagnostic } from "@/features/artifacts/artifactModels";
import type { EnvironmentSummary, WorkspaceDeploymentRunStatus, WorkspaceDeploymentTier } from "@/features/deployments/deploymentModels";
import { artifactDisplayName, deployPhaseLabel, isRunInFlight, runStatusLabel, useRunStatus, type DeployChainPhase } from "@/features/deployments/deployFlow";
import { defaultTierPresets } from "@/features/deployments/tierPresets";
import { queryKeys } from "@/lib/query/queryClient";
import { cn } from "@/lib/utils";

// --- Tier seeding ------------------------------------------------------------------------------

/**
 * Shown at the top of the wizard when the workspace has no active tiers. Offers one-click seeding of
 * the default Dev/Test/Prod tiers so the operator never has to leave the wizard to unblock setup.
 */
export function TierSeedCard({
  isSeeding,
  error,
  canManageTiers,
  onSeed
}: {
  isSeeding: boolean;
  error?: string;
  canManageTiers: boolean;
  onSeed: () => void;
}) {
  return (
    <section className="rounded-ui border border-primary/30 bg-primary/5 p-4">
      <div className="flex items-start gap-3">
        <div className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-ui border border-primary/20 bg-primary/10 text-primary">
          <ShieldPlus className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1">
          <h2 className="font-semibold">Set up deployment tiers</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            This workspace has no active deployment tiers yet. Create the default Dev, Test, and Production tiers to continue.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            {defaultTierPresets.map((preset) => (
              <Badge key={preset.name}>{preset.name}</Badge>
            ))}
          </div>
          {error ? <p role="alert" className="mt-3 text-sm text-destructive">{error}</p> : null}
          {!canManageTiers ? (
            <p className="mt-3 text-sm text-muted-foreground">Workspace owner access is required to create tiers.</p>
          ) : null}
          <div className="mt-4 flex flex-wrap items-center gap-2">
            <Button type="button" disabled={!canManageTiers || isSeeding} onClick={onSeed}>
              {isSeeding ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
              {isSeeding ? "Creating tiers" : "Create default tiers"}
            </Button>
            <Link to="/admin/deployments/tiers" className={buttonClassName("secondary")}>
              Manage tiers manually
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}

// --- Multi-environment editor ------------------------------------------------------------------

export type EnvironmentDraft = {
  key: string;
  name: string;
  tierId: string;
};

export type EnvironmentRow = {
  name: string;
  tierId: string;
  tier: EnvironmentSummary["tier"];
};

function legacyTierFromName(name?: string): EnvironmentSummary["tier"] {
  if (name === "Dev" || name === "Test" || name === "Stage" || name === "Production") return name;
  return "Production";
}

let environmentDraftCounter = 0;
function nextDraftKey() {
  environmentDraftCounter += 1;
  return `env-draft-${environmentDraftCounter}`;
}

/**
 * Builds pre-filled environment drafts (Dev/Test/Prod) matched to the workspace's active tiers by
 * name, falling back to the first tier. Used to seed the multi-environment editor with sensible
 * defaults so the common case is a single confirmation click.
 */
export function defaultEnvironmentDrafts(activeTiers: WorkspaceDeploymentTier[]): EnvironmentDraft[] {
  if (activeTiers.length === 0) return [];
  const tierByName = new Map(activeTiers.map((tier) => [tier.name.toLowerCase(), tier]));
  const preferred = ["Dev", "Test", "Production"];
  const drafts = preferred
    .map((name) => {
      const tier = tierByName.get(name.toLowerCase());
      return tier ? { key: nextDraftKey(), name, tierId: tier.id } : null;
    })
    .filter((draft): draft is EnvironmentDraft => draft !== null);

  if (drafts.length > 0) return drafts;
  const fallback = activeTiers[0];
  return [{ key: nextDraftKey(), name: fallback.name, tierId: fallback.id }];
}

/**
 * Adds several environments (name + tier rows) in a single screen. Pre-filled with dev/test/prod
 * defaults; the operator can add, edit, or remove rows before creating them all at once.
 */
export function EnvironmentRowsEditor({
  activeTiers,
  canManageSetup,
  isSubmitting,
  error,
  createdEnvironmentNames,
  onCreate
}: {
  activeTiers: WorkspaceDeploymentTier[];
  canManageSetup: boolean;
  isSubmitting: boolean;
  error?: string;
  createdEnvironmentNames: string[];
  onCreate: (rows: EnvironmentRow[]) => void;
}) {
  const [drafts, setDrafts] = useState<EnvironmentDraft[]>(() => defaultEnvironmentDrafts(activeTiers));

  const tierOptions = useMemo(() => activeTiers.map((tier) => ({ id: tier.id, name: tier.name })), [activeTiers]);
  const usableDrafts = drafts.filter((draft) => draft.name.trim().length > 0 && draft.tierId.length > 0);
  const canSubmit = canManageSetup && usableDrafts.length > 0 && !isSubmitting;

  function updateDraft(key: string, patch: Partial<EnvironmentDraft>) {
    setDrafts((current) => current.map((draft) => (draft.key === key ? { ...draft, ...patch } : draft)));
  }

  function addRow() {
    const fallbackTier = tierOptions[0]?.id ?? "";
    setDrafts((current) => [...current, { key: nextDraftKey(), name: "", tierId: fallbackTier }]);
  }

  function removeRow(key: string) {
    setDrafts((current) => current.filter((draft) => draft.key !== key));
  }

  function submit() {
    if (!canSubmit) return;
    const rows: EnvironmentRow[] = usableDrafts.map((draft) => {
      const tier = activeTiers.find((item) => item.id === draft.tierId);
      return { name: draft.name.trim(), tierId: draft.tierId, tier: legacyTierFromName(tier?.name) };
    });
    onCreate(rows);
  }

  return (
    <form
      className="space-y-3 rounded-ui border border-border bg-surface p-4"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <div>
        <h2 className="text-sm font-semibold">Environments</h2>
        <p className="mt-1 text-xs text-muted-foreground">
          Add one or more environments in a single step. Defaults follow a Dev, Test, and Production ladder.
        </p>
      </div>
      {createdEnvironmentNames.length > 0 ? (
        <div className="flex flex-wrap items-center gap-2 rounded-ui border border-border bg-muted/30 px-3 py-2 text-xs">
          <span className="font-medium">Created:</span>
          {createdEnvironmentNames.map((name) => (
            <span key={name} className="inline-flex items-center gap-1 text-muted-foreground">
              <CheckCircle2 className="h-3.5 w-3.5 text-success" />
              {name}
            </span>
          ))}
        </div>
      ) : null}
      <div className="space-y-2">
        {drafts.length === 0 ? (
          <p className="text-sm text-muted-foreground">No environments queued. Add a row to continue.</p>
        ) : (
          drafts.map((draft) => (
            <div key={draft.key} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
              <label className="text-xs font-medium text-muted-foreground">
                Name
                <Input
                  className="mt-1"
                  value={draft.name}
                  disabled={!canManageSetup || isSubmitting}
                  onChange={(event) => updateDraft(draft.key, { name: event.target.value })}
                />
              </label>
              <label className="text-xs font-medium text-muted-foreground">
                Tier
                <Select
                  className="mt-1 w-full"
                  value={draft.tierId}
                  disabled={!canManageSetup || isSubmitting || tierOptions.length === 0}
                  onChange={(event) => updateDraft(draft.key, { tierId: event.target.value })}
                >
                  <option value="" disabled>
                    Select a tier
                  </option>
                  {tierOptions.map((tier) => (
                    <option key={tier.id} value={tier.id}>
                      {tier.name}
                    </option>
                  ))}
                </Select>
              </label>
              <SecondaryButton
                type="button"
                className="h-9 w-9 px-0"
                aria-label={`Remove ${draft.name || "environment"} row`}
                disabled={isSubmitting}
                onClick={() => removeRow(draft.key)}
              >
                <Trash2 className="h-4 w-4" />
              </SecondaryButton>
            </div>
          ))
        )}
      </div>
      {error ? <p role="alert" className="text-sm text-destructive">{error}</p> : null}
      {!canManageSetup ? <p className="text-sm text-muted-foreground">Deployment setup permission is required.</p> : null}
      <div className="flex flex-wrap gap-2">
        <SecondaryButton type="button" disabled={isSubmitting || tierOptions.length === 0} onClick={addRow}>
          <Plus className="h-4 w-4" />
          Add environment
        </SecondaryButton>
        <Button type="submit" disabled={!canSubmit}>
          {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
          {isSubmitting ? "Creating environments" : `Create ${usableDrafts.length || ""} environment${usableDrafts.length === 1 ? "" : "s"}`.trim()}
        </Button>
      </div>
    </form>
  );
}

// --- Artifact pick / upload step ----------------------------------------------------------------

type UploadPhase = "idle" | "creating" | "uploading" | "processing" | "failed";

/**
 * Wizard artifact step: pick an already-registered artifact, or upload a ZIP inline without leaving
 * the wizard. On successful upload the new artifact is auto-selected. Reuses the same upload API
 * chain as the standalone artifacts page.
 */
export function WizardArtifactStep({
  workspaceId,
  canManageSetup,
  maxUploadBytes,
  selectedArtifactId,
  onSelectArtifact,
  onContinue,
  onBack
}: {
  workspaceId: string;
  canManageSetup: boolean;
  maxUploadBytes: number;
  selectedArtifactId: string;
  onSelectArtifact: (artifact: WorkspaceArtifact) => void;
  onContinue: () => void;
  onBack: () => void;
}) {
  const queryClient = useQueryClient();
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [progress, setProgress] = useState(0);
  const [phase, setPhase] = useState<UploadPhase>("idle");
  const [diagnostics, setDiagnostics] = useState<WorkspaceArtifactDiagnostic[]>([]);
  const [uploadError, setUploadError] = useState<string | null>(null);

  const artifactsQuery = useQuery({
    queryKey: queryKeys.artifacts(workspaceId),
    queryFn: () => listWorkspaceArtifacts(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const listError = artifactsQuery.error instanceof Error ? artifactsQuery.error.message : null;
  const items = (artifactsQuery.data?.items ?? []).filter((artifact) => artifact.status !== "Archived");
  const isBusy = phase === "creating" || phase === "uploading" || phase === "processing";
  const fileTooLarge = Boolean(selectedFile && selectedFile.size > maxUploadBytes);
  const invalidExtension = Boolean(selectedFile && !selectedFile.name.toLowerCase().endsWith(".zip"));

  async function startUpload() {
    if (!selectedFile) return;
    setUploadError(null);
    setDiagnostics([]);
    setProgress(0);
    setPhase("creating");
    try {
      const session = await createArtifactUpload(workspaceId, {
        fileName: selectedFile.name,
        contentType: selectedFile.type || "application/zip",
        sizeBytes: selectedFile.size
      });
      setPhase("uploading");
      await uploadArtifactContent(workspaceId, session.uploadId, selectedFile, setProgress);
      setPhase("processing");
      const completed = await completeArtifactUpload(workspaceId, session.uploadId);
      setDiagnostics(completed.diagnostics);
      if (completed.artifact) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
        await artifactsQuery.refetch();
        onSelectArtifact(completed.artifact);
        setSelectedFile(null);
        setProgress(0);
        setPhase("idle");
        return;
      }
      setPhase("failed");
      setUploadError(completed.diagnostics.find((item) => item.severity === "Error")?.message ?? "Artifact upload could not be completed.");
    } catch (ex) {
      setPhase("failed");
      setUploadError(ex instanceof Error ? ex.message : "Artifact upload failed.");
    }
  }

  if (artifactsQuery.isLoading) {
    return <RequestStateView state="loading" title="Loading artifacts" />;
  }

  return (
    <div className="space-y-3">
      <section className="rounded-ui border border-border bg-surface p-4">
        <h2 className="text-sm font-semibold">Pick an artifact</h2>
        <p className="mt-1 text-xs text-muted-foreground">
          Choose a registered deployment artifact for the first version, or upload one below.
        </p>
        {listError ? <p role="alert" className="mt-3 text-sm text-destructive">{listError}</p> : null}
        {items.length === 0 ? (
          <EmptyState
            title="No artifacts registered yet"
            description="Upload a ZIP artifact below to create the first version."
          />
        ) : (
          <label className="mt-3 block text-sm font-medium">
            Artifact
            <Select
              className="mt-1 w-full"
              value={selectedArtifactId}
              onChange={(event) => {
                const artifact = items.find((item) => item.id === event.target.value);
                if (artifact) onSelectArtifact(artifact);
              }}
            >
              <option value="" disabled>
                Select an artifact
              </option>
              {items.map((artifact) => (
                <option key={artifact.id} value={artifact.id}>
                  {artifactDisplayName(artifact)}
                </option>
              ))}
            </Select>
          </label>
        )}
      </section>

      <section className="rounded-ui border border-border bg-surface p-4">
        <div className="flex items-start gap-3">
          <div className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-ui border border-primary/20 bg-primary/10 text-primary">
            <Upload className="h-4 w-4" />
          </div>
          <div className="min-w-0 flex-1">
            <h2 className="font-semibold">Upload a new artifact</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Upload a ZIP artifact. Identity, digest, and manifest metadata are computed from the package.
            </p>
          </div>
        </div>
        <div className="mt-4 grid gap-4 lg:grid-cols-[minmax(0,1fr)_20rem]">
          <label className="flex min-h-32 cursor-pointer flex-col items-center justify-center rounded-ui border border-dashed border-border bg-background px-4 py-6 text-center transition-colors hover:bg-muted/50">
            <FileArchive className="h-8 w-8 text-primary" />
            <span className="mt-3 text-sm font-medium">{selectedFile ? selectedFile.name : "Choose a ZIP artifact"}</span>
            <input
              aria-label="Artifact package"
              className="sr-only"
              type="file"
              accept=".zip,application/zip,application/x-zip-compressed"
              disabled={!canManageSetup || isBusy}
              onChange={(event) => {
                setSelectedFile(event.target.files?.[0] ?? null);
                setProgress(0);
                setPhase("idle");
                setUploadError(null);
                setDiagnostics([]);
              }}
            />
          </label>
          <div className="rounded-ui border border-border bg-background p-3 text-sm">
            <h3 className="font-medium">Upload status</h3>
            <p className="mt-2 text-xs text-muted-foreground">{uploadPhaseLabel(phase)}</p>
            {phase !== "idle" ? (
              <div className="mt-2 h-2 overflow-hidden rounded-full bg-muted">
                <div className="h-full bg-primary transition-all" style={{ width: `${progress}%` }} />
              </div>
            ) : null}
            {fileTooLarge ? <p role="alert" className="mt-3 text-sm text-destructive">File exceeds the upload limit.</p> : null}
            {invalidExtension ? <p role="alert" className="mt-3 text-sm text-destructive">Select a ZIP artifact package.</p> : null}
            {uploadError ? <p role="alert" className="mt-3 text-sm text-destructive">{uploadError}</p> : null}
            {diagnostics.length > 0 ? (
              <ul className="mt-3 space-y-1 text-xs text-muted-foreground">
                {diagnostics.map((diagnostic) => (
                  <li key={`${diagnostic.code}:${diagnostic.message}`}>{diagnostic.message}</li>
                ))}
              </ul>
            ) : null}
            <div className="mt-4 flex gap-2">
              <Button
                type="button"
                className="flex-1"
                disabled={!canManageSetup || !selectedFile || fileTooLarge || invalidExtension || isBusy}
                onClick={() => void startUpload()}
              >
                <Upload className="h-4 w-4" />
                {isBusy ? "Uploading" : "Upload ZIP"}
              </Button>
              {selectedFile && !isBusy ? (
                <SecondaryButton
                  type="button"
                  aria-label="Clear upload"
                  onClick={() => {
                    setSelectedFile(null);
                    setProgress(0);
                    setPhase("idle");
                    setUploadError(null);
                    setDiagnostics([]);
                  }}
                >
                  <X className="h-4 w-4" />
                </SecondaryButton>
              ) : null}
            </div>
          </div>
        </div>
      </section>

      <div className="flex flex-wrap gap-2">
        <Button type="button" disabled={!selectedArtifactId} onClick={onContinue}>
          Continue to deploy
        </Button>
        <SecondaryButton type="button" onClick={onBack}>
          Back
        </SecondaryButton>
      </div>
    </div>
  );
}

function uploadPhaseLabel(phase: UploadPhase) {
  switch (phase) {
    case "creating":
      return "Preparing upload";
    case "uploading":
      return "Uploading";
    case "processing":
      return "Processing";
    case "failed":
      return "Failed";
    default:
      return "Idle";
  }
}

// --- Deploy step -------------------------------------------------------------------------------

/**
 * Single-button deploy affordance for the wizard. Runs the deployability check -> confirmation ->
 * run chain behind one action with collapsed progress. "Confirmation" is never surfaced.
 */
export function WizardDeployStep({
  phase,
  error,
  environmentName,
  artifactName,
  engineOptions,
  selectedEngineId,
  onSelectEngine,
  onDeploy,
  onBack
}: {
  phase: DeployChainPhase;
  error?: string;
  environmentName: string;
  artifactName: string;
  engineOptions: { id: string; name: string }[];
  selectedEngineId: string;
  onSelectEngine: (engineId: string) => void;
  onDeploy: () => void;
  onBack: () => void;
}) {
  const isBusy = phase === "checking" || phase === "deploying";
  const canDeploy = engineOptions.length > 0 && Boolean(selectedEngineId) && !isBusy;

  return (
    <section className="space-y-4 rounded-ui border border-border bg-surface p-4">
      <div>
        <h2 className="text-sm font-semibold">Deploy the first version</h2>
        <p className="mt-1 text-xs text-muted-foreground">
          This creates revision r1 in {environmentName} from {artifactName || "the selected artifact"} and deploys it to the
          target engine in one step.
        </p>
      </div>
      {engineOptions.length === 0 ? (
        <EmptyState
          title="No engine registered"
          description="Register an engine for this environment before deploying."
        />
      ) : (
        <label className="block text-sm font-medium">
          Target engine
          <Select
            className="mt-1 w-full"
            value={selectedEngineId}
            disabled={isBusy}
            onChange={(event) => onSelectEngine(event.target.value)}
          >
            <option value="" disabled>
              Select an engine
            </option>
            {engineOptions.map((engine) => (
              <option key={engine.id} value={engine.id}>
                {engine.name}
              </option>
            ))}
          </Select>
        </label>
      )}
      {isBusy ? (
        <div className="flex items-center gap-2 rounded-ui border border-border bg-muted/30 px-3 py-2 text-sm">
          <Loader2 className="h-4 w-4 animate-spin text-primary" />
          {deployPhaseLabel(phase)}
        </div>
      ) : null}
      {error ? <p role="alert" className="text-sm text-destructive">{error}</p> : null}
      <div className="flex flex-wrap gap-2">
        <Button type="button" disabled={!canDeploy} onClick={onDeploy}>
          <Rocket className="h-4 w-4" />
          {isBusy ? deployPhaseLabel(phase) : "Deploy"}
        </Button>
        <SecondaryButton type="button" disabled={isBusy} onClick={onBack}>
          Back
        </SecondaryButton>
      </div>
    </section>
  );
}

// --- Done screen -------------------------------------------------------------------------------

/**
 * Wizard end-state: "v1 is running in {env}" with live run-status polling and a link to the
 * application view.
 */
export function WizardDoneScreen({
  workspaceId,
  runId,
  environmentName,
  applicationPath
}: {
  workspaceId: string;
  runId: string;
  environmentName: string;
  applicationPath: string;
}) {
  const { run, isError } = useRunStatus(workspaceId, runId);
  const status: WorkspaceDeploymentRunStatus = run?.status ?? "Queued";
  const inFlight = isRunInFlight(status);
  const succeeded = status === "Succeeded";

  return (
    <section className="space-y-4 rounded-ui border border-border bg-surface p-6 text-center">
      <div className="mx-auto inline-flex h-12 w-12 items-center justify-center rounded-full border border-border bg-background">
        {succeeded ? (
          <CheckCircle2 className="h-6 w-6 text-success" />
        ) : (
          <Loader2 className={cn("h-6 w-6 text-primary", inFlight ? "animate-spin" : "")} />
        )}
      </div>
      <div>
        <h2 className="text-lg font-semibold">
          {succeeded ? `v1 is running in ${environmentName}` : `Deploying v1 to ${environmentName}`}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {isError
            ? "The run was queued but its status could not be loaded. Open the application to review it."
            : succeeded
              ? "The first version has been deployed and is now running."
              : `Deployment run status: ${runStatusLabel(status)}. This screen updates automatically.`}
        </p>
      </div>
      {run?.failureMessage ? <p role="alert" className="text-sm text-destructive">{run.failureMessage}</p> : null}
      <div className="flex flex-wrap justify-center gap-2">
        <Link to={applicationPath} className={buttonClassName("primary")}>
          <Rocket className="h-4 w-4" />
          Open application
        </Link>
        <Link to="/admin/deployments" className={buttonClassName("secondary")}>
          Back to deployments
        </Link>
      </div>
    </section>
  );
}
