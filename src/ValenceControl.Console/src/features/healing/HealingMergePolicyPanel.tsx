import { useState } from "react";
import { Badge, Button, SecondaryButton } from "@/components/ui";
import type { HealingPermission, HealingRepairAttemptView } from "@/features/healing/healingModels";

export function HealingMergePolicyPanel({ attempts, permissions, pending = false, onRetry, onStop }: {
  attempts: HealingRepairAttemptView[];
  permissions: HealingPermission[];
  pending?: boolean;
  onRetry: () => void;
  onStop: () => void;
}) {
  const [confirmStop, setConfirmStop] = useState(false);
  const pullRequest = attempts.find((attempt) => attempt.pullRequest)?.pullRequest;
  const gates = pullRequest?.mergeGates ?? [];
  const blockers = gates.filter((gate) => gate.state.toLowerCase() !== "pass");
  const canRetry = permissions.includes("healing.repair.retry");
  const canStop = permissions.includes("healing.repair.stop");

  return <section className="space-y-4 rounded-ui border border-border bg-surface p-4" aria-label="Merge policy and repair controls">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h2 className="font-medium">Merge policy</h2><p className="mt-1 text-sm text-muted-foreground">Human merge remains available in the provider. Automatic merge requires every gate below to pass.</p></div>
      <Badge>{humanize(pullRequest?.autoMergeDecision ?? "Not evaluated")}</Badge>
    </div>
    {gates.length === 0 ? <p className="text-sm text-muted-foreground">No automatic-merge evaluation has been recorded.</p> :
      <ul className="grid gap-2 sm:grid-cols-2" aria-label="Automatic merge gates">{gates.map((gate) =>
        <li key={gate.gate} className={`rounded-ui border p-3 text-sm ${gate.state.toLowerCase() === "pass" ? "border-success/30" : "border-destructive/40 bg-destructive/5"}`}>
          <div className="flex items-center justify-between gap-2"><span className="font-medium">{humanize(gate.gate)}</span><Badge>{humanize(gate.state)}</Badge></div>
          <p className="mt-1 text-xs text-muted-foreground">{humanize(gate.reasonCode)}</p>
        </li>)}</ul>}
    {blockers.length > 0 ? <p role="alert" className="text-sm text-destructive">Automatic merge blocked by {blockers.length} required {blockers.length === 1 ? "gate" : "gates"}. Blockers cannot be waived from this panel.</p> : null}
    <div className="border-t border-border pt-4">
      <h3 className="text-sm font-medium">Human commands</h3>
      <div className="mt-3 flex flex-wrap gap-2">
        <Button type="button" disabled={!canRetry || pending} onClick={onRetry}>Retry repair</Button>
        <SecondaryButton type="button" disabled={!canStop || pending} onClick={() => setConfirmStop(true)}>Stop repair</SecondaryButton>
      </div>
      {!canRetry || !canStop ? <p className="mt-2 text-xs text-muted-foreground">Controls require their exact healing.repair permission. Provider comments also require a verified identity link and repository role.</p> : null}
    </div>
    {confirmStop ? <div role="dialog" aria-modal="true" aria-labelledby="healing-stop-title" className="rounded-ui border border-destructive/40 bg-background p-4">
      <h3 id="healing-stop-title" className="font-medium">Stop this repair?</h3>
      <p className="mt-2 text-sm text-muted-foreground">A one-use, incident-bound server confirmation will be created before the command executes.</p>
      <div className="mt-4 flex justify-end gap-2"><SecondaryButton type="button" onClick={() => setConfirmStop(false)}>Cancel</SecondaryButton><Button type="button" disabled={pending} onClick={() => { setConfirmStop(false); onStop(); }}>Confirm stop</Button></div>
    </div> : null}
  </section>;
}

function humanize(value: string) {
  return value.replaceAll(/[._-]+/g, " ").replaceAll(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/^./, (character) => character.toUpperCase());
}
