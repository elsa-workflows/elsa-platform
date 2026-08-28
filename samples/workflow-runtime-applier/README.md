# Workflow Runtime Applier Sample

This sample shows how an Elsa Workflows runtime host wires `ElsaControl.Workflows.RuntimeApplier` so the runtime can poll Control deployment commands, claim work, fetch workflow artifacts, apply them locally, and report results.

## Package

Add the runtime applier package to the Elsa Workflows runtime host:

```xml
<PackageReference Include="ElsaControl.Workflows.RuntimeApplier" Version="0.0.1" />
```

Use the same package version as the Control backend until an explicit Control API compatibility range is available.

## Program Wiring

```csharp
using System.Net.Http.Headers;
using ElsaControl.Workflows.RuntimeApplier;

var runtimeOptions = new WorkflowArtifactRuntimeOptions
{
    ControlEndpoint = new Uri(builder.Configuration["ElsaControl:Endpoint"]!),
    WorkspaceId = Guid.Parse(builder.Configuration["ElsaControl:WorkspaceId"]!),
    EngineId = Guid.Parse(builder.Configuration["ElsaControl:EngineId"]!),
    WorkerId = builder.Configuration["ElsaControl:WorkerId"] ?? Environment.MachineName,
    RuntimeVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
    ClaimLeaseDuration = TimeSpan.FromMinutes(5),
    HeartbeatInterval = TimeSpan.FromSeconds(30),
    Capabilities = ["workflow-definition.apply"],
    AllowedPayloadReferenceProviders = ["producer-managed"],
    AllowedPayloadHosts = builder.Configuration.GetSection("ElsaControl:AllowedPayloadHosts").Get<string[]>() ?? []
};
runtimeOptions.Validate();

builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddTransient<ControlAuthenticationHandler>();

builder.Services.AddHttpClient<IWorkflowRuntimeCommandClient, WorkflowRuntimeCommandHttpClient>(httpClient =>
{
    httpClient.BaseAddress = runtimeOptions.ControlEndpoint;
}).AddHttpMessageHandler<ControlAuthenticationHandler>();

builder.Services.AddHttpClient<IWorkflowArtifactEnvelopeProvider, WorkflowArtifactControlEnvelopeClient>(httpClient =>
{
    httpClient.BaseAddress = runtimeOptions.ControlEndpoint;
}).AddHttpMessageHandler<ControlAuthenticationHandler>();

builder.Services.AddSingleton<IWorkflowArtifactPayloadFetcher, WorkflowArtifactHttpPayloadFetcher>();
builder.Services.AddSingleton<IWorkflowArtifactSchemaValidator, WorkflowArtifactRuntimeContractValidator>();
builder.Services.AddSingleton<IWorkflowDefinitionApplier, WorkflowDefinitionJsonApplier>();
builder.Services.AddSingleton<IWorkflowArtifactApplyJournal, DurableWorkflowArtifactApplyJournal>();
builder.Services.AddSingleton<IWorkflowDefinitionRuntimeStore, ElsaWorkflowDefinitionRuntimeStore>();
builder.Services.AddSingleton<WorkflowArtifactCommandProcessor>();
builder.Services.AddHostedService<WorkflowArtifactCommandWorker>();
```

The host must provide:

- `ControlAuthenticationHandler`: attaches a short-lived Control access token to command and artifact-envelope API requests.
- `DurableWorkflowArtifactApplyJournal`: durable idempotency journal.
- `ElsaWorkflowDefinitionRuntimeStore`: adapter that saves workflow definitions to the local Elsa runtime store.
- `WorkflowArtifactCommandWorker`: background loop that polls, claims, and processes commands.

## Authentication Handler

```csharp
using System.Net.Http.Headers;

public sealed class ControlAuthenticationHandler(IControlTokenProvider tokenProvider) : DelegatingHandler
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
                logger.LogInformation("Processed Control command {CommandId} with status {Status}.", command.Id, result.Status);
            }

            await Task.Delay(options.PollInterval, stoppingToken);
        }
    }
}
```

Production workers should add bounded retry/backoff around poll failures and use the package lease/retry policies when deciding whether a command is still safe to apply.

## Advisory Webhook Endpoint

Polling remains required. If Control's disabled-by-default webhook dispatcher is enabled, expose the configured notification path and use it only to wake the normal poll/claim loop:

```csharp
app.MapPost("/api/elsa-control/deployment-command-notifications", async (
    ControlCommandWakeupQueue wakeups,
    CancellationToken cancellationToken) =>
{
    await wakeups.EnqueueAsync(cancellationToken);
    return Results.Accepted();
});
```

The webhook body is a safe command-available hint. Do not apply a workflow from the webhook request. Validate Control identity with host-owned authentication or network policy before waking the worker.

See [Runtime Transport Trust Policy](../../docs/runtime-transport-trust-policy.md) before enabling this endpoint outside local validation.

## Configuration

Start from [appsettings.example.json](appsettings.example.json).

Required values:

- `ElsaControl:Endpoint`: base Control URL.
- `ElsaControl:WorkspaceId`: workspace that owns the deployment engine.
- `ElsaControl:EngineId`: registered Control engine ID for this runtime.
- `ElsaControl:AllowedPayloadHosts`: approved public artifact payload hosts.

The default payload fetcher rejects redirects, proxy use, private-address hosts, unapproved hosts, invalid media types, expired references, and oversized payloads. If artifacts must be fetched from private infrastructure, replace `IWorkflowArtifactPayloadFetcher` with a host-owned implementation that keeps equivalent trust checks.
