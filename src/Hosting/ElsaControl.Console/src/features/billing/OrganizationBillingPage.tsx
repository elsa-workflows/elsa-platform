import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowUpRight, CheckCircle2, CreditCard, ExternalLink, Gauge, RefreshCw, ShieldAlert, TriangleAlert } from "lucide-react";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { Badge, Button, EmptyState, SecondaryButton } from "@/components/ui";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { createOrganizationBillingPortal, createOrganizationCheckout, getOrganizationBillingStatus } from "@/features/billing/billingApi";
import { getBillingStateMeta, type OrganizationBillingCapacity, type OrganizationBillingEntitlements, type OrganizationBillingState, type OrganizationBillingStatus } from "@/features/billing/billingModels";
import { ApiError } from "@/lib/api/httpClient";
import { queryKeys } from "@/lib/query/queryClient";
import { cn } from "@/lib/utils";

export function OrganizationBillingPage() {
  const { selectedOrganization, isLoading: workspaceLoading, isError: workspaceError } = useWorkspaceContext();
  const organizationId = selectedOrganization?.id ?? "";
  const billing = useQuery({
    queryKey: queryKeys.organizationBilling(organizationId),
    queryFn: () => getOrganizationBillingStatus(organizationId),
    enabled: Boolean(organizationId),
    retry: false
  });
  const queryClient = useQueryClient();
  const checkout = useMutation({
    mutationFn: async () => {
      const session = await createOrganizationCheckout(organizationId);
      openBillingSession(session.url);
    }
  });
  const portal = useMutation({
    mutationFn: async () => {
      const session = await createOrganizationBillingPortal(organizationId);
      openBillingSession(session.url);
    }
  });

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.organizationBilling(organizationId) });
  };

  if (workspaceLoading || billing.isLoading)
    return <RequestStateView state="loading" title="Loading organization billing" description="Checking lifecycle, entitlement, and capacity status." />;
  if (workspaceError)
    return <RequestStateView state="unexpected" title="Organization context could not load" description="Try again when Elsa Control is available." />;
  if (!selectedOrganization)
    return <RequestStateView state="empty" title="No organization selected" description="Select an organization to view its commercial status." />;
  if (billing.isError)
    return <BillingErrorState error={billing.error} onRetry={refresh} />;

  const status = billing.data;
  if (!status)
    return <EmptyState title="Billing status is unavailable" description="Elsa Control did not return a usable billing projection. Try again shortly." action={<SecondaryButton onClick={refresh}>Refresh</SecondaryButton>} />;

  const mutationError = checkout.error ?? portal.error;
  const state = status.subscription?.state ?? null;
  const stateMeta = getBillingStateMeta(state);
  const canManageBilling = ["Owner", "Administrator", "BillingAdmin"].includes(selectedOrganization.role);
  const primaryAction = primaryBillingAction(state);
  const actionPending = checkout.isPending || portal.isPending;

  return (
    <section className="space-y-6">
      <header className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
        <div className="max-w-3xl space-y-2">
          <p className="text-xs font-medium uppercase tracking-[0.16em] text-primary">Control</p>
          <h1 className="font-display text-3xl font-semibold tracking-normal md:text-4xl">Billing &amp; entitlements</h1>
          <p className="text-sm leading-6 text-muted-foreground md:text-base">
            A provider-neutral view of {selectedOrganization.name}&apos;s commercial access, capacity, and next safe action.
          </p>
        </div>
        <SecondaryButton onClick={refresh} disabled={billing.isFetching}>
          <RefreshCw aria-hidden className={cn("h-4 w-4", billing.isFetching && "animate-spin")} />
          Refresh
        </SecondaryButton>
      </header>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.45fr)_minmax(18rem,0.75fr)]">
        <LifecycleRail status={status} />
        <NextActionPanel
          action={primaryAction}
          canManageBilling={canManageBilling}
          isPending={actionPending}
          onCheckout={() => checkout.mutate()}
          onPortal={() => portal.mutate()}
        />
      </div>

      {mutationError ? <BillingMutationNotice error={mutationError} /> : null}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
        <CapacityLedger capacity={status.capacity} />
        <EntitlementLedger entitlements={status.entitlements} capabilities={status.capabilities} />
      </div>
    </section>
  );
}

