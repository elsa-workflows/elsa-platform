using System.Net;
using System.Net.Http.Json;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ElsaControl.Api.OrganizationBilling;
using ElsaControl.Api.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElsaControl.Api.Tests;

public sealed class OrganizationBillingApiTests : IAsyncLifetime
{
    private readonly FakeBillingProvider _provider = new();
    private readonly ControlApiTestApplication _app;

    public OrganizationBillingApiTests()
    {
        _app = new(
            new Dictionary<string, string?>
            {
                ["Billing:Stripe:Enabled"] = "true",
                ["Billing:Stripe:SecretKey"] = "sk_test_fake",
                ["Billing:Stripe:DefaultPriceId"] = "price_server_default",
                ["Billing:Stripe:CheckoutSuccessUrl"] = "https://console.test/success",
                ["Billing:Stripe:CheckoutCancelUrl"] = "https://console.test/cancel",
                ["Billing:Stripe:PortalReturnUrl"] = "https://console.test/billing"
            },
            services =>
            {
                services.RemoveAll<IBillingProvider>();
                services.AddSingleton<IBillingProvider>(_provider);
            });
    }

    [Fact]
    public async Task Checkout_starts_control_plane_trial_before_calling_provider()
    {
        await _app.SeedAsync(_ => Task.CompletedTask);
        var owner = _app.CreateControlIdentityClient(subject: "billing-owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;

        var response = await owner.PostControlJsonAsync($"/api/organizations/{organizationId}/billing/checkout", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadControlJsonAsync<OrganizationBillingSessionResponse>();
        Assert.Equal("https://checkout.fake.test/session", session!.Url);
        Assert.NotNull(_provider.LastCheckout);
        Assert.Equal(organizationId, _provider.LastCheckout!.OrganizationId);
        Assert.Equal("price_server_default", _provider.LastCheckout.PriceReference);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var subscription = await db.OrganizationSubscriptions.SingleAsync(x => x.OrganizationId == organizationId);
        Assert.Equal(OrganizationSubscriptionState.Trial, subscription.State);
        Assert.Equal(TimeSpan.FromDays(14), subscription.TrialEndsAt - subscription.TrialStartedAt);
    }

    [Fact]
    public async Task Organization_member_cannot_start_checkout_and_unknown_webhook_is_audit_only()
    {
        await _app.SeedAsync(_ => Task.CompletedTask);
        var owner = _app.CreateControlIdentityClient(subject: "billing-owner-2");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddMemberAsync(organizationId, "billing-member");
        var member = _app.CreateControlIdentityClient(subject: "billing-member");

        var forbidden = await member.PostControlJsonAsync($"/api/organizations/{organizationId}/billing/checkout", new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        _provider.WebhookResult = BillingWebhookNormalizationResult.UnknownEvent(new BillingProviderEvent(
            organizationId,
            BillingProviderNames.Stripe,
            "evt_unknown",
            "checkout.session.completed",
            null,
            DateTimeOffset.UtcNow,
            "sha256:" + new string('a', 64)));
        var webhook = await PostWebhookAsync("raw-unknown-body");

        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        Assert.Equal("recorded-unknown", (await webhook.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["status"]);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(1, await db.BillingProviderEvents.CountAsync());
        Assert.Null(await db.OrganizationSubscriptions.SingleOrDefaultAsync(x => x.OrganizationId == organizationId));
        var persistedText = await ReadPersistedBillingTextAsync(db);
        Assert.DoesNotContain("raw-unknown-body", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("signature-is-never-persisted", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("https://checkout.fake.test/session", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("https://portal.fake.test/session", persistedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Supported_webhook_replay_and_out_of_order_are_deterministic()
    {
        await _app.SeedAsync(_ => Task.CompletedTask);
        var owner = _app.CreateControlIdentityClient(subject: "billing-owner-3");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await owner.PostControlJsonAsync($"/api/organizations/{organizationId}/billing/checkout", new { });
        var first = new BillingProviderEvent(organizationId, BillingProviderNames.Stripe, "evt-active", "customer.subscription.updated", OrganizationSubscriptionState.Active, DateTimeOffset.UtcNow, Sha256("active"));
        _provider.WebhookResult = BillingWebhookNormalizationResult.KnownEvent(first);

        var applied = await PostWebhookAsync("raw-supported-body");
        var replayed = await PostWebhookAsync("raw-supported-body");
        var older = first with { ProviderEventId = "evt-older", OccurredAt = first.OccurredAt.AddMinutes(-1), State = OrganizationSubscriptionState.PastDue, EventType = "customer.subscription.updated", EventHash = Sha256("older") };
        _provider.WebhookResult = BillingWebhookNormalizationResult.KnownEvent(older);
        var outOfOrder = await PostWebhookAsync("raw-older-body");

        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, outOfOrder.StatusCode);
        Assert.Equal("applied", (await applied.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["status"]);
        Assert.Equal("replayed", (await replayed.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["status"]);
        Assert.Equal("ignored-out-of-order", (await outOfOrder.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["status"]);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(2, await db.BillingProviderEvents.CountAsync());
        var persistedText = await ReadPersistedBillingTextAsync(db);
        Assert.DoesNotContain("raw-supported-body", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Stripe-Signature", persistedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://checkout.fake.test/session", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("https://portal.fake.test/session", persistedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stripe_webhook_fails_closed_when_the_injected_provider_is_not_stripe()
    {
        _provider.Provider = "fake";

        var response = await PostWebhookAsync("provider-mismatch-body");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, _provider.WebhookVerificationCalls);
    }

    [Fact]
    public async Task Conflicting_webhook_facts_return_a_stable_client_error()
    {
        await _app.SeedAsync(_ => Task.CompletedTask);
        var owner = _app.CreateControlIdentityClient(subject: "billing-conflict-owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await owner.PostControlJsonAsync($"/api/organizations/{organizationId}/billing/checkout", new { });

        var first = new BillingProviderEvent(
            organizationId,
            BillingProviderNames.Stripe,
            "evt-conflicting-facts",
            "customer.subscription.updated",
            OrganizationSubscriptionState.Active,
            DateTimeOffset.UtcNow,
            Sha256("first-facts"));
        _provider.WebhookResult = BillingWebhookNormalizationResult.KnownEvent(first);
        var applied = await PostWebhookAsync("first-facts-body");

        var conflicting = first with
        {
            State = OrganizationSubscriptionState.PastDue,
            EventHash = Sha256("conflicting-facts")
        };
        _provider.WebhookResult = BillingWebhookNormalizationResult.KnownEvent(conflicting);
        var conflict = await PostWebhookAsync("conflicting-facts-body");

        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var body = await conflict.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("webhook.conflict", body!["code"]);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(1, await db.BillingProviderEvents.CountAsync());
    }

    [Fact]
    public async Task Invalid_and_malformed_webhook_results_fail_closed_without_persistence()
    {
        await _app.SeedAsync(_ => Task.CompletedTask);
        var owner = _app.CreateControlIdentityClient(subject: "billing-owner-4");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        _provider.WebhookResult = BillingWebhookNormalizationResult.Invalid("webhook.signature-invalid");

        var invalid = await PostWebhookAsync("sensitive-raw-body");
        _provider.WebhookResult = new BillingWebhookNormalizationResult(BillingWebhookNormalizationStatus.Known, null, "webhook.malformed");
        var malformed = await PostWebhookAsync("sensitive-raw-body-2");
        _provider.WebhookResult = BillingWebhookNormalizationResult.UnknownEvent(new BillingProviderEvent(
            organizationId,
            BillingProviderNames.Stripe,
            "evt-misclassified",
            "checkout.session.completed",
            OrganizationSubscriptionState.Active,
            DateTimeOffset.UtcNow,
            Sha256("misclassified-raw-body")));
        var misclassified = await PostWebhookAsync("misclassified-raw-body");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, misclassified.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(0, await db.BillingProviderEvents.CountAsync());
        var persistedText = await ReadPersistedBillingTextAsync(db);
        Assert.DoesNotContain("sensitive-raw-body", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-raw-body-2", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("misclassified-raw-body", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("signature-is-never-persisted", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("https://checkout.fake.test/session", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("https://portal.fake.test/session", persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain(organizationId.ToString("D"), persistedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabled_stripe_webhook_returns_safe_bad_request_without_constructing_a_client()
    {
        await using var app = new ControlApiTestApplication();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhooks/stripe")
        {
            Content = new StringContent("{}")
        };

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Stripe", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await ((IAsyncDisposable)_app).DisposeAsync();

    private async Task AddMemberAsync(Guid organizationId, string subject)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var account = new Account { DisplayName = subject, Email = $"{subject}@example.test" };
        account.ExternalIdentities.Add(new ExternalIdentity
        {
            Account = account,
            Issuer = ControlApiTestApplication.TestControlIdentityIssuer,
            Subject = subject,
            DisplayName = subject,
            Email = account.Email
        });
        db.Accounts.Add(account);
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            Account = account,
            OrganizationId = organizationId,
            Role = OrganizationRole.Member
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhooks/stripe")
        {
            Content = new StringContent(body)
        };
        request.Headers.Add("Stripe-Signature", "signature-is-never-persisted");
        return await _app.CreateClient().SendAsync(request);
    }

    private static string Sha256(string value) =>
        "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<string> ReadPersistedBillingTextAsync(CatalogDbContext db)
    {
        var inbox = await db.BillingProviderEvents.AsNoTracking().ToListAsync();
        var audits = await db.OrganizationAuditRecords.AsNoTracking().ToListAsync();
        var subscriptions = await db.OrganizationSubscriptions.AsNoTracking().ToListAsync();
        return string.Join(" ", inbox.SelectMany(x => new[]
        {
            x.Provider, x.ProviderEventId, x.EventType, x.EventHash,
            x.ProviderCustomerReference ?? "", x.ProviderSubscriptionReference ?? "", x.RejectionCode ?? ""
        }).Concat(audits.SelectMany(x => new[]
        {
            x.OperatorSubject ?? "", x.Action.ToString(), x.TargetType, x.TargetId, x.Summary
        })).Concat(subscriptions.SelectMany(x => new[]
        {
            x.Provider, x.ProviderCustomerReference ?? "", x.ProviderSubscriptionReference ?? "", x.LastProviderEventId ?? ""
        })));
    }

    private sealed class FakeBillingProvider : IBillingProvider
    {
        public string Provider { get; set; } = BillingProviderNames.Stripe;
        public int WebhookVerificationCalls { get; private set; }
        public BillingCheckoutSessionRequest? LastCheckout { get; private set; }
        public BillingWebhookNormalizationResult WebhookResult { get; set; } = BillingWebhookNormalizationResult.Invalid("webhook.invalid");

        public Task<BillingSessionLink> CreateCheckoutSessionAsync(BillingCheckoutSessionRequest request, CancellationToken cancellationToken = default)
        {
            LastCheckout = request;
            return Task.FromResult(new BillingSessionLink("https://checkout.fake.test/session"));
        }

        public Task<BillingSessionLink> CreateCustomerPortalSessionAsync(BillingCustomerPortalSessionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BillingSessionLink("https://portal.fake.test/session"));

        public BillingWebhookNormalizationResult VerifyAndNormalizeWebhook(ReadOnlyMemory<byte> rawBody, string signature, DateTimeOffset receivedAt)
        {
            WebhookVerificationCalls++;
            return WebhookResult;
        }
    }
}
