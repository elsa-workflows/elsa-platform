import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Archive, ArrowLeft, CheckCircle2, FileArchive, RefreshCw, RotateCcw, Save, Upload, Wand2, X } from "lucide-react";
import type { ReactNode } from "react";
import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { Badge, Button, EmptyState, Input, SecondaryButton, Select, Table, buttonClassName } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import {
  getWorkspaceArtifact,
  abortArtifactUpload,
  archiveWorkspaceArtifact,
  completeArtifactUpload,
  createArtifactUpload,
  createSampleArtifact,
  getArtifactUploadCapabilities,
  listWorkspaceArtifactTypes,
  listWorkspaceArtifacts,
  refreshWorkspaceArtifactInspection,
  registerWorkspaceArtifact,
  restoreWorkspaceArtifact,
  uploadArtifactContent
} from "@/features/artifacts/artifactApi";
import type { CompleteArtifactUploadResponse, WorkspaceArtifact, WorkspaceArtifactDiagnostic, WorkspaceArtifactRegistrationRequest } from "@/features/artifacts/artifactModels";
import { getDeploymentPermissions } from "@/features/deployments/deploymentApi";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";

const layoutVersion = "platform.elsa.io/deployment-artifact/v1alpha1";
const envelopeVersion = "platform.elsa.io/artifact-envelope/v1alpha1";
const workflowArtifactType = "elsa.workflow-definition";
type ArtifactListFilter = "active" | "archived" | "all";

