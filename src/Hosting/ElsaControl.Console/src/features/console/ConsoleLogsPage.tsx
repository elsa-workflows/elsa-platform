import type { HubConnection } from "@microsoft/signalr";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, Filter, Pause, Play, RefreshCw, Terminal } from "lucide-react";
import { Badge, EmptyState, Input, SecondaryButton, Select } from "@/components/ui";
import { getRecentConsoleLogs, listConsoleLogSources, consoleLogsHubPath } from "@/features/console/consoleLogApi";
import {
  consoleLogStreamFilters,
  streamFilterValue,
  streamLabel,
  type ConsoleLogConnectionStatus,
  type ConsoleLogDroppedSummary,
  type ConsoleLogFilter,
  type ConsoleLogLine,
  type ConsoleLogSource,
  type ConsoleLogStreamFilter
} from "@/features/console/consoleLogModels";
import { formatDateTime } from "@/lib/formatters";
import { cn } from "@/lib/utils";

const maxRows = 500;
const defaultBackfillLimit = 150;

export function ConsoleLogsPage() {
  const [rows, setRows] = useState<ConsoleLogLine[]>([]);
  const [sources, setSources] = useState<ConsoleLogSource[]>([]);
  const [dropped, setDropped] = useState<ConsoleLogDroppedSummary[]>([]);
  const [query, setQuery] = useState("");
  const [stream, setStream] = useState<ConsoleLogStreamFilter>("all");
  const [sourceId, setSourceId] = useState("");
  const [limit, setLimit] = useState(defaultBackfillLimit);
  const [paused, setPaused] = useState(false);
  const [status, setStatus] = useState<ConsoleLogConnectionStatus>("connecting");
  const [statusDetail, setStatusDetail] = useState("Opening live console stream");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);
  const filterRef = useRef<ConsoleLogFilter>({});
  const terminalRef = useRef<HTMLDivElement | null>(null);
  const pausedRef = useRef(paused);

  const filter = useMemo<ConsoleLogFilter>(() => ({
    sourceId: sourceId || undefined,
    stream: streamFilterValue(stream),
    query: query.trim() || undefined,
    limit
  }), [limit, query, sourceId, stream]);

  useEffect(() => {
    filterRef.current = filter;
  }, [filter]);

  useEffect(() => {
    pausedRef.current = paused;
  }, [paused]);

  const mergeRows = useCallback((incoming: ConsoleLogLine[]) => {
    if (incoming.length === 0 || pausedRef.current) {
      return;
    }

    setRows((current) => {
      const byId = new Map(current.map((row) => [row.id, row]));
      incoming.forEach((row) => byId.set(row.id, row));
      return Array.from(byId.values()).sort(compareRows).slice(-maxRows);
    });
  }, []);

  const loadRecent = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const payload = await getRecentConsoleLogs(filterRef.current);
      setRows((payload.items ?? []).sort(compareRows).slice(-maxRows));
      setDropped((payload.dropped ?? []).slice(-8));
      if (payload.sources) {
        setSources(payload.sources);
      } else {
        setSources(await listConsoleLogSources());
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadRecent();
  }, [loadRecent]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void loadRecent();
      const connection = connectionRef.current;
      if (connection?.state === "Connected") {
        void connection.invoke("UpdateFilterAsync", filterRef.current).catch((err) => {
          setStatusDetail(err instanceof Error ? err.message : String(err));
        });
      }
    }, 250);

    return () => window.clearTimeout(timer);
  }, [filter, loadRecent]);

  useEffect(() => {
    let disposed = false;
    let connection: HubConnection | null = null;

    void import("@microsoft/signalr").then(({ HubConnectionBuilder, LogLevel }) => {
      if (disposed) {
        return;
      }

      connection = new HubConnectionBuilder()
        .withUrl(consoleLogsHubPath, { withCredentials: true })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      connectionRef.current = connection;
      connection.on("ReceiveConsoleLogLineAsync", (line: ConsoleLogLine) => mergeRows([line]));
      connection.on("ReceiveDroppedLinesAsync", (summary: ConsoleLogDroppedSummary) => {
        setDropped((current) => [summary, ...current].slice(0, 8));
      });
      connection.on("ReceiveSourceChangedAsync", (source: ConsoleLogSource) => {
        setSources((current) => upsertSource(current, source));
      });
      connection.onreconnecting(() => {
        setStatus("reconnecting");
        setStatusDetail("Live stream reconnecting");
      });
      connection.onreconnected(() => {
        setStatus("connected");
        setStatusDetail("Live stream resumed");
        if (connection) {
          void subscribe(connection, filterRef.current, setStatusDetail);
        }
      });
      connection.onclose((err) => {
        setStatus("disconnected");
        setStatusDetail(err instanceof Error ? err.message : "Live stream disconnected");
      });

      void connection.start()
        .then(async () => {
          if (!connection || disposed) {
            return;
          }

          setStatus("connected");
          setStatusDetail("Live stream connected");
          await subscribe(connection, filterRef.current, setStatusDetail);
        })
        .catch((err) => {
          if (disposed) {
            return;
          }

          setStatus("disconnected");
          setStatusDetail(err instanceof Error ? err.message : String(err));
        });
    }).catch((err) => {
      if (disposed) {
        return;
      }

      setStatus("disconnected");
      setStatusDetail(err instanceof Error ? err.message : String(err));
    });

    return () => {
      disposed = true;
      connectionRef.current = null;
      void connection?.stop();
    };
  }, [mergeRows]);

  useEffect(() => {
    if (terminalRef.current) {
      terminalRef.current.scrollTop = terminalRef.current.scrollHeight;
    }
  }, [rows]);

  const totalDropped = dropped.reduce((total, summary) => total + summary.count, 0);
  const selectedSource = sourceId ? sources.find((source) => source.id === sourceId) : undefined;

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h1 className="text-xl font-semibold">Console</h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            Live backend stdout and stderr from the Elsa Control API process.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge status={status} detail={statusDetail} />
          <SecondaryButton onClick={() => setPaused((current) => !current)} title={paused ? "Resume live console updates" : "Pause live console updates"}>
            {paused ? <Play className="h-4 w-4" /> : <Pause className="h-4 w-4" />}
            {paused ? "Resume" : "Pause"}
          </SecondaryButton>
          <SecondaryButton onClick={() => void loadRecent()} disabled={loading} title="Refresh recent console lines">
            <RefreshCw className="h-4 w-4" />
            Refresh
          </SecondaryButton>
        </div>
      </div>

      <section className="grid gap-3 rounded-ui border border-border bg-surface p-3 lg:grid-cols-[minmax(16rem,1fr)_11rem_14rem_9rem]">
        <label className="space-y-1 text-xs font-medium text-muted-foreground">
          <span className="inline-flex items-center gap-1"><Filter className="h-3.5 w-3.5" /> Search</span>
          <Input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Filter line text" />
        </label>
        <label className="space-y-1 text-xs font-medium text-muted-foreground">
          Stream
          <Select className="w-full" value={stream} onChange={(event) => setStream(event.target.value as ConsoleLogStreamFilter)} aria-label="Stream">
            {consoleLogStreamFilters.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </Select>
        </label>
        <label className="space-y-1 text-xs font-medium text-muted-foreground">
          Source
          <Select className="w-full" value={sourceId} onChange={(event) => setSourceId(event.target.value)} aria-label="Source">
            <option value="">All sources</option>
            {sources.map((source) => <option key={source.id} value={source.id}>{source.displayName || source.id}</option>)}
          </Select>
        </label>
        <label className="space-y-1 text-xs font-medium text-muted-foreground">
          Backfill
          <Input type="number" min={25} max={1000} step={25} value={limit} onChange={(event) => setLimit(clampLimit(event.target.value))} aria-label="Backfill" />
        </label>
      </section>

      <section className="grid gap-3 md:grid-cols-3">
        <Metric label="Rows" value={rows.length} detail={`${limit} requested`} />
        <Metric label="Sources" value={sources.length} detail={selectedSource?.health ?? "All sources"} />
        <Metric label="Dropped" value={totalDropped} detail={dropped.length ? "Recent summaries" : "None reported"} />
      </section>

      {error ? (
        <div role="alert" className="flex items-start gap-2 rounded-ui border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{error}</span>
        </div>
      ) : null}

      <section className="overflow-hidden rounded-ui border border-border bg-[#07110f] text-[#d7fbe8] shadow-sm">
        <div className="flex items-center justify-between border-b border-white/10 bg-black/30 px-3 py-2 text-xs text-[#8fb7a2]">
          <span className="inline-flex items-center gap-2"><Terminal className="h-4 w-4" /> backend console</span>
          <span>{paused ? "paused" : loading ? "loading" : `${rows.length} lines`}</span>
        </div>
        <div ref={terminalRef} className="h-[34rem] overflow-auto p-3 font-mono text-xs leading-5">
          {rows.length === 0 ? (
            <EmptyConsole loading={loading} />
          ) : (
            rows.map((row) => <ConsoleLineRow key={row.id} row={row} />)
          )}
        </div>
      </section>

      {dropped.length > 0 ? (
        <section className="space-y-2">
          <h2 className="text-sm font-medium">Dropped line summaries</h2>
          <div className="grid gap-2 md:grid-cols-2">
            {dropped.map((summary, index) => (
              <div key={`${summary.sourceId ?? "all"}-${summary.stream ?? "all"}-${summary.reason}-${index}`} className="rounded-ui border border-border bg-surface p-3 text-sm">
                <div className="flex items-center justify-between gap-2">
                  <Badge>{streamLabel(summary.stream)}</Badge>
                  <span className="text-xs text-muted-foreground">{summary.count} dropped</span>
                </div>
                <p className="mt-2 text-muted-foreground">{summary.reason}</p>
              </div>
            ))}
          </div>
        </section>
      ) : null}
    </section>
  );
}

