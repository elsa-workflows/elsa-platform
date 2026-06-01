# Workflow Runtime Applier Sample

This sample shows how an Elsa Workflows runtime host wires `Elsa.Platform.Workflows.RuntimeApplier` so the runtime can poll Platform deployment commands, claim work, fetch workflow artifacts, apply them locally, and report results.

## Package

Add the runtime applier package to the Elsa Workflows runtime host:

```xml
<PackageReference Include="Elsa.Platform.Workflows.RuntimeApplier" Version="0.0.1" />
```

Use the same package version as the Platform backend until an explicit Platform API compatibility range is available.

## Program Wiring

```csharp
using System.Net.Http.Headers;
using Elsa.Platform.Workflows.RuntimeApplier;

var runtimeOptions = new WorkflowArtifactRuntimeOptions
{
    PlatformEndpoint = new Uri(builder.Configuration["ElsaPlatform:Endpoint"]!),
    WorkspaceId = Guid.Parse(builder.Configuration["ElsaPlatform:WorkspaceId"]!),
    EngineId = Guid.Parse(builder.Configuration["ElsaPlatform:EngineId"]!),
    WorkerId = builder.Configuration["ElsaPlatform:WorkerId"] ?? Environment.MachineName,
    RuntimeVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
    ClaimLeaseDuration = TimeSpan.FromMinutes(5),
    HeartbeatInterval = TimeSpan.FromSeconds(30),
    Capabilities = ["workflow-definition.apply"],
    AllowedPayloadReferenceProviders = ["producer-managed"],
    AllowedPayloadHosts = builder.Configuration.GetSection("ElsaPlatform:AllowedPayloadHosts").Get<string[]>() ?? []
};
runtimeOptions.Validate();

builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddTransient<PlatformAuthenticationHandler>();

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
builder.Services.AddHostedService<WorkflowArtifactCommandWorker>();
```

The host must provide:

- `PlatformAuthenticationHandler`: attaches a short-lived Platform access token to command and artifact-envelope API requests.
- `DurableWorkflowArtifactApplyJournal`: durable idempotency journal.
- `ElsaWorkflowDefinitionRuntimeStore`: adapter that saves workflow definitions to the local Elsa runtime store.
- `WorkflowArtifactCommandWorker`: background loop that polls, claims, and processes commands.

## Authentication Handler

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

## Worker Loop

```csharp
public sealed class WorkflowArtifactCommandWorker(
    IWorkflowRuntimeCommandClient commands,
    WorkflowArtifactCommandProcessor processor,
    WorkflowArtifactRuntimeOptions options,
    ILogger<WorkflowArtifactCommandWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pending = await commands.PollAsync(limit: 1, stoppingToken);
            foreach (var command in pending)
            {
                var claim = await commands.ClaimAsync(command.Id, stoppingToken);
                if (claim.Claim is null)
                    continue;

                var result = await processor.ProcessAsync(claim.Claim, stoppingToken);
                logger.LogInformation("Processed Platform command {CommandId} with status {Status}.", command.Id, result.Status);
            }

            await Task.Delay(options.PollInterval, stoppingToken);
        }
    }
}
```

Production workers should add bounded retry/backoff around poll failures and use the package lease/retry policies when deciding whether a command is still safe to apply.

## Advisory Webhook Endpoint

Polling remains required. If Platform's disabled-by-default webhook dispatcher is enabled, expose the configured notification path and use it only to wake the normal poll/claim loop:

```csharp
app.MapPost("/api/elsa-platform/deployment-command-notifications", async (
    PlatformCommandWakeupQueue wakeups,
    CancellationToken cancellationToken) =>
{
    await wakeups.EnqueueAsync(cancellationToken);
    return Results.Accepted();
});
```

The webhook body is a safe command-available hint. Do not apply a workflow from the webhook request. Validate Platform identity with host-owned authentication or network policy before waking the worker.

## Configuration

Start from [appsettings.example.json](appsettings.example.json).

Required values:

- `ElsaPlatform:Endpoint`: base Platform URL.
- `ElsaPlatform:WorkspaceId`: workspace that owns the deployment engine.
- `ElsaPlatform:EngineId`: registered Platform engine ID for this runtime.
- `ElsaPlatform:AllowedPayloadHosts`: approved public artifact payload hosts.

The default payload fetcher rejects redirects, proxy use, private-address hosts, unapproved hosts, invalid media types, expired references, and oversized payloads. If artifacts must be fetched from private infrastructure, replace `IWorkflowArtifactPayloadFetcher` with a host-owned implementation that keeps equivalent trust checks.