export function ArtifactsPage() {
  const workspaceContext = useWorkspaceContext();
  const workspaceId = workspaceContext.selectedWorkspaceId;
  const [filter, setFilter] = useState<ArtifactListFilter>("active");
  const permissions = useQuery({
    queryKey: queryKeys.deploymentPermissions(workspaceId),
    queryFn: () => getDeploymentPermissions(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const artifacts = useQuery({
    queryKey: [...queryKeys.artifacts(workspaceId), "include-archived"],
    queryFn: () => listWorkspaceArtifacts(workspaceId, true),
    enabled: Boolean(workspaceId)
  });
  const canManageSetup = Boolean(permissions.data?.permissions.includes("deployments.setup.manage"));
  const archive = useArtifactArchiveMutation(workspaceId);
  const restore = useArtifactRestoreMutation(workspaceId);

  if (workspaceContext.isLoading || artifacts.isLoading || permissions.isLoading)
    return <RequestStateView state="loading" title="Loading artifacts" />;
  if (!workspaceId)
    return <EmptyState title="No workspace selected" description="Select an organization workspace before registry metadata can be shown." />;
  if (workspaceContext.isError || artifacts.isError || permissions.isError)
    return <RequestStateView state="unexpected" title="Artifacts could not load" />;

  const allItems = artifacts.data?.items ?? [];
  const activeItems = allItems.filter((artifact) => artifact.status !== "Archived");
  const archivedItems = allItems.filter((artifact) => artifact.status === "Archived");
  const visibleItems = filter === "archived" ? archivedItems : filter === "all" ? allItems : activeItems;

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h1 className="text-xl font-semibold">Artifacts</h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            Workspace registry for immutable deployment artifact metadata, references, checksum state, and safe diagnostics.
          </p>
        </div>
        <div className="flex gap-2">
          <SecondaryButton onClick={() => void artifacts.refetch()}>
            <RefreshCw className="h-4 w-4" />
            Refresh
          </SecondaryButton>
          <Link to="/admin/artifacts/new" className={buttonClassName("primary", !canManageSetup ? "pointer-events-none opacity-50" : undefined)} aria-disabled={!canManageSetup ? true : undefined}>
            <Upload className="h-4 w-4" />
            Upload artifact
          </Link>
        </div>
      </div>

      {!canManageSetup ? <p className="text-xs text-muted-foreground">Deployment setup permission is required to upload or register artifacts.</p> : null}
      <div className="flex flex-wrap items-center gap-2">
        <FilterButton active={filter === "active"} onClick={() => setFilter("active")}>Active {activeItems.length}</FilterButton>
        <FilterButton active={filter === "archived"} onClick={() => setFilter("archived")}>Archived {archivedItems.length}</FilterButton>
        <FilterButton active={filter === "all"} onClick={() => setFilter("all")}>All {allItems.length}</FilterButton>
      </div>

      {visibleItems.length === 0 ? (
        <EmptyState
          title={filter === "archived" ? "No archived artifacts" : filter === "all" ? "No artifacts registered" : "No active artifacts"}
          description={filter === "archived" ? "Archived artifacts will appear here for audit and restore workflows." : "Uploaded and manually registered artifacts appear here with type, producer, digest, and compatibility metadata."}
          action={
            canManageSetup && filter !== "archived" ? (
              <Link to="/admin/artifacts/new" className={buttonClassName()}>
                <Upload className="h-4 w-4" />
                Upload artifact
              </Link>
            ) : undefined
          }
        />
      ) : (
        <Table>
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-muted/40 text-left text-xs text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Artifact</th>
                <th className="px-3 py-2 font-medium">Type</th>
                <th className="px-3 py-2 font-medium">Manifest</th>
                <th className="px-3 py-2 font-medium">Status</th>
                <th className="px-3 py-2 font-medium">Registered</th>
                <th className="px-3 py-2 text-right font-medium">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {visibleItems.map((artifact) => (
                <tr key={artifact.id} className="hover:bg-muted/50">
                  <td className="px-3 py-2">
                    <Link to={artifactPath(artifact.id)} className="font-medium text-foreground hover:text-primary">
                      {artifact.artifactId}
                    </Link>
                    <div className="text-xs text-muted-foreground">{artifact.producer?.producerName ?? "Manual registration"} · {artifact.resources.length} resources</div>
                  </td>
                  <td className="px-3 py-2"><Badge>{artifact.artifactTypeId ?? workflowArtifactType}</Badge></td>
                  <td className="px-3 py-2">{artifact.manifest.name || "Unnamed"} {artifact.manifest.version ? `v${artifact.manifest.version}` : ""}</td>
                  <td className="px-3 py-2">
                    <div className="flex flex-wrap gap-1">
                      <Badge>{artifact.inspectionStatus}</Badge>
                      {artifact.status === "Archived" ? <Badge>Archived</Badge> : null}
                    </div>
                  </td>
                  <td className="px-3 py-2 text-muted-foreground">{formatDateTime(artifact.registeredAt)}</td>
                  <td className="px-3 py-2 text-right">
                    <div className="flex justify-end gap-2">
                      <Link to={artifactPath(artifact.id)} className="text-xs font-medium text-primary hover:underline">
                        Details
                      </Link>
                      {canManageSetup ? (
                        artifact.status === "Archived" ? (
                          <button type="button" className="text-xs font-medium text-primary hover:underline" disabled={restore.isPending} onClick={() => restore.mutate(artifact.id)}>
                            Restore
                          </button>
                        ) : (
                          <button type="button" className="text-xs font-medium text-primary hover:underline" disabled={archive.isPending} onClick={() => archive.mutate(artifact.id)}>
                            Archive
                          </button>
                        )
                      ) : null}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Table>
      )}
      {archive.error instanceof Error || restore.error instanceof Error ? (
        <p role="alert" className="text-sm text-destructive">{archive.error instanceof Error ? archive.error.message : restore.error instanceof Error ? restore.error.message : null}</p>
      ) : null}
    </section>
  );
}

export function ArtifactCreatePage() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const workspaceContext = useWorkspaceContext();
  const workspaceId = workspaceContext.selectedWorkspaceId;
  const permissions = useQuery({
    queryKey: queryKeys.deploymentPermissions(workspaceId),
    queryFn: () => getDeploymentPermissions(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const canManageSetup = Boolean(permissions.data?.permissions.includes("deployments.setup.manage"));
  const register = useMutation({
    mutationFn: (request: WorkspaceArtifactRegistrationRequest) => registerWorkspaceArtifact(workspaceId, request),
    onSuccess: (artifact) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
      navigate(artifactPath(artifact.id));
    }
  });
  const uploadCapabilities = useQuery({
    queryKey: ["artifact-upload-capabilities", workspaceId],
    queryFn: () => getArtifactUploadCapabilities(workspaceId),
    enabled: Boolean(workspaceId)
  });

  if (workspaceContext.isLoading || permissions.isLoading || uploadCapabilities.isLoading)
    return <RequestStateView state="loading" title="Loading artifact setup" />;
  if (!workspaceId)
    return <EmptyState title="No workspace selected" description="Select an organization workspace before artifacts can be uploaded or registered." />;
  if (workspaceContext.isError || permissions.isError || uploadCapabilities.isError)
    return <RequestStateView state="unexpected" title="Artifact setup could not load" />;

  return (
    <section className="space-y-5">
      <nav aria-label="Breadcrumb" className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
        <Link to="/admin/artifacts" className="hover:text-foreground">Artifacts</Link>
        <span>/</span>
        <span className="text-foreground">New artifact</span>
      </nav>
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h1 className="text-xl font-semibold">New artifact</h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            Upload a deployment artifact package when backend upload sessions are available, or register safe metadata for an artifact managed by CI or another producer.
          </p>
        </div>
        <Link to="/admin/artifacts" className={buttonClassName("secondary", "self-start")}>
          <ArrowLeft className="h-4 w-4" />
          Back to list
        </Link>
      </div>

      {!canManageSetup ? (
        <p className="text-sm text-muted-foreground">Deployment setup permission is required to upload or register artifacts.</p>
      ) : null}

      <ArtifactUploadPanel canManageSetup={canManageSetup} maxUploadBytes={uploadCapabilities.data?.maxUploadBytes ?? 52_428_800} workspaceId={workspaceId} />

      {uploadCapabilities.data?.sampleArtifactGenerationEnabled ? (
        <SampleArtifactPanel canManageSetup={canManageSetup} workspaceId={workspaceId} />
      ) : null}

      <section className="space-y-3">
        <div>
          <h2 className="font-semibold">Advanced metadata registration</h2>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            Use this for CI, local test references, or integration debugging. Uploaded artifacts should not require users to type identity, digest, manifest, or resource metadata.
          </p>
        </div>
        <ArtifactRegistrationPanel
          disabled={!canManageSetup}
          isSubmitting={register.isPending}
          error={register.error instanceof Error ? register.error.message : undefined}
          onSubmit={(request) => register.mutate(request)}
        />
      </section>
    </section>
  );
}

export function ArtifactDetailsPage() {
  const queryClient = useQueryClient();
  const { artifactId } = useParams();
  const workspaceContext = useWorkspaceContext();
  const workspaceId = workspaceContext.selectedWorkspaceId;
  const [isInspectionRefreshing, setIsInspectionRefreshing] = useState(false);
  const inspectionRefreshTimeout = useRef<ReturnType<typeof window.setTimeout> | null>(null);
  const permissions = useQuery({
    queryKey: queryKeys.deploymentPermissions(workspaceId),
    queryFn: () => getDeploymentPermissions(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const artifact = useQuery({
    queryKey: queryKeys.artifactDetails(workspaceId, artifactId ?? ""),
    queryFn: () => getWorkspaceArtifact(workspaceId, artifactId!),
    enabled: Boolean(workspaceId && artifactId)
  });
  const artifactTypes = useQuery({
    queryKey: ["artifact-types", workspaceId],
    queryFn: () => listWorkspaceArtifactTypes(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const canManageSetup = Boolean(permissions.data?.permissions.includes("deployments.setup.manage"));
  const archive = useArtifactArchiveMutation(workspaceId, artifactId ?? "");
  const restore = useArtifactRestoreMutation(workspaceId, artifactId ?? "");
  const refresh = useMutation({
    mutationFn: (current: WorkspaceArtifact) => refreshWorkspaceArtifactInspection(workspaceId, current.id),
    onMutate: () => {
      if (inspectionRefreshTimeout.current) {
        window.clearTimeout(inspectionRefreshTimeout.current);
        inspectionRefreshTimeout.current = null;
      }
      setIsInspectionRefreshing(true);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
      if (artifactId)
        void queryClient.invalidateQueries({ queryKey: queryKeys.artifactDetails(workspaceId, artifactId) });
    },
    onSettled: () => {
      inspectionRefreshTimeout.current = window.setTimeout(() => {
        setIsInspectionRefreshing(false);
        inspectionRefreshTimeout.current = null;
      }, 600);
    }
  });

  useEffect(() => () => {
    if (inspectionRefreshTimeout.current)
      window.clearTimeout(inspectionRefreshTimeout.current);
  }, []);

  if (workspaceContext.isLoading || artifact.isLoading)
    return <RequestStateView state="loading" title="Loading artifact detail" />;
  if (!workspaceId)
    return <EmptyState title="No workspace selected" description="Select an organization workspace before artifact metadata can be shown." />;
  if (!artifactId || workspaceContext.isError || artifact.isError || !artifact.data)
    return <RequestStateView state="unexpected" title="Artifact could not load" />;

  const artifactTypeLabel = artifactTypes.data?.items.find((type) => type.typeId === (artifact.data.artifactTypeId ?? workflowArtifactType))?.displayName;
  const showInspectionRefreshing = refresh.isPending || isInspectionRefreshing;

  return (
    <section className="space-y-5">
      <nav aria-label="Breadcrumb" className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
        <Link to="/admin/artifacts" className="hover:text-foreground">Artifacts</Link>
        <span>/</span>
        <span className="text-foreground">{artifact.data.artifactId}</span>
      </nav>
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h1 className="break-words text-xl font-semibold">{artifact.data.artifactId}</h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            Immutable artifact metadata, references, checksum state, inspection diagnostics, and compatibility details.
          </p>
        </div>
        <div className="flex flex-wrap gap-2 self-start md:justify-end">
          {canManageSetup ? (
            artifact.data.status === "Archived" ? (
              <SecondaryButton disabled={restore.isPending} onClick={() => restore.mutate(artifact.data.id)}>
                <RotateCcw className="h-4 w-4" />
                {restore.isPending ? "Restoring" : "Restore"}
              </SecondaryButton>
            ) : (
              <SecondaryButton disabled={archive.isPending} onClick={() => archive.mutate(artifact.data.id)}>
                <Archive className="h-4 w-4" />
                {archive.isPending ? "Archiving" : "Archive"}
              </SecondaryButton>
            )
          ) : null}
          <SecondaryButton className="md:shrink-0" disabled={!canManageSetup || showInspectionRefreshing || artifact.data.status === "Archived"} onClick={() => refresh.mutate(artifact.data)}>
            <RefreshCw className={`h-4 w-4${showInspectionRefreshing ? " animate-spin" : ""}`} />
            {showInspectionRefreshing ? "Refreshing" : "Refresh inspection"}
          </SecondaryButton>
        </div>
      </div>
      {!canManageSetup ? <p className="text-xs text-muted-foreground">Deployment setup permission is required to refresh or archive artifacts.</p> : null}
      {artifact.data.status === "Archived" ? (
        <p className="rounded-ui border border-border bg-muted/40 px-3 py-2 text-sm text-muted-foreground">
          This artifact is archived and hidden from new revision selection. Existing history and references can still open this detail page.
        </p>
      ) : null}
      {archive.error instanceof Error || restore.error instanceof Error ? (
        <p role="alert" className="text-sm text-destructive">{archive.error instanceof Error ? archive.error.message : restore.error instanceof Error ? restore.error.message : null}</p>
      ) : null}
      <ArtifactDetail
        artifact={artifact.data}
        workspaceId={workspaceId}
        error={refresh.error instanceof Error ? refresh.error.message : undefined}
        artifactTypeLabel={artifactTypeLabel}
        isInspectionRefreshing={showInspectionRefreshing}
      />
    </section>
  );
}

function ArtifactRegistrationPanel({
  disabled = false,
  isSubmitting,
  error,
  onSubmit
}: {
  disabled?: boolean;
  isSubmitting: boolean;
  error?: string;
  onSubmit: (request: WorkspaceArtifactRegistrationRequest) => void;
}) {
  const [artifactId, setArtifactId] = useState("sha256:example-artifact");
  const [digest, setDigest] = useState("example-artifact");
  const [format, setFormat] = useState<WorkspaceArtifactRegistrationRequest["format"]>("Zip");
  const [reference, setReference] = useState("local:///tmp/claims-prod.zip");
  const [manifestName, setManifestName] = useState("claims");

  return (
    <form
      className="grid gap-3 rounded-ui border border-border bg-surface p-4 md:grid-cols-2"
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit({
          artifactId,
          layoutVersion,
          contentDigest: { algorithm: "sha256", value: digest },
          envelopeVersion,
          artifactTypeId: workflowArtifactType,
          artifactSchemaVersion: "1.0",
          payloadReference: { provider: "local", uri: reference, mediaType: null, sizeBytes: null, referenceDigest: null, expiresAt: null },
          producer: { producerType: "manual", producerName: "Manual registration", producerVersion: null, sourceReference: null },
          displayMetadata: { name: manifestName, version: "1.0.0", description: null, labels: {}, annotations: {}, source: "prod" },
          compatibilityHints: [
            {
              requiredArtifactType: workflowArtifactType,
              runtimeFamily: "elsa-workflows",
              runtimeVersionRange: null,
              requiredCapabilities: ["workflow-definition.apply"],
              environmentConstraints: {}
            }
          ],
          format,
          referenceProvider: "local",
          reference,
          manifest: { name: manifestName, version: "1.0.0", environment: "prod" },
          resources: [],
          diagnostics: []
        });
      }}
    >
      <label className="text-xs font-medium text-muted-foreground">
        Artifact identity
        <Input className="mt-1" disabled={disabled || isSubmitting} value={artifactId} onChange={(event) => setArtifactId(event.target.value)} />
      </label>
      <label className="text-xs font-medium text-muted-foreground">
        Content digest
        <Input className="mt-1" disabled={disabled || isSubmitting} value={digest} onChange={(event) => setDigest(event.target.value)} />
      </label>
      <label className="text-xs font-medium text-muted-foreground">
        Format
        <Select className="mt-1 w-full" disabled={disabled || isSubmitting} value={format} onChange={(event) => setFormat(event.target.value as WorkspaceArtifactRegistrationRequest["format"])}>
          <option value="Zip">Zip</option>
          <option value="Folder">Folder</option>
        </Select>
      </label>
      <label className="text-xs font-medium text-muted-foreground">
        Manifest name
        <Input className="mt-1" disabled={disabled || isSubmitting} value={manifestName} onChange={(event) => setManifestName(event.target.value)} />
      </label>
      <label className="md:col-span-2 text-xs font-medium text-muted-foreground">
        Reference
        <Input className="mt-1" disabled={disabled || isSubmitting} value={reference} onChange={(event) => setReference(event.target.value)} />
      </label>
      <div className="md:col-span-2 flex items-center justify-between gap-3">
        {error ? <p role="alert" className="text-sm text-destructive">{error}</p> : <span />}
        <Button disabled={disabled || isSubmitting}>
          <Save className="h-4 w-4" />
          {isSubmitting ? "Saving artifact" : "Save artifact"}
        </Button>
      </div>
    </form>
  );
}

function useArtifactArchiveMutation(workspaceId: string, detailArtifactId = "") {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (artifactId: string) => archiveWorkspaceArtifact(workspaceId, artifactId),
    onSuccess: async (artifact) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
      const detailKey = detailArtifactId || artifact.id;
      await queryClient.invalidateQueries({ queryKey: queryKeys.artifactDetails(workspaceId, detailKey) });
    }
  });
}

function useArtifactRestoreMutation(workspaceId: string, detailArtifactId = "") {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (artifactId: string) => restoreWorkspaceArtifact(workspaceId, artifactId),
    onSuccess: async (artifact) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
      const detailKey = detailArtifactId || artifact.id;
      await queryClient.invalidateQueries({ queryKey: queryKeys.artifactDetails(workspaceId, detailKey) });
    }
  });
}

function FilterButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      className={buttonClassName(active ? "primary" : "secondary")}
      onClick={onClick}
    >
      {children}
    </button>
  );
}

function ArtifactUploadPanel({
  canManageSetup,
  maxUploadBytes,
  workspaceId
}: {
  canManageSetup: boolean;
  maxUploadBytes: number;
  workspaceId: string;
}) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [uploadId, setUploadId] = useState<string | null>(null);
  const [progress, setProgress] = useState(0);
  const [phase, setPhase] = useState<"idle" | "creating" | "uploading" | "processing" | "completed" | "failed">("idle");
  const [diagnostics, setDiagnostics] = useState<WorkspaceArtifactDiagnostic[]>([]);
  const [error, setError] = useState<string | null>(null);
  const isBusy = phase === "creating" || phase === "uploading" || phase === "processing";

  async function startUpload() {
    if (!selectedFile)
      return;
    setError(null);
    setDiagnostics([]);
    setProgress(0);
    setPhase("creating");
    try {
      const session = await createArtifactUpload(workspaceId, {
        fileName: selectedFile.name,
        contentType: selectedFile.type || "application/zip",
        sizeBytes: selectedFile.size,
        idempotencyKey: `${selectedFile.name}:${selectedFile.size}:${selectedFile.lastModified}`
      });
      setUploadId(session.uploadId);
      setPhase("uploading");
      await uploadArtifactContent(workspaceId, session.uploadId, selectedFile, setProgress);
      setPhase("processing");
      const completed = await completeArtifactUpload(workspaceId, session.uploadId);
      setDiagnostics(completed.diagnostics);
      if (completed.artifact) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
        setPhase("completed");
        navigate(artifactPath(completed.artifact.id));
        return;
      }

      setPhase("failed");
      setError(uploadFailureMessage(completed));
    } catch (ex) {
      setPhase("failed");
      setError(ex instanceof Error ? ex.message : "Artifact upload failed.");
    }
  }

  async function abortUpload() {
    if (uploadId)
      await abortArtifactUpload(workspaceId, uploadId);
    setUploadId(null);
    setSelectedFile(null);
    setProgress(0);
    setPhase("idle");
    setDiagnostics([]);
    setError(null);
  }

  const fileTooLarge = Boolean(selectedFile && selectedFile.size > maxUploadBytes);
  const invalidExtension = Boolean(selectedFile && !selectedFile.name.toLowerCase().endsWith(".zip"));

  return (
    <section className="rounded-ui border border-border bg-surface p-4">
      <div className="flex items-start gap-3">
        <div className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-ui border border-primary/20 bg-primary/10 text-primary">
          <Upload className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1">
          <h2 className="font-semibold">Upload artifact package</h2>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            Upload a ZIP artifact. The backend computes identity, digest, manifest metadata, resource summaries, and inspection status from the package contents.
          </p>
        </div>
      </div>

      <div className="mt-4 grid gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <label className="flex min-h-36 cursor-pointer flex-col items-center justify-center rounded-ui border border-dashed border-border bg-background px-4 py-6 text-center transition-colors hover:bg-muted/50">
          <FileArchive className="h-8 w-8 text-primary" />
          <span className="mt-3 text-sm font-medium">{selectedFile ? selectedFile.name : "Choose a ZIP artifact"}</span>
          <span className="mt-1 text-xs text-muted-foreground">
            {selectedFile ? formatFileSize(selectedFile.size) : "Drag-and-drop can use this same surface once upload sessions are implemented."}
          </span>
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
              setDiagnostics([]);
              setError(null);
            }}
          />
        </label>

        <div className="rounded-ui border border-border bg-background p-3 text-sm">
          <h3 className="font-medium">Upload status</h3>
          <dl className="mt-3 space-y-3">
            <div>
              <dt className="text-xs text-muted-foreground">Selected file</dt>
              <dd className="mt-1 break-words font-medium">{selectedFile?.name ?? "None"}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Transfer</dt>
              <dd className="mt-1 font-medium">{uploadPhaseLabel(phase)}</dd>
              {phase !== "idle" ? (
                <div className="mt-2 h-2 overflow-hidden rounded-full bg-muted">
                  <div className="h-full bg-primary transition-all" style={{ width: `${progress}%` }} />
                </div>
              ) : null}
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Limits</dt>
              <dd className="mt-1 font-medium">ZIP up to {formatFileSize(maxUploadBytes)}</dd>
            </div>
          </dl>
          {fileTooLarge ? <p role="alert" className="mt-3 text-sm text-destructive">Selected file exceeds the upload limit.</p> : null}
          {invalidExtension ? <p role="alert" className="mt-3 text-sm text-destructive">Select a ZIP artifact package.</p> : null}
          {error ? <p role="alert" className="mt-3 text-sm text-destructive">{error}</p> : null}
          <DiagnosticList diagnostics={diagnostics} />
          <div className="mt-4 flex gap-2">
            <Button className="flex-1" disabled={!canManageSetup || !selectedFile || fileTooLarge || invalidExtension || isBusy} onClick={() => void startUpload()}>
              <Upload className="h-4 w-4" />
              {isBusy ? "Uploading" : "Upload ZIP"}
            </Button>
            {(uploadId || selectedFile) && !isBusy ? (
              <SecondaryButton type="button" onClick={() => void abortUpload()} aria-label="Clear upload">
                <X className="h-4 w-4" />
              </SecondaryButton>
            ) : null}
          </div>
        </div>
      </div>
    </section>
  );
}

