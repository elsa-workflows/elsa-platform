using System.Net;
using Elsa.Platform.Api.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Elsa.Platform.Api.Tests;

public sealed class CustomerAuthenticationTests
{
    [Fact]
    public async Task Session_reports_login_disabled_when_customer_oidc_is_not_configured()
    {
        await using var app = new PlatformApiTestApplication(new Dictionary<string, string?>
        {
            [$"{PlatformIdentityDefaults.ConfigurationSection}:Authority"] = "",
            [$"{PlatformIdentityDefaults.ConfigurationSection}:ClientId"] = ""
        });

        var response = await app.CreateClient().GetPlatformJsonAsync<CustomerAuthSessionResponse>(CustomerAuthenticationDefaults.SessionPath);

        response!.LoginEnabled.Should().BeFalse();
        response.Authenticated.Should().BeFalse();
        response.LoginPath.Should().Be(CustomerAuthenticationDefaults.LoginPath);
        response.LogoutPath.Should().Be(CustomerAuthenticationDefaults.LogoutPath);
    }

    [Fact]
    public async Task Login_fails_closed_when_customer_oidc_is_not_configured()
    {
        await using var app = new PlatformApiTestApplication(new Dictionary<string, string?>
        {
            [$"{PlatformIdentityDefaults.ConfigurationSection}:Authority"] = "",
            [$"{PlatformIdentityDefaults.ConfigurationSection}:ClientId"] = ""
        });

        var response = await app.CreateClient(new() { AllowAutoRedirect = false })
            .GetAsync($"{CustomerAuthenticationDefaults.LoginPath}?returnUrl=/admin/runtime-builder");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(null, CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("/admin/runtime-builder", "/admin/runtime-builder")]
    [InlineData("relative/path", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("//evil.example/admin", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("\\\\evil.example\\admin", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("https://evil.example/admin", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("/admin/login", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("/admin/logout", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("/api/auth/login", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("/api/auth/logout", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("/api/auth/sign-in", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("/api/auth/sign-out", CustomerAuthenticationDefaults.DefaultReturnPath)]
    [InlineData("/api/auth/callback", CustomerAuthenticationDefaults.DefaultReturnPath)]
    public void Safe_return_url_accepts_only_root_relative_paths(string? returnUrl, string expected)
    {
        CustomerAuthEndpoints.GetSafeReturnUrl(returnUrl).Should().Be(expected);
    }

    [Fact]
    public void Customer_session_cookie_is_separate_from_operator_cookie()
    {
        using var app = new PlatformApiTestApplication();
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        var customer = options.Get(CustomerAuthenticationDefaults.CookieScheme);
        var admin = options.Get(AdminDashboardAuthenticationDefaults.Scheme);

        customer.Cookie.Name.Should().Be(CustomerAuthenticationDefaults.CookieName);
        customer.Cookie.Name.Should().NotBe(admin.Cookie.Name);
        customer.Cookie.HttpOnly.Should().BeTrue();
        customer.ExpireTimeSpan.Should().Be(CustomerAuthenticationDefaults.SessionLifetime);
        customer.SlidingExpiration.Should().BeTrue();
    }

    [Fact]
    public void Customer_oidc_challenge_uses_callback_scoped_cookies_without_pushed_authorization()
    {
        var options = new OpenIdConnectOptions();
        CustomerOidcOptionsConfigurator.Configure(options, new PlatformIdentityOptions
        {
            Authority = "https://identity.example/realms/elsa-platform",
            ClientId = "elsa-platform-console",
            RedirectUri = CustomerAuthenticationDefaults.CallbackPath
        });

        options.PushedAuthorizationBehavior.Should().Be(PushedAuthorizationBehavior.Disable);
        options.CorrelationCookie.Path.Should().Be(CustomerAuthenticationDefaults.CallbackPath);
        options.NonceCookie.Path.Should().Be(CustomerAuthenticationDefaults.CallbackPath);
    }

    [Fact]
    public async Task Logout_rejects_cross_site_post()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, CustomerAuthenticationDefaults.LogoutPath);
        request.Headers.Add(HeaderNames.Origin, "https://evil.example");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Logout_accepts_same_origin_post()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, CustomerAuthenticationDefaults.LogoutPath);
        request.Headers.Add(HeaderNames.Origin, "http://localhost");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
