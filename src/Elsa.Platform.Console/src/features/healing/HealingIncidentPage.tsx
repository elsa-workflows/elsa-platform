import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { Badge, EmptyState, Table } from "@/components/ui";
import { getHealingIncident } from "@/features/healing/healingApi";
import { humanize } from "@/features/healing/HealingIncidentsPage";
import type { HealingIncidentDetail } from "@/features/healing/healingModels";
import { formatDateTime } from "@/lib/formatters";
import { statusToneClass, type StatusTone } from "@/lib/status/statusBadges";

type IncidentTab = "Overview" | "Occurrences" | "Attribution" | "Repair" | "Environments" | "Audit";
const tabs: IncidentTab[] = ["Overview", "Occurrences", "Attribution", "Repair", "Environments", "Audit"];

export function HealingIncidentPage() {
  const { incidentId = "" } = useParams();
  const workspace = useWorkspaceContext();
  const [tab, setTab] = useState<IncidentTab>("Overview");
  const incident = useQuery({
    queryKey: ["healing", "incident", workspace.selectedWorkspaceId, incidentId],
    queryFn: () => getHealingIncident(workspace.selectedWorkspaceId, incidentId),
    enabled: Boolean(workspace.selectedWorkspaceId && incidentId),
    retry: false
  });

  if (workspace.isLoading || incident.isLoading)
    return <RequestStateView state="loading" title="Loading healing incident" />;
  if (!incidentId || workspace.isError || incident.isError || !incident.data)
    return <RequestStateView state="not-found" title="Healing incident not found" description="The incident is unavailable in the selected workspace or you do not have access to it." />;

  const value = incident.data;
  const problem = value.occurrences[0]?.exceptionType || humanize(value.classification);
  const mergeSafetyNotice = value.needsHumanReason === "RevisionUnverified" || value.status === "PullRequestOpen";

  return (
    <section className="space-y-5">
      <header className="space-y-3">
        <Link className="text-sm text-primary" to="/admin/healing/incidents">← Healing incidents</Link>
        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div>
            <h1 className="font-display text-xl font-semibold">{problem}</h1>
            <p className="mt-1 text-sm text-muted-foreground">{value.occurrences[0]?.operationName || humanize(value.classification)}</p>
          </div>
          <div className="flex flex-wrap gap-2"><StatusBadge value={value.status} /><StatusBadge value={value.severity} /><Badge>{value.repairable ? "Repairable" : "Observation only"}</Badge></div>
        </div>
        <dl className="grid gap-3 rounded-ui border border-border bg-surface p-4 text-sm sm:grid-cols-2 lg:grid-cols-4">
          <Detail label="First seen" value={formatDateTime(value.firstSeenAt)} />
          <Detail label="Last seen" value={formatDateTime(value.lastSeenAt)} />
          <Detail label="Occurrences" value={String(value.occurrenceCount)} />
          <Detail label="Affected environments" value={String(uniqueEnvironmentCount(value))} />
        </dl>
      </header>

      {mergeSafetyNotice ? (
        <div role="alert" className="rounded-ui border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          <p className="font-medium">Human merge only until the producing revision and reproduction status are verified.</p>
          <p className="mt-1">The current incident response does not prove both gates. Automatic merge must remain blocked.</p>
        </div>
      ) : null}

      <div className="overflow-x-auto border-b border-border" role="tablist" aria-label="Incident detail sections">
        <div className="flex min-w-max gap-1">
          {tabs.map((item) => <button key={item} id={`incident-tab-${item}`} type="button" role="tab" aria-selected={tab === item} aria-controls={`incident-panel-${item}`} className={`px-3 py-2 text-sm font-medium ${tab === item ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"}`} onClick={() => setTab(item)}>{item}</button>)}
        </div>
      </div>

      <div id={`incident-panel-${tab}`} role="tabpanel" aria-labelledby={`incident-tab-${tab}`} aria-label={tab}>
        {tab === "Overview" ? <OverviewTab incident={value} /> : null}
        {tab === "Occurrences" ? <OccurrencesTab incident={value} /> : null}
        {tab === "Attribution" ? <AttributionTab incident={value} /> : null}
        {tab === "Repair" ? <RepairTab incident={value} /> : null}
        {tab === "Environments" ? <EnvironmentsTab incident={value} /> : null}
        {tab === "Audit" ? <AuditTab incident={value} /> : null}
      </div>
    </section>
  );
}

