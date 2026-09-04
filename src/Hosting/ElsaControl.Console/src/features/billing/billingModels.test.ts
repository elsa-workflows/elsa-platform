import { describe, expect, it } from "vitest";
import { getBillingStateMeta } from "@/features/billing/billingModels";

describe("billing lifecycle presentation", () => {
  it.each([
    ["Trial", "Trial"],
    ["Active", "Active"],
    ["PastDue", "Past due"],
    ["Constrained", "Constrained"],
    ["Suspended", "Suspended"],
    ["Retained", "Retained"],
    ["Deleted", "Closed"]
  ] as const)("presents %s as %s", (state, label) => {
    expect(getBillingStateMeta(state).label).toBe(label);
  });

  it("fails safely for missing or unknown lifecycle state", () => {
    expect(getBillingStateMeta(null).label).toBe("Not started");
    expect(getBillingStateMeta("future-state").label).toBe("Needs review");
  });
});
