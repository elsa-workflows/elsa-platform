using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Elsa.Platform.Api.Admin.Sources;
using Elsa.Platform.Api.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Elsa.Platform.Api.Tests;

public sealed class AdminDashboardAuthenticationTests
{
    [Fact]
    public async Task Dashboard_route_redirects_anonymous_browser_to_platform_sign_in()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/overview");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith($"{CustomerAuthenticationDefaults.LoginPath}?returnUrl=");
    }

    [Fact]
    public async Task Dashboard_asset_rejects_anonymous_non_browser_request()
    {
        await using var app = new PlatformApiTestApplication();
        var response = await app.CreateClient(new() { AllowAutoRedirect = false })
            .GetAsync("/admin/assets/index.js");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Legacy_login_path_redirects_to_platform_sign_in()
    {
        await using var app = new PlatformApiTestApplication();
        var response = await app.CreateClient(new() { AllowAutoRedirect = false })
            .GetAsync($"{AdminDashboardAuthenticationDefaults.LoginPath}?returnUrl=/admin/sources");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().Be($"{CustomerAuthenticationDefaults.LoginPath}?returnUrl=%2Fadmin%2Fsources");
    }

    [Fact]
    public async Task Legacy_login_path_ignores_unsafe_return_url()
    {
        await using var app = new PlatformApiTestApplication();
        var response = await app.CreateClient(new() { AllowAutoRedirect = false })
            .GetAsync($"{AdminDashboardAuthenticationDefaults.LoginPath}?returnUrl=https://evil.example/admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().Be($"{CustomerAuthenticationDefaults.LoginPath}?returnUrl={Uri.EscapeDataString(AdminDashboardAuthenticationDefaults.DefaultReturnPath)}");
    }

    [Fact]
    public async Task Legacy_login_path_ignores_logout_return_url()
    {
        await using var app = new PlatformApiTestApplication();
        var response = await app.CreateClient(new() { AllowAutoRedirect = false })
            .GetAsync($"{AdminDashboardAuthenticationDefaults.LoginPath}?returnUrl={AdminDashboardAuthenticationDefaults.LogoutPath}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().Be($"{CustomerAuthenticationDefaults.LoginPath}?returnUrl={Uri.EscapeDataString(AdminDashboardAuthenticationDefaults.DefaultReturnPath)}");
    }

    [Fact]
    public async Task Legacy_logout_post_preserves_post_method_for_customer_logout()
    {
        await using var app = new PlatformApiTestApplication();
        var response = await app.CreateClient(new() { AllowAutoRedirect = false })
            .PostAsync(AdminDashboardAuthenticationDefaults.LogoutPath, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location.Should().Be($"{CustomerAuthenticationDefaults.LogoutPath}?returnUrl={Uri.EscapeDataString(AdminDashboardAuthenticationDefaults.DefaultReturnPath)}");
    }

    [Fact]
    public async Task Api_key_authorizes_admin_api_for_machine_access()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var sources = await client.GetPlatformJsonAsync<List<AdminSourceResponse>>("/api/admin/sources");

        sources.Should().NotBeNull();
    }

    [Fact]
    public async Task Platform_admin_session_authorizes_admin_api()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        AddPlatformSessionCookie(app, client, new Claim("role", AdminAuthorization.PlatformAdminRole));

        var response = await client.GetAsync("/api/admin/sources");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Platform_session_without_admin_role_is_forbidden_for_admin_api()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        AddPlatformSessionCookie(app, client);

        var response = await client.GetAsync("/api/admin/sources");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Platform_admin_api_mutation_rejects_cross_origin_request()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        AddPlatformSessionCookie(app, client, new Claim("role", AdminAuthorization.PlatformAdminRole));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Add(HeaderNames.Origin, "https://evil.example");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Platform_admin_api_mutation_accepts_same_origin_request()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        AddPlatformSessionCookie(app, client, new Claim("role", AdminAuthorization.PlatformAdminRole));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Add(HeaderNames.Origin, "http://localhost");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Platform_admin_api_mutation_accepts_same_origin_referer_fallback()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        AddPlatformSessionCookie(app, client, new Claim("role", AdminAuthorization.PlatformAdminRole));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Referrer = new Uri("http://localhost/admin/overview");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Platform_admin_api_mutation_rejects_missing_origin_and_referer()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        AddPlatformSessionCookie(app, client, new Claim("role", AdminAuthorization.PlatformAdminRole));

        var response = await client.PostAsync("/api/admin/sync/packages/Elsa.Workflows", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_api_mutation_fails_closed_without_api_key_or_valid_platform_session()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Add(HeaderNames.Origin, "http://localhost");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Same_origin_validation_uses_effective_request_host()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        AddPlatformSessionCookie(app, client, new Claim("role", AdminAuthorization.PlatformAdminRole));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Host = "catalog.example";
        request.Headers.Add(HeaderNames.Origin, "http://catalog.example");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Same_origin_validation_uses_forwarded_scheme()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        AddPlatformSessionCookie(app, client, new Claim("role", AdminAuthorization.PlatformAdminRole));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Host = "catalog.example";
        request.Headers.Add(HeaderNames.Origin, "https://catalog.example");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Same_origin_validation_uses_forwarded_host()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        AddPlatformSessionCookie(app, client, new Claim("role", AdminAuthorization.PlatformAdminRole));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Host = "internal.example";
        request.Headers.Add(HeaderNames.Origin, "https://catalog.example");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "catalog.example");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Api_key_authenticated_admin_api_mutation_bypasses_browser_origin_check()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/sync/packages/Elsa.Workflows");
        request.Headers.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");
        request.Headers.Add(HeaderNames.Origin, "https://evil.example");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Public_endpoint_remains_anonymous()
    {
        await using var app = new PlatformApiTestApplication();
        var response = await app.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static void AddPlatformSessionCookie(PlatformApiTestApplication app, HttpClient client, params Claim[] additionalClaims)
    {
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CustomerAuthenticationDefaults.CookieScheme);
        var claims = new List<Claim>
        {
            new("sub", "admin-user"),
            new("iss", PlatformApiTestApplication.TestPlatformIdentityIssuer),
            new("name", "Admin User")
        };
        claims.AddRange(additionalClaims);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CustomerAuthenticationDefaults.CookieScheme));
        var ticket = new AuthenticationTicket(principal, CustomerAuthenticationDefaults.CookieScheme);
        var cookie = options.TicketDataFormat.Protect(ticket);
        client.DefaultRequestHeaders.Add("Cookie", $"{CustomerAuthenticationDefaults.CookieName}={cookie}");
    }
}
