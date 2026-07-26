import { useInfiniteQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { Badge, EmptyState, Input, SecondaryButton, Select, Table } from "@/components/ui";
import { listHealingIncidents } from "@/features/healing/healingApi";
import type { HealingIncidentFilters, HealingIncidentSummary } from "@/features/healing/healingModels";
import { formatDateTime } from "@/lib/formatters";
import { statusToneClass, type StatusTone } from "@/lib/status/statusBadges";

type FilterDraft = {
  applicationId: string;
  environmentId: string;
  status: string;
  severity: string;
  repairability: "" | "true" | "false";
};

const emptyFilters: FilterDraft = { applicationId: "", environmentId: "", status: "", severity: "", repairability: "" };

export function HealingIncidentsPage() {
  const workspace = useWorkspaceContext();
  const [searchParams, setSearchParams] = useSearchParams();
  const [draft, setDraft] = useState<FilterDraft>(() => filtersFromSearch(searchParams));
  const [filters, setFilters] = useState<FilterDraft>(() => filtersFromSearch(searchParams));
  const incidents = useInfiniteQuery({
    queryKey: ["healing", "incidents", workspace.selectedWorkspaceId, filters],
    queryFn: ({ pageParam }) => listHealingIncidents(workspace.selectedWorkspaceId, toApiFilters(filters, pageParam)),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) => page.nextCursor ?? undefined,
    enabled: Boolean(workspace.selectedWorkspaceId),
    retry: false
  });
  const items = incidents.data?.pages.flatMap((page) => page.items) ?? [];

  if (workspace.isLoading || incidents.isLoading)
    return <RequestStateView state="loading" title="Loading healing incidents" />;
  if (workspace.isError || !workspace.selectedWorkspaceId)
    return <RequestStateView state="unexpected" title="Workspace context could not load" />;
  if (incidents.isError && !incidents.data)
    return <RequestStateView state="unexpected" title="Healing incidents could not load" />;

  return (
    <section className="space-y-5">
      <header>
        <h1 className="font-display text-xl font-semibold">Healing incidents</h1>
        <p className="mt-1 text-sm text-muted-foreground">Review normalized exception groups, impact, and repair readiness for the selected workspace.</p>
      </header>

      {incidents.isRefetchError ? <RequestStateView state="stale" title="Showing the last loaded healing incidents" /> : null}

      <form
        className="grid gap-3 rounded-ui border border-border bg-surface p-4 md:grid-cols-2 xl:grid-cols-6"
        aria-label="Incident filters"
        onSubmit={(event) => {
          event.preventDefault();
          setFilters(draft);
          setSearchParams(toSearchParams(draft), { replace: true });
        }}
      >
        <FilterInput label="Application ID" value={draft.applicationId} onChange={(applicationId) => setDraft({ ...draft, applicationId })} />
        <FilterInput label="Environment ID" value={draft.environmentId} onChange={(environmentId) => setDraft({ ...draft, environmentId })} />
        <FilterSelect label="Incident status" value={draft.status} onChange={(status) => setDraft({ ...draft, status })} options={incidentStatuses} />
        <FilterSelect label="Severity" value={draft.severity} onChange={(severity) => setDraft({ ...draft, severity })} options={["Informational", "Warning", "Error", "Fatal"]} />
        <FilterSelect label="Repairability" value={draft.repairability} onChange={(repairability) => setDraft({ ...draft, repairability: repairability as FilterDraft["repairability"] })} options={[{ value: "true", label: "Repairable" }, { value: "false", label: "Observation only" }]} />
        <div className="flex items-end gap-2">
          <SecondaryButton type="submit">Apply filters</SecondaryButton>
          <SecondaryButton type="button" onClick={() => { setDraft(emptyFilters); setFilters(emptyFilters); setSearchParams({}, { replace: true }); }}>Clear</SecondaryButton>
        </div>
      </form>

      {items.length === 0 ? (
        <EmptyState
          title="No healing incidents"
          description="Automatic discovery requires the Platform OpenTelemetry module and discovery enabled for an application environment."
        />
      ) : (
        <>
          <div className="hidden md:block">
            <Table>
              <table className="min-w-full divide-y divide-border text-sm">
                <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2">Problem</th>
                    <th className="px-3 py-2">Status</th>
                    <th className="px-3 py-2">Severity</th>
                    <th className="px-3 py-2">Impact</th>
                    <th className="px-3 py-2">Repair</th>
                    <th className="px-3 py-2">Last seen</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {items.map((incident) => <IncidentRow key={incident.id} incident={incident} />)}
                </tbody>
              </table>
            </Table>
          </div>
          <div className="space-y-3 md:hidden" aria-label="Healing incident cards">
            {items.map((incident) => <IncidentCard key={incident.id} incident={incident} />)}
          </div>
          {incidents.hasNextPage ? (
            <div className="flex justify-center">
              <SecondaryButton type="button" disabled={incidents.isFetchingNextPage} onClick={() => incidents.fetchNextPage()}>
                {incidents.isFetchingNextPage ? "Loading more incidents" : "Load more incidents"}
              </SecondaryButton>
            </div>
          ) : null}
        </>
      )}
    </section>
  );
}