function SampleArtifactPanel({ canManageSetup, workspaceId }: { canManageSetup: boolean; workspaceId: string }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [artifactName, setArtifactName] = useState("dev-sample");
  const [version, setVersion] = useState("2026.06.04.1");
  const [environment, setEnvironment] = useState("dev");
  const [workflowId, setWorkflowId] = useState("order-approval");
  const [diagnostics, setDiagnostics] = useState<WorkspaceArtifactDiagnostic[]>([]);
  const sample = useMutation({
    mutationFn: () => createSampleArtifact(workspaceId, { artifactName, version, environment, workflowId }),
    onSuccess: async (completed) => {
      setDiagnostics(completed.diagnostics);
      if (completed.artifact) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
        navigate(artifactPath(completed.artifact.id));
      }
    }
  });

  return (
    <section className="rounded-ui border border-border bg-surface p-4">
      <div className="flex items-start gap-3">
        <div className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-ui border border-primary/20 bg-primary/10 text-primary">
          <Wand2 className="h-4 w-4" />
        </div>
        <div>
          <h2 className="font-semibold">Development sample artifact</h2>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            Generate a valid ZIP through the same backend upload completion path for local deployment testing.
          </p>
        </div>
      </div>
      <form
        className="mt-4 grid gap-3 md:grid-cols-4"
        onSubmit={(event) => {
          event.preventDefault();
          sample.mutate();
        }}
      >
        <label className="text-xs font-medium text-muted-foreground">
          Artifact name
          <Input className="mt-1" disabled={!canManageSetup || sample.isPending} value={artifactName} onChange={(event) => setArtifactName(event.target.value)} />
        </label>
        <label className="text-xs font-medium text-muted-foreground">
          Version
          <Input className="mt-1" disabled={!canManageSetup || sample.isPending} value={version} onChange={(event) => setVersion(event.target.value)} />
        </label>
        <label className="text-xs font-medium text-muted-foreground">
          Environment
          <Input className="mt-1" disabled={!canManageSetup || sample.isPending} value={environment} onChange={(event) => setEnvironment(event.target.value)} />
        </label>
        <label className="text-xs font-medium text-muted-foreground">
          Workflow id
          <Input className="mt-1" disabled={!canManageSetup || sample.isPending} value={workflowId} onChange={(event) => setWorkflowId(event.target.value)} />
        </label>
        <div className="md:col-span-4 flex items-center justify-between gap-3">
          <div>
            {sample.error instanceof Error ? <p role="alert" className="text-sm text-destructive">{sample.error.message}</p> : null}
            <DiagnosticList diagnostics={diagnostics} />
          </div>
          <Button disabled={!canManageSetup || sample.isPending}>
            <Wand2 className="h-4 w-4" />
            {sample.isPending ? "Creating sample" : "Create sample artifact"}
          </Button>
        </div>
      </form>
    </section>
  );
}

