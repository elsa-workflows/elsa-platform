import { Link } from "react-router-dom";
import type { SyncRun } from "@/features/sync-runs/syncRunModels";
import { shortId, sourceDisplayName, syncRunSourceLabel } from "@/features/sync-runs/syncRunModels";

export function SyncRunSourceValue({ run, previewLimit = 3 }: { run: SyncRun; previewLimit?: number }) {
  if (run.sources.length === 1) {
    const [source] = run.sources;
    return (
      <>
        <Link className="font-medium" to={`/admin/sources/${source.id}`}>
          {sourceDisplayName(source)}
        </Link>
        <div className="mt-1 font-mono text-xs text-muted-foreground">{shortId(source.id)}</div>
      </>
    );
  }

  if (run.sources.length > 1) {
    const visibleSources = run.sources.slice(0, previewLimit);
    const hiddenCount = run.sources.length - visibleSources.length;
    return (
      <>
        <span className="font-medium">{syncRunSourceLabel(run)}</span>
        <div className="mt-1 text-xs text-muted-foreground">
          {visibleSources.map(sourceDisplayName).join(", ")}
          {hiddenCount > 0 ? `, +${hiddenCount} more` : ""}
        </div>
      </>
    );
  }

  return <span className="text-muted-foreground">{syncRunSourceLabel(run)}</span>;
}
