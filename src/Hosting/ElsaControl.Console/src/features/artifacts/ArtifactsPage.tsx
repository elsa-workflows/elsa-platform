import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, ArrowLeft, CheckCircle2, Download, FileArchive, Plus, RefreshCw, RotateCcw, Search, Upload, X } from "lucide-react";
import { useState } from "react";
import type { ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { Badge, Button, EmptyState, Input, SecondaryButton, Select, Table, buttonClassName } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import {
  abortArtifactUpload,
  archiveWorkspaceArtifact,
  completeArtifactUpload,
  createArtifactUpload,
  createSampleArtifact,
  getArtifactUploadCapabilities,
  getWorkspaceArtifact,
  listWorkspaceArtifacts,
  refreshWorkspaceArtifact,
  restoreWorkspaceArtifact,
  uploadArtifactContent,
  workspaceArtifactDownloadUrl
} from "@/features/artifacts/artifactApi";
import {
  artifactDigest,
  artifactDisplayName,
  artifactFormatLabel,
  artifactStatusLabel,
  type CompleteArtifactUploadResponse,
  type WorkspaceArtifact,
  type WorkspaceArtifactDiagnostic
} from "@/features/artifacts/artifactModels";
import { getDeploymentPermissions } from "@/features/deployments/deploymentApi";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { sourceStatusTone, statusToneClass } from "@/lib/status/statusBadges";

const defaultMaxUploadBytes = 52_428_800;

export function ArtifactsPage() {
  const workspaceContext = useWorkspaceContext();
  const workspaceId = workspaceContext.selectedWorkspaceId;
  const [includeArchived, setIncludeArchived] = useState(false);
  const [search, setSearch] = useState("");
  const artifacts = useQuery({
    queryKey: [...queryKeys.artifacts(workspaceId), includeArchived],
    queryFn: () => listWorkspaceArtifacts(workspaceId, includeArchived),
    enabled: Boolean(workspaceId)
  });

  if (workspaceContext.isLoading) return <RequestStateView state="loading" title="Loading workspace context" />;
  if (workspaceContext.isError) return <RequestStateView state="unexpected" title="Workspace context could not load" />;
  if (!workspaceId) return <EmptyState title="No workspace selected" description="Select an organization workspace before managing artifacts." />;
  if (artifacts.isLoading) return <RequestStateView state="loading" title="Loading artifacts" />;
  if (artifacts.isError && !artifacts.data) return <RequestStateView state="unexpected" title="Artifacts could not load" />;

  const term = search.trim().toLowerCase();
  const items = (artifacts.data?.items ?? []).filter((artifact) => {
    if (!term) return true;
    return `${artifactDisplayName(artifact)} ${artifact.artifactId} ${artifact.artifactTypeId ?? ""} ${artifact.inspectionStatus}`.toLowerCase().includes(term);
  });

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h1 className="text-xl font-semibold">Artifacts</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            Register immutable deployment artifacts, inspect their provenance, and select verified packages for revisions.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <SecondaryButton onClick={() => void artifacts.refetch()} title="Refresh artifacts">
            <RefreshCw className="h-4 w-4" />
            Refresh
          </SecondaryButton>
          <Link to="/admin/artifacts/new" className={buttonClassName()}>
            <Plus className="h-4 w-4" />
            Upload artifact
          </Link>
        </div>
      </div>

      {artifacts.isRefetchError ? <RequestStateView state="stale" title="Showing the last loaded artifacts" /> : null}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <label className="relative block max-w-md flex-1">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input value={search} onChange={(event) => setSearch(event.target.value)} className="pl-9" placeholder="Search artifacts" />
        </label>
        <label className="flex items-center gap-2 text-sm text-muted-foreground">
          <input
            type="checkbox"
            className="h-4 w-4 rounded border-border"
            checked={includeArchived}
            onChange={(event) => setIncludeArchived(event.target.checked)}
          />
          Include archived
        </label>
      </div>

      {(artifacts.data?.items ?? []).length === 0 ? (
        <EmptyState
          title="No artifacts registered"
          description="Upload a ZIP artifact to make a verified package available for deployment revisions."
          action={<Link to="/admin/artifacts/new" className={buttonClassName()}><Upload className="h-4 w-4" />Upload artifact</Link>}
        />
      ) : items.length === 0 ? (
        <EmptyState title="No matching artifacts" description="Clear the search to see all registered artifacts." />
      ) : (
        <ArtifactTable artifacts={items} />
      )}
    </section>
  );
}

