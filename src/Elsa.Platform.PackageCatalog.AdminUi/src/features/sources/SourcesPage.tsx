import { useQuery } from "@tanstack/react-query";
import { RefreshCw, Search } from "lucide-react";
import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Badge, EmptyState, Input, SecondaryButton, Table, buttonClassName } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { SourceActions } from "@/features/sources/SourceActions";
import { listSources } from "@/features/sources/sourceApi";
import { sourceHealthText } from "@/features/sources/sourceModels";
import { useSyncingSourceIds } from "@/features/sources/sourceSyncState";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { sourceStatusTone, statusToneClass } from "@/lib/status/statusBadges";

export function SourcesPage() {
  const [filter, setFilter] = useState("");
  const { syncingSourceIds, setSourceSyncing } = useSyncingSourceIds();
  const sources = useQuery({ queryKey: queryKeys.sources, queryFn: listSources, refetchInterval: 30_000 });
  const filtered = useMemo(() => {
    const term = filter.trim().toLowerCase();
    if (!term) return sources.data ?? [];
    return (sources.data ?? []).filter((source) => {
      const isSyncing = source.isSyncing || syncingSourceIds.has(source.id);
      return `${source.name} ${source.url} ${source.status} ${isSyncing ? "syncing" : ""} ${source.lastSyncError ?? ""}`.toLowerCase().includes(term);
    });
  }, [filter, sources.data, syncingSourceIds]);

  if (sources.isLoading) return <RequestStateView state="loading" title="Loading sources" />;
  if (sources.isError && !sources.data) return <RequestStateView state="unexpected" title="Sources could not load" />;

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        <div>
          <h1 className="text-xl font-semibold">Sources</h1>
          <p className="mt-1 text-sm text-muted-foreground">Manage NuGet feeds, sync behavior, and indexing boundaries.</p>
        </div>
        <div className="flex gap-2">
          <SecondaryButton onClick={() => sources.refetch()} title="Refresh sources">
            <RefreshCw className="h-4 w-4" />
            Refresh
          </SecondaryButton>
          <Link className={buttonClassName()} to="/admin/sources/new">
            New Source
          </Link>
        </div>
      </div>
      {sources.isRefetchError ? <RequestStateView state="stale" title="Showing last loaded sources" /> : null}
      <label className="relative block max-w-md">
        <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input value={filter} onChange={(event) => setFilter(event.target.value)} className="pl-9" placeholder="Filter sources" />
      </label>
      {(sources.data ?? []).length === 0 ? (
        <EmptyState title="No package sources" description="Create a source to begin indexing packages." />
      ) : filtered.length === 0 ? (
        <EmptyState title="No matching sources" description="Clear the filter to see all active sources." />
      ) : (
        <Table>
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
              <tr>
                <th className="px-3 py-2">Name</th>
                <th className="px-3 py-2">URL</th>
                <th className="px-3 py-2">Status</th>
                <th className="px-3 py-2">Approval</th>
                <th className="px-3 py-2">Last Sync</th>
                <th className="px-3 py-2">Packages</th>
                <th className="px-3 py-2">Enabled</th>
                <th className="px-3 py-2">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {filtered.map((source) => {
                const isSyncing = source.isSyncing || syncingSourceIds.has(source.id);

                return (
                  <tr key={source.id}>
                    <td className="px-3 py-3 font-medium"><Link to={`/admin/sources/${source.id}`}>{source.name}</Link></td>
                    <td className="max-w-xs truncate px-3 py-3 text-muted-foreground">{source.url}</td>
                    <td className="px-3 py-3">
                      <div className="max-w-xs space-y-1">
                        <Badge className={statusToneClass(sourceStatusTone(isSyncing ? "syncing" : source.status))}>{sourceHealthText(source, isSyncing)}</Badge>
                        {source.lastSyncError && !isSyncing ? <p className="break-words text-xs leading-5 text-destructive">{source.lastSyncError}</p> : null}
                      </div>
                    </td>
                    <td className="px-3 py-3">{source.approvalPolicy}</td>
                    <td className="px-3 py-3">{formatDateTime(source.lastSuccessfulSyncAt ?? source.lastSyncedAt)}</td>
                    <td className="px-3 py-3">{source.packageCount}</td>
                    <td className="px-3 py-3">{source.enabled ? "Yes" : "No"}</td>
                    <td className="px-3 py-3"><SourceActions source={source} onSyncStateChange={setSourceSyncing} /></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </Table>
      )}
    </section>
  );
}
