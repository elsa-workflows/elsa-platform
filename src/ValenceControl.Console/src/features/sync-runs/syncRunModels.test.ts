import { describe, expect, it } from "vitest";
import { normalizeSyncRun, packagesScanned, packagesUpdated, syncFailures, syncRunItemStatusLabel, syncRunSourceLabel } from "@/features/sync-runs/syncRunModels";

const baseRun = {
  id: "sync-123",
  trigger: "Scheduled",
  status: "Completed",
  startedAt: "2026-05-15T08:00:00Z",
  completedAt: "2026-05-15T08:02:14Z",
  error: null,
  items: []
};

describe("sync run models", () => {
  it("prefers canonical counters over legacy fallback fields", () => {
    const run = normalizeSyncRun({
      ...baseRun,
      summaryCountersJson: JSON.stringify({ scanned: 100, indexed: 7, failed: 2 }),
      packagesScanned: 52,
      packagesUpdated: 4,
      failures: 1
    });

    expect(packagesScanned(run)).toBe(100);
    expect(packagesUpdated(run)).toBe(7);
    expect(syncFailures(run)).toBe(2);
  });

  it("sanitizes object-form counters before using them", () => {
    const run = normalizeSyncRun({
      ...baseRun,
      summaryCounters: {
        Scanned: 12,
        Indexed: 3,
        failed: 1,
        ignored: "not-a-number"
      }
    });

    expect(run.summaryCounters).toEqual({ scanned: 12, indexed: 3, failed: 1 });
    expect(packagesScanned(run)).toBe(12);
    expect(packagesUpdated(run)).toBe(3);
    expect(syncFailures(run)).toBe(1);
  });

  it("returns raw sync item status labels intentionally", () => {
    expect(syncRunItemStatusLabel("Unchanged")).toBe("Unchanged");
  });

  it("normalizes source summaries and item counts", () => {
    const run = normalizeSyncRun({
      ...baseRun,
      itemCount: 12,
      sources: [{ id: "source-1", name: "Elsa Official" }]
    });

    expect(run.itemCount).toBe(12);
    expect(syncRunSourceLabel(run)).toBe("Elsa Official");
  });

  it("uses legacy packages scanned when canonical counters only contain per-status counts", () => {
    const run = normalizeSyncRun({
      ...baseRun,
      summaryCountersJson: JSON.stringify({ indexed: 4, failed: 1 }),
      packagesScanned: 52
    });

    expect(packagesScanned(run)).toBe(52);
    expect(packagesUpdated(run)).toBe(4);
    expect(syncFailures(run)).toBe(1);
  });
});