function ArtifactTable({ artifacts }: { artifacts: WorkspaceArtifact[] }) {
  return (
    <Table>
      <table className="min-w-full divide-y divide-border text-sm">
        <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
          <tr>
            <th className="px-3 py-2">Artifact</th>
            <th className="px-3 py-2">Type</th>
            <th className="px-3 py-2">Format</th>
            <th className="px-3 py-2">Inspection</th>
            <th className="px-3 py-2">Checksum</th>
            <th className="px-3 py-2">Resources</th>
            <th className="px-3 py-2">Updated</th>
            <th className="px-3 py-2">Status</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {artifacts.map((artifact) => (
            <tr key={artifact.id}>
              <td className="max-w-xs px-3 py-3">
                <Link to={`/admin/artifacts/${encodeURIComponent(artifact.id)}`} className="font-medium hover:underline">
                  {artifactDisplayName(artifact)}
                </Link>
                <p className="mt-1 truncate font-mono text-xs text-muted-foreground" title={artifact.artifactId}>{artifact.artifactId}</p>
              </td>
              <td className="px-3 py-3 text-muted-foreground">{artifact.artifactTypeId ?? "Unknown"}</td>
              <td className="px-3 py-3">{artifactFormatLabel(artifact.format)}</td>
              <td className="px-3 py-3"><StatusBadge value={artifact.inspectionStatus} /></td>
              <td className="px-3 py-3"><StatusBadge value={artifact.checksumStatus} /></td>
              <td className="px-3 py-3">{artifact.resources.length}</td>
              <td className="whitespace-nowrap px-3 py-3 text-muted-foreground">{formatDateTime(artifact.updatedAt)}</td>
              <td className="px-3 py-3"><StatusBadge value={artifactStatusLabel(artifact.status)} /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </Table>
  );
}

export function ArtifactCreatePage() {
  const workspaceContext = useWorkspaceContext();
  const workspaceId = workspaceContext.selectedWorkspaceId;
  const navigate = useNavigate();
  const capabilities = useQuery({
    queryKey: [...queryKeys.artifacts(workspaceId), "upload-capabilities"],
    queryFn: () => getArtifactUploadCapabilities(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const canManageSetup = useCanManageArtifacts(workspaceId);

  if (workspaceContext.isLoading) return <RequestStateView state="loading" title="Loading workspace context" />;
  if (workspaceContext.isError) return <RequestStateView state="unexpected" title="Workspace context could not load" />;
  if (!workspaceId) return <EmptyState title="No workspace selected" description="Select an organization workspace before uploading artifacts." />;

  return (
    <section className="space-y-5">
      <div className="flex items-start gap-3">
        <Link to="/admin/artifacts" className={buttonClassName("secondary", "shrink-0")} aria-label="Back to artifacts">
          <ArrowLeft className="h-4 w-4" />
          Back
        </Link>
        <div>
          <h1 className="text-xl font-semibold">Upload artifact</h1>
          <p className="mt-1 text-sm text-muted-foreground">Upload a ZIP package. Elsa Control computes identity, checksums, and manifest metadata server side.</p>
        </div>
      </div>
      {!canManageSetup ? <ReadOnlyNotice /> : null}
      {capabilities.isError ? <p role="alert" className="text-sm text-destructive">Upload capabilities could not be loaded. The server will still enforce its upload limit.</p> : null}
      <ArtifactUploadCard
        workspaceId={workspaceId}
        canManageSetup={canManageSetup}
        maxUploadBytes={capabilities.data?.maxUploadBytes ?? defaultMaxUploadBytes}
        sampleArtifactGenerationEnabled={capabilities.data?.sampleArtifactGenerationEnabled ?? false}
        onCompleted={(result) => {
          if (!result.artifact) return;
          void navigate(`/admin/artifacts/${encodeURIComponent(result.artifact.id)}`);
        }}
      />
    </section>
  );
}

export function ArtifactDetailsPage() {
  const { artifactId = "" } = useParams();
  const workspaceContext = useWorkspaceContext();
  const workspaceId = workspaceContext.selectedWorkspaceId;
  const queryClient = useQueryClient();
  const [actionError, setActionError] = useState<string | null>(null);
  const artifact = useQuery({
    queryKey: queryKeys.artifactDetails(workspaceId, artifactId),
    queryFn: () => getWorkspaceArtifact(workspaceId, artifactId),
    enabled: Boolean(workspaceId && artifactId)
  });
  const canManageSetup = useCanManageArtifacts(workspaceId);
  const action = useMutation({
    mutationFn: async (kind: "archive" | "restore" | "refresh") => {
      if (kind === "archive") await archiveWorkspaceArtifact(workspaceId, artifactId);
      else if (kind === "restore") await restoreWorkspaceArtifact(workspaceId, artifactId);
      else await refreshWorkspaceArtifact(workspaceId, artifactId);
    },
    onSuccess: async () => {
      setActionError(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.artifactDetails(workspaceId, artifactId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) })
      ]);
    },
    onError: (error) => setActionError(error instanceof Error ? error.message : "Artifact action failed.")
  });

  if (workspaceContext.isLoading) return <RequestStateView state="loading" title="Loading workspace context" />;
  if (workspaceContext.isError) return <RequestStateView state="unexpected" title="Workspace context could not load" />;
  if (!workspaceId) return <EmptyState title="No workspace selected" description="Select an organization workspace before viewing artifacts." />;
  if (!artifactId) return <RequestStateView state="not-found" title="Artifact not found" />;
  if (artifact.isLoading) return <RequestStateView state="loading" title="Loading artifact details" />;
  if (artifact.isError || !artifact.data) return <RequestStateView state="not-found" title="Artifact not found" />;

  const current = artifact.data;
  const isArchived = current.status === "Archived";

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="flex items-start gap-3">
          <Link to="/admin/artifacts" className={buttonClassName("secondary", "shrink-0")} aria-label="Back to artifacts">
            <ArrowLeft className="h-4 w-4" />
            Back
          </Link>
          <div className="min-w-0">
            <h1 className="break-words text-xl font-semibold">{artifactDisplayName(current)}</h1>
            <p className="mt-1 break-all font-mono text-xs text-muted-foreground">{current.artifactId}</p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          {current.referenceProvider.toLowerCase() === "local" ? (
            <a href={workspaceArtifactDownloadUrl(workspaceId, current.id)} className={buttonClassName("secondary")} download>
              <Download className="h-4 w-4" />
              Download ZIP
            </a>
          ) : null}
          <SecondaryButton disabled={!canManageSetup || action.isPending} onClick={() => action.mutate("refresh")}>
            <RefreshCw className="h-4 w-4" />
            Refresh inspection
          </SecondaryButton>
          {isArchived ? (
            <Button disabled={!canManageSetup || action.isPending} onClick={() => action.mutate("restore")}>
              <RotateCcw className="h-4 w-4" />
              Restore
            </Button>
          ) : (
            <SecondaryButton disabled={!canManageSetup || action.isPending} onClick={() => action.mutate("archive")}>
              <Archive className="h-4 w-4" />
              Archive
            </SecondaryButton>
          )}
        </div>
      </div>

      {!canManageSetup ? <ReadOnlyNotice /> : null}
      {actionError ? <p role="alert" className="text-sm text-destructive">{actionError}</p> : null}
      {artifact.isRefetchError ? <RequestStateView state="stale" title="Showing the last loaded artifact details" /> : null}

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        <InfoCard label="Status" value={<StatusBadge value={current.status} />} />
        <InfoCard label="Artifact type" value={current.artifactTypeId ?? "Unknown"} />
        <InfoCard label="Format" value={artifactFormatLabel(current.format)} />
        <InfoCard label="Reference provider" value={current.referenceProvider} />
        <InfoCard label="Inspection" value={<StatusBadge value={current.inspectionStatus} />} />
        <InfoCard label="Checksum" value={<StatusBadge value={current.checksumStatus} />} />
        <InfoCard label="Registered" value={formatDateTime(current.registeredAt)} />
        <InfoCard label="Last inspected" value={formatDateTime(current.lastInspectedAt)} />
      </div>

      <section className="rounded-ui border border-border bg-surface p-4">
        <h2 className="font-display text-lg font-semibold">Content identity</h2>
        <dl className="mt-3 grid gap-3 md:grid-cols-2">
          <InfoRow label="Content digest" value={<code className="break-all text-xs">{artifactDigest(current)}</code>} />
          <InfoRow label="Manifest digest" value={current.manifestDigest ? <code className="break-all text-xs">{current.manifestDigest.algorithm}:{current.manifestDigest.value}</code> : "Unavailable"} />
          <InfoRow label="Layout version" value={current.layoutVersion} />
          <InfoRow label="Envelope version" value={current.envelopeVersion ?? "Unavailable"} />
          <InfoRow label="Schema version" value={current.artifactSchemaVersion ?? "Unavailable"} />
          <InfoRow label="Payload size" value={formatPayloadSize(current.payloadReference?.sizeBytes)} />
        </dl>
      </section>

      <section className="rounded-ui border border-border bg-surface p-4">
        <h2 className="font-display text-lg font-semibold">Manifest</h2>
        <dl className="mt-3 grid gap-3 md:grid-cols-3">
          <InfoRow label="Name" value={current.manifest.name ?? "Unavailable"} />
          <InfoRow label="Version" value={current.manifest.version ?? "Unavailable"} />
          <InfoRow label="Environment" value={current.manifest.environment ?? "Unavailable"} />
        </dl>
      </section>

      <ArtifactResources resources={current.resources} />
      <ArtifactDiagnostics diagnostics={current.diagnostics} />
      {current.producer ? (
        <section className="rounded-ui border border-border bg-surface p-4">
          <h2 className="font-display text-lg font-semibold">Producer</h2>
          <dl className="mt-3 grid gap-3 md:grid-cols-3">
            <InfoRow label="Type" value={current.producer.producerType} />
            <InfoRow label="Name" value={current.producer.producerName} />
            <InfoRow label="Version" value={current.producer.producerVersion ?? "Unavailable"} />
          </dl>
        </section>
      ) : null}
    </section>
  );
}

function ArtifactUploadCard({
  workspaceId,
  canManageSetup,
  maxUploadBytes,
  sampleArtifactGenerationEnabled,
  onCompleted
}: {
  workspaceId: string;
  canManageSetup: boolean;
  maxUploadBytes: number;
  sampleArtifactGenerationEnabled: boolean;
  onCompleted: (result: CompleteArtifactUploadResponse) => void;
}) {
  const queryClient = useQueryClient();
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [progress, setProgress] = useState(0);
  const [phase, setPhase] = useState<"idle" | "creating" | "uploading" | "processing" | "failed">("idle");
  const [diagnostics, setDiagnostics] = useState<WorkspaceArtifactDiagnostic[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [sampleName, setSampleName] = useState("Elsa sample");
  const [sampleVersion, setSampleVersion] = useState("1.0.0");
  const [sampleEnvironment, setSampleEnvironment] = useState("Development");
  const [sampleWorkflowId, setSampleWorkflowId] = useState("sample-workflow");
  const isBusy = phase === "creating" || phase === "uploading" || phase === "processing";
  const fileTooLarge = Boolean(selectedFile && selectedFile.size > maxUploadBytes);
  const invalidExtension = Boolean(selectedFile && !selectedFile.name.toLowerCase().endsWith(".zip"));
  const sampleMutation = useMutation({
    mutationFn: () => createSampleArtifact(workspaceId, {
      artifactName: sampleName.trim(),
      version: sampleVersion.trim(),
      environment: sampleEnvironment.trim(),
      workflowId: sampleWorkflowId.trim()
    }),
    onSuccess: async (result) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
      if (result.artifact) onCompleted(result);
    },
    onError: (exception) => setError(exception instanceof Error ? exception.message : "Sample artifact could not be created.")
  });

  async function startUpload() {
    if (!selectedFile || !canManageSetup || fileTooLarge || invalidExtension || isBusy) return;
    setError(null);
    setDiagnostics([]);
    setProgress(0);
    setPhase("creating");
    let uploadId: string | null = null;
    let completionStarted = false;
    try {
      const session = await createArtifactUpload(workspaceId, {
        fileName: selectedFile.name,
        contentType: selectedFile.type || "application/zip",
        sizeBytes: selectedFile.size
      });
      uploadId = session.uploadId;
      setPhase("uploading");
      await uploadArtifactContent(workspaceId, session.uploadId, selectedFile, setProgress);
      setPhase("processing");
      completionStarted = true;
      const completed = await completeArtifactUpload(workspaceId, session.uploadId);
      setDiagnostics(completed.diagnostics);
      if (!completed.artifact) {
        setPhase("failed");
        setError(completed.diagnostics.find((diagnostic) => diagnostic.severity === "Error")?.message ?? "Artifact upload could not be completed.");
        return;
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
      setSelectedFile(null);
      setProgress(0);
      setPhase("idle");
      onCompleted(completed);
    } catch (exception) {
      if (uploadId && !completionStarted) {
        try {
          await abortArtifactUpload(workspaceId, uploadId);
        } catch {
          // The original upload error is the useful operator-facing diagnostic. Expired sessions
          // are cleaned up server side when an abort is not available.
        }
      }
      setPhase("failed");
      setError(exception instanceof Error ? exception.message : "Artifact upload failed.");
    }
  }

  function clearFile() {
    setSelectedFile(null);
    setProgress(0);
    setPhase("idle");
    setError(null);
    setDiagnostics([]);
  }

  return (
    <div className="space-y-4">
      <section className="rounded-ui border border-border bg-surface p-4">
        <div className="flex items-start gap-3">
          <div className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-ui border border-primary/20 bg-primary/10 text-primary">
            <FileArchive className="h-4 w-4" />
          </div>
          <div>
            <h2 className="font-semibold">ZIP package</h2>
            <p className="mt-1 text-sm text-muted-foreground">Only ZIP payloads are accepted. Manifest identity and checksums are computed after upload.</p>
          </div>
        </div>
        <div className="mt-4 grid gap-4 lg:grid-cols-[minmax(0,1fr)_20rem]">
          <label className="flex min-h-36 cursor-pointer flex-col items-center justify-center rounded-ui border border-dashed border-border bg-background px-4 py-6 text-center hover:bg-muted/50">
            <Upload className="h-8 w-8 text-primary" />
            <span className="mt-3 text-sm font-medium">{selectedFile ? selectedFile.name : "Choose a ZIP artifact"}</span>
            <span className="mt-1 text-xs text-muted-foreground">Maximum {formatPayloadSize(maxUploadBytes)}</span>
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
                setError(null);
                setDiagnostics([]);
              }}
            />
          </label>
          <div className="rounded-ui border border-border bg-background p-3 text-sm">
            <h3 className="font-medium">Upload status</h3>
            <p className="mt-2 text-xs text-muted-foreground">{uploadPhaseLabel(phase)}</p>
            {isBusy ? (
              <div className="mt-2 h-2 overflow-hidden rounded-full bg-muted"><div className="h-full bg-primary transition-all" style={{ width: `${progress}%` }} /></div>
            ) : null}
            {fileTooLarge ? <p role="alert" className="mt-3 text-sm text-destructive">File exceeds the upload limit.</p> : null}
            {invalidExtension ? <p role="alert" className="mt-3 text-sm text-destructive">Select a ZIP artifact package.</p> : null}
            {error ? <p role="alert" className="mt-3 text-sm text-destructive">{error}</p> : null}
            {diagnostics.length > 0 ? <DiagnosticList diagnostics={diagnostics} className="mt-3" /> : null}
            <div className="mt-4 flex gap-2">
              <Button type="button" className="flex-1" disabled={!canManageSetup || !selectedFile || fileTooLarge || invalidExtension || isBusy} onClick={() => void startUpload()}>
                <Upload className="h-4 w-4" />
                {isBusy ? "Uploading" : "Upload ZIP"}
              </Button>
              {selectedFile && !isBusy ? <SecondaryButton type="button" aria-label="Clear upload" onClick={clearFile}><X className="h-4 w-4" /></SecondaryButton> : null}
            </div>
          </div>
        </div>
      </section>

      {sampleArtifactGenerationEnabled ? (
        <section className="rounded-ui border border-border bg-surface p-4">
          <h2 className="font-semibold">Development sample</h2>
          <p className="mt-1 text-sm text-muted-foreground">Create a server-generated sample artifact when sample generation is enabled for this environment.</p>
          <div className="mt-4 grid gap-3 md:grid-cols-2">
            <label className="text-sm font-medium">Artifact name<Input className="mt-1" value={sampleName} onChange={(event) => setSampleName(event.target.value)} /></label>
            <label className="text-sm font-medium">Version<Input className="mt-1" value={sampleVersion} onChange={(event) => setSampleVersion(event.target.value)} /></label>
            <label className="text-sm font-medium">Environment<Input className="mt-1" value={sampleEnvironment} onChange={(event) => setSampleEnvironment(event.target.value)} /></label>
            <label className="text-sm font-medium">Workflow ID<Input className="mt-1" value={sampleWorkflowId} onChange={(event) => setSampleWorkflowId(event.target.value)} /></label>
          </div>
          <Button className="mt-4" disabled={!canManageSetup || sampleMutation.isPending || !sampleName.trim() || !sampleVersion.trim() || !sampleEnvironment.trim() || !sampleWorkflowId.trim()} onClick={() => sampleMutation.mutate()}>
            <CheckCircle2 className="h-4 w-4" />
            {sampleMutation.isPending ? "Creating sample" : "Create sample artifact"}
          </Button>
        </section>
      ) : null}
    </div>
  );
}

function ArtifactResources({ resources }: { resources: WorkspaceArtifact["resources"] }) {
  return (
    <section className="rounded-ui border border-border bg-surface p-4">
      <h2 className="font-display text-lg font-semibold">Resources <span className="text-sm font-normal text-muted-foreground">({resources.length})</span></h2>
      {resources.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">No resources were reported by the artifact inspection.</p> : (
        <Table>
          <table className="mt-3 min-w-full divide-y divide-border text-sm">
            <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground"><tr><th className="px-3 py-2">Type</th><th className="px-3 py-2">Logical ID</th><th className="px-3 py-2">Scope</th><th className="px-3 py-2">Version</th><th className="px-3 py-2">Desired-state hash</th></tr></thead>
            <tbody className="divide-y divide-border">{resources.map((resource) => <tr key={`${resource.type}:${resource.logicalId}`}><td className="px-3 py-3">{resource.type}</td><td className="px-3 py-3 font-mono text-xs">{resource.logicalId}</td><td className="px-3 py-3 text-muted-foreground">{resource.scope ?? "-"}</td><td className="px-3 py-3">{resource.version ?? "-"}</td><td className="px-3 py-3 font-mono text-xs">{resource.desiredStateHash ? `${resource.desiredStateHash.algorithm}:${resource.desiredStateHash.value}` : "-"}</td></tr>)}</tbody>
          </table>
        </Table>
      )}
    </section>
  );
}

