import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { RuntimeBuilderPage } from "@/features/runtime-builder/RuntimeBuilderPage";
import type { BuilderCatalog, BuilderPlanResponse, RuntimeBuilderIntent } from "@/features/runtime-builder/runtimeBuilderModels";

const workspaceId = "00000000-0000-0000-0000-000000000010";

const catalogFixture: BuilderCatalog = {
  images: [
    {
      slug: "elsa-runtime",
      displayName: "Elsa Runtime",
      description: "Runs Elsa workflows with catalog-selected packages.",
      image: "elsa/runtime",
      availableTags: ["4.0.0", "4.0.1"],
      defaultTag: "4.0.1",
      defaultPort: 8080,
      hostPort: 13000,
      containerName: "elsa-runtime",
      licenseTier: "Community",
      stability: "Stable",
      capabilities: ["workflows", "http"],
      envVars: [
        {
          name: "ELSA_CONNECTION_STRING",
          displayName: "Connection string",
          description: "Database connection used by persistence providers.",
          required: true,
          secret: true,
          defaultValue: null,
          group: "Persistence",
          advanced: false
        }
      ],
      deploymentHints: {
        supportsDockerCompose: true,
        supportsKubernetes: true,
        requiresCompanionServer: false,
        needsSharedNetwork: false,
        companionImageSlug: null
      },
      docs: {
        dockerHubUrl: null,
        containerPaths: ["/app"],
        showPerShellAdmin: false,
        showNuplane: false
      }
    }
  ],
  packages: [
    {
      packageId: "Elsa.Persistence.PostgreSql",
      displayName: "PostgreSQL Persistence",
      source: {
        id: "00000000-0000-0000-0000-000000000001",
        name: "Elsa Official",
        url: "https://api.nuget.org/v3/index.json"
      },
      latestVersion: "1.0.2",
      versions: [
        {
          packageId: "Elsa.Persistence.PostgreSql",
          version: "1.0.2",
          source: {
            id: "00000000-0000-0000-0000-000000000001",
            name: "Elsa Official",
            url: "https://api.nuget.org/v3/index.json"
          },
          schemaVersion: "1.0",
          publishedAt: "2026-05-15T08:00:00Z",
          features: [
            {
              featureId: "postgresql",
              typeName: "Elsa.Persistence.PostgreSql.PostgreSqlFeature",
              displayName: "PostgreSQL Persistence",
              description: "Stores workflow state in PostgreSQL.",
              category: "Persistence",
              requiredCapabilities: ["persistence"],
              infrastructure: [
                {
                  id: "postgresql",
                  kind: "Database",
                  optional: false,
                  reason: "Stores workflow state.",
                  capabilities: ["relational"],
                  providers: ["PostgreSQL"],
                  configurationKeys: ["connectionString"]
                }
              ],
              advanced: false,
              experimental: false,
              settings: []
            }
          ]
        }
      ]
    }
  ],
  infrastructureProviders: [
    {
      id: "postgresql",
      displayName: "PostgreSQL",
      kind: "Database",
      strategy: "Managed",
      provider: "PostgreSQL",
      capabilities: ["relational"],
      outputs: ["connectionString"]
    }
  ]
};

