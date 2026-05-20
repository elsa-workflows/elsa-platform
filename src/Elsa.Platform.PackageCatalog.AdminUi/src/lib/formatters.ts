export function formatDateTime(value?: string | null) {
  if (!value) return "Never";
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
}

export function formatDuration(start?: string | null, end?: string | null) {
  if (!start) return "-";
  const startDate = new Date(start);
  const endDate = end ? new Date(end) : new Date();
  const seconds = Math.max(0, Math.round((endDate.getTime() - startDate.getTime()) / 1000));
  if (seconds < 60) return `${seconds}s`;
  const minutes = Math.floor(seconds / 60);
  return `${minutes}m ${seconds % 60}s`;
}

export function formatJson(raw?: string | null) {
  if (!raw) return { status: "Unavailable" as const, value: "" };
  try {
    return { status: "Formatted" as const, value: JSON.stringify(JSON.parse(raw), null, 2) };
  } catch {
    return { status: "RawOnly" as const, value: raw };
  }
}

export function truncate(value: string, max = 80) {
  return value.length > max ? `${value.slice(0, max - 1)}...` : value;
}
