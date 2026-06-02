using System.Text;
using System.Text.Json.Serialization;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Abstractions.Catalog;
using Elsa.Platform.PackageCatalog.Abstractions.Compatibility;
using Elsa.Platform.Api.Admin.Application;
using Elsa.Platform.Api.Admin.Workspaces;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Admin.Packages;
using Elsa.Platform.Api.Admin.Sources;
using Elsa.Platform.Api.Admin.Sync;
using Elsa.Platform.Api.Public.Builder;
using Elsa.Platform.Api.Public.Compatibility;
using Elsa.Platform.Api.Public.Features;
using Elsa.Platform.Api.Public.Packages;
using Elsa.Platform.Api.Public.Sources;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Approvals;
using Elsa.Platform.RuntimeBuilder.Abstractions;
using Elsa.Platform.RuntimeBuilder.Abstractions.Planner;
using Elsa.Platform.RuntimeBuilder.DeploymentTemplates;
using Elsa.Platform.PackageCatalog.Core.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Manifests;
using Elsa.Platform.PackageCatalog.Core.Packaging;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Persistence;
using Elsa.Platform.RuntimeBuilder.Abstractions.RuntimeConfigurations;
using Elsa.Platform.PackageCatalog.Core.Sources;
using Elsa.Platform.PackageCatalog.Core.Sync;
using Elsa.Platform.PackageCatalog.Sources.NuGet;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageManifests.Validation;
using Elsa.Platform.RuntimeBuilder.Core.Builder;
using Elsa.Platform.RuntimeBuilder.Core.Builder.Planner;
using Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers;
using Elsa.Platform.RuntimeBuilder.Core.RuntimeConfigurations;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;