function IncidentRow({ incident }: { incident: HealingIncidentSummary }) {
  return (
    <tr>
      <td className="px-3 py-3">
        <Link className="font-medium text-primary" to={`/admin/healing/incidents/${incident.id}`}>{humanize(incident.classification)}</Link>
        <p className="mt-1 text-xs text-muted-foreground">Application {shortId(incident.applicationId)}</p>
      </td>
      <td className="px-3 py-3"><StatusBadge value={incident.status} /></td>
      <td className="px-3 py-3"><StatusBadge value={incident.severity} /></td>
      <td className="px-3 py-3">{occurrenceLabel(incident.occurrenceCount)}<p className="text-xs text-muted-foreground">{incident.environmentImpacts.length} affected {incident.environmentImpacts.length === 1 ? "environment" : "environments"}</p></td>
      <td className="px-3 py-3">{incident.repairable ? "Repairable" : "Observation only"}{incident.needsHumanReason ? <p className="text-xs text-destructive">{humanize(incident.needsHumanReason)}</p> : null}</td>
      <td className="px-3 py-3">{formatDateTime(incident.lastSeenAt)}</td>
    </tr>
  );
}

function IncidentCard({ incident }: { incident: HealingIncidentSummary }) {
  return (
    <article className="space-y-3 rounded-ui border border-border bg-surface p-4">
      <div>
        <Link className="font-medium text-primary" to={`/admin/healing/incidents/${incident.id}`}>{humanize(incident.classification)}</Link>
        <p className="mt-1 text-xs text-muted-foreground">Application {shortId(incident.applicationId)}</p>
      </div>
      <div className="flex flex-wrap gap-2"><StatusBadge value={incident.status} /><StatusBadge value={incident.severity} /><Badge>{incident.repairable ? "Repairable" : "Observation only"}</Badge></div>
      <dl className="grid grid-cols-2 gap-3 text-sm"><Detail label="Impact" value={occurrenceLabel(incident.occurrenceCount)} /><Detail label="Last seen" value={formatDateTime(incident.lastSeenAt)} /></dl>
      {incident.needsHumanReason ? <p className="text-sm text-destructive">Blocked: {humanize(incident.needsHumanReason)}</p> : null}
    </article>
  );
}

function FilterInput({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  const id = `incident-${label.toLowerCase().replaceAll(" ", "-")}`;
  return <label className="space-y-1 text-sm font-medium" htmlFor={id}>{label}<Input id={id} value={value} onChange={(event) => onChange(event.target.value)} /></label>;
}

function FilterSelect({ label, value, options, onChange }: { label: string; value: string; options: Array<string | { value: string; label: string }>; onChange: (value: string) => void }) {
  const id = `incident-${label.toLowerCase().replaceAll(" ", "-")}`;
  return <label className="space-y-1 text-sm font-medium" htmlFor={id}>{label}<Select id={id} className="w-full" value={value} onChange={(event) => onChange(event.target.value)}><option value="">All</option>{options.map((option) => { const item = typeof option === "string" ? { value: option, label: humanize(option) } : option; return <option key={item.value} value={item.value}>{item.label}</option>; })}</Select></label>;
}

function Detail({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd>{value}</dd></div>;
}

function StatusBadge({ value }: { value: string }) {
  return <Badge className={statusToneClass(statusTone(value))}>{humanize(value)}</Badge>;
}

function statusTone(value: string): StatusTone {
  switch (value.toLowerCase()) {
    case "healed": case "merged": return "success";
    case "failed": case "failedverification": case "error": case "fatal": return "destructive";
    case "thresholdpending": case "needshuman": case "verifying": case "warning": return "warning";
    default: return "neutral";
  }
}

function toApiFilters(filters: FilterDraft, cursor?: string): HealingIncidentFilters {
  return {
    applicationId: filters.applicationId.trim() || undefined,
    environmentId: filters.environmentId.trim() || undefined,
    status: filters.status || undefined,
    severity: filters.severity || undefined,
    repairable: filters.repairability === "" ? undefined : filters.repairability === "true",
    cursor,
    take: 50
  };
}

export function humanize(value: string) {
  return value.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replaceAll("_", " ").replaceAll("-", " ").replace(/^./, (letter) => letter.toUpperCase());
}

function shortId(value: string) {
  return value.length > 12 ? `${value.slice(0, 8)}…` : value;
}

function occurrenceLabel(count: number) {
  return `${count} ${count === 1 ? "occurrence" : "occurrences"}`;
}

const incidentStatuses = ["Observed", "ThresholdPending", "ReadyForRepair", "Repairing", "PullRequestOpen", "ObservationOnly", "Suppressed", "NeedsHuman", "Failed", "Merged", "Verifying", "Healed", "FailedVerification", "Superseded", "Waived"];

function filtersFromSearch(search: URLSearchParams): FilterDraft {
  const repairability = search.get("repairable");
  return {
    applicationId: search.get("applicationId") ?? "",
    environmentId: search.get("environmentId") ?? "",
    status: search.get("status") ?? "",
    severity: search.get("severity") ?? "",
    repairability: repairability === "true" || repairability === "false" ? repairability : ""
  };
}

function toSearchParams(filters: FilterDraft) {
  const params = new URLSearchParams();
  if (filters.applicationId.trim()) params.set("applicationId", filters.applicationId.trim());
  if (filters.environmentId.trim()) params.set("environmentId", filters.environmentId.trim());
  if (filters.status) params.set("status", filters.status);
  if (filters.severity) params.set("severity", filters.severity);
  if (filters.repairability) params.set("repairable", filters.repairability);
  return params;
}
