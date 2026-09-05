import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ManagedElsaCreatePanel } from "./ManagedElsaCreatePanel";
import { createManagedElsaInstance } from "./managedElsaApi";
import type { ManagedElsaOnboardingOptions } from "./managedElsaModels";

vi.mock("./managedElsaApi", () => ({
  createManagedElsaInstance: vi.fn(),
  getManagedElsaOnboardingOptions: vi.fn(),
  getManagedElsaOperation: vi.fn()
}));

const workspaceId = "00000000-0000-0000-0000-000000000010";
const otherWorkspaceId = "00000000-0000-0000-0000-000000000020";
const firstDigest = `sha256:${"a".repeat(64)}`;
const nextDigest = `sha256:${"b".repeat(64)}`;
const optionsKey = (workspace: string) => ["managed-elsa", workspace, "onboarding-options"];

describe("ManagedElsaCreatePanel Preview authority", () => {
  afterEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
  });

  it("clears consent when the selected manifest changes, including when it changes back", async () => {
    const { client, user } = setup();
    await user.type(screen.getByLabelText("Instance name"), "Preview instance");
    await user.click(screen.getByRole("checkbox"));
    expect(screen.getByRole("button", { name: "Create instance" })).toBeEnabled();

    act(() => { client.setQueryData(optionsKey(workspaceId), options(nextDigest)); });
    await waitFor(() => expect(screen.getByRole("checkbox")).not.toBeChecked());
    expect(screen.getByRole("button", { name: "Create instance" })).toBeDisabled();
    act(() => { client.setQueryData(optionsKey(workspaceId), options(firstDigest)); });
    await waitFor(() => expect(screen.getByRole("checkbox")).not.toBeChecked());
  });

  it("does not carry consent across workspaces", async () => {
    const { client, user, rerender } = setup();
    await user.type(screen.getByLabelText("Instance name"), "Preview instance");
    await user.click(screen.getByRole("checkbox"));
    client.setQueryData(optionsKey(otherWorkspaceId), options(firstDigest));
    rerender(<QueryClientProvider client={client}><ManagedElsaCreatePanel workspaceId={otherWorkspaceId} /></QueryClientProvider>);
    await waitFor(() => expect(screen.getByRole("checkbox")).not.toBeChecked());
    expect(screen.getByRole("button", { name: "Create instance" })).toBeDisabled();
  });

  it("guards submission even when native form validation is bypassed", async () => {
    const { user } = setup();
    await user.type(screen.getByLabelText("Instance name"), "Preview instance");
    fireEvent.submit(screen.getByRole("button", { name: "Create instance" }).closest("form")!);
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(createManagedElsaInstance).not.toHaveBeenCalled();
  });

  it("does not offer malformed or ambiguous Preview identities", () => {
    const value = options(firstDigest);
    value.previewReleases!.push({ ...value.previewReleases![0], manifestDigest: nextDigest });
    value.previewReleases!.push({ ...value.previewReleases![0], version: "5.0.0-preview.2", manifestDigest: "unsafe-digest" });
    setup(value);
    expect(screen.queryByRole("option")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create instance" })).toBeDisabled();
  });

  it("preserves a Supported choice when stale Preview discovery has the same selection", async () => {
    const value = options(firstDigest);
    const { manifestDigest: _digest, ...supported } = value.previewReleases![0];
    value.releases.push(supported);
    const { user } = setup(value);
    await user.type(screen.getByLabelText("Instance name"), "Supported instance");
    expect(screen.getAllByRole("option")).toHaveLength(1);
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create instance" })).toBeEnabled();
  });
});

function setup(value = options(firstDigest)) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } } });
  client.setQueryData(optionsKey(workspaceId), value);
  const view = render(<QueryClientProvider client={client}><ManagedElsaCreatePanel workspaceId={workspaceId} /></QueryClientProvider>);
  return { ...view, client, user: userEvent.setup() };
}

function options(manifestDigest: string): ManagedElsaOnboardingOptions {
  return {
    releases: [],
    previewReleases: [{ distributionId: "future-runtime", releaseLine: "5.0", version: "5.0.0-preview.1", channel: "preview", topologyId: "combined", manifestDigest }],
    launchProfile: { name: "Managed", description: "Managed hosting", targetMode: "managed", regionCode: "westeurope", isolationProfile: "dedicated", capacityProfile: "standard-small", networkOutcome: "public", domainOutcome: "managed" }
  };
}
