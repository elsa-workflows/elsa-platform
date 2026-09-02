import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, CircleHelp, Clock3, LoaderCircle, RefreshCw, ShieldAlert, TriangleAlert, XCircle } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Badge, Button, EmptyState, SecondaryButton } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { getManagedElsaInstanceAudit, getManagedElsaInstanceHealth, listManagedElsaInstances } from "@/features/managed-elsa/managedElsaApi";
import {
  operationalCodeGuidance,
  operationalHealthGuidance,
  type ManagedElsaAuditEvent,
  type ManagedElsaInstance,
  type ManagedElsaInstanceHealth,
  type ManagedElsaOperationalHealthStatus
} from "@/features/managed-elsa/managedElsaModels";
import { ApiError } from "@/lib/api/httpClient";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { cn } from "@/lib/utils";

const listRefreshInterval = 30_000;
const healthRefreshInterval = 30_000;
const auditRefreshInterval = 60_000;

export function ManagedElsaOperationsPage() {
  const workspaceContext = useWorkspaceContext();
  const workspaceId = workspaceContext.selectedWorkspaceId;
  const [selectedInstanceId, setSelectedInstanceId] = useState("");
  const instances = useQuery({
    queryKey: queryKeys.managedElsaInstances(workspaceId),
    queryFn: () => listManagedElsaInstances(workspaceId),
    enabled: Boolean(workspaceId),
    retry: false,
    refetchInterval: listRefreshInterval
  });
  const items = instances.data?.items ?? [];

  // A workspace change invalidates both the selected ID and all detail queries.
  // Keep the old details disabled until this effect selects an instance from the
  // new list, so data cannot flash across workspace boundaries.
  useEffect(() => {
    setSelectedInstanceId((current) => items.some((item) => item.instanceId === current) ? current : items[0]?.instanceId ?? "");
  }, [workspaceId, items]);

  const selectedInstance = useMemo(
    () => items.find((item) => item.instanceId === selectedInstanceId) ?? null,
    [items, selectedInstanceId]
  );
  const health = useQuery({
    queryKey: queryKeys.managedElsaInstanceHealth(workspaceId, selectedInstanceId),
    queryFn: () => getManagedElsaInstanceHealth(workspaceId, selectedInstanceId),
    enabled: Boolean(workspaceId && selectedInstance),
    retry: false,
    refetchInterval: healthRefreshInterval
  });
  const audit = useQuery({
    queryKey: queryKeys.managedElsaInstanceAudit(workspaceId, selectedInstanceId),
    queryFn: () => getManagedElsaInstanceAudit(workspaceId, selectedInstanceId),
    enabled: Boolean(workspaceId && selectedInstance),
    retry: false,
    refetchInterval: auditRefreshInterval
  });

  if (workspaceContext.isLoading)
    return <RequestStateView state="loading" title="Loading workspace context" />;
  if (workspaceContext.isError)
    return <RequestStateView state="unexpected" title="Workspace context could not load" />;
  if (!workspaceId)
    return <RequestStateView state="empty" title="No workspace selected" description="Select an organization workspace to view runtime operations." />;
  if (instances.isLoading)
    return <RequestStateView state="loading" title="Loading managed instances" description="Checking the current workspace instance list." />;
  if (instances.isError && !instances.data)
    return <RetryState title="Managed instances could not load" description="Try again when Elsa Control is available." onRetry={() => void instances.refetch()} />;

  return (
    <section className="space-y-5">
      <OperationsHeader loading={instances.isFetching} onRefresh={() => void instances.refetch()} />

      {instances.isRefetchError ? (
        <InlineError title="The instance list could not be refreshed" onRetry={() => void instances.refetch()} />
      ) : null}

      {items.length === 0 ? (
        <EmptyState
          title="No managed Elsa instances"
          description="Authorized managed instances will appear here when they are provisioned."
          action={<SecondaryButton type="button" onClick={() => void instances.refetch()} disabled={instances.isFetching}>Refresh</SecondaryButton>}
        />
      ) : (
        <>
          <label className="block max-w-xl space-y-1 text-xs font-medium text-muted-foreground">
            <span>Managed instance</span>
            <select
              className="h-10 w-full rounded-ui border border-border bg-background px-3 text-sm text-foreground"
              aria-label="Managed instance"
              value={selectedInstanceId}
              onChange={(event) => setSelectedInstanceId(event.target.value)}
            >
              {items.map((instance) => <option key={instance.instanceId} value={instance.instanceId}>{instance.name}</option>)}
            </select>
          </label>
          {selectedInstance ? (
            <OperationsDetail
              key={`${workspaceId}:${selectedInstance.instanceId}`}
              instance={selectedInstance}
              health={health}
              audit={audit}
            />
          ) : (
            <EmptyState title="Select a managed instance" description="Choose an instance to inspect its operational health and audit history." />
          )}
        </>
      )}
    </section>
  );
}

