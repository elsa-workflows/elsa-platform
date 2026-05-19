export type SyncRunTrigger = "Scheduled" | "ManualAll" | "ManualSource" | "ManualPackage";
export type SyncRunStatus = "Running" | "Completed" | "Failed" | "CompletedWithErrors" | "Canceled";
export type SyncRunItemStatus = "Discovered" | "Skipped" | "Downloaded" | "Indexed" | "Unchanged" | "Invalid" | "Failed" | "Suspicious";

export type SummaryCounters = Record<string, number>;

export type SyncRunSource = {
  id: string;
  name?: string | null;
};

export type SyncRunItem = {
  id: string;
  sourceId?: string | null;
  packageId?: string | null;
  version?: string | null;
  status: SyncRunItemStatus;
  message?: string | null;
  error?: string | null;
  startedAt: string;
  completedAt?: string | null;
};

export type SyncRun = {
  id: string;
  trigger: SyncRunTrigger;
  status: SyncRunStatus;
  startedAt: string;
  completedAt?: string | null;
  error?: string | null;
  summaryCounters: SummaryCounters;
  itemCount: number;
  sources: SyncRunSource[];
  items: SyncRunItem[];
};

export type SyncRunCleanupPreview = {
  completedBefore: string;
  eligibleRunCount: number;
  eligibleItemCount: number;
  excludedRunCount: number;
  oldestEligibleCompletedAt?: string | null;
  newestEligibleCompletedAt?: string | null;
};

export type SyncRunCleanupResult = {
  deletedRunCount: number;
  deletedItemCount: number;
  excludedRunCount: number;
  notFoundRunCount: number;
  completedBefore?: string | null;
  deletedRunIds: string[];
};

type SyncRunResponse = Omit<SyncRun, "summaryCounters" | "itemCount" | "sources" | "items"> & {
  summaryCounters?: SummaryCounters | string | null;
  summaryCountersJson?: string | null;
  packagesScanned?: number;
  packagesUpdated?: number;
  failures?: number;
  itemCount?: number;
  sources?: SyncRunSource[] | null;
  items?: SyncRunItem[] | null;
};

const scannedKeys = ["scanned", "packagesscanned", "discovered", "downloaded", "indexed", "unchanged", "skipped", "invalid", "suspicious", "failed"];
const updatedKeys = ["updated", "packagesupdated", "indexed"];
const failureKeys = ["failures", "failed"];
const scannedGuardKeys = ["scanned", "packagesscanned"];
const warningStatuses: SyncRunItemStatus[] = ["Invalid", "Suspicious"];

export function normalizeSyncRuns(response: unknown): SyncRun[] {
  if (Array.isArray(response)) return response.map(normalizeSyncRun);
  if (hasItems(response)) return response.items.map(normalizeSyncRun);
  return [];
}

export function normalizeSyncRun(response: unknown): SyncRun {
  const run = response as SyncRunResponse;
  const items = run.items ?? [];
  return {
    id: run.id,
    trigger: run.trigger,
    status: run.status,
    startedAt: run.startedAt,
    completedAt: run.completedAt,
    error: run.error,
    summaryCounters: withLegacyCounterFallbacks(parseSummaryCounters(run), run),
    itemCount: typeof run.itemCount === "number" ? run.itemCount : items.length,
    sources: normalizeSources(run.sources),
    items
  };
}

export function isActiveSyncRun(run: SyncRun) {
  return run.status === "Running";
}

export function isTerminalSyncRun(run: SyncRun) {
  return run.status === "Completed" || run.status === "CompletedWithErrors" || run.status === "Failed";
}

export function syncRunHasAttention(run: SyncRun) {
  return run.status === "Failed" || run.status === "CompletedWithErrors";
}

export function syncRunTriggerLabel(trigger: SyncRunTrigger) {
  switch (trigger) {
    case "ManualAll":
      return "Manual all";
    case "ManualSource":
      return "Manual source";
    case "ManualPackage":
      return "Manual package";
    default:
      return "Scheduled";
  }
}

export function syncRunSourceLabel(run: SyncRun) {
  if (run.sources.length === 1) return sourceDisplayName(run.sources[0]);
  if (run.sources.length > 1) return `${run.sources.length} sources`;
  return run.trigger === "ManualSource" || run.trigger === "ManualPackage" ? "Unknown source" : "All enabled sources";
}

export function syncRunStatusLabel(status: SyncRunStatus) {
  switch (status) {
    case "CompletedWithErrors":
      return "Completed with errors";
    default:
      return status;
  }
}

export function syncRunItemStatusLabel(status: SyncRunItemStatus) {
  return status;
}

export function packagesScanned(run: SyncRun) {
  return typeof run.summaryCounters.scanned === "number"
    ? run.summaryCounters.scanned
    : sumCounters(run.summaryCounters, scannedKeys);
}