function LifecycleRail({ status }: { status: OrganizationBillingStatus }) {
  const state = status.subscription?.state ?? null;
  const meta = getBillingStateMeta(state);
  const subscription = status.subscription;
  const deadline = subscription?.state === "Trial" ? subscription.trialEndsAt : subscription?.pastDueAt;
  const statusTone = toneClasses(meta.tone);

  return (
    <article className="rounded-ui border border-border bg-surface p-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">Subscription lifecycle</p>
          <h2 className="mt-2 text-xl font-semibold">{meta.title}</h2>
        </div>
        <Badge className={cn("gap-1.5", statusTone.badge)}>
          <StateIcon tone={meta.tone} />
          {meta.label}
        </Badge>
      </div>
      <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">{meta.description}</p>

      <div className="mt-6 grid gap-3 border-t border-border pt-4 sm:grid-cols-3">
        <LifecycleFact label="Current state" value={meta.label} />
        <LifecycleFact label="Next deadline" value={deadline ? formatDate(deadline) : "No deadline reported"} />
        <LifecycleFact label="Last updated" value={subscription ? formatDate(subscription.updatedAt) : "Not initialized"} />
      </div>
      <LifecycleTrack state={state} />
    </article>
  );
}

function LifecycleTrack({ state }: { state: OrganizationBillingState | null }) {
  const states: OrganizationBillingState[] = ["Trial", "Active", "PastDue", "Constrained", "Suspended", "Retained"];
  const activeIndex = state ? states.indexOf(state) : -1;

  return (
    <div className="mt-6" aria-label="Subscription lifecycle progress" role="list">
      <div className="flex items-center" aria-hidden="true">
        {states.map((item, index) => (
          <div key={item} className="flex min-w-0 flex-1 items-center last:flex-none">
            <span className={cn("h-2.5 w-2.5 shrink-0 rounded-full border-2", index <= activeIndex ? "border-primary bg-primary" : "border-border bg-background")} />
            {index < states.length - 1 ? <span className={cn("h-px w-full", index < activeIndex ? "bg-primary/50" : "bg-border")} /> : null}
          </div>
        ))}
      </div>
      <div className="mt-2 flex justify-between gap-2 text-[11px] text-muted-foreground">
        {states.map((item) => <span key={item} role="listitem" className="min-w-0 truncate">{item === "PastDue" ? "Past due" : item}</span>)}
      </div>
    </div>
  );
}

function NextActionPanel({
  action,
  canManageBilling,
  isPending,
  onCheckout,
  onPortal
}: {
  action: "checkout" | "portal" | "none";
  canManageBilling: boolean;
  isPending: boolean;
  onCheckout: () => void;
  onPortal: () => void;
}) {
  const heading = action === "checkout" ? "Ready to enable billing?" : action === "portal" ? "Keep billing in good standing" : "No billing action is available";
  const description = action === "checkout"
    ? "Start a secure billing session to continue your organization beyond its included trial window."
    : action === "portal"
      ? "Open the secure billing workspace to update payment details or review invoices."
      : "The lifecycle is closed or not yet actionable. Review the status above before contacting support.";

  return (
    <aside className="rounded-ui border border-primary/25 bg-primary/5 p-5">
      <div className="flex items-start gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-ui border border-primary/25 bg-background text-primary">
          <CreditCard aria-hidden className="h-4 w-4" />
        </span>
        <div>
          <p className="text-xs font-medium uppercase tracking-[0.14em] text-primary">Next safe action</p>
          <h2 className="mt-2 text-lg font-semibold">{heading}</h2>
        </div>
      </div>
      <p className="mt-4 text-sm leading-6 text-muted-foreground">{description}</p>
      {action !== "none" ? (
        <Button
          className="mt-5 w-full justify-between"
          disabled={!canManageBilling || isPending}
          onClick={action === "checkout" ? onCheckout : onPortal}
          title={!canManageBilling ? "Billing administrator access is required" : undefined}
        >
          {isPending ? "Opening secure session…" : action === "checkout" ? "Continue to billing" : "Open billing workspace"}
          {isPending ? <RefreshCw aria-hidden className="h-4 w-4 animate-spin" /> : <ArrowUpRight aria-hidden className="h-4 w-4" />}
        </Button>
      ) : null}
      {!canManageBilling ? <p className="mt-3 text-xs text-muted-foreground">Billing actions are available to billing administrators, organization administrators, and owners.</p> : null}
    </aside>
  );
}

