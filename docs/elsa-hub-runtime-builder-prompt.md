# Lovable prompt — Elsa Hub runtime builder configurator

Paste the block below into Lovable for the Elsa Hub project. It describes the live Elsa Control
API that backs `/elsa-plus/runtime-builder/new`.

---

Update the runtime builder configurator at `/elsa-plus/runtime-builder/new` to run against the live
Elsa Control API instead of any mock or hard-coded data.

## API

Base URL: `https://api-m5uymkuaf222o.azurewebsites.net`

All configurator endpoints are under `/api/builder`. CORS is already configured server-side for
`https://www.elsaworkflows.io` and `https://elsaworkflows.io`, allowing `GET`, `POST`, `OPTIONS` and
the headers `Content-Type` and `X-Api-Key`.

| Endpoint | Method | Auth | Purpose |
|---|---|---|---|
| `/api/builder/catalog` | GET | none | Runtime images, packages with versions and features, infrastructure providers |
| `/api/builder/infrastructure/providers` | GET | none | Infrastructure providers only |
| `/api/builder/resolve` | POST | none | Resolve/validate a package + feature selection |
| `/api/builder/plan` | POST | none | Plan a configuration: auto-added packages, features, infrastructure, findings |
| `/api/builder/bundle` | POST | **`X-Api-Key`** | Generate the downloadable bundle (docker-compose, appsettings, etc.) |

Only `/api/builder/bundle` requires the API key.

### Do not put the API key in browser code

The key must never be shipped to the browser or committed. Call `/api/builder/bundle` from a
server-side function (a Supabase edge function is the natural fit in Lovable) that holds the key as a
server secret and proxies the request. The browser calls your function; your function calls the API
with the `X-Api-Key` header. Everything else is anonymous and can be called directly from the browser.

## Response shapes

`GET /api/builder/catalog` returns:

```jsonc
{
  "images": [
    {
      "slug": "elsa-pro-server",
      "displayName": "Elsa Professional Server",
      "description": "Professional Elsa Server runtime.",
      "image": "elsaworkflows/elsa-pro-server",
      "availableTags": ["latest"],
      "defaultTag": "latest",
      "defaultPort": 8080,
      "hostPort": 8080,
      "containerName": "elsa-pro-server",
      "licenseTier": "Professional",
      "stability": "Stable",
      "capabilities": ["server"],
      "runtimeKinds": ["elsa.server"],
      "envVars": [
        {
          "name": "ASPNETCORE_ENVIRONMENT",
          "displayName": "Environment",
          "description": "ASP.NET Core environment.",
          "required": false,
          "secret": false,
          "defaultValue": "Development",
          "group": "Runtime",
          "advanced": false
        }
      ],
      "deploymentHints": {
        "supportsDockerCompose": true,
        "supportsKubernetes": true,
        "requiresCompanionServer": false,
        "needsSharedNetwork": false,
        "companionImageSlug": null
      },
      "docs": { "dockerHubUrl": "https://hub.docker.com/", "containerPaths": [], "showPerShellAdmin": false, "showNuplane": true }
    }
  ],
  "packages": [
    {
      "packageId": "Elsa.Agents.Activities",
      "displayName": "…",
      "source": { "id": "…", "name": "NuGet", "url": "…", "kind": "…" },
      "runtimeKinds": [],
      "latestVersion": "3.8.0-preview.342",
      "versions": [
        {
          "packageId": "Elsa.Agents.Activities",
          "version": "3.8.0-preview.342",
          "source": { },
          "schemaVersion": "1.0",
          "runtimeKinds": [],
          "publishedAt": "…",
          "features": [
            {
              "featureId": "Elsa.Agents.Activities.AgentsActivities",
              "typeName": "…",
              "displayName": "Agents Activities",
              "description": "…",
              "category": null,
              "categories": [],
              "requiredCapabilities": [],
              "runtimeKinds": [],
              "dependencies": [],
              "conflicts": [],
              "infrastructure": [],
              "advanced": false,
              "experimental": false,
              "extensions": {},
              "settings": []
            }
          ]
        }
      ]
    }
  ],
  "infrastructureProviders": [
    { "id": "postgres-compose", "displayName": "PostgreSQL", "kind": "database", "strategy": "compose-sidecar", "provider": "postgres", "capabilities": ["relational"], "outputs": [] }
  ]
}
```