function OperationsHeader({ loading, onRefresh }: { loading: boolean; onRefresh: () => void }) {
  return (
    <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
      <div>
        <p className="text-xs font-medium uppercase tracking-[0.16em] text-primary">Operate</p>
        <h1 className="mt-1 text-3xl font-semibold">Runtime Operations</h1>
        <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
          Read-only operational health, lifecycle work, and recent safe audit events for the selected managed Elsa instance.
        </p>
      </div>
      <SecondaryButton type="button" onClick={onRefresh} disabled={loading} title="Refresh managed instances">
        <RefreshCw aria-hidden className={cn("h-4 w-4", loading && "animate-spin")} />
        Refresh
      </SecondaryButton>
    </div>
  );
}

function OperationsDetail({
  instance,
  health,
  audit
}: {
  instance: ManagedElsaInstance;
  health: ReturnType<typeof useQuery<ManagedElsaInstanceHealth>>;
  audit: ReturnType<typeof useQuery<{ items: ManagedElsaAuditEvent[] }>>;
}) {
  return (
    <div className="space-y-5" aria-live="polite">
      <section className="rounded-ui border border-border bg-surface p-4">
        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div>
            <h2 className="text-lg font-semibold">{instance.name}</h2>
            <p className="mt-1 text-sm text-muted-foreground">{instance.slug}</p>
          </div>
          <div className="space-y-1 text-right text-xs text-muted-foreground">
            <p>List health</p>
            <InstanceHealthBadge health={instance.health} />
          </div>
        </div>
      </section>

      <HealthSection query={health} />
      <AuditSection query={audit} />
    </div>
  );
}

function HealthSection({ query }: { query: ReturnType<typeof useQuery<ManagedElsaInstanceHealth>> }) {
  if (query.isLoading)
    return <RequestStateView state="loading" title="Loading runtime health" />;
  if (query.isError && !query.data)
    return <RetryState title={queryErrorTitle(query.error, "Runtime health could not load", "Runtime health not found")} description="Refresh this instance to request the current operational projection." onRetry={() => void query.refetch()} />;
  if (!query.data)
    return <RetryState title="Runtime health not found" description="The selected instance has no available operational projection." onRetry={() => void query.refetch()} />;

  const data = query.data;
  const guidance = operationalHealthGuidance[data.status] ?? "No fixed operator guidance is available for this status code.";
  return (
    <section className="space-y-3 rounded-ui border border-border bg-surface p-4">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h2 className="text-base font-semibold">Operational health</h2>
          <p className="mt-1 text-sm text-muted-foreground">Evaluated status and safe lifecycle projections.</p>
        </div>
        <SecondaryButton type="button" onClick={() => void query.refetch()} disabled={query.isFetching} title="Refresh runtime health">
          <RefreshCw aria-hidden className={cn("h-4 w-4", query.isFetching && "animate-spin")} />
          Refresh health
        </SecondaryButton>
      </div>

      {query.isRefetchError ? <InlineError title="Runtime health could not be refreshed" onRetry={() => void query.refetch()} /> : null}

      <div className="grid gap-3 md:grid-cols-3">
        <Metric label="Status" value={<OperationalStatusBadge status={data.status} />} />
        <Metric label="Diagnostic code" value={safeToken(data.diagnosticCode) ?? "Unavailable"} />
        <Metric label="Evaluated" value={formatDateTime(data.evaluatedAt)} detail={`Reconciled ${formatDateTime(data.reconciledAt)}`} />
      </div>
      <div className="rounded-ui border border-border bg-background p-3 text-sm">
        <p className="font-medium">Operator guidance</p>
        <p className="mt-1 text-muted-foreground">{guidance}</p>
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        <ProjectionCard title="Current operation" icon={<Clock3 aria-hidden className="h-4 w-4" />}>
          {data.operation ? <OperationProjection operation={data.operation} /> : <p className="text-sm text-muted-foreground">No current operation.</p>}
        </ProjectionCard>
        <ProjectionCard title="Current deployment run" icon={<LoaderCircle aria-hidden className="h-4 w-4" />}>
          {data.run ? <RunProjection run={data.run} /> : <p className="text-sm text-muted-foreground">No current deployment run.</p>}
        </ProjectionCard>
      </div>

      <Alerts alerts={data.alerts} />
    </section>
  );
}

