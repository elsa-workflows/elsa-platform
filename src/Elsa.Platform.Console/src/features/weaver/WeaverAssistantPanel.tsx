import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bot, MessageSquare, Send, SlidersHorizontal, X } from "lucide-react";
import { useLocation } from "react-router-dom";
import { useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { Button, Select, SecondaryButton } from "@/components/ui";
import { ApiError } from "@/lib/api/httpClient";
import { queryKeys } from "@/lib/query/queryClient";
import { cn } from "@/lib/utils";
import { approveWeaverPlan, createWeaverSession, executeWeaverPlan, getWeaverConfiguration, getWeaverSession, sendWeaverMessage } from "@/features/weaver/weaverApi";
import type { WeaverMode, WorkspaceWeaverMessage, WorkspaceWeaverToolCall } from "@/features/weaver/weaverModels";
import { WeaverPlanCard } from "@/features/weaver/WeaverPlanCard";

const modeOptions: Array<{ value: WeaverMode; label: string }> = [
  { value: "Inspect", label: "Inspect" },
  { value: "Plan", label: "Plan" },
  { value: "Operate", label: "Operate" }
];

export function WeaverAssistantPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const location = useLocation();
  const queryClient = useQueryClient();
  const { selectedWorkspaceId, selectedWorkspace } = useWorkspaceContext();
  const [draft, setDraft] = useState("");
  const [mode, setMode] = useState<WeaverMode>("Plan");
  const [sessionId, setSessionId] = useState<string | null>(null);
  const workspaceId = selectedWorkspaceId;
  const routePath = location.pathname;

  const configuration = useQuery({
    queryKey: workspaceId ? queryKeys.weaverConfiguration(workspaceId) : ["weaver", "no-workspace", "configuration"],
    queryFn: () => getWeaverConfiguration(workspaceId),
    enabled: open && Boolean(workspaceId),
    retry: false
  });
  const session = useQuery({
    queryKey: workspaceId && sessionId ? queryKeys.weaverSession(workspaceId, sessionId) : ["weaver", "no-session"],
    queryFn: () => getWeaverSession(workspaceId, sessionId!),
    enabled: open && Boolean(workspaceId && sessionId),
    retry: false
  });
  const send = useMutation({
    mutationFn: async (prompt: string) => {
      const activeSessionId = sessionId ?? (await createWeaverSession(workspaceId, { routePath, mode, context: currentContext(routePath) })).id;
      setSessionId(activeSessionId);
      await sendWeaverMessage(workspaceId, activeSessionId, { prompt, mode, delivery: "Immediate" });
      return activeSessionId;
    },
    onSuccess: async (activeSessionId) => {
      setDraft("");
      await queryClient.invalidateQueries({ queryKey: queryKeys.weaverSession(workspaceId, activeSessionId) });
    }
  });
  const approvePlan = useMutation({
    mutationFn: (input: { planId: string; version: number; decision: "Approved" | "Rejected" }) =>
      approveWeaverPlan(workspaceId, input.planId, { version: input.version, decision: input.decision }),
    onSuccess: async () => {
      if (sessionId)
        await queryClient.invalidateQueries({ queryKey: queryKeys.weaverSession(workspaceId, sessionId) });
    }
  });
  const executePlan = useMutation({
    mutationFn: (input: { planId: string; version: number }) =>
      executeWeaverPlan(workspaceId, input.planId, { version: input.version }),
    onSuccess: async () => {
      if (sessionId)
        await queryClient.invalidateQueries({ queryKey: queryKeys.weaverSession(workspaceId, sessionId) });
    }
  });

  const availableModes = useMemo(() => {
    const configuredModes = configuration.data?.modes ?? [];
    return modeOptions.filter((item) => configuredModes.includes(item.value));
  }, [configuration.data?.modes]);

  useEffect(() => {
    if (availableModes.length > 0 && !availableModes.some((item) => item.value === mode)) {
      setMode(availableModes[0].value);
    }
  }, [availableModes, mode]);

  if (!open)
    return null;

  const unavailableReason = !workspaceId
    ? "No workspace is selected."
    : configuration.isError
      ? errorMessage(configuration.error)
      : configuration.data?.disabledReason;
  const messages = session.data?.messages ?? [];
  const toolCalls = session.data?.toolCalls ?? [];
  const plans = session.data?.plans ?? [];
  const canSend = Boolean(draft.trim() && workspaceId && configuration.data && !configuration.data.disabledReason && !send.isPending);

  return (
    <>
      <div className="fixed inset-0 z-30 bg-background/60 backdrop-blur-sm md:hidden" onClick={onClose} />
      <aside
        aria-label="Weaver assistant"
        className="fixed inset-y-0 right-0 z-40 flex w-full max-w-[28rem] flex-col border-l border-border bg-surface shadow-2xl"
      >
        <div className="flex items-start justify-between gap-3 border-b border-border px-4 py-4">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <Bot aria-hidden className="h-4 w-4 text-primary" />
              <h2 className="font-semibold">Weaver</h2>
            </div>
            <p className="mt-1 truncate text-xs text-muted-foreground">{selectedWorkspace?.name ?? "Workspace assistant"}</p>
          </div>
          <button
            type="button"
            aria-label="Close Weaver assistant"
            className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-ui border border-border bg-background text-muted-foreground hover:bg-muted hover:text-foreground"
            onClick={onClose}
          >
            <X aria-hidden className="h-4 w-4" />
          </button>
        </div>

        <div className="border-b border-border px-4 py-3">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">Context</p>
          <div className="mt-2 flex items-center gap-2 rounded-ui border border-border bg-background px-3 py-2 text-sm">
            <MessageSquare aria-hidden className="h-4 w-4 text-primary" />
            <span className="min-w-0 truncate">{routePath}</span>
          </div>
        </div>

        <div className="flex-1 space-y-3 overflow-y-auto px-4 py-4">
          {unavailableReason ? (
            <div className="rounded-ui border border-border bg-background px-3 py-3 text-sm">
              <p className="font-medium">Unavailable</p>
              <p className="mt-1 text-muted-foreground">{unavailableReason}</p>
              <p className="mt-2 text-xs text-muted-foreground">docs/weaver-configuration.md</p>
            </div>
          ) : null}

          {messages.length === 0 && !unavailableReason ? (
            <MessageBubble
              message={{
                id: "welcome",
                role: "Assistant",
                content: "Ask me to inspect this page or draft a workspace plan.",
                redactionState: "None",
                sequence: 0,
                createdAt: new Date().toISOString()
              }}
            />
          ) : null}

          {messages.map((message) => <MessageBubble key={message.id} message={message} />)}

          {toolCalls.length > 0 ? (
            <div className="rounded-ui border border-border bg-background px-3 py-3">
              <p className="mb-2 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">Tool activity</p>
              <div className="space-y-2">
                {toolCalls.map((toolCall) => <ToolCallRow key={toolCall.id} toolCall={toolCall} />)}
              </div>
            </div>
          ) : null}

          {plans.length > 0 ? (
            <div className="space-y-2">
              <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">Plans</p>
              {plans.map((plan) => (
                <WeaverPlanCard
                  key={plan.id}
                  plan={plan}
                  busy={approvePlan.isPending || executePlan.isPending}
                  onApprove={() => approvePlan.mutate({ planId: plan.id, version: plan.version, decision: "Approved" })}
                  onReject={() => approvePlan.mutate({ planId: plan.id, version: plan.version, decision: "Rejected" })}
                  onExecute={() => executePlan.mutate({ planId: plan.id, version: plan.version })}
                />
              ))}
            </div>
          ) : null}

          {send.isError || approvePlan.isError || executePlan.isError ? (
            <div className="rounded-ui border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
              {errorMessage(send.error ?? approvePlan.error ?? executePlan.error)}
            </div>
          ) : null}
        </div>

        <form
          className="border-t border-border p-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (canSend)
              send.mutate(draft.trim());
          }}
        >
          <div className="mb-3 rounded-ui border border-border bg-background p-3">
            <div className="mb-3 flex items-center gap-2 text-sm font-medium">
              <SlidersHorizontal aria-hidden className="h-4 w-4 text-primary" />
              Steering
            </div>
            <label className="text-xs font-medium text-muted-foreground" htmlFor="weaver-mode">
              Mode
              <Select id="weaver-mode" className="mt-1 w-full" value={mode} onChange={(event) => setMode(event.target.value as WeaverMode)}>
                {(availableModes.length > 0 ? availableModes : modeOptions).map((item) => (
                  <option key={item.value} value={item.value}>{item.label}</option>
                ))}
              </Select>
            </label>
          </div>

          <label className="text-xs font-medium text-muted-foreground" htmlFor="weaver-message">
            Message Weaver
          </label>
          <textarea
            id="weaver-message"
            className="mt-2 min-h-24 w-full resize-none rounded-ui border border-border bg-background px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-60"
            placeholder="Ask Weaver to explain this page or draft a plan."
            value={draft}
            disabled={Boolean(unavailableReason)}
            onChange={(event) => setDraft(event.target.value)}
          />
          <div className="mt-3 flex items-center justify-between gap-3">
            <SecondaryButton type="button" disabled={Boolean(unavailableReason)} onClick={() => setDraft("Summarize the current page and recommended next actions.")}>
              Suggest prompt
            </SecondaryButton>
            <Button disabled={!canSend}>
              <Send aria-hidden className="h-4 w-4" />
              {send.isPending ? "Sending" : "Send"}
            </Button>
          </div>
        </form>
      </aside>
    </>
  );
}

