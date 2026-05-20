import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { Link, useParams } from "react-router-dom";
import { Badge, EmptyState, Table } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { getSyncRun } from "@/features/sync-runs/syncRunApi";
import { SyncRunSourceValue } from "@/features/sync-runs/SyncRunSourceValue";
import type { SyncRunItem } from "@/features/sync-runs/syncRunModels";
import {
  failedItems,
  packagesScanned,
  packagesUpdated,
  shortId,
  syncFailures,
  syncRunItemStatusLabel,
  syncRunStatusLabel,
  syncRunTriggerLabel,
  warningItems
} from "@/features/sync-runs/syncRunModels";
import { formatDateTime, formatDuration } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { sourceStatusTone, statusToneClass } from "@/lib/status/statusBadges";

export function SyncRunDetailsPage() {
  const { runId } = useParams();
  const syncRun = useQuery({
    queryKey: queryKeys.syncRun(runId ?? ""),
    queryFn: () => getSyncRun(runId!),
    enabled: Boolean(runId),
    refetchInterval: (query) => (query.state.data?.status === "Running" ? 5_000 : false)
  });

  if (syncRun.isLoading) return <RequestStateView state="loading" title="Loading sync run" />;
  if (syncRun.isError || !syncRun.data) return <RequestStateView state="not-found" title="Sync run not found" />;

  const run = syncRun.data;
  const failures = failedItems(run);
  const warnings = warningItems(run);
  const counterEntries = Object.entries(run.summaryCounters);

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <Link to="/admin/sync-runs" className="text-sm text-muted-foreground hover:text-foreground">
            Sync Runs
          </Link>
          <h1 className="mt-1 text-xl font-semibold">Sync Run {shortId(run.id)}</h1>
          <p className="mt-1 font-mono text-xs text-muted-foreground">{run.id}</p>
        </div>
        <Badge className={statusToneClass(sourceStatusTone(run.status))}>{syncRunStatusLabel(run.status)}</Badge>
      </div>

      <div className="grid gap-3 md:grid-cols-4">
        <Info label="Trigger" value={syncRunTriggerLabel(run.trigger)} />
        <Info label="Source" value={<SyncRunSourceValue run={run} />} />
        <Info label="Started" value={formatDateTime(run.startedAt)} />
        <Info label="Completed" value={run.completedAt ? formatDateTime(run.completedAt) : "Still running"} />
        <Info label="Duration" value={formatDuration(run.startedAt, run.completedAt)} />
        <Info label="Packages scanned" value={packagesScanned(run)} />
        <Info label="Packages updated" value={packagesUpdated(run)} />
        <Info label="Failures" value={syncFailures(run)} />
        <Info label="Items" value={run.itemCount} />
      </div>

      {run.error ? (
        <div className="rounded-ui border border-destructive/30 bg-destructive/5 p-4">
          <h2 className="text-sm font-medium text-destructive">Run error</h2>
          <p className="mt-2 text-sm">{run.error}</p>
        </div>
      ) : null}

      <div className="grid gap-3 md:grid-cols-2">
        <DiagnosticPanel title="Failures" items={failures} emptyText="No failed items." />
        <DiagnosticPanel title="Warnings" items={warnings} emptyText="No invalid or suspicious items." />
      </div>

      <div className="rounded-ui border border-border p-4">
        <h2 className="text-sm font-medium">Summary counters</h2>
        {counterEntries.length > 0 ? (
          <dl className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {counterEntries.map(([key, value]) => (
              <div key={key}>
                <dt className="text-xs uppercase text-muted-foreground">{key}</dt>
                <dd className="mt-1 text-sm font-medium">{value}</dd>
              </div>
            ))}
          </dl>
        ) : (
          <p className="mt-3 text-sm text-muted-foreground">No counters were recorded.</p>
        )}
      </div>

      {run.items.length === 0 ? (
        <EmptyState title="No sync items" description="This run has no package-level diagnostics." />
      ) : (
        <Table>
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
              <tr>
                <th className="px-3 py-2">Package</th>
                <th className="px-3 py-2">Version</th>
                <th className="px-3 py-2">Status</th>
                <th className="px-3 py-2">Duration</th>
                <th className="px-3 py-2">Message</th>
                <th className="px-3 py-2">Error</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {run.items.map((item) => (
                <tr key={item.id}>
                  <td className="px-3 py-3 font-medium">
                    {item.packageId ? <Link to={`/admin/packages/${encodeURIComponent(item.packageId)}`}>{item.packageId}</Link> : "Source"}
                    <div className="mt-1 font-mono text-xs text-muted-foreground">{item.sourceId ? shortId(item.sourceId) : "no source"}</div>
                  </td>
                  <td className="px-3 py-3">{item.version ?? "-"}</td>
                  <td className="px-3 py-3">
                    <Badge className={statusToneClass(sourceStatusTone(item.status))}>{syncRunItemStatusLabel(item.status)}</Badge>
                  </td>
                  <td className="px-3 py-3">{formatDuration(item.startedAt, item.completedAt)}</td>
                  <td className="max-w-sm px-3 py-3 text-muted-foreground">{item.message ?? "-"}</td>
                  <td className="max-w-sm px-3 py-3 text-destructive">{item.error ?? "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Table>
      )}
    </section>
  );
}

function Info({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="rounded-ui border border-border p-4">
      <div className="text-xs uppercase text-muted-foreground">{label}</div>
      <div className="mt-2 text-sm font-medium">{value}</div>
    </div>
  );
}

function DiagnosticPanel({ title, items, emptyText }: { title: string; items: SyncRunItem[]; emptyText: string }) {
  const visibleItems = items.slice(0, 5);
  const hiddenCount = items.length - visibleItems.length;

  return (
    <div className="rounded-ui border border-border p-4">
      <h2 className="text-sm font-medium">{title}</h2>
      {items.length === 0 ? (
        <p className="mt-2 text-sm text-muted-foreground">{emptyText}</p>
      ) : (
        <>
          <ul className="mt-3 space-y-2">
            {visibleItems.map((item) => (
              <li key={item.id} className="text-sm">
                <span className="font-medium">{item.packageId ?? item.sourceId ?? "Run item"}</span>
                <span className="text-muted-foreground"> {item.version ?? ""}</span>
                <p className="mt-1 text-muted-foreground">{item.error ?? item.message ?? syncRunItemStatusLabel(item.status)}</p>
              </li>
            ))}
          </ul>
          {hiddenCount > 0 ? (
            <p className="mt-3 text-sm text-muted-foreground">
              {hiddenCount} more {hiddenCount === 1 ? "item is" : "items are"} shown in the full table below.
            </p>
          ) : null}
        </>
      )}
    </div>
  );
}
