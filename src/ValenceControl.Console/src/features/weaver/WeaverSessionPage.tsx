import { useQuery } from "@tanstack/react-query";
import { Bot } from "lucide-react";
import { useParams } from "react-router-dom";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { getWeaverSession } from "@/features/weaver/weaverApi";
import { WeaverPlanCard } from "@/features/weaver/WeaverPlanCard";
import { queryKeys } from "@/lib/query/queryClient";

export function WeaverSessionPage() {
  const { sessionId = "" } = useParams();
  const { selectedWorkspaceId } = useWorkspaceContext();
  const session = useQuery({
    queryKey: selectedWorkspaceId && sessionId ? queryKeys.weaverSession(selectedWorkspaceId, sessionId) : ["weaver", "session-detail", "missing"],
    queryFn: () => getWeaverSession(selectedWorkspaceId, sessionId),
    enabled: Boolean(selectedWorkspaceId && sessionId),
    retry: false
  });

  if (session.isLoading)
    return <RequestStateView state="loading" title="Loading Weaver session" />;
  if (session.isError)
    return <RequestStateView state="unexpected" title="Weaver session could not load." />;
  if (!session.data)
    return <RequestStateView state="empty" title="Weaver session was not found." />;

  return (
    <section className="space-y-5">
      <div className="flex items-center gap-2">
        <Bot aria-hidden className="h-5 w-5 text-primary" />
        <div>
          <h1 className="font-display text-xl font-semibold">Weaver session</h1>
          <p className="text-sm text-muted-foreground">{session.data.session.status} · {session.data.session.mode}</p>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="space-y-3">
          <h2 className="text-sm font-semibold">Messages</h2>
          {session.data.messages.map((message) => (
            <article key={message.id} className="rounded-ui border border-border bg-surface px-3 py-2 text-sm">
              <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">{message.role}</p>
              <p className="mt-1 break-words">{message.content}</p>
              {message.redactionState === "Redacted" ? <p className="mt-2 text-xs text-muted-foreground">Contains redactions</p> : null}
            </article>
          ))}
        </div>

        <div className="space-y-3">
          <h2 className="text-sm font-semibold">Tool calls</h2>
          {session.data.toolCalls.length === 0 ? <p className="text-sm text-muted-foreground">No tool calls recorded.</p> : null}
          {session.data.toolCalls.map((toolCall) => (
            <article key={toolCall.id} className="rounded-ui border border-border bg-surface px-3 py-2 text-sm">
              <p className="font-medium">{toolCall.toolName}</p>
              <p className="mt-1 text-xs text-muted-foreground">{toolCall.status} · {toolCall.authorizationResult}</p>
            </article>
          ))}
        </div>
      </div>

      {session.data.plans.length > 0 ? (
        <div className="space-y-3">
          <h2 className="text-sm font-semibold">Plans</h2>
          {session.data.plans.map((plan) => <WeaverPlanCard key={plan.id} plan={plan} />)}
        </div>
      ) : null}
    </section>
  );
}
