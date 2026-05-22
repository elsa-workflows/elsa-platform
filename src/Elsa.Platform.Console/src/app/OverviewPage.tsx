import { Activity, Archive, Boxes, CheckCircle2, Clock3, PackageSearch, Rocket, TriangleAlert } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { Badge } from "@/components/ui";

const platformSignals: Signal[] = [
  {
    label: "Deployment readiness",
    value: "3 artifacts",
    description: "Validated artifacts ready for dry-run once deployment APIs land.",
    icon: Archive,
    status: "Ready"
  },
  {
    label: "Package approvals",
    value: "12 pending",
    description: "Package versions waiting on catalog administrator review.",
    icon: PackageSearch,
    status: "Needs review",
    tone: "warning"
  },
  {
    label: "Runtime builder",
    value: "4 configs",
    description: "Saved runtime configurations are planned as a platform module.",
    icon: Boxes,
    status: "Roadmap"
  },
  {
    label: "Managed operations",
    value: "Not enabled",
    description: "Health, backup, restore, upgrade, and rollback views are reserved.",
    icon: Activity,
    status: "Future"
  }
];

const activityItems = [
  {
    title: "Package source sync completed",
    detail: "NuGet professional feed scanned 148 packages with 2 validation warnings.",
    time: "12 min ago",
    icon: CheckCircle2
  },
  {
    title: "Artifact provenance module reserved",
    detail: "Artifact details will track manifest, payload, checksum, plan, dry-run, apply, and history.",
    time: "Planned",
    icon: Archive
  },
  {
    title: "Environment workbench reserved",
    detail: "Deployment environments will collect desired state, last artifact, runtime health, and operations.",
    time: "Planned",
    icon: Rocket
  }
];

type Signal = {
  label: string;
  value: string;
  description: string;
  icon: LucideIcon;
  status: string;
  tone?: "warning";
};

export function OverviewPage() {
  return (
    <section className="space-y-8">
      <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
        <div className="max-w-3xl space-y-3">
          <h1 className="font-display text-3xl font-semibold tracking-normal md:text-4xl">Platform Overview</h1>
          <p className="text-sm leading-6 text-muted-foreground md:text-base">
            One platform console for package governance, runtime building, deployment artifacts, environment workbenches, and managed
            runtime operations.
          </p>
        </div>
        <div className="flex items-center gap-2 rounded-ui border border-border bg-surface px-3 py-2 text-sm text-muted-foreground">
          <Clock3 aria-hidden className="h-4 w-4 text-primary" />
          <span>SignalR activity stream reserved</span>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {platformSignals.map((signal) => (
          <article key={signal.label} className="rounded-ui border border-border bg-surface p-4">
            <div className="flex items-start justify-between gap-3">
              <div className="rounded-ui border border-border bg-background p-2 text-primary">
                <signal.icon aria-hidden className="h-4 w-4" />
              </div>
              <Badge
                className={
                  signal.tone === "warning"
                    ? "border-warning/30 bg-warning/10 text-warning"
                    : "border-primary/30 bg-primary/10 text-primary"
                }
              >
                {signal.status}
              </Badge>
            </div>
            <div className="mt-4 space-y-1">
              <p className="text-sm text-muted-foreground">{signal.label}</p>
              <p className="text-2xl font-semibold">{signal.value}</p>
              <p className="text-sm leading-5 text-muted-foreground">{signal.description}</p>
            </div>
          </article>
        ))}
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_360px]">
        <section className="rounded-ui border border-border bg-surface">
          <div className="border-b border-border px-4 py-3">
            <h2 className="font-display text-lg font-semibold">Control Plane Modules</h2>
            <p className="mt-1 text-sm text-muted-foreground">Option A shell with environment and provenance modules reserved.</p>
          </div>
          <div className="divide-y divide-border">
            <ModuleRow
              title="Package Catalog"
              description="Sources, package versions, approval, validation, manifests, and sync runs."
              status="Active"
            />
            <ModuleRow
              title="Deployment"
              description="Manifests, immutable artifacts, validation, diff, dry-run, apply, and history."
              status="Roadmap"
            />
            <ModuleRow
              title="Environment Workbench"
              description="Option B module for desired state, last artifact, runtime health, diagnostics, and operations per environment."
              status="Roadmap"
            />
            <ModuleRow
              title="Artifact Provenance"
              description="Option C module for manifest, payload, checksum inventory, package requirements, plans, and deployment runs."
              status="Roadmap"
            />
            <ModuleRow
              title="Runtime Operations"
              description="Managed runtime health, logs, backups, restores, controlled upgrades, rollback, and audit events."
              status="Future"
            />
          </div>
        </section>

        <section className="rounded-ui border border-border bg-surface">
          <div className="border-b border-border px-4 py-3">
            <h2 className="font-display text-lg font-semibold">Recent Activity</h2>
            <p className="mt-1 text-sm text-muted-foreground">Static until platform activity APIs are introduced.</p>
          </div>
          <div className="space-y-4 p-4">
            {activityItems.map((item) => (
              <div key={item.title} className="flex gap-3">
                <div className="mt-0.5 rounded-ui border border-border bg-background p-2 text-primary">
                  <item.icon aria-hidden className="h-4 w-4" />
                </div>
                <div className="min-w-0 space-y-1">
                  <p className="text-sm font-medium">{item.title}</p>
                  <p className="text-sm leading-5 text-muted-foreground">{item.detail}</p>
                  <p className="text-xs text-muted-foreground">{item.time}</p>
                </div>
              </div>
            ))}
          </div>
        </section>
      </div>
    </section>
  );
}

function ModuleRow({ title, description, status }: { title: string; description: string; status: string }) {
  const isActive = status === "Active";

  return (
    <div className="flex flex-col gap-3 px-4 py-4 md:flex-row md:items-center md:justify-between">
      <div className="min-w-0">
        <p className="font-medium">{title}</p>
        <p className="mt-1 text-sm leading-5 text-muted-foreground">{description}</p>
      </div>
      <Badge className={isActive ? "border-primary/30 bg-primary/10 text-primary" : "text-muted-foreground"}>
        {isActive ? <CheckCircle2 aria-hidden className="mr-1 h-3 w-3" /> : <TriangleAlert aria-hidden className="mr-1 h-3 w-3" />}
        {status}
      </Badge>
    </div>
  );
}
