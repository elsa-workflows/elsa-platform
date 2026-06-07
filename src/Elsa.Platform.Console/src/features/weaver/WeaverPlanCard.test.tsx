import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { WeaverPlanCard } from "@/features/weaver/WeaverPlanCard";
import type { WorkspaceWeaverPlan } from "@/features/weaver/weaverModels";

describe("WeaverPlanCard", () => {
  it("renders plan review actions", async () => {
    const onApprove = vi.fn();
    const onReject = vi.fn();

    render(<WeaverPlanCard plan={planFixture()} onApprove={onApprove} onReject={onReject} />);

    expect(screen.getByText("Draft promotion plan")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Approve" }));
    await userEvent.click(screen.getByRole("button", { name: "Reject" }));

    expect(onApprove).toHaveBeenCalledOnce();
    expect(onReject).toHaveBeenCalledOnce();
  });

  it("renders execute action for approved plans", async () => {
    const onExecute = vi.fn();

    render(<WeaverPlanCard plan={{ ...planFixture(), status: "Approved" }} onExecute={onExecute} />);

    await userEvent.click(screen.getByRole("button", { name: "Execute" }));

    expect(onExecute).toHaveBeenCalledOnce();
  });
});

function planFixture(): WorkspaceWeaverPlan {
  return {
    id: "plan-1",
    version: 1,
    planType: "Promotion",
    title: "Draft promotion plan",
    summary: "Promote Production.",
    targetJson: "{\"environment\":\"Production\"}",
    impactJson: "{\"changes\":\"No mutation until approval\"}",
    validationJson: "{\"status\":\"Requires review\"}",
    rollbackJson: "{\"path\":\"Previous revision\"}",
    risk: "Medium",
    status: "ReadyForApproval",
    createdAt: "2026-06-07T12:00:00Z",
    updatedAt: "2026-06-07T12:00:00Z"
  };
}