## Runtime kind filtering — important

Each image declares `runtimeKinds` (for example `["elsa.server"]`), and packages/versions/features
may declare their own. Filter features for the selected image with this rule:

- If the image declares no runtime kinds → every feature is compatible.
- If a feature (or its version/package) declares **no** runtime kinds → treat it as compatible. An
  undeclared list means "no constraint", not "incompatible".
- Otherwise → compatible when the two lists intersect, compared case-insensitively.

This matters: most published Elsa manifests carry no `compatibility` block yet, so their features
have `runtimeKinds: []`. Excluding them would show "0 of 0 features", which is the bug this replaces.
As packages start declaring runtime kinds, the same rule narrows results automatically.

Resolve a feature's effective runtime kinds as: the feature's own list if non-empty, otherwise the
version's, otherwise the package's.

## Configurator flow

Wire the existing five steps to the API:

1. **Runtime** — list `images` from the catalog. Show `displayName`, `description`, `licenseTier`,
   `stability`, and let the user pick a tag from `availableTags` (default `defaultTag`) and a host
   port (default `hostPort`). If `deploymentHints.requiresCompanionServer` is true, surface that the
   image needs the companion named in `companionImageSlug`.
2. **Features** — flatten `packages[].versions[].features[]`, apply the runtime-kind rule above
   against the selected image, and group by `categories` (fall back to `category`, then
   "Uncategorised"). Show the count honestly: "Showing X of Y features". Mark `advanced` and
   `experimental` features. Support the existing search box over `displayName`, `featureId` and
   `description`.
3. **Settings** — render each selected feature's `settings[]`, plus the image's `envVars[]` grouped
   by `group`, hiding `advanced` entries behind a toggle. Never pre-fill anything marked `secret`.
4. **Infrastructure** — offer `infrastructureProviders`, and pre-select providers implied by the
   selected features' `infrastructure[]` requirements.
5. **Review** — POST to `/api/builder/plan` with the selection, then display `autoAdded.packages`,
   `autoAdded.features`, `autoAdded.infrastructure` and any `findings` (each has a severity, code,
   message and scope). Render errors and warnings distinctly and block generation on errors.

The generate action posts to your server-side proxy, which calls `/api/builder/bundle` with the API
key and streams the resulting files back for download.

Request body shape for `/plan` and `/bundle`:

```jsonc
{
  "intent": {
    "image": { "slug": "elsa-pro-server", "tag": "latest", "hostPort": 8080, "envOverrides": {} },
    "packages": [
      { "sourceId": "<source guid>", "packageId": "Elsa.Workflows", "version": "3.8.0", "selectedFeatures": ["<featureId>"], "settings": {} }
    ],
    "packageSources": [{ "sourceId": "<source guid>" }],
    "infrastructure": [{ "kind": "database", "providerId": "postgres-compose", "strategy": "compose-sidecar", "settings": {} }]
  }
}
```

`sourceId` comes from `packages[].source.id` in the catalog — carry it through the selection rather
than inventing one.

## Behaviour requirements

- Fetch the catalog once per session and cache it; it is a large response.
- Show a real loading state while the catalog loads and a retry affordance on failure, rather than an
  empty grid.
- If the catalog returns zero packages, say so explicitly ("No packages are indexed yet") instead of
  rendering an empty feature list.
- Keep the step state in the URL so `?step=2` continues to deep-link.
- Surface the API's `findings` verbatim; do not invent client-side validation messages that could
  contradict the server.
