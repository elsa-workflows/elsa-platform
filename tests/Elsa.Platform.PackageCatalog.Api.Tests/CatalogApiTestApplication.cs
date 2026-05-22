using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Platform.PackageCatalog.Api.Tests;

internal sealed class CatalogApiTestApplication : WebApplicationFactory<Program>, IAsyncDisposable
{
    public const string TestRemoteIpHeader = "X-Test-Remote-Ip";
    public const string TestPlatformIdentityIssuer = "https://local.elsa-platform.test";
    public const string TestPlatformIdentityAudience = "elsa-platform-tests";
    public const string TestPlatformIdentitySigningKey = "local-test-platform-identity-signing-key-change-me-12345";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"elsa-catalog-{Guid.NewGuid():N}.db");
    private readonly IReadOnlyDictionary<string, string?> _configuration;

    public CatalogApiTestApplication(IReadOnlyDictionary<string, string?>? configuration = null)
    {
        _configuration = configuration ?? new Dictionary<string, string?>();
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
                [$"{PlatformIdentityDefaults.ConfigurationSection}:Provider"] = PlatformIdentityProviderKind.GenericOidc.ToString(),
                [$"{PlatformIdentityDefaults.ConfigurationSection}:Audience"] = TestPlatformIdentityAudience,
                [$"{PlatformIdentityDefaults.ConfigurationSection}:Issuer"] = TestPlatformIdentityIssuer,
                [$"{PlatformIdentityDefaults.ConfigurationSection}:SymmetricSigningKey"] = TestPlatformIdentitySigningKey,
                [$"{PlatformIdentityDefaults.ConfigurationSection}:RequireHttpsMetadata"] = "false",
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

internal static class CatalogApiJsonExtensions
{
    public static Task<T?> GetCatalogJsonAsync<T>(this HttpClient client, string requestUri, CancellationToken cancellationToken = default) =>
        client.GetFromJsonAsync<T>(requestUri, CatalogApiTestApplication.JsonOptions, cancellationToken);

    public static Task<HttpResponseMessage> PostCatalogJsonAsync<T>(this HttpClient client, string requestUri, T value, CancellationToken cancellationToken = default) =>
        client.PostAsJsonAsync(requestUri, value, CatalogApiTestApplication.JsonOptions, cancellationToken);

    public static Task<HttpResponseMessage> PutCatalogJsonAsync<T>(this HttpClient client, string requestUri, T value, CancellationToken cancellationToken = default) =>
        client.PutAsJsonAsync(requestUri, value, CatalogApiTestApplication.JsonOptions, cancellationToken);

    public static Task<T?> ReadCatalogJsonAsync<T>(this HttpContent content, CancellationToken cancellationToken = default) =>
        content.ReadFromJsonAsync<T>(CatalogApiTestApplication.JsonOptions, cancellationToken);
}
