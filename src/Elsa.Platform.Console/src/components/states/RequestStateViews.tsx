import { EmptyState } from "@/components/ui";

export type RequestState = "loading" | "empty" | "stale" | "unauthorized" | "not-found" | "unexpected";

const defaultText: Record<RequestState, { title: string; description: string }> = {
  loading: { title: "Loading", description: "Fetching the latest catalog data." },
  empty: { title: "Nothing here yet", description: "There are no records for this view." },
  stale: { title: "Showing stale data", description: "The last refresh failed. Try again when the API is available." },
  unauthorized: { title: "Access problem", description: "Your console session is missing or no longer valid." },
  "not-found": { title: "Not found", description: "This record may have been removed or changed." },
  unexpected: { title: "Something went wrong", description: "The console could not complete the request." }
};

export function RequestStateView({
  state,
  title,
  description
}: {
  state: RequestState;
  title?: string;
  description?: string;
}) {
  const copy = defaultText[state];
  return <EmptyState title={title ?? copy.title} description={description ?? copy.description} />;
}