function ArtifactDiagnostics({ diagnostics }: { diagnostics: WorkspaceArtifactDiagnostic[] }) {
  return (
    <section className="rounded-ui border border-border bg-surface p-4">
      <h2 className="font-display text-lg font-semibold">Diagnostics <span className="text-sm font-normal text-muted-foreground">({diagnostics.length})</span></h2>
      {diagnostics.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">No inspection diagnostics.</p> : <DiagnosticList diagnostics={diagnostics} className="mt-3" />}
    </section>
  );
}

function DiagnosticList({ diagnostics, className }: { diagnostics: WorkspaceArtifactDiagnostic[]; className?: string }) {
  return <ul className={`${className ?? ""} space-y-2 text-sm`}>{diagnostics.map((diagnostic) => <li key={`${diagnostic.code}:${diagnostic.message}`} className="rounded-ui border border-border p-3"><div className="flex flex-wrap items-center gap-2"><StatusBadge value={diagnostic.severity} /><code className="text-xs text-muted-foreground">{diagnostic.code}</code></div><p className="mt-1 text-muted-foreground">{diagnostic.message}</p></li>)}</ul>;
}

function StatusBadge({ value }: { value: string }) {
  return <Badge className={statusToneClass(sourceStatusTone(value))}>{value}</Badge>;
}

function InfoCard({ label, value }: { label: string; value: ReactNode }) {
  return <div className="rounded-ui border border-border p-4"><div className="text-xs uppercase text-muted-foreground">{label}</div><div className="mt-2 text-sm font-medium">{value}</div></div>;
}

function InfoRow({ label, value }: { label: string; value: ReactNode }) {
  return <div><dt className="text-xs uppercase text-muted-foreground">{label}</dt><dd className="mt-1 break-words text-sm">{value}</dd></div>;
}

function ReadOnlyNotice() {
  return <p className="rounded-ui border border-warning/30 bg-warning/5 p-3 text-sm text-muted-foreground">Workspace deployment setup permission is required to change artifacts.</p>;
}

function useCanManageArtifacts(workspaceId: string) {
  const workspaceContext = useWorkspaceContext();
  const permissions = useQuery({
    queryKey: queryKeys.deploymentPermissions(workspaceId),
    queryFn: () => getDeploymentPermissions(workspaceId),
    enabled: Boolean(workspaceId)
  });
  if (permissions.data) return permissions.data.permissions.includes("deployments.setup.manage");
  return workspaceContext.selectedWorkspace?.role === "Owner";
}

function uploadPhaseLabel(phase: "idle" | "creating" | "uploading" | "processing" | "failed") {
  switch (phase) {
    case "creating": return "Creating upload session";
    case "uploading": return "Uploading ZIP content";
    case "processing": return "Inspecting artifact";
    case "failed": return "Upload failed";
    default: return "Ready for upload";
  }
}

function formatPayloadSize(bytes?: number | null) {
  if (!bytes || bytes < 0) return "Unavailable";
  if (bytes < 1024) return `${bytes} B`;
  const units = ["KB", "MB", "GB"];
  let value = bytes;
  let unit = "B";
  for (const nextUnit of units) {
    value /= 1024;
    unit = nextUnit;
    if (value < 1024 || nextUnit === units.at(-1)) break;
  }
  return `${value >= 10 ? Math.round(value) : value.toFixed(1)} ${unit}`;
}