IdentityModelEventSource.ShowPII = true;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(AdminDashboardAuthenticationDefaults.DevelopmentUrlConfigurationKey);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
});
builder.Services.Configure<PlatformIdentityOptions>(builder.Configuration.GetSection(PlatformIdentityDefaults.ConfigurationSection));
var configuredPlatformIdentity = builder.Configuration.GetSection(PlatformIdentityDefaults.ConfigurationSection).Get<PlatformIdentityOptions>() ?? new PlatformIdentityOptions();
var authentication = builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = PlatformIdentityDefaults.Scheme;
        options.DefaultChallengeScheme = PlatformIdentityDefaults.Scheme;
    })
    .AddJwtBearer(PlatformIdentityDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationDefaults.Scheme, _ => { })
    .AddScheme<AuthenticationSchemeOptions, BuilderClientApiKeyAuthenticationHandler>(BuilderClientApiKeyAuthenticationDefaults.Scheme, _ => { })
    .AddCookie(CustomerAuthenticationDefaults.CookieScheme, options =>
    {
        options.Cookie.Name = CustomerAuthenticationDefaults.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = CustomerAuthenticationDefaults.SessionLifetime;
        options.LoginPath = CustomerAuthenticationDefaults.LoginPath;
        options.LogoutPath = CustomerAuthenticationDefaults.LogoutPath;
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
if (configuredPlatformIdentity.IsCustomerLoginConfigured)
{
    authentication.AddOpenIdConnect(CustomerAuthenticationDefaults.OidcScheme, _ => { });
    builder.Services.AddOptions<OpenIdConnectOptions>(CustomerAuthenticationDefaults.OidcScheme)
        .Configure<IOptions<PlatformIdentityOptions>>((options, platformIdentityOptions) =>
            CustomerOidcOptionsConfigurator.Configure(options, platformIdentityOptions.Value));
}
builder.Services.AddOptions<JwtBearerOptions>(PlatformIdentityDefaults.Scheme)
    .Configure<IOptions<PlatformIdentityOptions>>((options, platformIdentityOptions) =>
    {
        var platformIdentity = platformIdentityOptions.Value;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = platformIdentity.RequireHttpsMetadata;
        options.Authority = string.IsNullOrWhiteSpace(platformIdentity.Authority) ? null : platformIdentity.Authority;
        options.Audience = string.IsNullOrWhiteSpace(platformIdentity.Audience) ? null : platformIdentity.Audience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(platformIdentity.Issuer)
                || !string.IsNullOrWhiteSpace(platformIdentity.Authority)
                || !string.IsNullOrWhiteSpace(platformIdentity.SymmetricSigningKey),
            ValidIssuer = string.IsNullOrWhiteSpace(platformIdentity.Issuer)
                ? (string.IsNullOrWhiteSpace(platformIdentity.Authority) ? null : platformIdentity.Authority)
                : platformIdentity.Issuer,
            ValidateAudience = true,
            ValidAudience = string.IsNullOrWhiteSpace(platformIdentity.Audience) ? null : platformIdentity.Audience,
            ValidateIssuerSigningKey = !string.IsNullOrWhiteSpace(platformIdentity.SymmetricSigningKey),
            IssuerSigningKey = string.IsNullOrWhiteSpace(platformIdentity.SymmetricSigningKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(platformIdentity.SymmetricSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddCatalogAuthorization();
builder.Services.AddBuilderClientAuthorization();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AdminApiKeyValidator>();
builder.Services.AddSingleton<BuilderClientApiKeyValidator>();
builder.Services.AddCatalogDbContext(builder.Configuration);
builder.Services.AddScoped<ICatalogStore, EfCoreCatalogStore>();
builder.Services.AddScoped<IPublicCatalogQueries, PublicCatalogQueries>();
builder.Services.AddScoped<PublicCatalogQueryService>();
builder.Services.AddScoped<IPublicSourceQueries, PublicSourceQueries>();
builder.Services.AddScoped<PublicSourceQueryService>();
builder.Services.AddScoped<IAccountWorkspaceStore, AccountWorkspaceStore>();
builder.Services.AddScoped<AccountWorkspaceService>();
builder.Services.AddScoped<WorkspaceSourceService>();
builder.Services.AddScoped<RuntimeConfigurationService>();
builder.Services.AddScoped<PlatformIdentityReader>();
builder.Services.AddScoped<CustomerSessionIdentityReader>();
builder.Services.AddScoped<TrustedHeaderWorkspaceIdentityReader>();
builder.Services.AddScoped<WorkspaceAccessResolver>();
builder.Services.AddHostedService<PlatformIdentityConfigurationValidator>();
builder.Services.AddScoped<IWorkspaceIdentityReader>(services => new CompositeWorkspaceIdentityReader([
    services.GetRequiredService<PlatformIdentityReader>(),
    services.GetRequiredService<CustomerSessionIdentityReader>(),
    services.GetRequiredService<TrustedHeaderWorkspaceIdentityReader>()
]));
builder.Services.AddSingleton<PublicCatalogCache>();
builder.Services.AddSingleton<IPublicCatalogCacheInvalidator>(services => services.GetRequiredService<PublicCatalogCache>());
builder.Services.AddScoped<IPackageSourceStore, PackageSourceStore>();
builder.Services.AddScoped<PackageSourceService>();
builder.Services.AddScoped<ISyncCatalogStore, SyncCatalogStore>();
builder.Services.AddScoped<ISyncRunStore, SyncRunStore>();
builder.Services.AddScoped<IApprovalStore, ApprovalStore>();
builder.Services.AddScoped<ICompatibilityQueries, CompatibilityQueries>();
builder.Services.AddScoped<IRuntimeConfigurationStore, RuntimeConfigurationStore>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<CompatibilityCheckService>();
builder.Services.AddScoped<IPackageCompatibilityService>(services => services.GetRequiredService<CompatibilityCheckService>());
builder.Services.AddScoped<DeploymentCockpitService>();
builder.Services.AddScoped<IWorkspaceDeploymentStore, DeploymentWorkspaceStore>();
builder.Services.AddScoped<IWorkspaceDeploymentTierStore, DeploymentWorkspaceStore>();
builder.Services.AddScoped<IWorkspaceArtifactStore, DeploymentWorkspaceStore>();
builder.Services.AddScoped<IWorkspacePermissionStore, DeploymentWorkspaceStore>();
builder.Services.AddScoped<IWorkspaceDeploymentMutationStore, DeploymentWorkspaceStore>();
builder.Services.AddScoped<IWorkspaceDeploymentCommandStore, DeploymentWorkspaceStore>();
builder.Services.AddScoped<WorkspaceDeploymentService>();
builder.Services.AddScoped<DeploymentTierService>();
builder.Services.AddSingleton<IArtifactTypeRegistry, ArtifactTypeRegistry>();
builder.Services.AddSingleton<ArtifactEnvelopeValidator>();
builder.Services.AddScoped<IDeploymentArtifactReader, DeploymentArtifactReader>();
builder.Services.AddScoped<WorkspaceArtifactService>();
builder.Services.AddScoped<WorkspacePermissionService>();
builder.Services.AddScoped<DeploymentValidationService>();
builder.Services.AddScoped<DeploymentPromotionService>();
builder.Services.AddHttpClient<IEngineHealthProbe, HttpEngineHealthProbe>(client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddScoped<EngineHealthService>();
builder.Services.AddScoped<DeploymentRunService>();
builder.Services.AddScoped<DeploymentCommandService>();
builder.Services.Configure<DeploymentWebhookDispatchOptions>(builder.Configuration.GetSection("Deployment:WebhookDispatch"));
builder.Services.AddHttpClient<IDeploymentWebhookSender, HttpDeploymentWebhookSender>(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<DeploymentWebhookDispatchService>(services =>
    new DeploymentWebhookDispatchService(
        services.GetRequiredService<IWorkspaceDeploymentCommandStore>(),
        services.GetRequiredService<IDeploymentWebhookSender>(),
        services.GetRequiredService<IOptions<DeploymentWebhookDispatchOptions>>().Value,
        services.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<DeploymentQueueWorker>();
builder.Services.AddScoped<RuntimeControlService>();
builder.Services.AddScoped<ConfirmationService>();
builder.Services.AddScoped<ObservabilityDriftService>();
var deploymentQueueWorkerEnabled = builder.Configuration.GetValue("Deployment:QueueWorker:Enabled", false);
if (deploymentQueueWorkerEnabled && !builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<DeploymentQueueHostedService>();
var webhookDispatchEnabled = builder.Configuration.GetValue("Deployment:WebhookDispatch:Enabled", false);
if (webhookDispatchEnabled && !builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<DeploymentWebhookDispatchHostedService>();
builder.Services.AddScoped<IPackageVersionDiscoveryClient, NuGetPackageSourceClient>();
builder.Services.AddScoped<IPackageArchiveDownloader, NuGetSyncPackageDownloader>();
builder.Services.AddScoped<IPackageArchiveManifestReader, PackageArchiveManifestReader>();
builder.Services.AddScoped<ManifestIngestionService>();
builder.Services.AddScoped<PackageSyncService>();
builder.Services.AddScoped<SyncRunCleanupService>();
builder.Services.AddSingleton<PackageSourceValidator>();
builder.Services.AddSingleton<PackageSourcePatternMatcher>();
builder.Services.AddSingleton<ManifestValidator>();
builder.Services.AddSingleton<ApprovalPolicy>();
builder.Services.AddSingleton<VersionRangeEvaluator>();
builder.Services.AddSingleton<InfrastructureProviderCatalog>();
builder.Services.AddSingleton<RuntimeImageCatalog>();
builder.Services.AddSingleton<RuntimeImageValidator>();
builder.Services.AddSingleton<BundleFindingPolicy>();
builder.Services.AddSingleton<BundleFilePolicy>();
builder.Services.AddScoped<Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.AppSettingsBundleRenderer>();
builder.Services.AddScoped<Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.PackageLockBundleRenderer>();
builder.Services.AddScoped<Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.EnvExampleBundleRenderer>();
builder.Services.AddScoped<Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.ReadmeBundleRenderer>();
builder.Services.AddScoped<Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers.ProgramReferenceBundleRenderer>();
builder.Services.AddScoped<IDeploymentTemplateRenderer, DockerComposeBundleRenderer>();
builder.Services.AddScoped<IDeploymentTemplateRenderer, AzureContainerAppsTemplateRenderer>();
builder.Services.AddScoped<IDeploymentTemplateRenderer, KubernetesHelmTemplateRenderer>();
builder.Services.AddScoped<DeploymentTemplateRegistry>();
builder.Services.AddScoped<BundleGenerationService>();
builder.Services.AddScoped<BuilderPlannerService>();
builder.Services.AddSingleton<SyncConcurrencyGuard>();
builder.Services.AddSingleton<SourceSyncActivityTracker>();
builder.Services.AddSingleton<SyncRunCancellationRegistry>();
builder.Services.AddSingleton<ManualSyncQueue>();
builder.Services.AddSingleton<PublicCatalogVisibilityPolicy>();
builder.Services.AddSingleton<PackageVersionPolicy>();
builder.Services.AddSingleton<ISyncDiagnostics, NoopSyncDiagnostics>();
builder.Services.AddHostedService<ManualSyncHostedService>();
builder.Services.AddHostedService<ScheduledSyncHostedService>();

var app = builder.Build();

var runtimeImageFindings = app.Services.GetRequiredService<RuntimeImageValidator>()
    .Validate(app.Services.GetRequiredService<RuntimeImageCatalog>().ListImages());
if (runtimeImageFindings.Count > 0)
    throw new InvalidOperationException($"Runtime image catalog is invalid: {string.Join(" ", runtimeImageFindings.Select(x => $"{x.Code} {x.Scope}"))}");

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAdminDashboardAuthentication();
app.UseAdminDashboardRequestForgeryGuard();
app.UseStaticFiles();
app.UseAuthorization();

app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/", () => "Elsa Platform API");
if (!AdminConsoleAssetsExist(app.Environment) &&
    GetAdminConsoleDevelopmentUrl(app.Configuration) is { } adminConsoleDevelopmentUrl)
{
    app.MapMethods("/admin/{*path}", [HttpMethods.Get, HttpMethods.Head], (
        HttpContext context,
        IHttpClientFactory httpClientFactory) =>
        ProxyAdminConsoleDevelopmentServerAsync(context, httpClientFactory, adminConsoleDevelopmentUrl));
}
else if (!AdminConsoleAssetsExist(app.Environment))
{
    app.MapGet("/admin/", () => Results.Content(AdminConsoleFallbackPage(), "text/html"));
    app.MapGet("/admin/{*path:nonfile}", () => Results.Content(AdminConsoleFallbackPage(), "text/html"));
}
app.MapCustomerAuthEndpoints();
app.MapAdminDashboardAuthEndpoints();
app.MapPublicPackageEndpoints();
app.MapPublicSourceEndpoints();
app.MapPublicFeatureEndpoints();
app.MapBuilderEndpoints();
app.MapCompatibilityEndpoints();
app.MapWorkspaceMeEndpoints();
app.MapOrganizationWorkspaceEndpoints();
app.MapWorkspaceSourceEndpoints();
app.MapWorkspacePackageEndpoints();
app.MapWorkspaceBuilderEndpoints();
app.MapWorkspaceRuntimeConfigurationEndpoints();
app.MapWorkspaceDeploymentEndpoints();
app.MapWorkspaceArtifactEndpoints();
app.MapRuntimeCommandEndpoints();
app.MapAdminApplicationEndpoints();
app.MapAdminSourceEndpoints();
app.MapAdminSyncEndpoints();
app.MapAdminPackageEndpoints();
app.MapAdminApprovalEndpoints();
app.MapAdminValidationEndpoints();
app.MapAdminWorkspaceEntitlementEndpoints();
if (AdminConsoleAssetsExist(app.Environment))
    app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");

app.Run();

static bool AdminConsoleAssetsExist(IWebHostEnvironment environment) =>
    !string.IsNullOrWhiteSpace(environment.WebRootPath) &&
    File.Exists(Path.Combine(environment.WebRootPath, "admin", "index.html"));

static Uri? GetAdminConsoleDevelopmentUrl(IConfiguration configuration)
{
    var value = configuration[AdminDashboardAuthenticationDefaults.DevelopmentUrlConfigurationKey];
    return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        ? uri
        : null;
}

static async Task ProxyAdminConsoleDevelopmentServerAsync(
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    Uri adminConsoleDevelopmentUrl)
{
    var targetUri = GetAdminConsoleDevelopmentProxyUri(adminConsoleDevelopmentUrl, context.Request.Path, context.Request.QueryString);
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
    CopyProxyRequestHeaders(context.Request, request);

    var client = httpClientFactory.CreateClient(AdminDashboardAuthenticationDefaults.DevelopmentUrlConfigurationKey);
    using var response = await SendAdminConsoleDevelopmentProxyRequestAsync(
        client,
        request,
        context,
        adminConsoleDevelopmentUrl);
    if (response is null)
        return;

    context.Response.StatusCode = (int)response.StatusCode;
    CopyProxyResponseHeaders(response, context.Response);

    if (!HttpMethods.IsHead(context.Request.Method))
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
}

static async Task<HttpResponseMessage?> SendAdminConsoleDevelopmentProxyRequestAsync(
    HttpClient client,
    HttpRequestMessage request,
    HttpContext context,
    Uri adminConsoleDevelopmentUrl)
{
    try
    {
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    }
    catch (HttpRequestException)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/html";
        if (!HttpMethods.IsHead(context.Request.Method))
            await context.Response.WriteAsync(AdminConsoleDevelopmentUnavailablePage(adminConsoleDevelopmentUrl), context.RequestAborted);
        return null;
    }
}

static Uri GetAdminConsoleDevelopmentProxyUri(Uri adminConsoleDevelopmentUrl, PathString requestPath, QueryString queryString)
{
    var basePath = adminConsoleDevelopmentUrl.AbsolutePath.TrimEnd('/');
    var path = requestPath.Value ?? "/admin/";
    var builder = new UriBuilder(adminConsoleDevelopmentUrl)
    {
        Path = $"{basePath}{path}",
        Query = queryString.HasValue ? queryString.Value![1..] : string.Empty
    };
    return builder.Uri;
}

static void CopyProxyRequestHeaders(HttpRequest source, HttpRequestMessage target)
{
    foreach (var header in source.Headers)
    {
        if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!target.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            target.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    }
}

static void CopyProxyResponseHeaders(HttpResponseMessage source, HttpResponse target)
{
    foreach (var header in source.Headers)
        target.Headers[header.Key] = header.Value.ToArray();

    foreach (var header in source.Content.Headers)
        target.Headers[header.Key] = header.Value.ToArray();

    target.Headers.Remove("transfer-encoding");
}

static string AdminConsoleFallbackPage() =>
    """
    <!doctype html>
    <html lang="en">
      <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Elsa Platform Console</title>
        <style>
          body { margin: 0; min-height: 100vh; display: grid; place-items: center; font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; background: #f8fafc; color: #0f172a; }
          main { width: min(440px, calc(100vw - 32px)); border: 1px solid #e2e8f0; border-radius: 8px; background: #fff; padding: 24px; box-shadow: 0 16px 40px rgb(15 23 42 / 0.08); }
          h1 { margin: 0 0 8px; font-size: 1.35rem; }
          p { margin: 0 0 20px; color: #475569; line-height: 1.5; }
          a { display: inline-flex; min-height: 40px; align-items: center; justify-content: center; border-radius: 6px; background: #2563eb; color: #fff; padding: 0 16px; font-weight: 600; text-decoration: none; }
        </style>
      </head>
      <body>
        <main>
          <h1>Elsa Platform Console</h1>
          <p>Sign in with the configured local identity provider to continue.</p>
          <a href="/api/auth/login?returnUrl=%2Fadmin%2Foverview">Sign in</a>
        </main>
      </body>
    </html>
    """;

static string AdminConsoleDevelopmentUnavailablePage(Uri adminConsoleDevelopmentUrl) =>
    $$"""
    <!doctype html>
    <html lang="en">
      <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Elsa Platform Console</title>
        <style>
          body { margin: 0; min-height: 100vh; display: grid; place-items: center; font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; background: #f8fafc; color: #0f172a; }
          main { width: min(520px, calc(100vw - 32px)); border: 1px solid #e2e8f0; border-radius: 8px; background: #fff; padding: 24px; box-shadow: 0 16px 40px rgb(15 23 42 / 0.08); }
          h1 { margin: 0 0 8px; font-size: 1.35rem; }
          p { margin: 0 0 20px; color: #475569; line-height: 1.5; }
          code { border-radius: 4px; background: #f1f5f9; padding: 2px 5px; }
        </style>
      </head>
      <body>
        <main>
          <h1>Elsa Platform Console</h1>
          <p>The local console dev server is not responding at <code>{{adminConsoleDevelopmentUrl}}</code>.</p>
          <p>Start the Aspire console resource or run <code>npm run dev</code> in <code>src/Elsa.Platform.Console</code>, then refresh this page.</p>
        </main>
      </body>
    </html>
    """;

[UsedImplicitly]
public partial class Program;
