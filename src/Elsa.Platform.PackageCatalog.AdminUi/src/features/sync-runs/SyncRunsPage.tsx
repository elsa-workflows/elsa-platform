import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Play, RefreshCw, Search, Square, Trash2, X } from "lucide-react";
import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Badge, Button, DialogPanel, EmptyState, Input, SecondaryButton, Select, Table } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { cancelSyncRun, deleteSyncRun, deleteSyncRunsBefore, listSyncRuns, previewSyncRunCleanup, syncAll } from "@/features/sync-runs/syncRunApi";
import { SyncRunSourceValue } from "@/features/sync-runs/SyncRunSourceValue";
import type { SyncRunStatus, SyncRunTrigger } from "@/features/sync-runs/syncRunModels";
import {
  isActiveSyncRun,
  isTerminalSyncRun,
  type SyncRunCleanupPreview,
  packagesScanned,
  packagesUpdated,
  shortId,
  syncFailures,
  syncRunHasAttention,
  syncRunStatusLabel,
  syncRunTriggerLabel,
  toUtcCutoff
} from "@/features/sync-runs/syncRunModels";
import { formatDateTime, formatDuration } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { sourceStatusTone, statusToneClass } from "@/lib/status/statusBadges";

const statuses: Array<SyncRunStatus | "All"> = ["All", "Running", "Completed", "CompletedWithErrors", "Failed", "Canceled"];
const triggers: Array<SyncRunTrigger | "All"> = ["All", "Scheduled", "ManualAll", "ManualSource", "ManualPackage"];

