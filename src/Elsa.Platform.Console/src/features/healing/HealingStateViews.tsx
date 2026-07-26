import type { ReactNode } from "react";
import { EmptyState, SecondaryButton } from "@/components/ui";
import { formatDateTime } from "@/lib/formatters";

export function HealingLoadingState({ title }: { title: string }) {
  return (
    <section className="rounded-ui border border-border bg-surface p-5" role="status" aria-live="polite" aria-busy="true">
      <h1 className="font-display text-xl font-semibold">{title}</h1>
      <p className="mt-2 text-sm text-muted-foreground">Loading the latest workspace-scoped Healing report.</p>
    </section>
  );
}

export function HealingEmptyState({ title, description, action }: { title: string; description: string; action?: ReactNode }) {
  return <EmptyState title={title} description={description} action={action} />;
}

export function HealingErrorState({ title, onRetry }: { title: string; onRetry?: () => void }) {
  return (
    <section className="rounded-ui border border-danger/30 bg-danger/5 p-5" role="alert">
      <h1 className="font-display text-xl font-semibold">{title}</h1>
      <p className="mt-2 text-sm text-muted-foreground">No protected details were returned. Check access or retry the workspace query.</p>
      {onRetry ? <SecondaryButton className="mt-4" type="button" onClick={onRetry}>Retry report</SecondaryButton> : null}
    </section>
  );
}

export function HealingStaleState({ updatedAt }: { updatedAt?: string }) {
  return (
    <p className="rounded-ui border border-warning/30 bg-warning/5 p-3 text-sm" role="status" aria-live="polite">
      Showing the last authoritative Healing state{updatedAt ? ` from ${formatDateTime(updatedAt)}` : ""}. Refresh is delayed.
    </p>
  );
}
