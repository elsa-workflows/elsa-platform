using System.Net;
using Elsa.Platform.PackageCatalog.Api.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.PackageCatalog.Api.Tests;

public sealed class CustomerAuthenticationTests
{
    [Fact]
    public async Task Session_reports_login_disabled_when_customer_oidc_is_not_configured()
    {
        await using var app = new CatalogApiTestApplication(new Dictionary<string, string?>
        {
            [$"{PlatformIdentityDefaults.ConfigurationSection}:Authority"] = "",
            [$"{PlatformIdentityDefaults.ConfigurationSection}:ClientId"] = ""
        });

        var response = await app.CreateClient().GetCatalogJsonAsync<CustomerAuthSessionResponse>(CustomerAuthenticationDefaults.SessionPath);

        response!.LoginEnabled.Should().BeFalse();
        response.Authenticated.Should().BeFalse();
        response.LoginPath.Should().Be(CustomerAuthenticationDefaults.LoginPath);
        response.LogoutPath.Should().Be(CustomerAuthenticationDefaults.LogoutPath);
    }

    [Fact]
    public async Task Login_fails_closed_when_customer_oidc_is_not_configured()
    {
        await using var app = new CatalogApiTestApplication(new Dictionary<string, string?>
        {
            [$"{PlatformIdentityDefaults.ConfigurationSection}:Authority"] = "",
            [$"{PlatformIdentityDefaults.ConfigurationSection}:ClientId"] = ""
        });

        var response = await app.CreateClient(new() { AllowAutoRedirect = false })
            .GetAsync($"{CustomerAuthenticationDefaults.LoginPath}?returnUrl=/admin/runtime-builder");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public void Customer_session_cookie_is_separate_from_operator_cookie()
    {
        using var app = new CatalogApiTestApplication();
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        var customer = options.Get(CustomerAuthenticationDefaults.CookieScheme);
        var admin = options.Get(AdminDashboardAuthenticationDefaults.Scheme);

        customer.Cookie.Name.Should().Be(CustomerAuthenticationDefaults.CookieName);
        customer.Cookie.Name.Should().NotBe(admin.Cookie.Name);
        customer.Cookie.HttpOnly.Should().BeTrue();
        customer.ExpireTimeSpan.Should().Be(CustomerAuthenticationDefaults.SessionLifetime);
        customer.SlidingExpiration.Should().BeTrue();
    }
}
