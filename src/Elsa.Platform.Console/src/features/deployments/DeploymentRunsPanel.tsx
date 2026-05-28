import { History } from "lucide-react";
import { Badge, Table } from "@/components/ui";
import {
  engineLabel,
  environmentLabel,
  type DeploymentCockpit,
  type DeploymentStatus,
  type WorkspaceDeploymentRunStatus
} from "@/features/deployments/deploymentModels";
import { formatDateTime } from "@/lib/formatters";
import { statusToneClass, type StatusTone } from "@/lib/status/statusBadges";

type DeploymentRunsPanelProps = {
  data: DeploymentCockpit;
};

export function DeploymentRunsPanel({ data }: DeploymentRunsPanelProps) {
  const latestRun = data.history[0];

  return (
    <section className="space-y-3 rounded-ui border border-border bg-surface p-4">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <h3 className="flex items-center gap-2 text-sm font-semibold"><History className="h-4 w-4" />Run history</h3>
        {latestRun ? <StatusBadge value={`Latest ${latestRun.status}`} tone={deploymentTone(latestRun.status)} /> : null}
      </div>
      {data.history.length === 0 ? (
        <p className="rounded-ui border border-dashed border-border px-3 py-6 text-center text-sm text-muted-foreground">
          No deployment runs have been queued for this workspace.
        </p>
      ) : (
        <Table>
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
              <tr>
                <th className="px-3 py-2">Revision</th>
                <th className="px-3 py-2">Status</th>
                <th className="px-3 py-2">Actor</th>
                <th className="px-3 py-2">Target</th>
                <th className="px-3 py-2">Validation</th>
                <th className="px-3 py-2">When</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {data.history.map((event) => {
                const environment = findEnvironment(event.environmentId, data.applications);
                return (
                  <tr key={event.id}>
                    <td className="px-3 py-3">
                      r{event.revision}
                      {event.rollbackSourceRevision ? <div className="text-xs text-muted-foreground">from r{event.rollbackSourceRevision}</div> : null}
                    </td>
                    <td className="px-3 py-3"><StatusBadge value={event.status} tone={deploymentTone(event.status)} /></td>
                    <td className="px-3 py-3">{event.actor}</td>
                    <td className="px-3 py-3 text-muted-foreground">
                      {environment?.name ?? environmentLabel(event.environmentId, data.applications)} / {engineLabel(event.engineId, data.engines)}
                      <div className="text-xs">{environment?.tierName ?? environment?.tier ?? "Unknown tier"}</div>
                    </td>
                    <td className="px-3 py-3">{event.validationOutcome}</td>
                    <td className="px-3 py-3">{formatDateTime(event.occurredAt)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </Table>
      )}
    </section>
  );
}

function findEnvironment(environmentId: string, applications: DeploymentCockpit["applications"]) {
  for (const application of applications) {
    const environment = application.environments.find((item) => item.id === environmentId);
    if (environment) return environment;
  }
  return undefined;
}

function StatusBadge({ value, tone }: { value: string; tone: StatusTone }) {
  return <Badge className={statusToneClass(tone)}>{value}</Badge>;
}

function deploymentTone(status: DeploymentStatus | WorkspaceDeploymentRunStatus): StatusTone {
  if (status === "Succeeded") return "success";
  if (status === "Running" || status === "RolledBack" || status === "Queued" || status === "RecoveryRequired") return "warning";
  if (status === "Cancelled") return "neutral";
  return "destructive";
}