function MessageBubble({ message }: { message: WorkspaceWeaverMessage }) {
  const isAssistant = message.role === "Assistant";
  return (
    <div
      className={cn(
        "rounded-ui border px-3 py-2 text-sm",
        isAssistant ? "border-border bg-background text-foreground" : "ml-8 border-primary/30 bg-primary/10 text-foreground"
      )}
    >
      <p className="mb-1 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
        {isAssistant ? "Weaver" : "You"}
      </p>
      <p className="break-words">{message.content}</p>
      {message.redactionState === "Redacted" ? <p className="mt-2 text-xs text-muted-foreground">Contains redactions</p> : null}
    </div>
  );
}

function ToolCallRow({ toolCall }: { toolCall: WorkspaceWeaverToolCall }) {
  return (
    <div className="rounded-ui border border-border px-2 py-2 text-sm">
      <div className="flex items-center justify-between gap-2">
        <span className="min-w-0 truncate font-medium">{toolCall.toolName}</span>
        <span className="shrink-0 text-xs text-muted-foreground">{toolCall.status}</span>
      </div>
      {toolCall.resultSummaryJson ? <p className="mt-1 break-words text-xs text-muted-foreground">{toolSummary(toolCall.resultSummaryJson)}</p> : null}
    </div>
  );
}

function toolSummary(value: string) {
  try {
    const parsed = JSON.parse(value) as { summary?: string };
    return parsed.summary ?? value;
  } catch {
    return value;
  }
}

function currentContext(routePath: string) {
  return { routePath };
}

function errorMessage(error: unknown) {
  if (error instanceof ApiError)
    return error.message;
  if (error instanceof Error)
    return error.message;
  return "Weaver is unavailable.";
}
