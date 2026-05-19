import { describe, expect, it } from "vitest";
import { isPackageIncluded, previewPatterns } from "@/features/sources/patternTester";

describe("source pattern tester", () => {
  it("uses case-insensitive include and exclude glob precedence", () => {
    expect(isPackageIncluded("elsa.Persistence.PostgreSql", ["Elsa.*"], [])).toBe(true);
    expect(isPackageIncluded("Elsa.Tests", ["Elsa.*"], ["*.Tests"])).toBe(false);
  });

  it("previews each package id with the same inclusion result", () => {
    const preview = previewPatterns(["Elsa.*"], ["*.Abstractions"], [
      "Elsa.Messaging.RabbitMQ",
      "Elsa.Abstractions",
      "Other.Package"
    ]);

    expect(preview).toEqual([
      { packageId: "Elsa.Messaging.RabbitMQ", included: true },
      { packageId: "Elsa.Abstractions", included: false },
      { packageId: "Other.Package", included: false }
    ]);
  });
});
