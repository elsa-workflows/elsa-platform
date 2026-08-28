import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api/httpClient";
import { queryClient, queryKeys } from "@/lib/query/queryClient";

describe("queryClient", () => {
  afterEach(() => {
    queryClient.clear();
    vi.restoreAllMocks();
  });

  it("refreshes the auth session when any request comes back unauthorized", async () => {
    const invalidateQueries = vi.spyOn(queryClient, "invalidateQueries");

    await expectRejection(new ApiError("Unauthorized", "Session expired.", 401));

    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: queryKeys.authSession });
  });

  it("leaves the auth session alone for failures that are not about authentication", async () => {
    const invalidateQueries = vi.spyOn(queryClient, "invalidateQueries");

    await expectRejection(new ApiError("Unavailable", "Try later.", 503));

    expect(invalidateQueries).not.toHaveBeenCalled();
  });

  it("does not retry an unauthorized request, so the redirect is not delayed", async () => {
    const queryFn = vi.fn(() => Promise.reject(new ApiError("Unauthorized", "Session expired.", 401)));

    await expectRejection(queryFn);

    expect(queryFn).toHaveBeenCalledTimes(1);
  });

  it("still retries a transient failure once", async () => {
    const queryFn = vi.fn(() => Promise.reject(new ApiError("Unavailable", "Try later.", 503)));

    await expectRejection(queryFn);

    expect(queryFn).toHaveBeenCalledTimes(2);
  });
});

let queryCounter = 0;

async function expectRejection(failure: ApiError | (() => Promise<never>)) {
  const queryFn = typeof failure === "function" ? failure : () => Promise.reject(failure);
  await expect(queryClient.fetchQuery({
    queryKey: ["queryClient-test", queryCounter++],
    queryFn,
    retryDelay: 0
  })).rejects.toBeInstanceOf(ApiError);
}