function ArtifactDetail({
  artifact,
  workspaceId,
  error,
  artifactTypeLabel,
  isInspectionRefreshing = false
}: {
  artifact: WorkspaceArtifact;
  workspaceId: string;
  error?: string;
  artifactTypeLabel?: string;
  isInspectionRefreshing?: boolean;
}) {
  const compatibility = artifact.compatibilityHints ?? [];
  const compatibilityItems = compatibility
    .flatMap((hint) => [hint.runtimeFamily, ...hint.requiredCapabilities])
    .filter((item): item is string => Boolean(item));
  const display = artifact.displayMetadata;
  const reference = artifactReferenceDisplay(artifact);
  return (
    <div className="space-y-4 rounded-ui border border-border bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-2">
          <Archive className="h-4 w-4" />
          <h2 className="font-semibold">Artifact metadata</h2>
        </div>
      </div>
      <dl className="grid gap-3 text-sm sm:grid-cols-2">
        <Detail label="Type" value={`${artifact.artifactTypeId ?? workflowArtifactType}${artifactTypeLabel ? ` · ${artifactTypeLabel}` : ""}`} />
        <Detail label="Producer" value={artifact.producer?.producerName ?? "Manual registration"} />
        <Detail label="Display" value={[display?.name ?? artifact.manifest.name, display?.version ?? artifact.manifest.version].filter(Boolean).join(" ")} />
        <Detail label="Layout" value={artifact.layoutVersion} />
        <Detail label="Envelope" value={artifact.envelopeVersion ?? envelopeVersion} />
        <Detail label="Schema" value={artifact.artifactSchemaVersion ?? "1.0"} />
        <Detail label="Digest" value={`${artifact.contentDigest.algorithm}:${artifact.contentDigest.value}`} />
        <Detail
          label="Reference"
          value={
            isDownloadableLocalArtifact(artifact) ? (
              <a className="text-primary underline-offset-4 hover:underline" href={artifactDownloadPath(workspaceId, artifact.id)} download>
                {reference}
              </a>
            ) : reference
          }
        />
        <Detail label="Checksum" value={artifact.checksumStatus} />
        <Detail
          label="Inspection"
          value={
            isInspectionRefreshing ? (
              <span aria-live="polite" className="inline-flex items-center gap-1.5 text-primary">
                <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                Inspecting
              </span>
            ) : artifact.inspectionStatus
          }
        />
        <Detail label="Lifecycle" value={artifact.status} />
        {artifact.archivedAt ? <Detail label="Archived" value={formatDateTime(artifact.archivedAt)} /> : null}
        <Detail label="Last inspected" value={formatDateTime(artifact.lastInspectedAt)} />
      </dl>
      {compatibility.length > 0 ? (
        <div>
          <h3 className="text-sm font-medium">Compatibility</h3>
          <div className="mt-2 flex flex-wrap gap-2">
            {compatibilityItems.map((item) => (
              <Badge key={item}>{item}</Badge>
            ))}
          </div>
        </div>
      ) : null}
      <div>
        <h3 className="text-sm font-medium">Resources</h3>
        <div className="mt-2 flex flex-wrap gap-2">
          {artifact.resources.length === 0 ? <span className="text-xs text-muted-foreground">No resource summaries registered.</span> : null}
          {artifact.resources.map((resource) => (
            <Badge key={`${resource.type}:${resource.logicalId}`}>{resource.type} · {resource.logicalId}</Badge>
          ))}
        </div>
      </div>
      {artifact.diagnostics.length > 0 ? (
        <div className="space-y-2">
          <h3 className="text-sm font-medium">Diagnostics</h3>
          {artifact.diagnostics.map((diagnostic) => (
            <div key={`${diagnostic.code}:${diagnostic.message}`} className="rounded-ui border border-border bg-background px-3 py-2 text-sm">
              <span className="font-medium">{diagnostic.severity}</span> · {diagnostic.message}
            </div>
          ))}
        </div>
      ) : null}
      {error ? <p role="alert" className="text-sm text-destructive">{error}</p> : null}
    </div>
  );
}

