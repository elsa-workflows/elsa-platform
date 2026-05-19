using System.Net;
using Microsoft.Net.Http.Headers;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Elsa.Catalog.Api.Admin.Sources;
using Elsa.Catalog.Api.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Catalog.Api.Tests;

public sealed class AdminDashboardAuthenticationTests
{
    [Fact]
    public async Task Dashboard_route_redirects_anonymous_browser_to_login()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/overview");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should().StartWith("/admin/login");
    }

    [Fact]
    public async Task Dashboard_asset_rejects_anonymous_non_browser_request()
    {
        await using var app = new CatalogApiTestApplication();
        var response = await app.CreateClient(new() { AllowAutoRedirect = false })
            .GetAsync("/admin/assets/index.js");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_page_is_available_anonymously()
    {
        await using var app = new CatalogApiTestApplication();
        var response = await app.CreateClient().GetAsync(AdminDashboardAuthenticationDefaults.LoginPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Elsa Catalog Admin");
    }

    [Fact]
    public async Task Login_rejects_invalid_admin_key()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, "wrong");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeFalse();
    }

    [Fact]
    public async Task Login_rejects_when_admin_key_is_not_configured()
    {
        await using var app = new CatalogApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ApiKeyAuthenticationDefaults.ConfigurationKey] = ""
                }));
        });
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, "local-dev-key");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeFalse();
    }

    [Fact]
    public void Dashboard_cookie_uses_expected_security_options()
    {
        using var app = new CatalogApiTestApplication();

        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AdminDashboardAuthenticationDefaults.Scheme);

        options.Cookie.Name.Should().Be(AdminDashboardAuthenticationDefaults.CookieName);
        options.Cookie.HttpOnly.Should().BeTrue();
        options.ExpireTimeSpan.Should().Be(AdminDashboardAuthenticationDefaults.SessionLifetime);
        options.SlidingExpiration.Should().BeTrue();
    }

    [Fact]
    public async Task Login_ignores_unsafe_return_url()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, "local-dev-key", "https://evil.example/admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().Be(AdminDashboardAuthenticationDefaults.DefaultReturnPath);
    }

    [Fact]
    public async Task Login_with_valid_admin_key_authorizes_admin_api_with_cookie()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        var login = await PostLoginAsync(client, "local-dev-key");

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location.Should().Be("/admin/overview");

        var sources = await client.GetAsync("/api/admin/sources");

        sources.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await sources.Content.ReadFromJsonAsync<List<AdminSourceResponse>>();
        payload.Should().NotBeNull();
    }

    [Fact]
    public async Task Logout_clears_dashboard_session()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        await PostLoginAsync(client, "local-dev-key");

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, AdminDashboardAuthenticationDefaults.LogoutPath);
        logoutRequest.Headers.Referrer = new Uri("http://localhost/admin/overview");
        var logout = await client.SendAsync(logoutRequest);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/overview");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var dashboard = await client.SendAsync(request);

        logout.StatusCode.Should().Be(HttpStatusCode.Redirect);
        logout.Headers.Location.Should().Be(AdminDashboardAuthenticationDefaults.LoginPath);
        dashboard.StatusCode.Should().Be(HttpStatusCode.Redirect);
        dashboard.Headers.Location!.OriginalString.Should().StartWith("/admin/login");
    }

    [Fact]
    public async Task Logout_rejects_cross_site_post()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        await PostLoginAsync(client, "local-dev-key");
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, AdminDashboardAuthenticationDefaults.LogoutPath);
        logoutRequest.Headers.Add("Origin", "https://evil.example");

        var logout = await client.SendAsync(logoutRequest);
        var sources = await client.GetAsync("/api/admin/sources");

        logout.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        sources.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Public_endpoint_remains_anonymous()
    {
        await using var app = new CatalogApiTestApplication();
        var response = await app.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cookie_authenticated_admin_api_mutation_rejects_cross_origin_request()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        await PostLoginAsync(client, "local-dev-key");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Add(HeaderNames.Origin, "https://evil.example");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cookie_authenticated_admin_api_mutation_accepts_same_origin_request()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        await PostLoginAsync(client, "local-dev-key");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Add(HeaderNames.Origin, "http://localhost");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cookie_authenticated_admin_api_mutation_accepts_same_origin_referer_fallback()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        await PostLoginAsync(client, "local-dev-key");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Referrer = new Uri("http://localhost/admin/overview");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cookie_authenticated_admin_api_mutation_rejects_missing_origin_and_referer()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        await PostLoginAsync(client, "local-dev-key");

        var response = await client.PostAsync("/api/admin/sync/packages/Elsa.Workflows", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Same_origin_validation_uses_effective_request_host()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        await PostLoginAsync(client, "local-dev-key");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Host = "catalog.example";
        request.Headers.Add(HeaderNames.Origin, "http://catalog.example");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Api_key_authenticated_admin_api_mutation_bypasses_browser_origin_check()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");
        request.Headers.Add(HeaderNames.Origin, "https://evil.example");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_throttles_after_repeated_failed_attempts()
    {
        var time = new FakeTimeProvider();
        await using var app = new CatalogApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse("203.0.113.10")));
            });
        });
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        for (var i = 0; i < AdminDashboardAuthenticationDefaults.LoginThrottleFailureThreshold; i++)
        {
            var failure = await PostLoginAsync(client, "wrong");
            failure.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var throttled = await PostLoginAsync(client, "wrong");

        throttled.StatusCode.Should().Be((HttpStatusCode)StatusCodes.Status429TooManyRequests);
        throttled.Headers.RetryAfter!.Delta.Should().Be(AdminDashboardAuthenticationDefaults.LoginThrottleDelay);
    }

    [Fact]
    public async Task Login_throttle_retry_after_uses_remaining_delay()
    {
        var time = new FakeTimeProvider();
        await using var app = new CatalogApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse("203.0.113.10")));
            });
        });
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        for (var i = 0; i < AdminDashboardAuthenticationDefaults.LoginThrottleFailureThreshold; i++)
            await PostLoginAsync(client, "wrong");

        time.Advance(TimeSpan.FromMinutes(4));
        var throttled = await PostLoginAsync(client, "wrong");

        throttled.StatusCode.Should().Be((HttpStatusCode)StatusCodes.Status429TooManyRequests);
        throttled.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Login_throttle_allows_attempt_after_retry_delay()
    {
        var time = new FakeTimeProvider();
        await using var app = new CatalogApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse("203.0.113.10")));
            });
        });
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        for (var i = 0; i < AdminDashboardAuthenticationDefaults.LoginThrottleFailureThreshold; i++)
            await PostLoginAsync(client, "wrong");

        time.Advance(AdminDashboardAuthenticationDefaults.LoginThrottleDelay);
        var response = await PostLoginAsync(client, "wrong");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Successful_login_resets_failed_login_throttle()
    {
        await using var app = new CatalogApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        for (var i = 0; i < AdminDashboardAuthenticationDefaults.LoginThrottleFailureThreshold - 1; i++)
            await PostLoginAsync(client, "wrong");

        var success = await PostLoginAsync(client, "local-dev-key");
        for (var i = 0; i < AdminDashboardAuthenticationDefaults.LoginThrottleFailureThreshold - 1; i++)
        {
            var failure = await PostLoginAsync(client, "wrong");
            failure.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        success.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public void Login_throttle_uses_remote_ip_as_client_key()
    {
        var throttle = new AdminDashboardLoginThrottle(TimeProvider.System);
        var firstClient = CreateContext(IPAddress.Parse("203.0.113.10"));
        var secondClient = CreateContext(IPAddress.Parse("203.0.113.11"));
        var firstDecision = throttle.Check(firstClient);

        for (var i = 0; i < AdminDashboardAuthenticationDefaults.LoginThrottleFailureThreshold; i++)
            throttle.RecordFailure(firstDecision.ClientKey);

        throttle.Check(firstClient).IsThrottled.Should().BeTrue();
        throttle.Check(secondClient).IsThrottled.Should().BeFalse();
    }

    [Fact]
    public void Login_throttle_does_not_share_state_when_remote_ip_is_missing()
    {
        var throttle = new AdminDashboardLoginThrottle(TimeProvider.System);
        var firstClient = CreateContext(null);
        var secondClient = CreateContext(null);
        var firstDecision = throttle.Check(firstClient);

        for (var i = 0; i < AdminDashboardAuthenticationDefaults.LoginThrottleFailureThreshold; i++)
            throttle.RecordFailure(firstDecision.ClientKey);

        throttle.Check(firstClient).IsThrottled.Should().BeFalse();
        throttle.Check(secondClient).IsThrottled.Should().BeFalse();
    }

    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string apiKey, string returnUrl = "/admin/overview") =>
        client.PostAsync(AdminDashboardAuthenticationDefaults.LoginPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["apiKey"] = apiKey,
            ["returnUrl"] = returnUrl
        }));

    private static HttpContext CreateContext(IPAddress? remoteIpAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIpAddress;
        return context;
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }

    private sealed class RemoteIpStartupFilter(IPAddress remoteIpAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = remoteIpAddress;
                    return nextMiddleware(context);
                });

                next(app);
            };
    }
}
