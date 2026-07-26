export type ConsoleLogStream = 0 | 1;
export type ConsoleLogStreamFilter = "all" | "stdout" | "stderr";
export type ConsoleLogConnectionStatus = "connecting" | "connected" | "reconnecting" | "disconnected";

export type ConsoleLogFilter = {
  sourceId?: string;
  stream?: ConsoleLogStream;
  query?: string;
  limit?: number;
};

export type ConsoleLogLine = {
  id: string;
  timestamp: string;
  receivedAt: string;
  sequence: number;
  stream: ConsoleLogStream;
  text: string;
  source: ConsoleLogSource;
  metadata?: Record<string, string>;
  truncated: boolean;
};

export type ConsoleLogSource = {
  id: string;
  displayName: string;
  serviceName?: string | null;
  processId?: number | null;
  machineName?: string | null;
  podName?: string | null;
  containerName?: string | null;
  namespace?: string | null;
  nodeName?: string | null;
  startedAt?: string | null;
  lastSeen?: string | null;
  health: "Unknown" | "Connected" | "Stale" | "Disconnected";
  metadata?: Record<string, string>;
};

export type ConsoleLogDroppedSummary = {
  sourceId?: string | null;
  stream?: ConsoleLogStream | null;
  reason: string;
  count: number;
  from?: string | null;
  to?: string | null;
};

export type RecentConsoleLogsResult = {
  items: ConsoleLogLine[];
  dropped?: ConsoleLogDroppedSummary[];
  sources?: ConsoleLogSource[];
};

export const consoleLogStreamFilters: Array<{ value: ConsoleLogStreamFilter; label: string; apiValue?: ConsoleLogStream }> = [
  { value: "all", label: "All streams" },
  { value: "stdout", label: "stdout", apiValue: 0 },
  { value: "stderr", label: "stderr", apiValue: 1 }
];

export function streamLabel(stream: ConsoleLogStream | null | undefined) {
  if (stream === 1) return "stderr";
  if (stream === 0) return "stdout";
  return "all";
}

export function streamFilterValue(stream: ConsoleLogStreamFilter): ConsoleLogStream | undefined {
  return consoleLogStreamFilters.find((item) => item.value === stream)?.apiValue;
}
