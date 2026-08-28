export type StatusTone = "neutral" | "success" | "warning" | "destructive";

export function sourceStatusTone(status?: string): StatusTone {
  switch (status?.toLowerCase()) {
    case "healthy":
    case "completed":
    case "valid":
    case "approved":
      return "success";
    case "warning":
    case "pending":
    case "syncing":
    case "completedwitherrors":
    case "notvalidated":
    case "canceled":
      return "warning";
    case "error":
    case "failed":
    case "invalid":
    case "rejected":
    case "suspicious":
    case "unsupportedschema":
    case "blocking":
      return "destructive";
    default:
      return "neutral";
  }
}

export function statusToneClass(tone: StatusTone) {
  switch (tone) {
    case "success":
      return "border-success/40 text-success";
    case "warning":
      return "border-warning/40 text-warning";
    case "destructive":
      return "border-destructive/40 text-destructive";
    default:
      return "text-muted-foreground";
  }
}