function artifactPath(artifactId: string) {
  return `/admin/artifacts/${encodeURIComponent(artifactId)}`;
}

function artifactDownloadPath(workspaceId: string, artifactId: string) {
  return `/api/workspaces/${encodeURIComponent(workspaceId)}/artifacts/${encodeURIComponent(artifactId)}/download`;
}

function artifactReferenceDisplay(artifact: WorkspaceArtifact) {
  return `${artifact.payloadReference?.provider ?? artifact.referenceProvider} · ${artifact.payloadReference?.uri ?? artifact.reference}`;
}

function isDownloadableLocalArtifact(artifact: WorkspaceArtifact) {
  const displayedProvider = artifact.payloadReference?.provider ?? artifact.referenceProvider;
  return displayedProvider.toLowerCase() === "local" && artifact.referenceProvider.toLowerCase() === "local" && artifact.format !== "Folder";
}

function formatFileSize(size: number) {
  if (size < 1024)
    return `${size} B`;

  const units = ["KB", "MB", "GB"];
  let value = size / 1024;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unitIndex]}`;
}

function uploadPhaseLabel(phase: "idle" | "creating" | "uploading" | "processing" | "completed" | "failed") {
  switch (phase) {
    case "creating":
      return "Creating upload session";
    case "uploading":
      return "Uploading ZIP bytes";
    case "processing":
      return "Inspecting artifact";
    case "completed":
      return "Artifact registered";
    case "failed":
      return "Upload failed";
    default:
      return "Waiting for a ZIP";
  }
}

function uploadFailureMessage(completed: CompleteArtifactUploadResponse) {
  const error = completed.diagnostics.find((diagnostic) => diagnostic.severity === "Error");
  return error?.message ?? "Artifact upload could not be completed.";
}

function DiagnosticList({ diagnostics }: { diagnostics: WorkspaceArtifactDiagnostic[] }) {
  if (diagnostics.length === 0)
    return null;

  return (
    <div className="mt-3 space-y-2">
      {diagnostics.map((diagnostic) => (
        <div key={`${diagnostic.code}:${diagnostic.message}`} className="flex gap-2 rounded-ui border border-border bg-surface px-3 py-2 text-xs">
          {diagnostic.severity === "Error" ? <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-destructive" /> : <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0 text-primary" />}
          <div>
            <div className="font-medium">{diagnostic.code}</div>
            <div className="mt-0.5 text-muted-foreground">{diagnostic.message}</div>
          </div>
        </div>
      ))}
    </div>
  );
}

function Detail({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-medium">{value || "-"}</dd>
    </div>
  );
}
