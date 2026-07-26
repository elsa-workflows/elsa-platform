import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { HealingRepairPanel } from "@/features/healing/HealingRepairPanel";
import type { HealingRepairAttemptView } from "@/features/healing/healingModels";

describe("HealingRepairPanel", () => {
  afterEach(cleanup);

  it("shows bounded attempt, evidence, reproduction, validation, and pull-request state", () => {
    render(<HealingRepairPanel attempts={[attemptFixture]} />);

    const attempt = screen.getByRole("article", { name: "Repair attempt 1" });
    expect(within(attempt).getByText("Running")).toBeInTheDocument();
    expect(within(attempt).getByText("Default Redacted")).toBeInTheDocument();
    expect(within(attempt).getByText("Exception message omitted")).toBeInTheDocument();
    expect(within(attempt).getByText("Attempted—not reproduced")).toBeInTheDocument();
    expect(within(attempt).getByText("93% confidence")).toBeInTheDocument();
    expect(within(attempt).getByText("Focused tests passed")).toBeInTheDocument();
    expect(within(attempt).getByRole("link", { name: "Open pull request #84" })).toHaveAttribute(
      "href",
      "https://github.com/acme/orders/pull/84"
    );
    expect(screen.getByRole("alert")).toHaveTextContent("Human merge required");
    expect(screen.queryByText("ghs_provider_write_token")).not.toBeInTheDocument();
    expect(screen.queryByText("diff --git")).not.toBeInTheDocument();
  });

  it("states when reproduction was not attempted and rejects unsafe provider links", () => {
    render(<HealingRepairPanel attempts={[{
      ...attemptFixture,
      classification: "RevisionUnverified",
      reproduction: {
        wasAttempted: false,
        wasReproduced: false,
        classification: "not-attempted",
        summary: "The producing revision could not be resolved."
      },
      pullRequest: { ...attemptFixture.pullRequest!, url: "javascript:alert(1)" }
    }]} />);

    expect(screen.getByText("Not attempted")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Open pull request/ })).not.toBeInTheDocument();
    expect(screen.getByText("Provider link unavailable")).toBeInTheDocument();
  });

  it("renders an explicit empty state", () => {
    render(<HealingRepairPanel attempts={[]} />);
    expect(screen.getByRole("heading", { name: "No repair attempts" })).toBeInTheDocument();
  });
});

const attemptFixture = {
  id: "attempt-1",
  attemptNumber: 1,
  status: "Running",
  targetRevision: "target-def",
  producingRevision: "producing-abc",
  evidence: {
    tier: "DefaultRedacted",
    omittedFields: ["exception.message"],
    expiresAt: "2026-07-16T12:30:00Z"
  },
  classification: "InferredHighConfidence",
  confidence: 0.93,
  causalSummary: "A null guard is missing from the order projection.",
  reproduction: {
    wasAttempted: true,
    wasReproduced: false,
    classification: "not-reproduced",
    summary: "The original fixture was unavailable."
  },
  validations: [{ kind: "test", outcome: "passed", safeSummary: "Focused tests passed" }],
  pullRequest: {
    number: 84,
    url: "https://github.com/acme/orders/pull/84",
    isDraft: true,
    mergeState: "Open",
    checksState: "Pending",
    autoMergeDecision: "HumanOnly",
    mergeGates: []
  },
  providerCredential: "ghs_provider_write_token",
  unifiedDiff: "diff --git"
} satisfies HealingRepairAttemptView & { providerCredential: string; unifiedDiff: string };
