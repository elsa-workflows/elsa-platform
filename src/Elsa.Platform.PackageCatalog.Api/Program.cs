using System.Text.Json.Serialization;
using Elsa.Platform.PackageCatalog.Api.Admin.Application;
using Elsa.Platform.PackageCatalog.Api.Admin.Workspaces;
using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Api.Admin.Packages;
using Elsa.Platform.PackageCatalog.Api.Admin.Sources;
using Elsa.Platform.PackageCatalog.Api.Admin.Sync;
using Elsa.Platform.PackageCatalog.Api.Public.Builder;
using Elsa.Platform.PackageCatalog.Api.Public.Compatibility;
using Elsa.Platform.PackageCatalog.Api.Public.Features;
using Elsa.Platform.PackageCatalog.Api.Public.Packages;
using Elsa.Platform.PackageCatalog.Api.Public.Sources;
using Elsa.Platform.PackageCatalog.Api.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Approvals;
using Elsa.Platform.PackageCatalog.Core.Builder;
using Elsa.Platform.PackageCatalog.Core.Builder.Planner;
using Elsa.Platform.PackageCatalog.Core.DeploymentTemplates;
using Elsa.Platform.PackageCatalog.Core.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Manifests;
using Elsa.Platform.PackageCatalog.Core.Packaging;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Persistence;
using Elsa.Platform.PackageCatalog.Core.RuntimeConfigurations;
using Elsa.Platform.PackageCatalog.Core.Sources;
using Elsa.Platform.PackageCatalog.Core.Sync;
using Elsa.Platform.PackageCatalog.Sources.NuGet;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageManifests.Validation;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
});
builder.Services.AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationDefaults.Scheme, _ => { })
    .AddScheme<AuthenticationSchemeOptions, BuilderClientApiKeyAuthenticationHandler>(BuilderClientApiKeyAuthenticationDefaults.Scheme, _ => { })
    .AddCookie(AdminDashboardAuthenticationDefaults.Scheme, options =>
    {
        options.Cookie.Name = AdminDashboardAuthenticationDefaults.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = AdminDashboardAuthenticationDefaults.SessionLifetime;
        options.LoginPath = AdminDashboardAuthenticationDefaults.LoginPath;
        options.LogoutPath = AdminDashboardAuthenticationDefaults.LogoutPath;
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddCatalogAuthorization();
builder.Services.AddBuilderClientAuthorization();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AdminApiKeyValidator>();
builder.Services.AddSingleton<BuilderClientApiKeyValidator>();
builder.Services.AddSingleton<AdminDashboardLoginThrottle>();
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
builder.Services.AddScoped<IWorkspaceIdentityReader, TrustedHeaderWorkspaceIdentityReader>();
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
builder.Services.AddScoped<Elsa.Platform.PackageCatalog.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.PackageCatalog.Core.Builder.Renderers.AppSettingsBundleRenderer>();
builder.Services.AddScoped<Elsa.Platform.PackageCatalog.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.PackageCatalog.Core.Builder.Renderers.PackageLockBundleRenderer>();
builder.Services.AddScoped<Elsa.Platform.PackageCatalog.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.PackageCatalog.Core.Builder.Renderers.EnvExampleBundleRenderer>();
builder.Services.AddScoped<Elsa.Platform.PackageCatalog.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.PackageCatalog.Core.Builder.Renderers.ReadmeBundleRenderer>();
builder.Services.AddScoped<Elsa.Platform.PackageCatalog.Core.Builder.Renderers.IBundleFileRenderer, Elsa.Platform.PackageCatalog.Core.Builder.Renderers.ProgramReferenceBundleRenderer>();
builder.Services.AddScoped<IDeploymentTemplateRenderer, Elsa.Platform.PackageCatalog.Core.Builder.Renderers.DockerComposeBundleRenderer>();
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
app.MapGet("/", () => "Elsa Package Catalog");
app.MapGet("/admin", () => Results.Redirect("/admin/overview"));
app.MapAdminDashboardAuthEndpoints();
app.MapPublicPackageEndpoints();
app.MapPublicSourceEndpoints();
app.MapPublicFeatureEndpoints();
app.MapBuilderEndpoints();
app.MapCompatibilityEndpoints();
app.MapWorkspaceMeEndpoints();
app.MapWorkspaceSourceEndpoints();
app.MapWorkspacePackageEndpoints();
app.MapWorkspaceBuilderEndpoints();
app.MapWorkspaceRuntimeConfigurationEndpoints();
app.MapAdminApplicationEndpoints();
app.MapAdminSourceEndpoints();
app.MapAdminSyncEndpoints();
app.MapAdminPackageEndpoints();
app.MapAdminApprovalEndpoints();
app.MapAdminValidationEndpoints();
app.MapAdminWorkspaceEntitlementEndpoints();
app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");

app.Run();

[UsedImplicitly]
public partial class Program;
