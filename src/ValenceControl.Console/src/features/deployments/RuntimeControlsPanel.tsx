import { RefreshCw } from "lucide-react";
import { Badge, SecondaryButton } from "@/components/ui";
import {
  supportedControlIds,
  type RuntimeControl,
  type WorkflowEngineRegistration
} from "@/features/deployments/deploymentModels";

type RuntimeControlsPanelProps = {
  engine: WorkflowEngineRegistration;
  canExecuteControls: boolean;
  isRunning: boolean;
  notice: string;
  error?: string;
  onRunControl: (control: RuntimeControl) => void;
};

export function RuntimeControlsPanel({
  engine,
  canExecuteControls,
  isRunning,
  notice,
  error,
  onRunControl
}: RuntimeControlsPanelProps) {
  const controls = supportedControlIds(engine);

  return (
    <div className="space-y-2">
      {controls.map((control) => (
        <div key={control.id} className="flex flex-col gap-2 rounded-ui border border-border p-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <div className="font-medium">{control.label}</div>
            <div className="text-xs text-muted-foreground">{control.boundary} boundary · {control.description}</div>
          </div>
          <SecondaryButton
            className="h-8 shrink-0"
            disabled={!canExecuteControls || isRunning || engine.health === "Unreachable"}
            onClick={() => onRunControl(control)}
          >
            <RefreshCw className="h-4 w-4" />
            Run
          </SecondaryButton>
        </div>
      ))}
      <p className="text-xs text-muted-foreground">Unavailable controls stay hidden unless a matching engine or hosting capability is advertised.</p>
      {engine.health === "Unreachable" ? <p className="text-xs text-muted-foreground">Verify the engine or wait for a fresh heartbeat before running controls.</p> : null}
      {!canExecuteControls ? <p className="text-xs text-muted-foreground">Runtime control permission is required.</p> : null}
      {notice ? <div role="status" className="rounded-ui border border-primary/30 bg-primary/10 px-3 py-2 text-sm">{notice}</div> : null}
      {error ? <div role="alert" className="rounded-ui border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{error}</div> : null}
      {isRunning ? <Badge>Running control</Badge> : null}
    </div>
  );
}
