# Platform-Integrated Studio Sample

This sample shows how an Elsa Studio host wires the `Elsa.Studio.PlatformIntegration` module so workflow authors can choose **Submit to Platform** from the workflow editor toolbar, workflow definition list bulk menu, and workflow definition row menu.

## Package

Add the Studio integration package to the Elsa Studio host:

```xml
<PackageReference Include="Elsa.Studio.PlatformIntegration" Version="0.0.1" />
```

Use the same package version as the Platform backend until an explicit Platform API compatibility range is available.

## Program Wiring

```csharp
using System.Net.Http.Headers;
using Elsa.Studio.PlatformIntegration.Extensions;

var platformTokenProvider = new PlatformTokenProvider(builder.Configuration);
builder.Services.AddSingleton(platformTokenProvider);

builder.Services.AddPlatformIntegrationModule(options =>
{
    options.Enabled = builder.Configuration.GetValue("ElsaPlatform:Submit:Enabled", true);
    options.PlatformEndpoint = new Uri(builder.Configuration["ElsaPlatform:Endpoint"]!);
    options.WorkspaceId = Guid.Parse(builder.Configuration["ElsaPlatform:WorkspaceId"]!);
    options.ProducerName = "Elsa Studio";
    options.ProducerVersion = typeof(Program).Assembly.GetName().Version?.ToString();
    options.ArtifactSchemaVersion = "1.0";
    options.RequiredCapabilities = ["workflow-definition.apply"];

    // Keep direct runtime Publish separate from Platform submission.
    options.SubmitOnWorkflowPublished = false;

    options.ConfigureRequestAsync = async (request, cancellationToken) =>
    {
        var accessToken = await platformTokenProvider.GetPlatformAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    };
});
```

`PlatformTokenProvider` is host-owned. Implement it with the same identity provider and secret rules used by the Studio deployment. Do not write bearer tokens or client secrets into artifact metadata.

## Configuration

Start from [appsettings.example.json](appsettings.example.json).

Required values:

- `ElsaPlatform:Endpoint`: base Platform URL.
- `ElsaPlatform:WorkspaceId`: workspace receiving submitted artifacts.
- `ElsaPlatform:Submit:Enabled`: feature flag for the Studio module.
- `ElsaPlatform:Auth:CredentialReference`: reference to a host-owned credential, not a raw secret.

## UX Behavior

When enabled and configured, Studio shows **Submit to Platform** beside existing publish surfaces:

- Workflow editor toolbar.
- Workflow definition list bulk action menu.
- Workflow definition row action menu.

Submission creates an immutable Platform artifact. It does not release, promote, deploy, or make the workflow immediately executable.