function OperationProjection({ operation }: { operation: NonNullable<ManagedElsaInstanceHealth["operation"]> }) {
  return (
    <dl className="grid gap-x-4 gap-y-2 text-sm sm:grid-cols-2">
      <Detail label="State" value={safeToken(operation.state) ?? "Unavailable"} />
      <Detail label="Attempt" value={`Attempt ${operation.attemptNumber}`} />
      <Detail label="Accepted" value={formatDateTime(operation.acceptedAt)} />
      <Detail label="Started" value={formatDateTime(operation.startedAt)} />
      <Detail label="Heartbeat" value={formatDateTime(operation.heartbeatAt)} />
      <Detail label="Last progress" value={formatDateTime(operation.lastProgressAt)} />
      <Detail label="Safe code" value={safeToken(operation.diagnosticCode) ?? "Unavailable"} />
    </dl>
  );
}

function RunProjection({ run }: { run: NonNullable<ManagedElsaInstanceHealth["run"]> }) {
  return (
    <dl className="grid gap-x-4 gap-y-2 text-sm sm:grid-cols-2">
      <Detail label="State" value={safeToken(run.status) ?? "Unavailable"} />
      <Detail label="Attempt" value={`Attempt ${run.attemptNumber}`} />
      <Detail label="Queued" value={formatDateTime(run.queuedAt)} />
      <Detail label="Started" value={formatDateTime(run.startedAt)} />
      <Detail label="Heartbeat" value={formatDateTime(run.heartbeatAt)} />
      <Detail label="Last progress" value={formatDateTime(run.lastProgressAt)} />
      <Detail label="Safe code" value={safeToken(run.diagnosticCode) ?? "Unavailable"} />
    </dl>
  );
}

