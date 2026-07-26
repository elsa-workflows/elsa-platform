import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { Badge, Input, SecondaryButton, Select } from "@/components/ui";
import { getHealingAudit, getHealingOverview, getHealingUsage } from "@/features/healing/healingApi";
import type {
  HealingAuditItem,
  HealingAuditPage,
  HealingNamedCount,
  HealingOverviewFilters,
  HealingUsageReport
} from "@/features/healing/healingModels";
import {
  HealingEmptyState,
  HealingErrorState,
  HealingLoadingState,
  HealingStaleState
} from "@/features/healing/HealingStateViews";
import { humanize } from "@/features/healing/HealingIncidentsPage";
import { formatDateTime } from "@/lib/formatters";

type FilterDraft = {
  applicationId: string;
  environmentId: string;
  status: string;
  severity: string;
  repairability: "" | "true" | "false";
  from: string;
  to: string;
};

export function HealingOverviewPage() {
  const workspace = useWorkspaceContext();
  const [searchParams, setSearchParams] = useSearchParams();
  const [draft, setDraft] = useState<FilterDraft>(() => filtersFromSearch(searchParams));
  const [filters, setFilters] = useState<FilterDraft>(() => filtersFromSearch(searchParams));
  const apiFilters = toOverviewFilters(filters);
  const overview = useQuery({
    queryKey: ["healing", "overview", workspace.selectedWorkspaceId, apiFilters],
    queryFn: () => getHealingOverview(workspace.selectedWorkspaceId, apiFilters),
    enabled: Boolean(workspace.selectedWorkspaceId),
    retry: false
  });
  const usage = useQuery({
    queryKey: ["healing", "usage", workspace.selectedWorkspaceId, apiFilters.applicationId, apiFilters.from, apiFilters.to],
    queryFn: () => getHealingUsage(workspace.selectedWorkspaceId, apiFilters),
    enabled: Boolean(workspace.selectedWorkspaceId),
    retry: false
  });
  const audit = useQuery({
    queryKey: ["healing", "audit-preview", workspace.selectedWorkspaceId, apiFilters.applicationId],
    queryFn: () => getHealingAudit(workspace.selectedWorkspaceId, { applicationId: apiFilters.applicationId, take: 8 }),
    enabled: Boolean(workspace.selectedWorkspaceId),
    retry: false
  });

  if (workspace.isLoading || overview.isLoading)
    return <HealingLoadingState title="Loading Healing overview" />;
  if (workspace.isError || !workspace.selectedWorkspaceId)
    return <HealingErrorState title="Workspace context could not load" />;
  if (overview.isError && !overview.data)
    return <HealingErrorState title="Healing overview could not load" onRetry={() => void overview.refetch()} />;

  const report = overview.data;
  if (!report)
    return <HealingErrorState title="Healing overview could not load" />;
  const isEmpty = report.applications.total === 0 && report.incidentStates.every((item) => item.count === 0);

  return (
    <section className="space-y-5">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="font-display text-xl font-semibold">Healing overview</h1>
          <p className="mt-1 text-sm text-muted-foreground">Workspace-isolated repair activity, safety outcomes, usage, and audit decisions.</p>
        </div>
        <nav className="flex gap-4 text-sm" aria-label="Healing reports">
          <Link className="text-primary" to="/admin/healing/incidents">Incidents</Link>
          <Link className="text-primary" to="/admin/healing/audit">Full audit</Link>
        </nav>
      </header>

      {overview.isRefetchError || usage.isRefetchError || audit.isRefetchError ? <HealingStaleState updatedAt={report.updatedAt} /> : null}
      <p className="text-xs text-muted-foreground">
        {report.permissions.includes("healing.configure") ? "Configuration access enabled" : "Read-only operational report"}
      </p>

      <OverviewFilters
        draft={draft}
        onChange={setDraft}
        onApply={() => {
          setFilters(draft);
          setSearchParams(toSearchParams(draft), { replace: true });
        }}
        onClear={() => {
          const cleared = emptyFilters();
          setDraft(cleared);
          setFilters(cleared);
          setSearchParams({}, { replace: true });
        }}
      />

      {isEmpty ? (
        <HealingEmptyState
          title="Healing is ready for configuration"
          description="Automatic discovery requires the Platform OpenTelemetry module and Healing enabled for at least one application environment."
        />
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" aria-label="Healing status summary">
            <Metric title="Enabled applications" value={report.applications.enabled} detail={`${report.applications.stopped} stopped`} />
            <Metric title="Open incidents" value={report.openIncidents} detail={formatCounts(report.severities)} />
            <Metric title="Active repairs" value={report.repairActivity.activeAttempts} detail={`${report.repairActivity.blockedAttempts} blocked`} />
            <Metric title="Open pull requests" value={report.repairActivity.openPullRequests} detail={`${report.repairActivity.blockedPullRequests} blocked`} />
          </div>

          <div className="grid gap-4 xl:grid-cols-2">
            <Distribution title="Incident states" items={report.incidentStates} />
            <Distribution title="Environment verification" items={report.verificationOutcomes} />
          </div>

          <section className="space-y-3 rounded-ui border border-border bg-surface p-4" aria-labelledby="healing-recent-incidents">
            <div className="flex items-center justify-between gap-3">
              <h2 id="healing-recent-incidents" className="font-semibold">Recent open incidents</h2>
              <span className="text-xs text-muted-foreground">{report.repairability.repairable} repairable · {report.repairability.observationOnly} observation only</span>
            </div>
            {report.recentIncidents.length === 0 ? <p className="text-sm text-muted-foreground">No open incidents match these filters.</p> : (
              <ul className="divide-y divide-border">
                {report.recentIncidents.map((incident) => (
                  <li key={incident.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
                    <div>
                      <Link className="font-medium text-primary" to={`/admin/healing/incidents/${incident.id}`}>{humanize(incident.classification)}</Link>
                      <p className="text-xs text-muted-foreground">{incident.occurrenceCount} occurrences · {formatDateTime(incident.lastSeenAt)}</p>
                    </div>
                    <div className="flex gap-2"><Badge>{humanize(incident.severity)}</Badge><Badge>{humanize(incident.status)}</Badge></div>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}

      <UsagePanel usage={usage.data ?? report.usage} loading={usage.isLoading} />
      <AuditTimeline items={audit.data?.items ?? []} loading={audit.isLoading} />
    </section>
  );
}

export function HealingAuditPage() {
  const workspace = useWorkspaceContext();
  const [searchParams, setSearchParams] = useSearchParams();
  const [applicationId, setApplicationId] = useState(searchParams.get("applicationId") ?? "");
  const [incidentId, setIncidentId] = useState(searchParams.get("incidentId") ?? "");
  const [filters, setFilters] = useState({ applicationId, incidentId });
  const audit = useInfiniteQuery({
    queryKey: ["healing", "audit", workspace.selectedWorkspaceId, filters],
    queryFn: ({ pageParam }) => getHealingAudit(workspace.selectedWorkspaceId, {
      applicationId: filters.applicationId || undefined,
      incidentId: filters.incidentId || undefined,
      cursor: pageParam,
      take: 50
    }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page: HealingAuditPage) => page.nextCursor ?? undefined,
    enabled: Boolean(workspace.selectedWorkspaceId),
    retry: false
  });
  const items = audit.data?.pages.flatMap((page) => page.items) ?? [];

  if (workspace.isLoading || audit.isLoading)
    return <HealingLoadingState title="Loading Healing audit" />;
  if (workspace.isError || !workspace.selectedWorkspaceId)
    return <HealingErrorState title="Workspace context could not load" />;
  if (audit.isError && !audit.data)
    return <HealingErrorState title="Healing audit could not load" onRetry={() => void audit.refetch()} />;

  return (
    <section className="space-y-5">
      <header>
        <Link className="text-sm text-primary" to="/admin/healing">← Healing overview</Link>
        <h1 className="mt-3 font-display text-xl font-semibold">Healing audit</h1>
        <p className="mt-1 text-sm text-muted-foreground">Safe, append-only decisions for the selected workspace. Protected evidence and source are excluded.</p>
      </header>
      {audit.isRefetchError ? <HealingStaleState /> : null}
      <form className="grid gap-3 rounded-ui border border-border bg-surface p-4 md:grid-cols-3" aria-label="Audit filters" onSubmit={(event) => {
        event.preventDefault();
        const next = { applicationId: applicationId.trim(), incidentId: incidentId.trim() };
        setFilters(next);
        const params = new URLSearchParams();
        if (next.applicationId) params.set("applicationId", next.applicationId);
        if (next.incidentId) params.set("incidentId", next.incidentId);
        setSearchParams(params, { replace: true });
      }}>
        <FilterInput label="Application ID" value={applicationId} onChange={setApplicationId} />
        <FilterInput label="Incident ID" value={incidentId} onChange={setIncidentId} />
        <div className="flex items-end"><SecondaryButton type="submit">Apply audit filters</SecondaryButton></div>
      </form>
      {items.length === 0 ? (
        <HealingEmptyState title="No audit decisions" description="No safe audit events match the selected workspace filters." />
      ) : <AuditTimeline items={items} />}
      {audit.hasNextPage ? <SecondaryButton type="button" disabled={audit.isFetchingNextPage} onClick={() => audit.fetchNextPage()}>{audit.isFetchingNextPage ? "Loading more decisions" : "Load more decisions"}</SecondaryButton> : null}
    </section>
  );
}

function OverviewFilters({ draft, onChange, onApply, onClear }: {
  draft: FilterDraft;
  onChange: (value: FilterDraft) => void;
  onApply: () => void;
  onClear: () => void;
}) {
  return (
    <form className="grid gap-3 rounded-ui border border-border bg-surface p-4 md:grid-cols-2 xl:grid-cols-4" aria-label="Healing overview filters" onSubmit={(event) => { event.preventDefault(); onApply(); }}>
      <FilterInput label="Application ID" value={draft.applicationId} onChange={(applicationId) => onChange({ ...draft, applicationId })} />
      <FilterInput label="Environment ID" value={draft.environmentId} onChange={(environmentId) => onChange({ ...draft, environmentId })} />
      <FilterSelect label="Incident status" value={draft.status} options={incidentStatuses} onChange={(status) => onChange({ ...draft, status })} />
      <FilterSelect label="Severity" value={draft.severity} options={["Informational", "Warning", "Error", "Fatal"]} onChange={(severity) => onChange({ ...draft, severity })} />
      <FilterSelect label="Repairability" value={draft.repairability} options={[{ value: "true", label: "Repairable" }, { value: "false", label: "Observation only" }]} onChange={(repairability) => onChange({ ...draft, repairability: repairability as FilterDraft["repairability"] })} />
      <FilterInput label="From" type="date" value={draft.from} onChange={(from) => onChange({ ...draft, from })} />
      <FilterInput label="To" type="date" value={draft.to} onChange={(to) => onChange({ ...draft, to })} />
      <div className="flex items-end gap-2"><SecondaryButton type="submit">Apply filters</SecondaryButton><SecondaryButton type="button" onClick={onClear}>Clear</SecondaryButton></div>
    </form>
  );
}

function Metric({ title, value, detail }: { title: string; value: number; detail: string }) {
  return <article className="rounded-ui border border-border bg-surface p-4"><p className="text-sm text-muted-foreground">{title}</p><p className="mt-2 text-2xl font-semibold">{formatNumber(value)}</p><p className="mt-1 text-xs text-muted-foreground">{detail}</p></article>;
}

function Distribution({ title, items }: { title: string; items: HealingNamedCount[] }) {
  return <section className="rounded-ui border border-border bg-surface p-4"><h2 className="font-semibold">{title}</h2>{items.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">No matching outcomes.</p> : <dl className="mt-3 grid grid-cols-2 gap-3">{items.map((item) => <div key={item.name}><dt className="text-xs text-muted-foreground">{humanize(item.name)}</dt><dd className="font-medium">{formatNumber(item.count)}</dd></div>)}</dl>}</section>;
}

function UsagePanel({ usage, loading }: { usage: HealingUsageReport; loading?: boolean }) {
  return (
    <section className="rounded-ui border border-border bg-surface p-4" aria-labelledby="healing-usage">
      <h2 id="healing-usage" className="font-semibold">Provider and inference usage</h2>
      {loading ? <p className="mt-3 text-sm text-muted-foreground" role="status">Refreshing bounded usage…</p> : null}
      <dl className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <UsageValue label="Inference units" value={`${formatNumber(usage.inputUnits + usage.outputUnits)} / ${formatNumber(usage.inferenceBudget)}`} />
        <UsageValue label="Repository runs" value={`${formatNumber(usage.repositoryRuns)} / ${formatNumber(usage.repositoryRunBudget)}`} />
        <UsageValue label="Agent duration" value={formatDuration(usage.agentDurationSeconds)} />
        <UsageValue label="Provider operations" value={`${formatNumber(usage.providerOperations)} (${formatNumber(usage.failedProviderOperations)} failed)`} />
      </dl>
    </section>
  );
}

export function AuditTimeline({ items, loading = false }: { items: HealingAuditItem[]; loading?: boolean }) {
  return (
    <section className="rounded-ui border border-border bg-surface p-4" aria-labelledby="healing-audit-timeline">
      <h2 id="healing-audit-timeline" className="font-semibold">Audit timeline</h2>
      <p className="sr-only">Chronological safe Healing decisions, newest first, with actor, reason, and timestamp.</p>
      {loading ? <p className="mt-3 text-sm text-muted-foreground" role="status">Loading safe audit decisions…</p> : null}
      {!loading && items.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">No audit decisions match this view.</p> : (
        <ol className="mt-3 divide-y divide-border" aria-label="Healing audit decisions">
          {items.map((item) => (
            <li key={item.id} className="py-3">
              <div className="flex flex-wrap items-center justify-between gap-2"><span className="font-medium">{humanize(item.eventType)}</span><time className="text-xs text-muted-foreground" dateTime={item.occurredAt}>{formatDateTime(item.occurredAt)}</time></div>
              <p className="mt-1 text-sm text-muted-foreground">{humanize(item.reasonCode)} · {humanize(item.actorType)} {item.actorId}</p>
              {Object.keys(item.details).length > 0 ? <dl className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs">{Object.entries(item.details).map(([name, value]) => <div key={name}><dt className="inline text-muted-foreground">{humanize(name)}: </dt><dd className="inline">{value ?? "—"}</dd></div>)}</dl> : null}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function UsageValue({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className="font-medium">{value}</dd></div>;
}

function FilterInput({ label, value, onChange, type = "text" }: { label: string; value: string; onChange: (value: string) => void; type?: string }) {
  const id = `healing-report-${label.toLowerCase().replaceAll(" ", "-")}`;
  return <label className="space-y-1 text-sm font-medium" htmlFor={id}>{label}<Input id={id} type={type} value={value} onChange={(event) => onChange(event.target.value)} /></label>;
}

function FilterSelect({ label, value, options, onChange }: { label: string; value: string; options: Array<string | { value: string; label: string }>; onChange: (value: string) => void }) {
  const id = `healing-report-${label.toLowerCase().replaceAll(" ", "-")}`;
  return <label className="space-y-1 text-sm font-medium" htmlFor={id}>{label}<Select id={id} className="w-full" value={value} onChange={(event) => onChange(event.target.value)}><option value="">All</option>{options.map((option) => { const item = typeof option === "string" ? { value: option, label: humanize(option) } : option; return <option key={item.value} value={item.value}>{item.label}</option>; })}</Select></label>;
}

function toOverviewFilters(filters: FilterDraft): HealingOverviewFilters {
  return {
    applicationId: filters.applicationId.trim() || undefined,
    environmentId: filters.environmentId.trim() || undefined,
    status: filters.status || undefined,
    severity: filters.severity || undefined,
    repairable: filters.repairability === "" ? undefined : filters.repairability === "true",
    from: filters.from ? `${filters.from}T00:00:00Z` : undefined,
    to: filters.to ? `${filters.to}T23:59:59Z` : undefined
  };
}

function filtersFromSearch(search: URLSearchParams): FilterDraft {
  const repairability = search.get("repairable");
  return {
    applicationId: search.get("applicationId") ?? "",
    environmentId: search.get("environmentId") ?? "",
    status: search.get("status") ?? "",
    severity: search.get("severity") ?? "",
    repairability: repairability === "true" || repairability === "false" ? repairability : "",
    from: search.get("from") ?? "",
    to: search.get("to") ?? ""
  };
}

function toSearchParams(filters: FilterDraft) {
  const params = new URLSearchParams();
  Object.entries(filters).forEach(([key, value]) => { if (value) params.set(key, value); });
  return params;
}

function emptyFilters(): FilterDraft {
  return { applicationId: "", environmentId: "", status: "", severity: "", repairability: "", from: "", to: "" };
}

function formatCounts(items: HealingNamedCount[]) {
  return items.length === 0 ? "No matching severity" : items.map((item) => `${item.count} ${humanize(item.name).toLowerCase()}`).join(" · ");
}

function formatNumber(value: number) {
  return new Intl.NumberFormat().format(value);
}

function formatDuration(seconds: number) {
  if (seconds < 60) return `${Math.round(seconds)} seconds`;
  return `${Math.round(seconds / 60)} minutes`;
}

const incidentStatuses = ["Observed", "ThresholdPending", "ReadyForRepair", "Repairing", "PullRequestOpen", "ObservationOnly", "Suppressed", "NeedsHuman", "Failed", "Merged", "Verifying", "Healed", "FailedVerification", "Superseded", "Waived"];
