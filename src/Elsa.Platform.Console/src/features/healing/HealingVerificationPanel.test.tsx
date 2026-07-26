import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { HealingVerificationPanel } from "@/features/healing/HealingVerificationPanel";
import type { HealingEnvironmentImpact } from "@/features/healing/healingModels";

describe("HealingVerificationPanel", () => {
  afterEach(cleanup);

  it("keeps each environment independent and distinguishes deployment from positive healing", () => {
    render(<HealingVerificationPanel
      incidentStatus="Verifying"
      impacts={[
        impact("environment-development", "Healed", "fixed-sha"),
        impact("environment-production", "DeployedUnverified", "fixed-sha")
      ]}
      observations={[
        observation("environment-development", "development-deploy"),
        observation("environment-production", "production-deploy")
      ]}
      results={[
        result("environment-development", "Healed", 3, 0),
        result("environment-production", "DeployedUnverified", 0, 0)
      ]}
      permissions={[]}
    />);

    expect(screen.getAllByText("Healed").length).toBeGreaterThan(0);
    expect(screen.getByText("Deployed—unverified")).toBeInTheDocument();
    expect(screen.getAllByText("Repair merged")).toHaveLength(2);
    expect(screen.getAllByText(/Revision deployed/)).toHaveLength(2);
    expect(screen.getByText(/No-traffic silence never proves healing/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Waive environment verification" })).not.toBeInTheDocument();
  });

  it("shows recurrence failure and requires an explicit reason before a permitted waiver", async () => {
    const onWaive = vi.fn();
    render(<HealingVerificationPanel
      incidentStatus="Verifying"
      impacts={[impact("environment-production", "DeployedUnverified", "fixed-sha")]}
      observations={[observation("environment-production", "production-deploy")]}
      results={[result("environment-production", "DeployedUnverified", 0, 1)]}
      permissions={["healing.verification.waive"]}
      onWaive={onWaive}
    />);

    expect(screen.getByText("Matching recurrence detected")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Waive environment verification" }));
    const confirm = screen.getByRole("button", { name: "Confirm terminal waiver" });
    expect(confirm).toBeDisabled();
    await userEvent.type(screen.getByLabelText("Waiver reason"), "Environment retired after migration");
    await userEvent.click(confirm);
    expect(onWaive).toHaveBeenCalledWith("environment-production", "Environment retired after migration");
  });
});

const episodeId = "00000000-0000-0000-0000-000000000001";

function impact(environmentId: string, verificationStatus: string, currentDeployedRevision: string): HealingEnvironmentImpact {
  return {
    episodeId,
    environmentId,
    firstSeenAt: "2026-07-16T08:00:00Z",
    lastSeenAt: "2026-07-16T09:00:00Z",
    occurrenceCount: 2,
    producingRevisions: ["broken-sha"],
    currentDeployedRevision,
    verificationStatus,
    occurrenceThreshold: 1,
    debounceWindow: "00:00:00"
  };
}

function observation(environmentId: string, sourceObservationId: string) {
  return {
    id: crypto.randomUUID(), environmentId, revision: "fixed-sha",
    deployedAt: "2026-07-16T10:00:00Z", source: "ExternalDelivery",
    sourceObservationId, acceptedAt: "2026-07-16T10:00:01Z"
  };
}

function result(environmentId: string, outcome: string, successes: number, recurrences: number) {
  return {
    id: crypto.randomUUID(), episodeId, environmentId, repairedRevision: "fixed-sha",
    windowStartedAt: "2026-07-16T10:00:00Z", windowEndsAt: "2026-07-16T11:00:00Z",
    relevantOperationSuccessCount: successes,
    lastRelevantOperationSuccessAt: successes ? "2026-07-16T10:10:00Z" : null,
    recurrenceCount: recurrences,
    lastRecurrenceAt: recurrences ? "2026-07-16T10:20:00Z" : null,
    outcome,
    decidedAt: outcome === "Healed" ? "2026-07-16T11:00:00Z" : null,
    decisionReason: null,
    waiverExpiresAt: null
  };
}