async function subscribe(connection: HubConnection, filter: ConsoleLogFilter, setStatusDetail: (value: string) => void) {
  try {
    await connection.invoke("SubscribeAsync", filter);
  } catch (err) {
    setStatusDetail(err instanceof Error ? err.message : String(err));
  }
}

function ConsoleLineRow({ row }: { row: ConsoleLogLine }) {
  const isStderr = row.stream === 1;
  return (
    <div className="grid grid-cols-[9.5rem_4.5rem_minmax(0,1fr)] gap-2 border-b border-white/[0.04] py-1 last:border-b-0">
      <span className="text-[#789585]">{formatTerminalTime(row.receivedAt || row.timestamp)}</span>
      <span className={cn("font-semibold", isStderr ? "text-[#ffb4a8]" : "text-[#81e6b6]")}>{streamLabel(row.stream)}</span>
      <span className="min-w-0 whitespace-pre-wrap break-words">
        {row.text}
        {row.truncated ? <span className="ml-2 text-[#facc15]">truncated</span> : null}
      </span>
    </div>
  );
}

function EmptyConsole({ loading }: { loading: boolean }) {
  if (loading) {
    return <div className="text-[#8fb7a2]">Loading recent console output...</div>;
  }

  return (
    <div className="flex h-full items-center justify-center">
      <EmptyState title="No console output" description="Matching stdout and stderr lines will appear here as the backend writes to the console." />
    </div>
  );
}