function Alerts({ alerts }: { alerts: ManagedElsaInstanceHealth["alerts"] }) {
  if (!alerts.length)
    return <p className="text-sm text-muted-foreground">No active alerts.</p>;

  return (
    <section className="space-y-2" aria-labelledby="runtime-alerts-heading">
      <h3 id="runtime-alerts-heading" className="text-sm font-semibold">Alerts</h3>
      <div className="grid gap-2 md:grid-cols-2">
        {alerts.map((alert, index) => {
          const code = safeToken(alert.code) ?? "Unavailable";
          return (
            <div key={`${code}-${index}`} className="rounded-ui border border-border bg-background p-3">
              <div className="flex items-center justify-between gap-2">
                <Badge className={alert.severity === "Critical" ? "border-destructive/30 bg-destructive/10 text-destructive" : "border-warning/30 bg-warning/10 text-warning"}>
                  {alert.severity === "Critical" ? <ShieldAlert aria-hidden className="mr-1 h-3 w-3" /> : <TriangleAlert aria-hidden className="mr-1 h-3 w-3" />}
                  {safeToken(alert.severity) ?? "Unknown"}
                </Badge>
                <span className="font-mono text-xs text-muted-foreground">{code}</span>
              </div>
              <p className="mt-2 text-sm text-muted-foreground">{operationalCodeGuidance[code] ?? "No fixed operator guidance is available for this code."}</p>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function AuditSection({ query }: { query: ReturnType<typeof useQuery<{ items: ManagedElsaAuditEvent[] }>> }) {
  if (query.isLoading)
    return <RequestStateView state="loading" title="Loading safe audit events" />;
  if (query.isError && !query.data)
    return <RetryState title={queryErrorTitle(query.error, "Audit history could not load", "Audit history not found")} description="Refresh this instance to request its recent safe audit events." onRetry={() => void query.refetch()} />;
  if (!query.data)
    return <RetryState title="Audit history not found" description="The selected instance has no available audit projection." onRetry={() => void query.refetch()} />;

  return (
    <section className="space-y-3 rounded-ui border border-border bg-surface p-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold">Recent audit events</h2>
          <p className="mt-1 text-sm text-muted-foreground">Customer-safe event, state, diagnostic-code, and time information.</p>
        </div>
        <SecondaryButton type="button" onClick={() => void query.refetch()} disabled={query.isFetching} title="Refresh audit history">
          <RefreshCw aria-hidden className={cn("h-4 w-4", query.isFetching && "animate-spin")} />
          Refresh audit
        </SecondaryButton>
      </div>
      {query.isRefetchError ? <InlineError title="Audit history could not be refreshed" onRetry={() => void query.refetch()} /> : null}
      {query.data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">No audit events are available for this instance.</p>
      ) : (
        <div className="divide-y divide-border rounded-ui border border-border bg-background">
          {query.data.items.slice(0, 25).map((event) => <SafeAuditEvent key={event.id} event={event} />)}
        </div>
      )}
    </section>
  );
}

function SafeAuditEvent({ event }: { event: ManagedElsaAuditEvent }) {
  const eventType = safeToken(event.eventType) ?? "Unknown event";
  const prior = safeToken(event.priorState);
  const next = safeToken(event.newState);
  const diagnosticCode = safeToken(event.diagnosticCode);
  return (
    <article className="flex flex-col gap-2 px-3 py-3 text-sm md:flex-row md:items-start md:justify-between">
      <div className="min-w-0">
        <p className="font-medium">{eventType}</p>
        <p className="mt-1 text-xs text-muted-foreground">
          {prior && next ? `${prior} → ${next}` : next ?? prior ?? "State unavailable"}
          {diagnosticCode ? <span className="ml-2 font-mono">{diagnosticCode}</span> : null}
        </p>
      </div>
      <time className="shrink-0 text-xs text-muted-foreground" dateTime={event.occurredAt}>{formatDateTime(event.occurredAt)}</time>
    </article>
  );
}

function ProjectionCard({ title, icon, children }: { title: string; icon: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="rounded-ui border border-border bg-background p-3">
      <h3 className="flex items-center gap-2 text-sm font-semibold">{icon}{title}</h3>
      <div className="mt-3">{children}</div>
    </section>
  );
}

function Metric({ label, value, detail }: { label: string; value: React.ReactNode; detail?: string }) {
  return <div className="rounded-ui border border-border bg-background p-3"><p className="text-xs text-muted-foreground">{label}</p><p className="mt-1 text-sm font-medium">{value}</p>{detail ? <p className="mt-1 text-xs text-muted-foreground">{detail}</p> : null}</div>;
}

function Detail({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className="mt-0.5 break-words">{value}</dd></div>;
}

function InstanceHealthBadge({ health }: { health: ManagedElsaInstance["health"] }) {
  const healthy = health === "Healthy";
  return <Badge className={healthy ? "border-primary/30 bg-primary/10 text-primary" : "border-warning/30 bg-warning/10 text-warning"}>{healthy ? <CheckCircle2 aria-hidden className="mr-1 h-3 w-3" /> : <TriangleAlert aria-hidden className="mr-1 h-3 w-3" />}{health}</Badge>;
}

function OperationalStatusBadge({ status }: { status: string }) {
  const Icon = status === "Healthy" ? CheckCircle2 : status === "Failed" || status === "RecoveryRequired" ? XCircle : status === "Unknown" ? CircleHelp : AlertTriangle;
  const className = status === "Healthy" ? "border-primary/30 bg-primary/10 text-primary" : status === "Failed" || status === "RecoveryRequired" ? "border-destructive/30 bg-destructive/10 text-destructive" : status === "Unknown" ? "border-border bg-muted text-muted-foreground" : "border-warning/30 bg-warning/10 text-warning";
  return <span role="status" aria-label={`Operational status: ${status}`} className={cn("inline-flex items-center rounded-ui border px-2 py-0.5 text-xs", className)}><Icon aria-hidden className="mr-1 h-3 w-3" />{status}</span>;
}

function RetryState({ title, description, onRetry }: { title: string; description: string; onRetry: () => void }) {
  return <EmptyState title={title} description={description} action={<SecondaryButton type="button" onClick={onRetry}>Retry</SecondaryButton>} />;
}

function InlineError({ title, onRetry }: { title: string; onRetry: () => void }) {
  return <div role="alert" className="flex flex-wrap items-center justify-between gap-3 rounded-ui border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive"><span className="inline-flex items-center gap-2"><AlertTriangle aria-hidden className="h-4 w-4" />{title}</span><Button type="button" onClick={onRetry}>Retry</Button></div>;
}

function queryErrorTitle(error: unknown, unexpectedTitle: string, notFoundTitle: string) {
  return error instanceof ApiError && error.kind === "NotFound" ? notFoundTitle : unexpectedTitle;
}

function safeToken(value: string | null | undefined) {
  if (!value || value.length > 128 || !/^[A-Za-z0-9._:-]+$/.test(value)) return null;
  return value;
}