export function packagesUpdated(run: SyncRun) {
  return counterValue(run.summaryCounters, updatedKeys);
}

export function syncFailures(run: SyncRun) {
  return counterValue(run.summaryCounters, failureKeys);
}

export function warningItems(run: SyncRun) {
  return run.items.filter((item) => warningStatuses.includes(item.status));
}

export function failedItems(run: SyncRun) {
  return run.items.filter((item) => item.status === "Failed");
}

export function shortId(id: string) {
  return id.length > 8 ? id.slice(0, 8) : id;
}

export function sourceDisplayName(source: SyncRunSource) {
  const name = source.name?.trim();
  return name ? name : shortId(source.id);
}

export function normalizeCleanupPreview(response: unknown): SyncRunCleanupPreview {
  const preview = response as SyncRunCleanupPreview;
  return {
    completedBefore: preview.completedBefore,
    eligibleRunCount: numberOrZero(preview.eligibleRunCount),
    eligibleItemCount: numberOrZero(preview.eligibleItemCount),
    excludedRunCount: numberOrZero(preview.excludedRunCount),
    oldestEligibleCompletedAt: preview.oldestEligibleCompletedAt,
    newestEligibleCompletedAt: preview.newestEligibleCompletedAt
  };
}

export function normalizeCleanupResult(response: unknown): SyncRunCleanupResult {
  const result = response as SyncRunCleanupResult;
  return {
    deletedRunCount: numberOrZero(result.deletedRunCount),
    deletedItemCount: numberOrZero(result.deletedItemCount),
    excludedRunCount: numberOrZero(result.excludedRunCount),
    notFoundRunCount: numberOrZero(result.notFoundRunCount),
    completedBefore: result.completedBefore,
    deletedRunIds: Array.isArray(result.deletedRunIds) ? result.deletedRunIds.filter((id): id is string => typeof id === "string") : []
  };
}

export function toUtcCutoff(value: string) {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : date.toISOString();
}

function parseSummaryCounters(run: SyncRunResponse): SummaryCounters {
  if (typeof run.summaryCountersJson === "string") return parseCounterJson(run.summaryCountersJson);
  if (typeof run.summaryCounters === "string") return parseCounterJson(run.summaryCounters);
  if (run.summaryCounters && typeof run.summaryCounters === "object") return normalizeCounterObject(run.summaryCounters);
  return {};
}

function parseCounterJson(value: string): SummaryCounters {
  try {
    const parsed = JSON.parse(value) as unknown;
    return normalizeCounterObject(parsed);
  } catch {
    return {};
  }
}

function withLegacyCounterFallbacks(counters: SummaryCounters, run: SyncRunResponse): SummaryCounters {
  return {
    ...(typeof run.packagesScanned === "number" && !hasNumericCounter(counters, scannedGuardKeys) ? { scanned: run.packagesScanned } : {}),
    ...(typeof run.packagesUpdated === "number" && !hasNumericCounter(counters, updatedKeys) ? { updated: run.packagesUpdated } : {}),
    ...(typeof run.failures === "number" && !hasNumericCounter(counters, failureKeys) ? { failed: run.failures } : {}),
    ...counters
  };
}

function counterValue(counters: SummaryCounters, keys: string[]) {
  const exactMatch = keys.find((key) => typeof counters[key] === "number");
  if (exactMatch) return counters[exactMatch];
  return sumCounters(counters, keys);
}

function sumCounters(counters: SummaryCounters, keys: string[]) {
  return Object.entries(counters)
    .filter(([key, value]) => keys.includes(key.toLowerCase()) && typeof value === "number")
    .reduce((total, [, value]) => total + value, 0);
}

function hasNumericCounter(counters: SummaryCounters, keys: string[]) {
  return Object.entries(counters).some(([key, value]) => keys.includes(key.toLowerCase()) && typeof value === "number");
}

function normalizeCounterObject(value: unknown): SummaryCounters {
  if (!value || typeof value !== "object" || Array.isArray(value)) return {};
  return Object.fromEntries(
    Object.entries(value)
      .filter(([, counter]) => typeof counter === "number")
      .map(([key, counter]) => [key.toLowerCase(), counter as number])
  );
}

function normalizeSources(sources: SyncRunSource[] | null | undefined): SyncRunSource[] {
  if (!Array.isArray(sources)) return [];
  return sources.filter((source) => Boolean(source.id));
}

function hasItems(response: unknown): response is { items: unknown[] } {
  return Boolean(response && typeof response === "object" && "items" in response && Array.isArray(response.items));
}

function numberOrZero(value: unknown) {
  return typeof value === "number" ? value : 0;
}