function CapacityLedger({ capacity }: { capacity: OrganizationBillingCapacity }) {
  return (
    <article className="rounded-ui border border-border bg-surface p-5">
      <div className="flex items-start gap-3">
        <Gauge aria-hidden className="mt-0.5 h-4 w-4 text-primary" />
        <div>
          <h2 className="text-base font-semibold">Capacity ledger</h2>
          <p className="mt-1 text-sm text-muted-foreground">Current usage against organization limits.</p>
        </div>
      </div>
      <div className="mt-5 space-y-5">
        <CapacityRow label="Managed runtimes" used={capacity.managedInstancesUsed} limit={capacity.managedInstancesLimit} />
        <CapacityRow label="Organization workspaces" used={capacity.workspacesUsed} limit={capacity.workspacesLimit} />
      </div>
    </article>
  );
}

function CapacityRow({ label, used, limit }: { label: string; used: number; limit: number | null }) {
  const percentage = limit && limit > 0 ? Math.min(100, Math.round((used / limit) * 100)) : null;
  const nearLimit = percentage !== null && percentage >= 80;

  return (
    <div>
      <div className="flex items-center justify-between gap-3 text-sm">
        <span className="font-medium">{label}</span>
        <span className={cn("tabular-nums", nearLimit ? "text-warning" : "text-muted-foreground")}>
          {used} used {limit === null ? "· limit not reported" : `of ${limit}`}
        </span>
      </div>
      <div
        className="mt-2 h-2 overflow-hidden rounded-full bg-muted"
        role="progressbar"
        aria-label={`${label} usage`}
        aria-valuenow={percentage ?? undefined}
        aria-valuemin={percentage === null ? undefined : 0}
        aria-valuemax={percentage === null ? undefined : 100}
      >
        {percentage !== null ? <div className={cn("h-full rounded-full", nearLimit ? "bg-warning" : "bg-primary")} style={{ width: `${percentage}%` }} /> : null}
      </div>
      {limit === null ? <p className="mt-1 text-xs text-muted-foreground">Capacity is tracked while the current limit is being provisioned.</p> : null}
    </div>
  );
}

function EntitlementLedger({ entitlements, capabilities }: { entitlements: OrganizationBillingEntitlements | null; capabilities: string[] }) {
  if (!entitlements) {
    return (
      <article className="rounded-ui border border-border bg-surface p-5">
        <h2 className="text-base font-semibold">Entitlement ledger</h2>
        <p className="mt-1 text-sm text-muted-foreground">No entitlement projection has been published for this organization yet.</p>
      </article>
    );
  }

  const rows = [
    ["Managed hosting", entitlements.managedHostingEnabled ? "Available" : "Unavailable"],
    ["Deployment targets", entitlements.deploymentTargetsEnabled ? "Available" : "Unavailable"],
    ["Custom package sources", entitlements.canCreateCustomSources ? `${entitlements.maxSources} allowed` : "Unavailable"],
    ["Private feeds", entitlements.privateFeedsEnabled ? "Available" : "Unavailable"],
    ["Package index", entitlements.maxPackagesIndexed === null ? "No limit reported" : `${entitlements.maxPackagesIndexed} packages`]
  ] as const;

  return (
    <article className="rounded-ui border border-border bg-surface p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold">Entitlement ledger</h2>
          <p className="mt-1 text-sm text-muted-foreground">Capabilities are derived from current server policy.</p>
        </div>
        <Badge>{capabilities.length} capabilities</Badge>
      </div>
      <dl className="mt-4 divide-y divide-border">
        {rows.map(([label, value]) => (
          <div key={label} className="flex items-center justify-between gap-4 py-3 text-sm">
            <dt className="text-muted-foreground">{label}</dt>
            <dd className={cn("text-right font-medium", value === "Unavailable" ? "text-muted-foreground" : "text-foreground")}>{value}</dd>
          </div>
        ))}
      </dl>
      <p className="mt-3 text-xs text-muted-foreground">Last synchronized {formatDate(entitlements.syncedAt)}.</p>
    </article>
  );
}

