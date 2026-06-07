import type { WorkspaceWeaverPlan } from "@/features/weaver/weaverModels";

export function WeaverPlanCard({
  plan,
  busy = false,
  onApprove,
  onReject,
  onExecute
}: {
  plan: WorkspaceWeaverPlan;
  busy?: boolean;
  onApprove?: () => void;
  onReject?: () => void;
  onExecute?: () => void;
}) {
  const canReview = plan.status === "ReadyForApproval";
  const canExecute = plan.status === "Approved";

  return (
    <div className="rounded-ui border border-primary/30 bg-primary/5 px-3 py-3 text-sm">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-medium">{plan.title}</p>
          <p className="mt-1 text-xs text-muted-foreground">{plan.planType} · {plan.status} · {plan.risk} risk</p>
        </div>
        <span className="shrink-0 rounded-sm border border-border bg-background px-1.5 py-0.5 text-xs text-muted-foreground">
          v{plan.version}
        </span>
      </div>
      <p className="mt-2 break-words text-muted-foreground">{plan.summary}</p>
      <dl className="mt-3 grid gap-2 text-xs text-muted-foreground">
        <PlanJson label="Target" value={plan.targetJson} />
        <PlanJson label="Impact" value={plan.impactJson} />
        <PlanJson label="Validation" value={plan.validationJson} />
        {plan.rollbackJson ? <PlanJson label="Rollback" value={plan.rollbackJson} /> : null}
      </dl>
      {canReview || canExecute ? (
        <div className="mt-3 flex flex-wrap justify-end gap-2">
          {canReview ? (
            <>
              <button
                type="button"
                className="inline-flex h-8 items-center rounded-ui border border-border bg-background px-3 text-xs font-medium text-muted-foreground hover:bg-muted hover:text-foreground disabled:cursor-not-allowed disabled:opacity-60"
                disabled={busy}
                onClick={onReject}
              >
                Reject
              </button>
              <button
                type="button"
                className="inline-flex h-8 items-center rounded-ui bg-primary px-3 text-xs font-medium text-primary-foreground disabled:cursor-not-allowed disabled:opacity-60"
                disabled={busy}
                onClick={onApprove}
              >
                Approve
              </button>
            </>
          ) : null}
          {canExecute ? (
            <button
              type="button"
              className="inline-flex h-8 items-center rounded-ui bg-primary px-3 text-xs font-medium text-primary-foreground disabled:cursor-not-allowed disabled:opacity-60"
              disabled={busy}
              onClick={onExecute}
            >
              Execute
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function PlanJson({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="font-semibold text-foreground">{label}</dt>
      <dd className="mt-1 break-words">{summarizeJson(value)}</dd>
    </div>
  );
}

function summarizeJson(value: string) {
  try {
    const parsed = JSON.parse(value);
    if (parsed && typeof parsed === "object") {
      return Object.entries(parsed)
        .map(([key, item]) => `${key}: ${Array.isArray(item) ? item.join(", ") : String(item)}`)
        .join(" · ");
    }
    return String(parsed);
  } catch {
    return value;
  }
}
