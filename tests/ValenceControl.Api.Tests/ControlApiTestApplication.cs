using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValenceControl.Api.Authentication;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ValenceControl.Api.Tests;

internal sealed class ControlApiTestApplication : WebApplicationFactory<Program>, IAsyncDisposable
{
    public const string TestRemoteIpHeader = "X-Test-Remote-Ip";
    public const string TestControlIdentityIssuer = "https://local.valence-control.test";
    public const string TestControlIdentityAudience = "valence-control-tests";
    public const string TestControlIdentitySigningKey = "local-test-control-identity-signing-key-change-me-12345";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"valence-control-catalog-{Guid.NewGuid():N}.db");
    private readonly IReadOnlyDictionary<string, string?> _configuration;
    private readonly Action<IServiceCollection>? _configureServices;

    public ControlApiTestApplication(
        IReadOnlyDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _configuration = configuration ?? new Dictionary<string, string?>();
        _configureServices = configureServices;
    }

    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public string ConnectionString => $"Data Source={_databasePath}";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                [ApiKeyAuthenticationDefaults.ConfigurationKey] = "local-dev-key",
                [BuilderClientApiKeyAuthenticationDefaults.ConfigurationKey] = "builder-dev-key",
                [$"{ControlIdentityDefaults.ConfigurationSection}:Provider"] = ControlIdentityProviderKind.GenericOidc.ToString(),
                [$"{ControlIdentityDefaults.ConfigurationSection}:Authority"] = "",
                [$"{ControlIdentityDefaults.ConfigurationSection}:Audience"] = TestControlIdentityAudience,
                [$"{ControlIdentityDefaults.ConfigurationSection}:Issuer"] = TestControlIdentityIssuer,
                [$"{ControlIdentityDefaults.ConfigurationSection}:SymmetricSigningKey"] = TestControlIdentitySigningKey,
                [$"{ControlIdentityDefaults.ConfigurationSection}:ClientId"] = "",
                [$"{ControlIdentityDefaults.ConfigurationSection}:ClientSecret"] = "",
                [$"{ControlIdentityDefaults.ConfigurationSection}:RequireHttpsMetadata"] = "false",
                [TrustedHeaderWorkspaceIdentityReader.EnabledConfigurationKey] = "true",
                [TrustedHeaderWorkspaceIdentityReader.AllowedProxyNetworksConfigurationKey] = "127.0.0.1/32,::1/128"
            };
            foreach (var (key, value) in _configuration)
                values[key] = value;

            configuration.AddInMemoryCollection(values);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CatalogDbContext>>();
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();

            services.AddDbContext<CatalogDbContext>(options =>
                options.UseSqlite(ConnectionString, sqlite =>
                {
                    sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly);
                }));
            services.RemoveAll<IEngineHealthProbe>();
            services.AddSingleton<IEngineHealthProbe, TestEngineHealthProbe>();
            _configureServices?.Invoke(services);
        });
    }

    public async Task SeedAsync(Func<CatalogDbContext, Task> seed)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await seed(db);
        await db.SaveChangesAsync();
    }

    private sealed class TestEngineHealthProbe : IEngineHealthProbe
    {
        public Task<EngineHealthProbeResult> ProbeAsync(WorkspaceWorkflowEngine engine, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EngineHealthProbeResult(
                false,
                engine.Version,
                engine.CertificateStatus,
                CredentialVerificationStatus.Unverified,
                "Test probe did not contact the endpoint."));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    var remoteIp = context.Request.Headers[TestRemoteIpHeader].FirstOrDefault();
                    context.Connection.RemoteIpAddress = IPAddress.TryParse(remoteIp, out var address)
                        ? address
                        : IPAddress.Loopback;
                    await nextMiddleware();
                });
                next(app);
            };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}

internal static class ControlApiJsonExtensions
{
    public static Task<T?> GetControlJsonAsync<T>(this HttpClient client, string requestUri, CancellationToken cancellationToken = default) =>
        client.GetFromJsonAsync<T>(requestUri, ControlApiTestApplication.JsonOptions, cancellationToken);

    public static Task<HttpResponseMessage> PostControlJsonAsync<T>(this HttpClient client, string requestUri, T value, CancellationToken cancellationToken = default) =>
        client.PostAsJsonAsync(requestUri, value, ControlApiTestApplication.JsonOptions, cancellationToken);

    public static Task<HttpResponseMessage> PutControlJsonAsync<T>(this HttpClient client, string requestUri, T value, CancellationToken cancellationToken = default) =>
        client.PutAsJsonAsync(requestUri, value, ControlApiTestApplication.JsonOptions, cancellationToken);

    public static Task<T?> ReadControlJsonAsync<T>(this HttpContent content, CancellationToken cancellationToken = default) =>
        content.ReadFromJsonAsync<T>(ControlApiTestApplication.JsonOptions, cancellationToken);
}