function LifecycleFact({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted-foreground">{label}</dt><dd className="mt-1 text-sm font-medium">{value}</dd></div>;
}

function BillingErrorState({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  const apiError = error instanceof ApiError ? error : null;
  const title = apiError?.kind === "Forbidden" ? "Billing access is restricted" : apiError?.kind === "Unavailable" ? "Billing service is unavailable" : "Billing status could not load";
  const description = apiError?.kind === "Forbidden"
    ? "Your organization membership does not permit this commercial status view."
    : "Try again when Elsa Control is available. No billing action was started.";
  return (
    <div role="alert" className="rounded-ui border border-warning/30 bg-warning/10 p-5">
      <div className="flex items-start gap-3">
        <ShieldAlert aria-hidden className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
        <div className="flex-1">
          <h2 className="font-semibold">{title}</h2>
          <p className="mt-1 text-sm text-muted-foreground">{description}</p>
          <SecondaryButton className="mt-4" onClick={onRetry}><RefreshCw aria-hidden className="h-4 w-4" />Retry</SecondaryButton>
        </div>
      </div>
    </div>
  );
}

function BillingMutationNotice({ error }: { error: unknown }) {
  const unavailable = error instanceof ApiError && error.kind === "Unavailable";
  return (
    <div role="alert" className="flex items-start gap-3 rounded-ui border border-warning/30 bg-warning/10 p-4 text-sm">
      <TriangleAlert aria-hidden className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
      <div>
        <p className="font-medium">{unavailable ? "Billing session is unavailable" : "Billing session could not be opened"}</p>
        <p className="mt-1 text-muted-foreground">{unavailable ? "The billing provider is not available right now. No session link was retained." : "No billing session was opened. Try again when the service is available."}</p>
      </div>
    </div>
  );
}

function StateIcon({ tone }: { tone: ReturnType<typeof getBillingStateMeta>["tone"] }) {
  return tone === "active" ? <CheckCircle2 aria-hidden className="h-3 w-3" /> : tone === "neutral" ? <ExternalLink aria-hidden className="h-3 w-3" /> : <TriangleAlert aria-hidden className="h-3 w-3" />;
}

function toneClasses(tone: ReturnType<typeof getBillingStateMeta>["tone"]) {
  return {
    active: { badge: "border-success/30 bg-success/10 text-success" },
    warning: { badge: "border-warning/30 bg-warning/10 text-warning" },
    danger: { badge: "border-destructive/30 bg-destructive/10 text-destructive" },
    neutral: { badge: "border-border bg-muted text-muted-foreground" }
  }[tone];
}

function primaryBillingAction(state: OrganizationBillingState | null): "checkout" | "portal" | "none" {
  if (!state || state === "Trial") return "checkout";
  if (["Active", "PastDue", "Constrained", "Suspended", "Retained"].includes(state)) return "portal";
  return "none";
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Not available";
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
}

function openBillingSession(url: string) {
  // The link exists only at the response boundary. It is never logged, put in
  // application state, or persisted in browser storage.
  window.location.assign(trustedBillingSessionUrl(url).toString());
}

export function trustedBillingSessionUrl(value: string) {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error("The billing session URL is unavailable.");
  }
  const localHttp = url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]", "::1"].includes(url.hostname);
  if ((url.protocol !== "https:" && !localHttp) || url.username || url.password)
    throw new Error("The billing session URL is unavailable.");
  return url;
}