function StatusBadge({ status, detail }: { status: ConsoleLogConnectionStatus; detail: string }) {
  const tone = status === "connected"
    ? "border-success/30 bg-success/10 text-success"
    : status === "reconnecting"
      ? "border-warning/30 bg-warning/10 text-warning"
      : "border-destructive/30 bg-destructive/10 text-destructive";

  return (
    <span title={detail}>
      <Badge className={cn("h-9 max-w-full gap-2", tone)}>
        <span className="h-2 w-2 rounded-full bg-current" aria-hidden />
        <span className="truncate">{status}</span>
      </Badge>
    </span>
  );
}

function Metric({ label, value, detail }: { label: string; value: number | string; detail: string }) {
  return (
    <div className="rounded-ui border border-border bg-surface p-3">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="mt-1 text-2xl font-semibold">{value}</p>
      <p className="mt-1 truncate text-xs text-muted-foreground">{detail}</p>
    </div>
  );
}

function compareRows(left: ConsoleLogLine, right: ConsoleLogLine) {
  const byTime = Date.parse(left.receivedAt || left.timestamp) - Date.parse(right.receivedAt || right.timestamp);
  return byTime === 0 ? left.sequence - right.sequence : byTime;
}

function upsertSource(current: ConsoleLogSource[], source: ConsoleLogSource) {
  const next = current.filter((item) => item.id !== source.id);
  next.push(source);
  return next.sort((left, right) => (left.displayName || left.id).localeCompare(right.displayName || right.id));
}

function clampLimit(value: string) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    return defaultBackfillLimit;
  }

  return Math.min(1000, Math.max(25, Math.trunc(parsed)));
}

function formatTerminalTime(value: string) {
  if (!value) {
    return "--:--:--";
  }

  try {
    return new Intl.DateTimeFormat(undefined, {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      fractionalSecondDigits: 3
    }).format(new Date(value));
  } catch {
    return formatDateTime(value);
  }
}
