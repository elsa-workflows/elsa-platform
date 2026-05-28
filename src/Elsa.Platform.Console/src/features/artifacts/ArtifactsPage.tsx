import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, Plus, RefreshCw, Save } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Badge, Button, EmptyState, Input, SecondaryButton, Select, Table } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import {
  getWorkspaceArtifact,
  listWorkspaceArtifactTypes,
  listWorkspaceArtifacts,
  refreshWorkspaceArtifactInspection,
  registerWorkspaceArtifact
} from "@/features/artifacts/artifactApi";
import type { WorkspaceArtifact, WorkspaceArtifactRegistrationRequest } from "@/features/artifacts/artifactModels";
import { getDeploymentPermissions, getDeploymentWorkspaceContext } from "@/features/deployments/deploymentApi";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";

const layoutVersion = "platform.elsa.io/deployment-artifact/v1alpha1";
const envelopeVersion = "platform.elsa.io/artifact-envelope/v1alpha1";
const workflowArtifactType = "elsa.workflow-definition";

export function ArtifactsPage() {
  const queryClient = useQueryClient();
  const [selectedArtifactId, setSelectedArtifactId] = useState("");
  const [showRegister, setShowRegister] = useState(false);
  const workspaceContext = useQuery({ queryKey: queryKeys.deploymentWorkspaceContext, queryFn: getDeploymentWorkspaceContext });
  const workspaceId = workspaceContext.data?.workspaces[0]?.id ?? "";
  const permissions = useQuery({
    queryKey: queryKeys.deploymentPermissions(workspaceId),
    queryFn: () => getDeploymentPermissions(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const artifacts = useQuery({
    queryKey: queryKeys.artifacts(workspaceId),
    queryFn: () => listWorkspaceArtifacts(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const artifactTypes = useQuery({
    queryKey: ["artifact-types", workspaceId],
    queryFn: () => listWorkspaceArtifactTypes(workspaceId),
    enabled: Boolean(workspaceId)
  });
  const selectedArtifact = useMemo(
    () => artifacts.data?.items.find((artifact) => artifact.id === selectedArtifactId) ?? artifacts.data?.items[0],
    [artifacts.data?.items, selectedArtifactId]
  );
  const detail = useQuery({
    queryKey: queryKeys.artifactDetails(workspaceId, selectedArtifact?.id ?? ""),
    queryFn: () => getWorkspaceArtifact(workspaceId, selectedArtifact!.id),
    enabled: Boolean(workspaceId && selectedArtifact?.id)
  });
  const canManageSetup = Boolean(permissions.data?.permissions.includes("deployments.setup.manage"));
  const invalidateArtifacts = () => queryClient.invalidateQueries({ queryKey: queryKeys.artifacts(workspaceId) });
  const register = useMutation({
    mutationFn: (request: WorkspaceArtifactRegistrationRequest) => registerWorkspaceArtifact(workspaceId, request),
    onSuccess: (artifact) => {
      setSelectedArtifactId(artifact.id);
      setShowRegister(false);
      void invalidateArtifacts();
    }
  });
  const refresh = useMutation({
    mutationFn: (artifact: WorkspaceArtifact) => refreshWorkspaceArtifactInspection(workspaceId, artifact.id),
    onSuccess: () => {
      void invalidateArtifacts();
      if (selectedArtifact?.id)
        void queryClient.invalidateQueries({ queryKey: queryKeys.artifactDetails(workspaceId, selectedArtifact.id) });
    }
  });

  useEffect(() => {
    if (!selectedArtifactId && selectedArtifact?.id)
      setSelectedArtifactId(selectedArtifact.id);
  }, [selectedArtifact?.id, selectedArtifactId]);

  if (workspaceContext.isLoading || artifacts.isLoading)
    return <RequestStateView state="loading" title="Loading artifacts" />;
  if (!workspaceId)
    return <EmptyState title="No workspaces available" description="Artifacts need a workspace before registry metadata can be shown." />;
  if (workspaceContext.isError || artifacts.isError)
    return <RequestStateView state="unexpected" title="Artifacts could not load" />;

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
          <Button disabled={!canManageSetup} onClick={() => setShowRegister((current) => !current)}>
            <Plus className="h-4 w-4" />
            Register artifact
          </Button>
        </div>
      </div>

      {!canManageSetup ? <p className="text-xs text-muted-foreground">Deployment setup permission is required to register or refresh artifacts.</p> : null}
      {showRegister ? (
        <ArtifactRegistrationPanel
          isSubmitting={register.isPending}
          error={register.error instanceof Error ? register.error.message : undefined}
          onSubmit={(request) => register.mutate(request)}
        />
      ) : null}

      {artifacts.data?.items.length === 0 ? (
        <EmptyState title="No artifacts registered" description="Submitted and manually registered artifacts appear here with type, producer, digest, and compatibility metadata." />
      ) : (
        <div className="grid gap-4 lg:grid-cols-[1.1fr_1fr]">
          <Table>
            <table className="min-w-full divide-y divide-border text-sm">
              <thead className="bg-muted/40 text-left text-xs text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 font-medium">Artifact</th>
                  <th className="px-3 py-2 font-medium">Type</th>
                  <th className="px-3 py-2 font-medium">Manifest</th>
                  <th className="px-3 py-2 font-medium">Status</th>
                  <th className="px-3 py-2 font-medium">Registered</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {artifacts.data!.items.map((artifact) => (
                  <tr
                    key={artifact.id}
                    className={artifact.id === selectedArtifact?.id ? "bg-primary/5" : "cursor-pointer hover:bg-muted/50"}
                    onClick={() => setSelectedArtifactId(artifact.id)}
                  >
                    <td className="px-3 py-2">
                      <div className="font-medium">{artifact.artifactId}</div>
                      <div className="text-xs text-muted-foreground">{artifact.producer?.producerName ?? "Manual registration"} · {artifact.resources.length} resources</div>
                    </td>
                    <td className="px-3 py-2"><Badge>{artifact.artifactTypeId ?? workflowArtifactType}</Badge></td>
                    <td className="px-3 py-2">{artifact.manifest.name || "Unnamed"} {artifact.manifest.version ? `v${artifact.manifest.version}` : ""}</td>
                    <td className="px-3 py-2"><Badge>{artifact.inspectionStatus}</Badge></td>
                    <td className="px-3 py-2 text-muted-foreground">{formatDateTime(artifact.registeredAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Table>

          {detail.data ? (
            <ArtifactDetail
              artifact={detail.data}
              canRefresh={canManageSetup}
              isRefreshing={refresh.isPending}
              error={refresh.error instanceof Error ? refresh.error.message : undefined}
              onRefresh={() => refresh.mutate(detail.data)}
              artifactTypeLabel={artifactTypes.data?.items.find((type) => type.typeId === (detail.data.artifactTypeId ?? workflowArtifactType))?.displayName}
            />
          ) : (
            <RequestStateView state="loading" title="Loading artifact detail" />
          )}
        </div>
      )}
    </section>
  );
}

function ArtifactRegistrationPanel({
  isSubmitting,
  error,
  onSubmit
}: {
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
        <Input className="mt-1" value={artifactId} onChange={(event) => setArtifactId(event.target.value)} />
      </label>
      <label className="text-xs font-medium text-muted-foreground">
        Content digest
        <Input className="mt-1" value={digest} onChange={(event) => setDigest(event.target.value)} />
      </label>
      <label className="text-xs font-medium text-muted-foreground">
        Format
        <Select className="mt-1 w-full" value={format} onChange={(event) => setFormat(event.target.value as WorkspaceArtifactRegistrationRequest["format"])}>
          <option value="Zip">Zip</option>
          <option value="Folder">Folder</option>
        </Select>
      </label>
      <label className="text-xs font-medium text-muted-foreground">
        Manifest name
        <Input className="mt-1" value={manifestName} onChange={(event) => setManifestName(event.target.value)} />
      </label>
      <label className="md:col-span-2 text-xs font-medium text-muted-foreground">
        Reference
        <Input className="mt-1" value={reference} onChange={(event) => setReference(event.target.value)} />
      </label>
      <div className="md:col-span-2 flex items-center justify-between gap-3">
        {error ? <p role="alert" className="text-sm text-destructive">{error}</p> : <span />}
        <Button disabled={isSubmitting}>
          <Save className="h-4 w-4" />
          Save artifact
        </Button>
      </div>
    </form>
  );
}

function ArtifactDetail({
  artifact,
  canRefresh,
  isRefreshing,
  error,
  onRefresh,
  artifactTypeLabel
}: {
  artifact: WorkspaceArtifact;
  canRefresh: boolean;
  isRefreshing: boolean;
  error?: string;
  onRefresh: () => void;
  artifactTypeLabel?: string;
}) {
  const compatibility = artifact.compatibilityHints ?? [];
  const compatibilityItems = compatibility
    .flatMap((hint) => [hint.runtimeFamily, ...hint.requiredCapabilities])
    .filter((item): item is string => Boolean(item));
  const display = artifact.displayMetadata;
  return (
    <div className="space-y-3 rounded-ui border border-border bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-2">
          <Archive className="h-4 w-4" />
          <h2 className="font-semibold">{artifact.artifactId}</h2>
        </div>
        <SecondaryButton disabled={!canRefresh || isRefreshing} onClick={onRefresh}>
          <RefreshCw className="h-4 w-4" />
          {isRefreshing ? "Refreshing" : "Refresh inspection"}
        </SecondaryButton>
      </div>
      <dl className="grid gap-3 text-sm sm:grid-cols-2">
        <Detail label="Type" value={`${artifact.artifactTypeId ?? workflowArtifactType}${artifactTypeLabel ? ` · ${artifactTypeLabel}` : ""}`} />
        <Detail label="Producer" value={artifact.producer?.producerName ?? "Manual registration"} />
        <Detail label="Display" value={[display?.name ?? artifact.manifest.name, display?.version ?? artifact.manifest.version].filter(Boolean).join(" ")} />
        <Detail label="Layout" value={artifact.layoutVersion} />
        <Detail label="Envelope" value={artifact.envelopeVersion ?? envelopeVersion} />
        <Detail label="Schema" value={artifact.artifactSchemaVersion ?? "1.0"} />
        <Detail label="Digest" value={`${artifact.contentDigest.algorithm}:${artifact.contentDigest.value}`} />
        <Detail label="Reference" value={`${artifact.payloadReference?.provider ?? artifact.referenceProvider} · ${artifact.payloadReference?.uri ?? artifact.reference}`} />
        <Detail label="Checksum" value={artifact.checksumStatus} />
        <Detail label="Inspection" value={artifact.inspectionStatus} />
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

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-medium">{value || "-"}</dd>
    </div>
  );
}
