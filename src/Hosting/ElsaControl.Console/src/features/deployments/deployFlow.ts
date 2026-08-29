import { useCallback, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  createActionConfirmation,
  getDeploymentRun,
  getRevisionDeployability,
  queueDeploymentRun
} from "@/features/deployments/deploymentApi";
import type {
  DeploymentDeployabilityResult,
  WorkspaceDeploymentRun,
  WorkspaceDeploymentRunStatus,
  WorkspaceDesiredStateRecordRequest
} from "@/features/deployments/deploymentModels";
import { artifactDisplayName, type WorkspaceArtifact } from "@/features/artifacts/artifactModels";

/**
 * The user-facing phases of the "deploy this revision" chain. The confirmation step is an
 * internal ceremony (mint a single-use token, then queue the run against it) and is deliberately
 * collapsed into a single "deploying" phase so the console never surfaces "confirmation" as a
 * concept the operator has to reason about.
 */
export type DeployChainPhase = "idle" | "checking" | "deploying" | "queued" | "failed";

export type DeployChainInput = {
  revisionId: string;
  targetEnvironmentId: string;
  targetEngineId: string;
  /** When false, deployability is checked first and a blocked result aborts before minting a run. */
  skipDeployabilityCheck?: boolean;
};

export type DeployChainResult = {
  run: WorkspaceDeploymentRun;
  deployability: DeploymentDeployabilityResult | null;
};

/**
 * Runs the full deploy chain client-side behind a single action: (optional) deployability check ->
 * mint confirmation -> queue run. Shared by the setup wizard's final step and the promote-and-deploy
 * affordance so the confirm+run ceremony lives in exactly one place.
 */
export async function runDeployChain(workspaceId: string, input: DeployChainInput): Promise<DeployChainResult> {
  let deployability: DeploymentDeployabilityResult | null = null;

  if (!input.skipDeployabilityCheck) {
    deployability = await getRevisionDeployability(workspaceId, input.revisionId, input.targetEnvironmentId, input.targetEngineId);
    if (!deployability.canDeploy) {
      throw new DeployBlockedError(deployability);
    }
  }

  const confirmation = await createActionConfirmation(workspaceId, {
    actionType: "Deploy",
    targetId: input.revisionId,
    lifetimeSeconds: null
  });

  const run = await queueDeploymentRun(workspaceId, {
    sourceRevisionId: input.revisionId,
    targetEnvironmentId: input.targetEnvironmentId,
    targetEngineId: input.targetEngineId,
    confirmationId: confirmation.id,
    mode: "Apply"
  });

  return { run, deployability };
}

/** Raised when a deployability check blocks the deploy chain before any run is queued. */
export class DeployBlockedError extends Error {
  constructor(public readonly deployability: DeploymentDeployabilityResult) {
    super(deployBlockedMessage(deployability));
    this.name = "DeployBlockedError";
  }
}

function deployBlockedMessage(deployability: DeploymentDeployabilityResult) {
  const blocker = deployability.blockers.find((item) => item.severity === "Blocker") ?? deployability.blockers[0];
  return blocker
    ? `Deployment is blocked: ${blocker.message}`
    : "Deployment is blocked by an unmet requirement.";
}

const terminalRunStatuses: ReadonlySet<WorkspaceDeploymentRunStatus> = new Set([
  "Succeeded",
  "Failed",
  "Blocked",
  "Cancelled",
  "RolledBack",
  "RecoveryRequired"
]);

export function isRunTerminal(status: WorkspaceDeploymentRunStatus) {
  return terminalRunStatuses.has(status);
}

export function isRunInFlight(status: WorkspaceDeploymentRunStatus) {
  return status === "Queued" || status === "Running";
}

/**
 * Polls a queued run until it reaches a terminal status. Reused by the wizard end-state screen and
 * anywhere else that wants a live "is it running yet" readout without re-implementing the polling
 * cadence.
 */
export function useRunStatus(workspaceId: string, runId: string | null) {
  const query = useQuery({
    queryKey: ["deployments", workspaceId, "runs", runId ?? "none", "status"],
    queryFn: () => getDeploymentRun(workspaceId, runId as string),
    enabled: Boolean(workspaceId && runId),
    refetchInterval: (query) => {
      const status = query.state.data?.run.status;
      return status && isRunTerminal(status) ? false : 3_000;
    }
  });

  return {
    run: query.data?.run ?? null,
    history: query.data?.history ?? [],
    commands: query.data?.commands ?? [],
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error instanceof Error ? query.error : null
  };
}

/**
 * Drives the deploy chain as local component state with collapsed user-facing phases. Returned
 * `start` resolves to the queued run (or throws) so callers can chain navigation/toast logic.
 */
export function useDeployChain(workspaceId: string) {
  const [phase, setPhase] = useState<DeployChainPhase>("idle");
  const [run, setRun] = useState<WorkspaceDeploymentRun | null>(null);
  const [deployability, setDeployability] = useState<DeploymentDeployabilityResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reset = useCallback(() => {
    setPhase("idle");
    setRun(null);
    setDeployability(null);
    setError(null);
  }, []);

  const start = useCallback(
    async (input: DeployChainInput) => {
      setError(null);
      setDeployability(null);
      setRun(null);
      setPhase(input.skipDeployabilityCheck ? "deploying" : "checking");
      try {
        if (!input.skipDeployabilityCheck) {
          const result = await getRevisionDeployability(workspaceId, input.revisionId, input.targetEnvironmentId, input.targetEngineId);
          setDeployability(result);
          if (!result.canDeploy) {
            throw new DeployBlockedError(result);
          }
        }
        setPhase("deploying");
        const chain = await runDeployChain(workspaceId, { ...input, skipDeployabilityCheck: true });
        setRun(chain.run);
        setPhase("queued");
        return chain.run;
      } catch (ex) {
        if (ex instanceof DeployBlockedError) {
          setDeployability(ex.deployability);
        }
        setPhase("failed");
        setError(ex instanceof Error ? ex.message : "Deployment could not be started.");
        throw ex;
      }
    },
    [workspaceId]
  );

  return { phase, run, deployability, error, isBusy: phase === "checking" || phase === "deploying", start, reset };
}

/** Human label for the collapsed deploy phase, used by progress affordances. */
export function deployPhaseLabel(phase: DeployChainPhase) {
  switch (phase) {
    case "checking":
      return "Checking deployability";
    case "deploying":
      return "Deploying";
    case "queued":
      return "Deployment queued";
    case "failed":
      return "Deployment failed";
    default:
      return "Idle";
  }
}

/** Human label for a run status, shared by run-status readouts. */
export function runStatusLabel(status: WorkspaceDeploymentRunStatus) {
  switch (status) {
    case "RecoveryRequired":
      return "Recovery required";
    case "RolledBack":
      return "Rolled back";
    default:
      return status;
  }
}

// --- Desired-state record builders --------------------------------------------------------------
// Extracted from DeploymentsPage so the setup wizard and the revision-create page build the exact
// same records from an artifact selection.

export { artifactDisplayName } from "@/features/artifacts/artifactModels";

export function artifactRevisionRecord(artifact: WorkspaceArtifact): WorkspaceDesiredStateRecordRequest {
  return {
    kind: "ArtifactReference",
    name: artifactDisplayName(artifact),
    payload: {
      artifactRecordId: artifact.id,
      artifactId: artifact.artifactId,
      artifactTypeId: artifact.artifactTypeId ?? "elsa.workflow-definition",
      contentDigest: artifact.contentDigest,
      metadata: artifact.displayMetadata?.labels ?? {}
    }
  };
}
