# Platform Integration Packaging And Host Configuration

Status: initial packaging guidance

Last updated: 2026-05-31

Tracking issue: [#41](https://github.com/elsa-workflows/elsa-platform/issues/41)

## Package Decisions

The artifact-driven workflow path has two installable integration roles:

| Role | Package ID | Source repository | Purpose |
| --- | --- | --- | --- |
| Studio producer integration | `Elsa.Studio.PlatformIntegration` | `elsa-studio` | Adds **Submit to Platform** actions to Elsa Studio and submits `elsa.workflow-definition` artifact metadata to Platform. |
| Workflow runtime consumer integration | `Elsa.Platform.Workflows.RuntimeApplier` | `elsa-platform` | Polls Platform runtime commands, fetches workflow artifact payloads, verifies them, applies them through a runtime store boundary, and reports results. |

The current platform repository also contains `Elsa.Platform.Studio.Submit`. Treat it as shared producer-side primitives for non-Studio producers or a future extraction point. The Studio UI module currently owns its host-specific widgets, notification handling, and request customization in `elsa-studio`.

Version compatibility is same-version by default: integration package versions should match the Platform backend package version until the Platform API exposes an explicit compatibility/version negotiation contract.

## Studio Producer Configuration

Install `Elsa.Studio.PlatformIntegration` into a platform-integrated Elsa Studio host.

Register it with the Studio service collection:

```csharp
using System.Net.Http.Headers;
using Elsa.Studio.PlatformIntegration.Extensions;

builder.Services.AddPlatformIntegrationModule(options =>
{
    options.PlatformEndpoint = new Uri(builder.Configuration["ElsaPlatform:Endpoint"]!);
    options.WorkspaceId = Guid.Parse(builder.Configuration["ElsaPlatform:WorkspaceId"]!);
    options.ProducerName = "Elsa Studio";
    options.ProducerVersion = typeof(Program).Assembly.GetName().Version?.ToString();
    options.RequiredCapabilities = ["workflow-definition.apply"];

    options.ConfigureRequestAsync = async (request, cancellationToken) =>
    {
        var accessToken = await tokenProvider.GetPlatformAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    };
});
```

Recommended settings:

- `PlatformEndpoint`: the base Platform URL, for example `https://platform.example.com`.
- `WorkspaceId`: the workspace that receives submitted artifacts.
- `ConfigureRequestAsync`: host-owned authentication hook. Attach a short-lived Platform access token or trusted service token here.
- `SubmitOnWorkflowPublished`: set to `false` when direct runtime **Publish** remains available as a separate fallback and Platform submission should happen only through the explicit **Submit to Platform** action.
- `PayloadProvider` and `PayloadUriScheme`: keep the defaults unless the host provides a payload service that the runtime applier can fetch and verify.

Security rules:

- Do not store bearer tokens, API keys, or provider credentials in Studio artifact metadata.
- Do not treat **Submit to Platform** as release, promotion, deployment, or runtime publish.
- Platform-integrated Studio should keep direct runtime **Publish** visibly separate from **Submit to Platform** when both are enabled.

## Runtime Applier Configuration

Install `Elsa.Platform.Workflows.RuntimeApplier` into the Elsa Workflows runtime host that should consume Platform deployment commands.

The runtime host owns four boundaries:

- Platform command authentication.
- Artifact payload transport trust.
- Local Elsa workflow definition persistence.
- Durable apply journal storage.

Minimal registration sketch:

```csharp
using System.Net.Http.Headers;
using Elsa.Platform.Workflows.RuntimeApplier;

builder.Services.AddTransient<PlatformAuthenticationHandler>();

var runtimeOptions = new WorkflowArtifactRuntimeOptions
{
    PlatformEndpoint = new Uri(builder.Configuration["ElsaPlatform:Endpoint"]!),
    WorkspaceId = Guid.Parse(builder.Configuration["ElsaPlatform:WorkspaceId"]!),
    EngineId = Guid.Parse(builder.Configuration["ElsaPlatform:EngineId"]!),
    RuntimeVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
    AllowedPayloadReferenceProviders = ["producer-managed"],
    AllowedPayloadHosts = builder.Configuration.GetSection("ElsaPlatform:AllowedPayloadHosts").Get<string[]>() ?? []
};
runtimeOptions.Validate();

builder.Services.AddSingleton(runtimeOptions);

builder.Services.AddHttpClient<IWorkflowRuntimeCommandClient, WorkflowRuntimeCommandHttpClient>(httpClient =>
{
    httpClient.BaseAddress = runtimeOptions.PlatformEndpoint;
}).AddHttpMessageHandler<PlatformAuthenticationHandler>();

builder.Services.AddHttpClient<IWorkflowArtifactEnvelopeProvider, WorkflowArtifactPlatformEnvelopeClient>(httpClient =>
{
    httpClient.BaseAddress = runtimeOptions.PlatformEndpoint;
}).AddHttpMessageHandler<PlatformAuthenticationHandler>();

builder.Services.AddSingleton<IWorkflowArtifactPayloadFetcher, WorkflowArtifactHttpPayloadFetcher>();
builder.Services.AddSingleton<IWorkflowArtifactSchemaValidator, WorkflowArtifactRuntimeContractValidator>();
builder.Services.AddSingleton<IWorkflowDefinitionApplier, WorkflowDefinitionJsonApplier>();
builder.Services.AddSingleton<IWorkflowArtifactApplyJournal, DurableWorkflowArtifactApplyJournal>();
builder.Services.AddSingleton<IWorkflowDefinitionRuntimeStore, ElsaWorkflowDefinitionRuntimeStore>();
builder.Services.AddSingleton<WorkflowArtifactCommandProcessor>();
```

Example authentication handler:

```csharp
using System.Net.Http.Headers;

public sealed class PlatformAuthenticationHandler(IPlatformTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var accessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
```

The code above is a wiring shape, not a full worker loop. The host should run a background service that polls commands, claims one command, and passes the claim to `WorkflowArtifactCommandProcessor.ProcessAsync`.

`IPlatformTokenProvider`, `DurableWorkflowArtifactApplyJournal`, and `ElsaWorkflowDefinitionRuntimeStore` are host-provided examples. The integration package defines the contracts and default safety helpers; the host decides how tokens are acquired and how Elsa workflow definitions and apply journal entries are persisted.

Required runtime options:

- `PlatformEndpoint`: the base Platform URL.
- `WorkspaceId`: the workspace that owns the runtime engine and commands.
- `EngineId`: the registered Platform deployment engine ID for this runtime.
- `WorkerId`: stable worker identity for command leases. The default is machine/process based; production hosts should set a stable value.
- `Capabilities`: must include `workflow-definition.apply` for workflow artifact deployment.
- `AllowedPayloadReferenceProviders`: approved artifact payload providers.
- `AllowedPayloadHosts`: approved public payload hosts.

Operational guidance:

- Use a durable `IWorkflowArtifactApplyJournal`; the in-memory implementation is for tests and local experiments only.
- Implement `IWorkflowDefinitionRuntimeStore` against the real Elsa Workflows definition store for the target runtime.
- The default HTTP payload fetcher rejects redirects, proxy use, unapproved hosts, non-public addresses, invalid media types, expired references, and oversized payloads.
- If artifacts live on private infrastructure, provide a host-owned `IWorkflowArtifactPayloadFetcher` with equivalent trust checks instead of weakening the default fetcher.
- Heartbeat and claim lease settings must leave enough margin for local validation and apply. Increase `ClaimLeaseDuration` before increasing payload size or apply complexity.

## Configuration Shape

Suggested host configuration keys:

```json
{
  "ElsaPlatform": {
    "Endpoint": "https://platform.example.com",
    "WorkspaceId": "00000000-0000-0000-0000-000000000001",
    "EngineId": "00000000-0000-0000-0000-000000000101",
    "AllowedPayloadHosts": [ "artifacts.example.com" ],
    "Auth": {
      "Mode": "ClientCredentials",
      "CredentialReference": "platform-runtime-client"
    }
  }
}
```

Secrets referenced by `ElsaPlatform:Auth` must resolve through the host's secret provider. They must not be written into artifact records, desired-state records, command records, or diagnostics.

## Publishing Checklist

Before publishing the integration packages externally:

- Confirm package IDs and repository ownership match the table above.
- Publish `Elsa.Studio.PlatformIntegration` from `elsa-studio`.
- Publish `Elsa.Platform.Workflows.RuntimeApplier` from `elsa-platform`.
- Keep `Elsa.Platform.Studio.Submit` internal unless a non-Studio producer or shared-contract extraction needs it.
- Document the same-version compatibility rule in package release notes.
- Add sample host wiring for Studio and runtime applications through [#42](https://github.com/elsa-workflows/elsa-platform/issues/42).
- Execute the E2E smoke path through [#43](https://github.com/elsa-workflows/elsa-platform/issues/43) before declaring the packages ready for production use.
