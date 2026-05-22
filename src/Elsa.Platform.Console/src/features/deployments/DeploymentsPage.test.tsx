import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { DeploymentsPage } from "@/features/deployments/DeploymentsPage";

describe("DeploymentsPage", () => {
  it("renders workflow applications and environment health without exposing credential values", async () => {
    renderDeployments();

    expect(await screen.findByRole("heading", { name: "Deployments" })).toBeInTheDocument();
    expect(screen.getByText("Claims Operations")).toBeInTheDocument();
    expect(screen.getByText("Workspace tenant boundary")).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: /Prod Production/i })).toBeInTheDocument();
    expect(screen.getAllByText("Drift detected").length).toBeGreaterThan(0);
    expect(screen.queryByText(/password|token|secret value/i)).not.toBeInTheDocument();
  });

  it("shows only capability-supported engine controls and records selected operations", async () => {
    renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Engine Registration" }));

    expect(screen.getAllByText("claims-stage-weu-01").length).toBeGreaterThan(0);
    expect(screen.getByText("kv://acme-platform/stage/elsa-api")).toBeInTheDocument();
    expect(screen.getByText("Pause Processing")).toBeInTheDocument();
    expect(screen.getAllByText("Reload Configuration").length).toBeGreaterThan(0);
    expect(screen.queryByText("Restart Shell")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Restart$/i })).not.toBeInTheDocument();

    await userEvent.click(screen.getAllByRole("button", { name: "Run" })[1]);

    expect(screen.getByRole("status")).toHaveTextContent("Reload Configuration queued as a EngineApi control");
  });

  it("blocks deployment when promotion validation finds missing secrets and incompatible capabilities", async () => {
    renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Promotion Diff" }));

    expect(screen.getByText("Payment Retry")).toBeInTheDocument();
    expect(screen.getAllByText("Secret references").length).toBeGreaterThan(0);
    expect(screen.getByText("Payment API secret reference is missing or not verified in Prod.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deploy Revision" })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Roll Back to r39/i })).toBeEnabled();
  });

  it("enables deployment for a comparison with passing validations", async () => {
    renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Promotion Diff" }));
    await userEvent.selectOptions(screen.getByLabelText("Source revision"), "claims-dev");
    await userEvent.selectOptions(screen.getByLabelText("Target revision"), "claims-test");

    expect(screen.getByText("Required secret references are verified for Test.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deploy Revision" })).toBeEnabled();
  });

  it("keeps assistant plans immutable and distinguishes proposed from executed actions", async () => {
    renderDeployments();

    await screen.findByRole("heading", { name: "Deployments" });
    await userEvent.click(screen.getByRole("button", { name: "Assistant Review" }));

    expect(screen.getByText("Immutable plan plan-20260522-001 v3")).toBeInTheDocument();
    expect(screen.getByText("Proposed actions")).toBeInTheDocument();
    expect(screen.getByText("Executed actions")).toBeInTheDocument();
    expect(screen.getByText("No platform mutations executed.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve Plan" })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: "Reject Plan" }));
    expect(screen.getByRole("status")).toHaveTextContent("Plan marked Rejected");
    expect(screen.getByText("No platform mutations executed.")).toBeInTheDocument();
  });
});

function renderDeployments() {
  render(
    <TestQueryProvider>
      <DeploymentsPage />
    </TestQueryProvider>
  );
}

function TestQueryProvider({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