export function SyncRunsPage() {
  const [filter, setFilter] = useState("");
  const [status, setStatus] = useState<SyncRunStatus | "All">("All");
  const [trigger, setTrigger] = useState<SyncRunTrigger | "All">("All");
  const [cleanupCutoff, setCleanupCutoff] = useState("");
  const [cleanupPreview, setCleanupPreview] = useState<SyncRunCleanupPreview | null>(null);
  const queryClient = useQueryClient();
  const syncRuns = useQuery({ queryKey: queryKeys.syncRuns, queryFn: listSyncRuns, refetchInterval: 15_000 });
  const startSync = useMutation({
    mutationFn: syncAll,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.syncRuns })
  });
  const cancelSync = useMutation({
    mutationFn: cancelSyncRun,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.syncRuns })
  });
  const deleteRun = useMutation({
    mutationFn: deleteSyncRun,
    onSuccess: () => {
      setCleanupPreview(null);
      queryClient.invalidateQueries({ queryKey: queryKeys.syncRuns });
    }
  });
  const previewCleanup = useMutation({
    mutationFn: previewSyncRunCleanup,
    onSuccess: setCleanupPreview
  });
  const bulkCleanup = useMutation({
    mutationFn: deleteSyncRunsBefore,
    onSuccess: () => {
      setCleanupPreview(null);
      queryClient.invalidateQueries({ queryKey: queryKeys.syncRuns });
    }
  });

  const filtered = useMemo(() => {
    const term = filter.trim().toLowerCase();
    return (syncRuns.data ?? []).filter((run) => {
      const sourceText = run.sources.map((source) => `${source.id} ${source.name ?? ""}`).join(" ");
      const matchesTerm = !term || `${run.id} ${run.status} ${run.trigger} ${run.error ?? ""} ${sourceText}`.toLowerCase().includes(term);
      const matchesStatus = status === "All" || run.status === status;
      const matchesTrigger = trigger === "All" || run.trigger === trigger;
      return matchesTerm && matchesStatus && matchesTrigger;
    });
  }, [filter, status, syncRuns.data, trigger]);

  const hasFilters = Boolean(filter.trim()) || status !== "All" || trigger !== "All";
  const hasActiveRun = (syncRuns.data ?? []).some(isActiveSyncRun);
  const activeRun = (syncRuns.data ?? []).find(isActiveSyncRun);

  function clearFilters() {
    setFilter("");
    setStatus("All");
    setTrigger("All");
  }

  function previewBulkCleanup() {
    const cutoff = toUtcCutoff(cleanupCutoff);
    if (!cutoff) return;
    previewCleanup.mutate(cutoff);
  }

  function confirmBulkCleanup() {
    if (!cleanupPreview) return;
    bulkCleanup.mutate(cleanupPreview.completedBefore);
  }

  function confirmDeleteRun(runId: string) {
    if (!window.confirm("Delete this sync run and its item diagnostics?")) return;
    deleteRun.mutate(runId);
  }

  if (syncRuns.isLoading) return <RequestStateView state="loading" title="Loading sync runs" />;
  if (syncRuns.isError && !syncRuns.data) return <RequestStateView state="unexpected" title="Sync runs could not load" />;

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        <div>
          <h1 className="text-xl font-semibold">Sync Runs</h1>
          <p className="mt-1 text-sm text-muted-foreground">Review synchronization history, outcomes, and item diagnostics.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <SecondaryButton onClick={() => syncRuns.refetch()} title="Refresh sync runs">
            <RefreshCw className="h-4 w-4" />
            Refresh
          </SecondaryButton>
          <Button onClick={() => startSync.mutate()} disabled={startSync.isPending || hasActiveRun} title="Sync all enabled sources">
            <Play className="h-4 w-4" />
            Sync All
          </Button>
          {activeRun ? (
            <SecondaryButton
              onClick={() => cancelSync.mutate(activeRun.id)}
              disabled={cancelSync.isPending}
              className="text-destructive"
              title="Cancel active sync"
            >
              <Square className="h-4 w-4" />
              Cancel Sync
            </SecondaryButton>
          ) : null}
        </div>
      </div>

      {syncRuns.isRefetchError ? <RequestStateView state="stale" title="Showing last loaded sync runs" /> : null}
      {startSync.isError ? <RequestStateView state="unexpected" title="Sync could not start" /> : null}
      {cancelSync.isError ? <RequestStateView state="unexpected" title="Sync could not be canceled" /> : null}
      {deleteRun.isError ? <RequestStateView state="unexpected" title="Sync run could not be deleted" /> : null}
      {previewCleanup.isError ? <RequestStateView state="unexpected" title="Cleanup preview could not load" /> : null}
      {bulkCleanup.isError ? <RequestStateView state="unexpected" title="Bulk cleanup could not complete" /> : null}

      <DialogPanel>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <h2 className="text-sm font-medium">Bulk cleanup</h2>
            <p className="mt-1 text-sm text-muted-foreground">Preview terminal sync runs completed before a UTC cutoff, then delete eligible history.</p>
          </div>
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <Input
              aria-label="Cleanup cutoff"
              type="datetime-local"
              value={cleanupCutoff}
              onChange={(event) => {
                setCleanupCutoff(event.target.value);
                setCleanupPreview(null);
              }}
            />
            <SecondaryButton onClick={previewBulkCleanup} disabled={!cleanupCutoff || previewCleanup.isPending}>
              Preview
            </SecondaryButton>
            <Button onClick={confirmBulkCleanup} disabled={!cleanupPreview || bulkCleanup.isPending || cleanupPreview.eligibleRunCount === 0}>
              <Trash2 className="h-4 w-4" />
              Delete Eligible
            </Button>
          </div>
        </div>
        {cleanupPreview ? (
          <div className="mt-3 grid gap-2 text-sm sm:grid-cols-3">
            <span>Eligible runs: {cleanupPreview.eligibleRunCount}</span>
            <span>Item records: {cleanupPreview.eligibleItemCount}</span>
            <span>Excluded active runs: {cleanupPreview.excludedRunCount}</span>
          </div>
        ) : null}
      </DialogPanel>

      <div className="flex flex-col gap-2 lg:flex-row lg:items-center">
        <label className="relative block w-full max-w-md">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input value={filter} onChange={(event) => setFilter(event.target.value)} className="pl-9" placeholder="Filter sync runs" />
        </label>
        <Select aria-label="Filter by status" value={status} onChange={(event) => setStatus(event.target.value as SyncRunStatus | "All")}>
          {statuses.map((option) => (
            <option key={option} value={option}>
              {option === "All" ? "All statuses" : syncRunStatusLabel(option)}
            </option>
          ))}
        </Select>
        <Select aria-label="Filter by trigger" value={trigger} onChange={(event) => setTrigger(event.target.value as SyncRunTrigger | "All")}>
          {triggers.map((option) => (
            <option key={option} value={option}>
              {option === "All" ? "All triggers" : syncRunTriggerLabel(option)}
            </option>
          ))}
        </Select>
        {hasFilters ? (
          <SecondaryButton onClick={clearFilters} title="Clear filters">
            <X className="h-4 w-4" />
            Clear
          </SecondaryButton>
        ) : null}
      </div>

      {(syncRuns.data ?? []).length === 0 ? (
        <EmptyState title="No sync runs" description="Run a source sync from Sources or start a full sync here." />
      ) : filtered.length === 0 ? (
        <EmptyState title="No matching sync runs" description="Clear the filters to see all synchronization history." />
      ) : (
        <Table>
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
              <tr>
                <th className="px-3 py-2">Started</th>
                <th className="px-3 py-2">Duration</th>
                <th className="px-3 py-2">Source</th>
                <th className="px-3 py-2">Trigger</th>
                <th className="px-3 py-2">Status</th>
                <th className="px-3 py-2">Scanned</th>
                <th className="px-3 py-2">Updated</th>
                <th className="px-3 py-2">Failures</th>
                <th className="px-3 py-2">Items</th>
                <th className="px-3 py-2">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {filtered.map((run) => (
                <tr key={run.id} className={syncRunHasAttention(run) ? "bg-destructive/5" : undefined}>
                  <td className="px-3 py-3 font-medium">
                    <Link to={`/admin/sync-runs/${run.id}`}>{formatDateTime(run.startedAt)}</Link>
                    <div className="mt-1 font-mono text-xs text-muted-foreground">{shortId(run.id)}</div>
                  </td>
                  <td className="px-3 py-3">{formatDuration(run.startedAt, run.completedAt)}</td>
                  <td className="px-3 py-3">
                    <SyncRunSourceValue run={run} />
                  </td>
                  <td className="px-3 py-3">{syncRunTriggerLabel(run.trigger)}</td>
                  <td className="px-3 py-3">
                    <Badge className={statusToneClass(sourceStatusTone(run.status))}>{syncRunStatusLabel(run.status)}</Badge>
                  </td>
                  <td className="px-3 py-3">{packagesScanned(run)}</td>
                  <td className="px-3 py-3">{packagesUpdated(run)}</td>
                  <td className="px-3 py-3">{syncFailures(run)}</td>
                  <td className="px-3 py-3">{run.itemCount}</td>
                  <td className="px-3 py-3">
                    {isActiveSyncRun(run) ? (
                      <SecondaryButton
                        onClick={() => cancelSync.mutate(run.id)}
                        disabled={cancelSync.isPending}
                        className="text-destructive"
                        title="Cancel sync run"
                      >
                        <Square className="h-4 w-4" />
                        Cancel
                      </SecondaryButton>
                    ) : isTerminalSyncRun(run) ? (
                      <SecondaryButton onClick={() => confirmDeleteRun(run.id)} disabled={deleteRun.isPending} title="Delete sync run">
                        <Trash2 className="h-4 w-4" />
                        Delete
                      </SecondaryButton>
                    ) : (
                      <span className="text-xs text-muted-foreground">Active</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Table>
      )}
    </section>
  );
}
