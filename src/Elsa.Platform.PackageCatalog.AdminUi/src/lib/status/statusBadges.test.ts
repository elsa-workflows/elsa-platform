import { describe, expect, it } from "vitest";
import { sourceStatusTone } from "@/lib/status/statusBadges";

describe("sourceStatusTone", () => {
  it("treats unsupported schemas as destructive validation states", () => {
    expect(sourceStatusTone("UnsupportedSchema")).toBe("destructive");
  });

  it("treats blocking review reasons as destructive states", () => {
    expect(sourceStatusTone("Blocking")).toBe("destructive");
  });

  it("treats syncing as an in-progress warning state", () => {
    expect(sourceStatusTone("Syncing")).toBe("warning");
  });
});
