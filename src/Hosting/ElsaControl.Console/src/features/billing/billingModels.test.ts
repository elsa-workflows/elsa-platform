import { describe, expect, it } from "vitest";
import { getBillingStateMeta } from "@/features/billing/billingModels";
import { trustedBillingSessionUrl } from "@/features/billing/billingNavigation";

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

describe("billing session navigation", () => {
  it("accepts secure remote and local development URLs", () => {
    expect(trustedBillingSessionUrl("https://billing.example.test/session?id=1").protocol).toBe("https:");
    expect(trustedBillingSessionUrl("http://localhost:4242/session").hostname).toBe("localhost");
  });

  it.each([
    "javascript:alert(1)",
    "data:text/html,unsafe",
    "http://billing.example.test/session",
    "https://user:password@billing.example.test/session",
    "/relative/session"
  ])("rejects unsafe session URL %s", (value) => {
    expect(() => trustedBillingSessionUrl(value)).toThrow("billing session URL is unavailable");
  });
});
