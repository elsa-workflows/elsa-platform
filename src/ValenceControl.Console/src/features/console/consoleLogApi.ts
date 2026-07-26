import { apiRequest } from "@/lib/api/httpClient";
import type { ConsoleLogFilter, ConsoleLogSource, RecentConsoleLogsResult } from "@/features/console/consoleLogModels";

const consoleLogsBasePath = "/api/admin/console-logs";

export const consoleLogsHubPath = `${consoleLogsBasePath}/hub`;

export function getRecentConsoleLogs(filter: ConsoleLogFilter) {
  return apiRequest<RecentConsoleLogsResult>(`${consoleLogsBasePath}/recent?${toQueryString(filter)}`);
}

export function listConsoleLogSources() {
  return apiRequest<ConsoleLogSource[]>(`${consoleLogsBasePath}/sources`);
}

function toQueryString(filter: ConsoleLogFilter) {
  const params = new URLSearchParams();
  append(params, "sourceId", filter.sourceId);
  append(params, "stream", filter.stream);
  append(params, "query", filter.query);
  append(params, "limit", filter.limit);
  return params.toString();
}

function append(params: URLSearchParams, key: string, value: string | number | undefined) {
  if (value === undefined || value === "") {
    return;
  }

  params.set(key, String(value));
}