function OverviewTab({ incident }: { incident: HealingIncidentDetail }) {
  return (
    <div className="space-y-5">
      <section className="grid gap-4 lg:grid-cols-2">
        <Panel title="Normalized problem">
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <Detail label="Classification" value={humanize(incident.classification)} />
            <Detail label="Severity" value={humanize(incident.severity)} />
            <Detail label="Repair eligibility" value={incident.repairable ? "Repairable" : "Observation only"} />
            <Detail label="Current blocker" value={incident.needsHumanReason ? humanize(incident.needsHumanReason) : "None reported"} />
          </dl>
        </Panel>
        <Panel title="Threshold and work item">
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <Detail label="Ready after" value={incident.readyAfter ? formatDateTime(incident.readyAfter) : "Not scheduled"} />
            <Detail label="Work item" value={incident.workItem?.number ? `#${incident.workItem.number}` : incident.workItem ? "Recorded" : "Not created"} />
            <Detail label="Projection" value={incident.workItem ? humanize(incident.workItem.projectionStatus) : "Not applicable"} />
            <Detail label="Environments" value={String(uniqueEnvironmentCount(incident))} />
          </dl>
        </Panel>
      </section>
      <Panel title="Episodes">
        {incident.episodes.length === 0 ? <p className="text-sm text-muted-foreground">No incident episodes are recorded.</p> : (
          <Table>
            <table className="min-w-full divide-y divide-border text-sm">
              <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground"><tr><th className="px-3 py-2">Opened</th><th className="px-3 py-2">Outcome</th><th className="px-3 py-2">Producing revisions</th><th className="px-3 py-2">Target revision</th></tr></thead>
              <tbody className="divide-y divide-border">{incident.episodes.map((episode) => <tr key={episode.id}><td className="px-3 py-3">{formatDateTime(episode.openedAt)}</td><td className="px-3 py-3"><StatusBadge value={episode.outcome} /></td><td className="px-3 py-3">{joinOrFallback(episode.producingRevisions)}</td><td className="px-3 py-3">{episode.targetRevision ?? "Not selected"}</td></tr>)}</tbody>
            </table>
          </Table>
        )}
      </Panel>
    </div>
  );
}

function OccurrencesTab({ incident }: { incident: HealingIncidentDetail }) {
  if (incident.occurrences.length === 0)
    return <EmptyState title="No bounded occurrences" description="No safe occurrence metadata is available for this incident." />;
  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">Only bounded, redacted occurrence metadata is shown. Raw stacks and protected payloads are omitted.</p>
      <Table><table className="min-w-full divide-y divide-border text-sm"><thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground"><tr><th className="px-3 py-2">Occurred</th><th className="px-3 py-2">Exception</th><th className="px-3 py-2">Operation</th><th className="px-3 py-2">Environment</th><th className="px-3 py-2">Revision</th><th className="px-3 py-2">Evidence</th></tr></thead><tbody className="divide-y divide-border">{incident.occurrences.map((occurrence) => <tr key={occurrence.id}><td className="px-3 py-3">{formatDateTime(occurrence.occurredAt)}</td><td className="px-3 py-3">{occurrence.exceptionType}<p className="text-xs text-muted-foreground">{humanize(occurrence.classification)} · {humanize(occurrence.retryState)}</p></td><td className="px-3 py-3">{occurrence.operationName || "Not reported"}</td><td className="px-3 py-3">{occurrence.environmentId}</td><td className="px-3 py-3">{occurrence.revisionId ?? "Unverified"}</td><td className="px-3 py-3">{humanize(occurrence.evidenceTier)}</td></tr>)}</tbody></table></Table>
    </div>
  );
}

function AttributionTab({ incident }: { incident: HealingIncidentDetail }) {
  if (incident.attributions.length === 0)
    return <EmptyState title="No component attribution" description="Repair remains observation-only until a trusted component and ownership binding are selected." />;
  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">Attribution is based on trusted, redacted signal metadata and component manifests.</p>
      <Table><table className="min-w-full divide-y divide-border text-sm"><thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground"><tr><th className="px-3 py-2">Resolution</th><th className="px-3 py-2">Confidence</th><th className="px-3 py-2">Basis</th><th className="px-3 py-2">Binding</th><th className="px-3 py-2">Decision reasons</th></tr></thead><tbody className="divide-y divide-border">{incident.attributions.map((attribution) => <tr key={attribution.id}><td className="px-3 py-3"><StatusBadge value={attribution.resolution} /></td><td className="px-3 py-3">{Math.round(attribution.confidence * 100)}%</td><td className="px-3 py-3">{typeof attribution.basis === "number" ? `Recorded basis ${attribution.basis}` : humanize(attribution.basis)}</td><td className="px-3 py-3">{attribution.bindingId ? "Authorized binding selected" : "No authorized binding"}</td><td className="px-3 py-3">{attribution.reasonCodes.map(humanize).join(", ") || "None reported"}</td></tr>)}</tbody></table></Table>
    </div>
  );
}

