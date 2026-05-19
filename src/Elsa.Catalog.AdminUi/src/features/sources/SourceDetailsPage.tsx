import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { Badge, buttonClassName } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { SourceActions } from "@/features/sources/SourceActions";
import { getSource } from "@/features/sources/sourceApi";
import { sourceHealthText, versionDiscoveryPolicyText } from "@/features/sources/sourceModels";
import { useSyncingSourceIds } from "@/features/sources/sourceSyncState";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { sourceStatusTone, statusToneClass } from "@/lib/status/statusBadges";

export function SourceDetailsPage() {
  const { sourceId } = useParams();
  const navigate = useNavigate();
  const { syncingSourceIds, setSourceSyncing } = useSyncingSourceIds();
  const source = useQuery({ queryKey: [...queryKeys.sources, sourceId], queryFn: () => getSource(sourceId!), enabled: Boolean(sourceId), refetchInterval: 30_000 });

  if (source.isLoading) return <RequestStateView state="loading" title="Loading source" />;
  if (source.isError || !source.data) return <RequestStateView state="not-found" title="Source not found" />;

  const isSyncing = source.data.isSyncing || syncingSourceIds.has(source.data.id);

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h1 className="text-xl font-semibold">{source.data.name}</h1>
          <p className="mt-1 break-all text-sm text-muted-foreground">{source.data.url}</p>
        </div>
        <div className="flex gap-2">
          <Link className={buttonClassName("secondary")} to={`/admin/sources/${source.data.id}/edit`}>
            Edit
          </Link>
        </div>
      </div>
      <div className="grid gap-3 md:grid-cols-3">
        <Info label="Health" value={<Badge className={statusToneClass(sourceStatusTone(isSyncing ? "syncing" : source.data.status))}>{sourceHealthText(source.data, isSyncing)}</Badge>} />
        <Info label="Last successful sync" value={formatDateTime(source.data.lastSuccessfulSyncAt)} />
        <Info label="Package count" value={source.data.packageCount} />
        <Info label="Polling interval" value={source.data.pollingInterval ?? "Manual"} />
        <Info label="Approval policy" value={source.data.approvalPolicy} />
        <Info label="Version discovery" value={versionDiscoveryPolicyText(source.data.versionDiscoveryPolicy)} />
        <Info label="Enabled" value={source.data.enabled ? "Yes" : "No"} />
      </div>
      {source.data.lastSyncError && !isSyncing ? (
        <div className="rounded-ui border border-destructive/30 bg-destructive/5 p-4">
          <div className="text-xs uppercase text-destructive">Last sync error</div>
          <p className="mt-2 break-words text-sm text-foreground">{source.data.lastSyncError}</p>
        </div>
      ) : null}
      <div className="rounded-ui border border-border p-4">
        <h2 className="text-sm font-medium">Indexing boundaries</h2>
        <div className="mt-3 grid gap-4 md:grid-cols-2">
          <PatternList title="Include" items={source.data.includePatterns} />
          <PatternList title="Exclude" items={source.data.excludePatterns} />
        </div>
      </div>
      <SourceActions source={source.data} onDeleted={() => navigate("/admin/sources")} onSyncStateChange={setSourceSyncing} showEdit={false} />
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

function PatternList({ title, items }: { title: string; items: string[] }) {
  return (
    <div>
      <div className="text-xs uppercase text-muted-foreground">{title}</div>
      <ul className="mt-2 space-y-1 font-mono text-sm">
        {items.length ? items.map((item) => <li key={item}>{item}</li>) : <li className="text-muted-foreground">None</li>}
      </ul>
    </div>
  );
}
