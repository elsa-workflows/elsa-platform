import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { HealingMergePolicyPanel } from "@/features/healing/HealingMergePolicyPanel";
import type { HealingRepairAttemptView } from "@/features/healing/healingModels";

describe("HealingMergePolicyPanel", () => {
  afterEach(cleanup);

  it("renders every failed, unknown, and passing gate as an explicit decision", () => {
    render(<HealingMergePolicyPanel attempts={[attempt]} permissions={[]} onRetry={vi.fn()} onStop={vi.fn()} />);

    expect(screen.getByText(/Human only/i)).toBeInTheDocument();
    expect(screen.getByText("Producing revision")).toBeInTheDocument();
    expect(screen.getByText("Reproduction")).toBeInTheDocument();
    expect(screen.getByText("Required checks")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("blocked by 2 required gates");
    expect(screen.getByRole("button", { name: "Retry repair" })).toBeDisabled();
  });

  it("requires an explicit stop confirmation and exposes authorized retry", async () => {
    const onRetry = vi.fn();
    const onStop = vi.fn();
    render(<HealingMergePolicyPanel attempts={[attempt]} permissions={["healing.repair.retry", "healing.repair.stop"]} onRetry={onRetry} onStop={onStop} />);

    await userEvent.click(screen.getByRole("button", { name: "Retry repair" }));
    expect(onRetry).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole("button", { name: "Stop repair" }));
    expect(onStop).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toHaveTextContent("one-use, incident-bound server confirmation");
    await userEvent.click(screen.getByRole("button", { name: "Confirm stop" }));
    expect(onStop).toHaveBeenCalledOnce();
  });
});

const attempt = {
  id: "attempt-1", attemptNumber: 1, status: "PullRequestOpen", targetRevision: "a".repeat(40), producingRevision: "b".repeat(40),
  evidence: { tier: "DefaultRedacted", omittedFields: [] }, classification: "Reproduced", confidence: 0.99,
  reproduction: { wasAttempted: true, wasReproduced: true, classification: "reproduced", summary: "Reproduced." }, validations: [],
  pullRequest: {
    number: 12, url: "https://github.com/acme/app/pull/12", isDraft: false, mergeState: "Open", checksState: "Passed",
    autoMergeDecision: "HumanOnly",
    mergeGates: [
      { gate: "producing-revision", state: "Pass", reasonCode: "producing-revision-verified" },
      { gate: "reproduction", state: "Block", reasonCode: "reproduction-blocked" },
      { gate: "required-checks", state: "Unknown", reasonCode: "required-checks-missing" }
    ]
  }
} satisfies HealingRepairAttemptView;
