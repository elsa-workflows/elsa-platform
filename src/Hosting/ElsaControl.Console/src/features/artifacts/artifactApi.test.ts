import { afterEach, describe, expect, it, vi } from "vitest";
import {
  completeArtifactUpload,
  createArtifactUpload,
  getArtifactUploadCapabilities,
  listWorkspaceArtifacts,
  uploadArtifactContent,
  workspaceArtifactDownloadUrl
} from "@/features/artifacts/artifactApi";

afterEach(() => vi.unstubAllGlobals());

describe("artifact API", () => {
  it("uses workspace-scoped list and capability endpoints", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = input instanceof Request ? input.url : input.toString();
      if (url.endsWith("/artifacts")) return Response.json({ items: [] });
      return Response.json({ maxUploadBytes: 1000, sampleArtifactGenerationEnabled: false });
    });
    vi.stubGlobal("fetch", fetchMock);

    await listWorkspaceArtifacts("workspace/1");
    await getArtifactUploadCapabilities("workspace/1");

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "/api/workspaces/workspace%2F1/artifacts",
      expect.objectContaining({ credentials: "same-origin" })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "/api/workspaces/workspace%2F1/artifact-uploads/capabilities",
      expect.objectContaining({ credentials: "same-origin" })
    );
    expect(workspaceArtifactDownloadUrl("workspace/1", "artifact/1")).toBe("/api/workspaces/workspace%2F1/artifacts/artifact%2F1/download");
  });

  it("creates a session, uploads opaque content, and completes it", async () => {
    const uploadId = "upload-1";
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : input.toString();
      if (url.endsWith("/artifact-uploads")) return Response.json({ uploadId, status: "Pending", expiresAt: "2026-08-29T09:00:00Z", maxUploadBytes: 1000 }, { status: 201 });
      if (url.endsWith("/content")) return new Response(null, { status: 204 });
      return Response.json({ uploadId, status: "Completed", artifact: null, created: false, diagnostics: [] });
    });
    vi.stubGlobal("fetch", fetchMock);

    await createArtifactUpload("workspace-1", { fileName: "recipe.zip", contentType: "application/zip", sizeBytes: 3, idempotencyKey: "recipe-1" });
    const progress: number[] = [];
    const payload = new Blob(["zip"], { type: "application/zip" });
    await uploadArtifactContent("workspace-1", uploadId, payload, (value) => progress.push(value));
    await completeArtifactUpload("workspace-1", uploadId);

    const uploadCall = fetchMock.mock.calls[1];
    expect(uploadCall[0]).toBe("/api/workspaces/workspace-1/artifact-uploads/upload-1/content");
    expect(uploadCall[1]).toEqual(expect.objectContaining({ method: "PUT", body: payload }));
    expect(new Headers(uploadCall[1]?.headers).get("Content-Type")).toBe("application/zip");
    expect(progress).toEqual([0, 100]);
  });
});