describe("RuntimeBuilderPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("builds a runtime plan and previews generated bundle files", async () => {
    const fetchMock = createRuntimeBuilderFetchMock({
      planResponse: (intent) => ({
        resolved: {
          ...intent,
          infrastructure: [postgresqlInfrastructure]
        },
        autoAdded: {
          packages: [],
          features: [],
          infrastructure: [postgresqlInfrastructure]
        },
        findings: [{ level: "info", code: "planner.infrastructure", message: "PostgreSQL provider added.", scope: null }]
      })
    });
    renderRuntimeBuilder(fetchMock);

    expect((await screen.findAllByText("Elsa Runtime")).length).toBeGreaterThan(0);

    await userEvent.click(screen.getByRole("button", { name: "Add Elsa.Persistence.PostgreSql" }));
    expect(screen.getAllByText("PostgreSQL Persistence").length).toBeGreaterThan(1);

    await userEvent.click(screen.getByRole("button", { name: "Plan Runtime" }));
    await screen.findByText("Planner additions");
    expect(screen.getByText("PostgreSQL provider added.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Generate Bundle" }));
    expect(await screen.findByText("Bundle bundle-1")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "docker-compose.yml" })).toBeInTheDocument();
    expect(screen.getByText(/image: elsa\/runtime:4\.0\.1/)).toBeInTheDocument();

    await waitFor(() => {
      const planCall = findPlanCall(fetchMock);
      expect(planCall).toBeDefined();
      const planBody = readBody<{ intent: RuntimeBuilderIntent }>(planCall?.[1]);
      expect(planBody.intent.packages).toHaveLength(1);
      expect(planBody.intent.packages[0].selectedFeatures).toEqual(["postgresql"]);
    });
  });

  it("hides planner additions when no resources were auto-added", async () => {
    const fetchMock = createRuntimeBuilderFetchMock({
      planResponse: (intent) => ({
        resolved: intent,
        autoAdded: { packages: [], features: [], infrastructure: [] },
        findings: []
      })
    });
    renderRuntimeBuilder(fetchMock);

    expect((await screen.findAllByText("Elsa Runtime")).length).toBeGreaterThan(0);
    await userEvent.click(screen.getByRole("button", { name: "Plan Runtime" }));

    await waitFor(() => expect(findPlanCall(fetchMock)).toBeDefined());
    expect(screen.queryByText("Planner additions")).not.toBeInTheDocument();
  });

  it("shows a generic error state for non-auth workspace failures", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = input instanceof Request ? input.url : input.toString();
      return url.endsWith("/api/me/workspaces") ? jsonResponse({ title: "API unavailable" }, 500) : jsonResponse({ title: "Not found" }, 404);
    });
    renderRuntimeBuilder(fetchMock);

    expect(await screen.findByText("Workspace context could not load")).toBeInTheDocument();
    expect(screen.getByText("The dashboard could not complete the request.")).toBeInTheDocument();
    expect(screen.queryByText("Your admin credentials are missing or no longer valid.")).not.toBeInTheDocument();
  });
});

const postgresqlInfrastructure = { kind: "Database", providerId: "postgresql", strategy: "Managed", settings: null };

function createRuntimeBuilderFetchMock({ planResponse }: { planResponse: (intent: RuntimeBuilderIntent) => BuilderPlanResponse }) {
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    if (url.endsWith("/api/me/workspaces")) {
      return jsonResponse({
        account: { id: "account-1", displayName: "Test User", email: "test@example.com" },
        workspaces: [{ id: workspaceId, name: "Default workspace", kind: "Personal", role: "Owner" }]
      });
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/builder/catalog`)) {
      return jsonResponse(catalogFixture);
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/runtime-configurations`) && (!init?.method || init.method === "GET")) {
      return jsonResponse([]);
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/builder/plan`)) {
      const body = readBody<{ intent: RuntimeBuilderIntent }>(init);
      return jsonResponse(planResponse(body.intent));
    }
    if (url.endsWith(`/api/workspaces/${workspaceId}/builder/bundle`)) {
      return jsonResponse({
        bundleId: "bundle-1",
        files: [
          {
            path: "docker-compose.yml",
            language: "yaml",
            contentType: "text/yaml",
            required: true,
            contents: "services:\n  elsa-runtime:\n    image: elsa/runtime:4.0.1\n"
          }
        ],
        findings: []
      });
    }

    return jsonResponse({ title: "Not found" }, 404);
  });
}

function findPlanCall(fetchMock: ReturnType<typeof createRuntimeBuilderFetchMock>) {
  return fetchMock.mock.calls.find((call) => call[0].toString().endsWith(`/api/workspaces/${workspaceId}/builder/plan`));
}

function renderRuntimeBuilder(fetchMock: unknown) {
  vi.stubGlobal("fetch", fetchMock);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });

  render(
    <QueryClientProvider client={queryClient}>
      <RuntimeBuilderPage />
    </QueryClientProvider>
  );
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function readBody<T>(init?: RequestInit) {
  return JSON.parse(String(init?.body ?? "{}")) as T;
}
