import { useState } from "react";
import { Badge, EmptyState } from "@/components/ui";
import { humanize } from "@/features/healing/HealingIncidentsPage";
import type {
  HealingDeploymentObservation,
  HealingEnvironmentImpact,
  HealingVerificationResult
} from "@/features/healing/healingModels";
import { formatDateTime } from "@/lib/formatters";
import { statusToneClass } from "@/lib/status/statusBadges";

type Props = {
  incidentStatus: string;
  impacts: HealingEnvironmentImpact[];
  observations: HealingDeploymentObservation[];
  results: HealingVerificationResult[];
  permissions: string[];
  pending?: boolean;
  onWaive?: (environmentId: string, reason: string) => void;
};

export function HealingVerificationPanel({
  incidentStatus,
  impacts,
  observations,
  results,
  permissions,
  pending = false,
  onWaive
}: Props) {
  const [waiving, setWaiving] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  if (impacts.length === 0)
    return <EmptyState title="No environment impacts" description="No environment occurrence or verification timeline is recorded." />;
  const canWaive = permissions.includes("healing.verification.waive");

  return <div className="space-y-4">
    <p className="text-sm text-muted-foreground">
      Merged, deployed, deployed—unverified, and healed are separate outcomes. No-traffic silence never proves healing.
    </p>
    {impacts.map((impact) => {
      const environmentObservations = observations.filter(x => x.environmentId === impact.environmentId);
      const verification = results
        .filter(x => x.environmentId === impact.environmentId && x.episodeId === impact.episodeId)
        .sort((left, right) => Date.parse(right.windowStartedAt ?? "") - Date.parse(left.windowStartedAt ?? ""))[0];
      const events = timeline(incidentStatus, impact, environmentObservations, verification);
      const terminal = ["Healed", "FailedVerification", "Superseded", "Waived"].includes(impact.verificationStatus);
      return <article key={`${impact.episodeId}:${impact.environmentId}`} className="space-y-4 rounded-ui border border-border bg-surface p-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-medium">Environment {impact.environmentId}</h2>
          <Badge className={statusToneClass(tone(impact.verificationStatus))}>{verificationLabel(impact.verificationStatus)}</Badge>
        </div>
        <dl className="grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
          <Detail label="Threshold impact" value={`${impact.occurrenceCount} of ${impact.occurrenceThreshold} occurrences`} />
          <Detail label="Current deployed revision" value={impact.currentDeployedRevision ?? "Not observed"} />
          <Detail label="Repaired revision" value={verification?.repairedRevision ?? "Awaiting merge"} />
          <Detail label="Positive affected operations" value={String(verification?.relevantOperationSuccessCount ?? 0)} />
          <Detail label="Matching recurrences" value={String(verification?.recurrenceCount ?? 0)} />
          <Detail label="Verification window starts" value={formatDateTime(verification?.windowStartedAt)} />
          <Detail label="Verification window ends" value={formatDateTime(verification?.windowEndsAt)} />
          <Detail label="Last positive operation" value={formatDateTime(verification?.lastRelevantOperationSuccessAt)} />
          <Detail label="Last recurrence" value={formatDateTime(verification?.lastRecurrenceAt)} />
        </dl>
        <ol aria-label={`Environment ${impact.environmentId} verification timeline`} className="space-y-2 border-l border-border pl-4">
          {events.map((event, index) => <li key={`${event.at}:${event.label}:${index}`} className="text-sm">
            <p className="font-medium">{event.label}</p>
            <time className="text-xs text-muted-foreground" dateTime={event.at}>{formatDateTime(event.at)}</time>
          </li>)}
        </ol>
        {canWaive && !terminal && onWaive ? waiving === impact.environmentId ? <div role="dialog" aria-label="Confirm environment verification waiver" className="space-y-3 rounded-ui border border-warning/40 bg-warning/5 p-3 text-sm">
          <p>This closes verification for this incident episode and environment. It does not deploy or roll back the application.</p>
          <label className="block">Waiver reason<input className="mt-1 w-full rounded-ui border border-border bg-background px-3 py-2" value={reason} onChange={event => setReason(event.target.value)} /></label>
          <div className="flex gap-2"><button type="button" disabled={pending || !reason.trim()} className="rounded-ui bg-primary px-3 py-2 text-primary-foreground disabled:opacity-50" onClick={() => onWaive(impact.environmentId, reason.trim())}>Confirm terminal waiver</button><button type="button" className="rounded-ui border border-border px-3 py-2" onClick={() => { setWaiving(null); setReason(""); }}>Cancel</button></div>
        </div> : <button type="button" className="rounded-ui border border-border px-3 py-2 text-sm" onClick={() => setWaiving(impact.environmentId)}>Waive environment verification</button> : null}
      </article>;
    })}
  </div>;
}

function timeline(
  incidentStatus: string,
  impact: HealingEnvironmentImpact,
  observations: HealingDeploymentObservation[],
  verification?: HealingVerificationResult
) {
  const events = [{ at: impact.firstSeenAt, label: "Environment affected" }];
  if (["Merged", "Verifying", "Healed", "FailedVerification", "Superseded", "Waived"].includes(incidentStatus))
    events.push({ at: verification?.windowStartedAt ?? impact.lastSeenAt, label: "Repair merged" });
  observations.forEach(item => events.push({ at: item.deployedAt, label: `Revision deployed (${humanize(item.source)})` }));
  if (verification?.lastRelevantOperationSuccessAt)
    events.push({ at: verification.lastRelevantOperationSuccessAt, label: "Affected operation succeeded" });
  if (verification?.lastRecurrenceAt)
    events.push({ at: verification.lastRecurrenceAt, label: "Matching recurrence detected" });
  if (verification?.decidedAt)
    events.push({ at: verification.decidedAt, label: verificationLabel(verification.outcome) });
  return events.sort((left, right) => Date.parse(left.at) - Date.parse(right.at));
}

function Detail({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className="mt-0.5 break-words">{value}</dd></div>;
}

function verificationLabel(value: string) {
  return value === "DeployedUnverified" ? "Deployed—unverified" : humanize(value);
}

function tone(value: string): "neutral" | "success" | "warning" | "destructive" {
  if (value === "Healed") return "success";
  if (value === "FailedVerification") return "destructive";
  if (value === "DeployedUnverified" || value === "PendingDeployment") return "warning";
  return "neutral";
}
