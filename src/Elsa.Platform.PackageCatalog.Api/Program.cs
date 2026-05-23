using System.Text;
using System.Text.Json.Serialization;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.PackageCatalog.Abstractions.Catalog;
using Elsa.Platform.PackageCatalog.Abstractions.Compatibility;
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
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
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
    });
if (configuredPlatformIdentity.IsCustomerLoginConfigured)
{
    authentication.AddOpenIdConnect(CustomerAuthenticationDefaults.OidcScheme, _ => { });
    builder.Services.AddOptions<OpenIdConnectOptions>(CustomerAuthenticationDefaults.OidcScheme)
        .Configure<IOptions<PlatformIdentityOptions>>((options, platformIdentityOptions) =>
        {
            var platformIdentity = platformIdentityOptions.Value;
            options.SignInScheme = CustomerAuthenticationDefaults.CookieScheme;
            options.ResponseType = "code";
            options.UsePkce = true;
            options.SaveTokens = true;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = platformIdentity.RequireHttpsMetadata;
            options.Authority = string.IsNullOrWhiteSpace(platformIdentity.Authority) ? null : platformIdentity.Authority;
            options.ClientId = string.IsNullOrWhiteSpace(platformIdentity.ClientId) ? null : platformIdentity.ClientId;
            options.ClientSecret = string.IsNullOrWhiteSpace(platformIdentity.ClientSecret) ? null : platformIdentity.ClientSecret;
            options.CallbackPath = PathStringFromUri(platformIdentity.RedirectUri, CustomerAuthenticationDefaults.CallbackPath);
            options.SignedOutCallbackPath = PathStringFromUri(platformIdentity.PostLogoutRedirectUri, "/api/auth/logout-callback");
            options.SignedOutRedirectUri = string.IsNullOrWhiteSpace(platformIdentity.PostLogoutRedirectUri)
                ? CustomerAuthenticationDefaults.DefaultReturnPath
                : platformIdentity.PostLogoutRedirectUri;
            options.Scope.Clear();
            foreach (var scope in platformIdentity.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).Select(scope => scope.Trim()).Distinct(StringComparer.Ordinal))
                options.Scope.Add(scope);
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrWhiteSpace(platformIdentity.Issuer) || !string.IsNullOrWhiteSpace(platformIdentity.Authority),
                ValidIssuer = string.IsNullOrWhiteSpace(platformIdentity.Issuer)
                    ? (string.IsNullOrWhiteSpace(platformIdentity.Authority) ? null : platformIdentity.Authority)
                    : platformIdentity.Issuer,
                NameClaimType = platformIdentity.Claims.DisplayName.FirstOrDefault() ?? "name",
                RoleClaimType = "roles"
            };
            options.Events.OnTokenValidated = context =>
            {
                if (context.Properties is { } properties)
                    properties.StoreTokens(properties.GetTokens().Where(token => token.Name == "id_token"));

                return Task.CompletedTask;
            };
        });
}

authentication.AddCookie(AdminDashboardAuthenticationDefaults.Scheme, options =>
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
builder.Services.AddScoped<PlatformIdentityReader>();
builder.Services.AddScoped<CustomerSessionIdentityReader>();
builder.Services.AddScoped<TrustedHeaderWorkspaceIdentityReader>();
builder.Services.AddScoped<WorkspaceAccessResolver>();
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
builder.Services.AddSingleton<IDeploymentCockpitStore, InMemoryDeploymentCockpitStore>();
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
app.MapGet("/admin", () => Results.Redirect("/admin/overview"));
app.MapCustomerAuthEndpoints();
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
app.MapWorkspaceDeploymentEndpoints();
app.MapAdminApplicationEndpoints();
app.MapAdminSourceEndpoints();
app.MapAdminSyncEndpoints();
app.MapAdminPackageEndpoints();
app.MapAdminApprovalEndpoints();
app.MapAdminValidationEndpoints();
app.MapAdminWorkspaceEntitlementEndpoints();
app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");

app.Run();

static PathString PathStringFromUri(string? uri, string fallback)
{
    if (string.IsNullOrWhiteSpace(uri))
        return new PathString(fallback);

    if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
        return new PathString(absolute.AbsolutePath);

    return uri.StartsWith("/", StringComparison.Ordinal)
        ? new PathString(uri)
        : new PathString(fallback);
}

[UsedImplicitly]
public partial class Program;