function RepairTab({ incident }: { incident: HealingIncidentDetail }) {
  const item = incident.workItem;
  if (!item)
    return <EmptyState title="No repair work item" description={incident.repairable ? "The incident is eligible, but repair work has not been projected yet." : "Repair dispatch is blocked by the current attribution or policy state."} />;
  const providerUrl = safeProviderUrl(item.url);
  return (
    <Panel title={item.number ? `Provider work item #${item.number}` : "Provider work item"}>
      <dl className="grid gap-4 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <Detail label="Projection" value={humanize(item.projectionStatus)} />
        <Detail label="Provider state" value={item.providerState ? humanize(item.providerState) : "Not reported"} />
        <Detail label="Last projected" value={formatDateTime(item.lastProjectedAt)} />
        <Detail label="Last observed" value={formatDateTime(item.lastObservedAt)} />
      </dl>
      {providerUrl ? <a className="mt-4 inline-block text-sm text-primary" href={providerUrl} target="_blank" rel="noreferrer">Open provider work item</a> : null}
      <p className="mt-4 text-sm text-muted-foreground">Reproduction, agent result, checks, and merge eligibility are not present in this response. They must not be inferred from provider state.</p>
    </Panel>
  );
}

function EnvironmentsTab({ incident }: { incident: HealingIncidentDetail }) {
  if (incident.environmentImpacts.length === 0)
    return <EmptyState title="No environment impacts" description="No environment occurrence or verification timeline is recorded." />;
  return (
    <div className="space-y-3">
      <p className="sr-only">Environment verification states distinguish deployed, deployed unverified, healed, failed verification, superseded, and waived outcomes.</p>
      {incident.environmentImpacts.map((impact) => (
        <article key={`${impact.episodeId}:${impact.environmentId}`} className="space-y-3 rounded-ui border border-border bg-surface p-4">
          <div className="flex flex-wrap items-center justify-between gap-2"><h2 className="font-medium">Environment {impact.environmentId}</h2><StatusBadge value={verificationLabel(impact.verificationStatus)} /></div>
          <dl className="grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
            <Detail label="Threshold impact" value={`${impact.occurrenceCount} of ${impact.occurrenceThreshold} occurrences`} />
            <Detail label="Debounce" value={impact.debounceWindow} />
            <Detail label="Producing revisions" value={joinOrFallback(impact.producingRevisions)} />
            <Detail label="Current deployed revision" value={impact.currentDeployedRevision ?? "Not observed"} />
            <Detail label="First seen" value={formatDateTime(impact.firstSeenAt)} />
            <Detail label="Last seen" value={formatDateTime(impact.lastSeenAt)} />
            <Detail label="Threshold reached" value={impact.thresholdReachedAt ? formatDateTime(impact.thresholdReachedAt) : "Not reached"} />
            <Detail label="Ready after" value={impact.readyAfter ? formatDateTime(impact.readyAfter) : "Not scheduled"} />
          </dl>
        </article>
      ))}
    </div>
  );
}

function AuditTab({ incident }: { incident: HealingIncidentDetail }) {
  const milestones = [
    { at: incident.firstSeenAt, label: "Incident first observed" },
    ...incident.episodes.map((episode) => ({ at: episode.openedAt, label: episode.previousEpisodeId ? "Regression episode opened" : "Episode opened" })),
    { at: incident.lastSeenAt, label: "Latest occurrence observed" }
  ].sort((left, right) => Date.parse(left.at) - Date.parse(right.at));
  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">This safe milestone summary is not the complete workspace audit log.</p>
      <ol className="space-y-3" aria-label="Incident milestone timeline">{milestones.map((milestone, index) => <li key={`${milestone.at}:${index}`} className="rounded-ui border border-border p-3 text-sm"><p className="font-medium">{milestone.label}</p><time className="text-muted-foreground" dateTime={milestone.at}>{formatDateTime(milestone.at)}</time></li>)}</ol>
    </div>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return <section className="rounded-ui border border-border bg-surface p-4"><h2 className="mb-4 font-medium">{title}</h2>{children}</section>;
}

function Detail({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className="mt-0.5 break-words">{value}</dd></div>;
}

function StatusBadge({ value }: { value: string }) {
  return <Badge className={statusToneClass(statusTone(value))}>{humanize(value)}</Badge>;
}

function statusTone(value: string): StatusTone {
  switch (value.replaceAll("—", "").toLowerCase()) {
    case "healed": case "merged": case "selected": case "active": case "current": return "success";
    case "failed": case "failedverification": case "error": case "fatal": case "blocked": return "destructive";
    case "needshuman": case "deployedunverified": case "warning": case "pendingdeployment": return "warning";
    default: return "neutral";
  }
}

function verificationLabel(value: string) {
  return value === "DeployedUnverified" ? "Deployed—unverified" : humanize(value);
}

function uniqueEnvironmentCount(incident: HealingIncidentDetail) {
  return new Set(incident.environmentImpacts.map((impact) => impact.environmentId)).size;
}

function joinOrFallback(values: string[]) {
  return values.length > 0 ? values.join(", ") : "Not verified";
}

function safeProviderUrl(value?: string | null) {
  if (!value) return null;
  try {
    const url = new URL(value);
    return url.protocol === "https:" ? url.toString() : null;
  } catch {
    return null;
  }
}
