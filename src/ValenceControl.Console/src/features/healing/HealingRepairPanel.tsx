import { Badge, EmptyState } from "@/components/ui";
import type { HealingRepairAttemptView } from "@/features/healing/healingModels";
import { formatDateTime } from "@/lib/formatters";
import { statusToneClass, type StatusTone } from "@/lib/status/statusBadges";

export function HealingRepairPanel({ attempts }: { attempts: HealingRepairAttemptView[] }) {
  if (attempts.length === 0)
    return <EmptyState title="No repair attempts" description="No bounded repair execution has been authorized for this incident episode." />;

  return (
    <section className="space-y-4" aria-label="Repair attempts">
      {attempts.map((attempt) => <RepairAttemptCard key={attempt.id} attempt={attempt} />)}
    </section>
  );
}

function RepairAttemptCard({ attempt }: { attempt: HealingRepairAttemptView }) {
  const requiresHumanMerge = attempt.classification !== "Reproduced" || !attempt.reproduction.wasReproduced;
  const providerUrl = safeProviderUrl(attempt.pullRequest?.url);
  return (
    <article className="space-y-4 rounded-ui border border-border bg-surface p-4" aria-label={`Repair attempt ${attempt.attemptNumber}`}>
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-medium">Repair attempt {attempt.attemptNumber}</h2>
          <p className="mt-1 text-xs text-muted-foreground">Target {attempt.targetRevision} · Producing {attempt.producingRevision || "revision unverified"}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge value={attempt.status} />
          <StatusBadge value={attempt.classification} />
          {attempt.confidence != null ? <Badge>{Math.round(attempt.confidence * 100)}% confidence</Badge> : null}
        </div>
      </header>

      {requiresHumanMerge ? (
        <div role="alert" className="rounded-ui border border-warning/40 bg-warning/5 p-3 text-sm">
          <p className="font-medium text-warning">Human merge required</p>
          <p className="mt-1 text-muted-foreground">This repair is unreproduced or revision-unverified. Automatic merge remains blocked even when repository checks pass.</p>
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="rounded-ui border border-border p-3" aria-label="Evidence bundle">
          <h3 className="text-sm font-medium">Evidence</h3>
          <dl className="mt-3 grid gap-3 text-sm sm:grid-cols-2">
            <Detail label="Tier" value={humanize(attempt.evidence.tier)} />
            <Detail label="Expires" value={attempt.evidence.expiresAt ? formatDateTime(attempt.evidence.expiresAt) : "Not reported"} />
          </dl>
          <div className="mt-3">
            <p className="text-xs text-muted-foreground">Omissions</p>
            {attempt.evidence.omittedFields.length > 0 ? (
              <ul className="mt-1 space-y-1 text-sm">
                {attempt.evidence.omittedFields.map((field) => <li key={field}>{humanize(field)} omitted</li>)}
              </ul>
            ) : <p className="mt-1 text-sm">No policy omissions reported</p>}
          </div>
        </section>

        <section className="rounded-ui border border-border p-3" aria-label="Reproduction evidence">
          <h3 className="text-sm font-medium">Reproduction</h3>
          <p className="mt-3 font-medium">{reproductionLabel(attempt)}</p>
          <p className="mt-1 text-sm text-muted-foreground">{attempt.reproduction.summary}</p>
          {attempt.causalSummary ? <p className="mt-3 text-sm"><span className="text-xs text-muted-foreground">Agent finding</span><br />{attempt.causalSummary}</p> : null}
        </section>
      </div>

      <section aria-label="Validation results">
        <h3 className="text-sm font-medium">Validation</h3>
        {attempt.validations.length === 0 ? <p className="mt-2 text-sm text-muted-foreground">No validation result was reported.</p> : (
          <ul className="mt-2 space-y-2">
            {attempt.validations.map((validation, index) => (
              <li key={`${validation.kind}:${index}`} className="flex flex-wrap items-center gap-2 rounded-ui border border-border p-2 text-sm">
                <StatusBadge value={validation.outcome} />
                <span>{validation.safeSummary}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="rounded-ui border border-border p-3" aria-label="Pull request state">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h3 className="text-sm font-medium">Pull request</h3>
            {attempt.pullRequest ? <p className="mt-1 text-xs text-muted-foreground">{attempt.pullRequest.isDraft ? "Draft" : "Ready for review"} · Checks {humanize(attempt.pullRequest.checksState)}</p> : null}
          </div>
          {attempt.pullRequest ? <StatusBadge value={attempt.pullRequest.mergeState} /> : null}
        </div>
        {!attempt.pullRequest ? <p className="mt-2 text-sm text-muted-foreground">No pull request has been published.</p> : providerUrl ? (
          <a className="mt-3 inline-block text-sm text-primary" href={providerUrl} target="_blank" rel="noreferrer">Open pull request #{attempt.pullRequest.number}</a>
        ) : <p className="mt-2 text-sm text-muted-foreground">Provider link unavailable</p>}
      </section>
    </article>
  );
}

function reproductionLabel(attempt: HealingRepairAttemptView) {
  if (!attempt.reproduction.wasAttempted) return "Not attempted";
  return attempt.reproduction.wasReproduced ? "Reproduced" : "Attempted—not reproduced";
}

function Detail({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className="mt-0.5 break-words">{value}</dd></div>;
}

function StatusBadge({ value }: { value: string }) {
  return <Badge className={statusToneClass(statusTone(value))}>{humanize(value)}</Badge>;
}

function statusTone(value: string): StatusTone {
  switch (value.toLowerCase()) {
    case "reproduced": case "passed": case "succeeded": case "merged": return "success";
    case "inferredhighconfidence": case "revisionunverified": case "running": case "pending": case "open": return "warning";
    case "failed": case "stopped": case "expired": case "insufficientconfidence": return "destructive";
    default: return "neutral";
  }
}

function humanize(value: string) {
  return value
    .replaceAll(/[._-]+/g, " ")
    .replaceAll(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/^./, (character) => character.toUpperCase());
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
