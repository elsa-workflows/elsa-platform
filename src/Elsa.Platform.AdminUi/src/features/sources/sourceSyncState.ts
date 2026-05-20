import { useCallback, useState } from "react";

export function useSyncingSourceIds() {
  const [syncingSourceIds, setSyncingSourceIds] = useState<ReadonlySet<string>>(() => new Set<string>());

  const setSourceSyncing = useCallback((sourceId: string, isSyncing: boolean) => {
    setSyncingSourceIds((current) => {
      if (current.has(sourceId) === isSyncing) return current;

      const next = new Set<string>(current);
      if (isSyncing) {
        next.add(sourceId);
      } else {
        next.delete(sourceId);
      }

      return next;
    });
  }, []);

  return { syncingSourceIds, setSourceSyncing };
}
