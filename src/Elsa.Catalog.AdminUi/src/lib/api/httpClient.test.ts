import { afterEach, describe, expect, it, vi } from "vitest";
import { apiRequest } from "@/lib/api/httpClient";

describe("apiRequest", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("surfaces API validation error arrays", async () => {
    stubErrorResponse({
      errors: [
        "Polling interval must be an ISO 8601 duration, for example PT30M.",
        "At least one include pattern is required."
      ]
    });

    await expect(apiRequest("/api/admin/sources")).rejects.toMatchObject({
      kind: "Validation",
      message: "Polling interval must be an ISO 8601 duration, for example PT30M. | At least one include pattern is required."
    });
  });

  it("surfaces ASP.NET model-state validation errors", async () => {
    stubErrorResponse({
      errors: {
        Name: ["The Name field is required."],
        Url: ["The Url field is not a valid fully-qualified URI."]
      }
    });

    await expect(apiRequest("/api/admin/sources")).rejects.toMatchObject({
      kind: "Validation",
      message: "The Name field is required. | The Url field is not a valid fully-qualified URI."
    });
  });
});

function stubErrorResponse(body: unknown) {
  vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify(body), {
    status: 400,
    statusText: "Bad Request",
    headers: { "Content-Type": "application/json" }
  })));
}
