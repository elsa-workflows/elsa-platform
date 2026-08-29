using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Core.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Tests;

public sealed class ManagedElsaHandoffTests
{
    [Fact]
    public async Task Existing_control_identity_can_issue_and_redeem_one_time_handoff()
    {
        var organizationId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var authorizer = new FakeHandoffAuthorizer(organizationId, instanceId);
        await using var app = CreateApplication(authorizer);
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "handoff-user");

        var issue = await client.PostControlJsonAsync(
            "/api/managed-elsa/handoff/issue",
            new ManagedElsaHandoffIssueRequest(
                organizationId,
                instanceId,
                authorizer.Audience,
                authorizer.RedirectUri.OriginalString));

        Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        var issued = (await issue.Content.ReadControlJsonAsync<ManagedElsaHandoffIssueResponse>())!;
        Assert.Equal(ManagedElsaHandoffDefaults.TokenType, issued.TokenType);
        Assert.Equal(authorizer.Audience, issued.Audience);
        Assert.Equal(authorizer.RedirectUri.OriginalString, issued.RedirectUri);

        var redeem = await app.CreateClient().PostControlJsonAsync(
            "/api/managed-elsa/handoff/redeem",
            new ManagedElsaHandoffRedeemRequest(issued.Token, authorizer.Audience, authorizer.RedirectUri.OriginalString));

        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        var session = (await redeem.Content.ReadControlJsonAsync<ManagedElsaHandoffRedeemResponse>())!;
        Assert.Equal(authorizer.OrganizationId, session.OrganizationId);
        Assert.Equal(authorizer.InstanceId, session.InstanceId);
        Assert.Contains(ManagedElsaHandoffDefaults.RuntimeSessionScope, session.Scopes);
    }

    [Fact]
    public async Task Handoff_endpoint_rejects_cross_organization_target()
    {
        var authorizer = new FakeHandoffAuthorizer(Guid.NewGuid(), Guid.NewGuid());
        await using var app = CreateApplication(authorizer);
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "handoff-user");

        var response = await client.PostControlJsonAsync(
            "/api/managed-elsa/handoff/issue",
            new ManagedElsaHandoffIssueRequest(
                Guid.NewGuid(),
                authorizer.InstanceId,
                authorizer.Audience,
                authorizer.RedirectUri.OriginalString));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Replay_is_rejected_atomically()
    {
        using var fixture = CreateFixture();
        var token = fixture.Issue();

        var first = await fixture.RedeemAsync(token);
        var second = await fixture.RedeemAsync(token);

        Assert.True(first.Succeeded);
        Assert.Equal(ManagedElsaHandoffRedeemFailure.Replay, second.Failure);
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        using var fixture = CreateFixture();

        var result = await fixture.RedeemAsync(fixture.Issue(), expectedAudience: "urn:elsa:instance:other");

        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, result.Failure);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-10));
        using var fixture = CreateFixture(clock);

        var result = await fixture.RedeemAsync(fixture.Issue());

        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, result.Failure);
    }

    [Fact]
    public async Task Revoked_membership_is_checked_again_at_redeem()
    {
        using var fixture = CreateFixture();
        var token = fixture.Issue();
        fixture.Authorizer.IsAuthorized = false;

        var result = await fixture.RedeemAsync(token);

        Assert.Equal(ManagedElsaHandoffRedeemFailure.AuthorizationRevoked, result.Failure);
    }

    [Fact]
    public async Task Redirect_binding_is_exact_and_rejects_a_different_callback()
    {
        using var fixture = CreateFixture();

        var result = await fixture.RedeemAsync(
            fixture.Issue(),
            expectedRedirectUri: new Uri("https://managed.example.test/another-callback"));

        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, result.Failure);
    }

    [Fact]
    public async Task Runtime_session_is_local_and_logout_revokes_it()
    {
        using var fixture = CreateFixture();
        var claims = (await fixture.RedeemAsync(fixture.Issue())).Claims!;
        var sessions = new InMemoryManagedElsaSessionStore(fixture.Clock);
        var session = sessions.Create(claims);

        Assert.True(sessions.IsActive(session.SessionId));
        Assert.True(sessions.Revoke(session.SessionId));
        Assert.False(sessions.IsActive(session.SessionId));
    }

    private static ControlApiTestApplication CreateApplication(FakeHandoffAuthorizer authorizer) =>
        new(new Dictionary<string, string?>
        {
            [$"{ManagedElsaHandoffDefaults.ConfigurationSection}:Enabled"] = "true"
        }, services =>
        {
            services.RemoveAll<IManagedElsaHandoffAuthorizer>();
            services.AddSingleton<IManagedElsaHandoffAuthorizer>(authorizer);
        });

    private static HandoffFixture CreateFixture(TestTimeProvider? clock = null)
    {
        clock ??= new TestTimeProvider(DateTimeOffset.UtcNow);
        var authorizer = new FakeHandoffAuthorizer(Guid.NewGuid(), Guid.NewGuid());
        var options = Options.Create(new ManagedElsaHandoffOptions
        {
            Enabled = true,
            Issuer = "https://cloud.example.test",
            TokenLifetime = TimeSpan.FromMinutes(1)
        });
        var keyRing = ManagedElsaHandoffKeyRing.CreateEphemeral();
        var replayStore = new InMemoryManagedElsaHandoffReplayStore(clock);
        var audit = new RecordingAuditSink();
        var issuer = new ManagedElsaHandoffIssuer(options, keyRing, clock);
        var redeemer = new ManagedElsaHandoffRedeemer(options, keyRing, replayStore, authorizer, clock, audit);
        return new HandoffFixture(clock, authorizer, issuer, redeemer, keyRing);
    }

    private sealed class HandoffFixture(
        TestTimeProvider clock,
        FakeHandoffAuthorizer authorizer,
        ManagedElsaHandoffIssuer issuer,
        ManagedElsaHandoffRedeemer redeemer,
        ManagedElsaHandoffKeyRing keyRing) : IDisposable
    {
        public TestTimeProvider Clock { get; } = clock;
        public FakeHandoffAuthorizer Authorizer { get; } = authorizer;
        private ManagedElsaHandoffIssuer Issuer { get; } = issuer;
        private ManagedElsaHandoffRedeemer Redeemer { get; } = redeemer;
        private ManagedElsaHandoffKeyRing KeyRing { get; } = keyRing;

        public ManagedElsaHandoffRequest Request => new(
            Authorizer.OrganizationId,
            Authorizer.InstanceId,
            Authorizer.Audience,
            Authorizer.RedirectUri);

        public string Issue() => Issuer.Issue(
            new TrustedWorkspaceIdentity("https://idp.example.test", "subject", "User", "user@example.test"),
            Request,
            Authorizer.Authorization).Token;

        public Task<ManagedElsaHandoffRedeemResult> RedeemAsync(
            string token,
            string? expectedAudience = null,
            Uri? expectedRedirectUri = null) =>
            Redeemer.RedeemAsync(
                token,
                expectedAudience ?? Authorizer.Audience,
                expectedRedirectUri ?? Authorizer.RedirectUri);

        public void Dispose() => KeyRing.Dispose();
    }

    private sealed class FakeHandoffAuthorizer(Guid organizationId, Guid instanceId) : IManagedElsaHandoffAuthorizer
    {
        public Guid OrganizationId { get; } = organizationId;
        public Guid InstanceId { get; } = instanceId;
        public Guid AccountId { get; } = Guid.NewGuid();
        public string Audience { get; } = $"urn:elsa:instance:{instanceId:D}";
        public Uri RedirectUri { get; } = new($"https://managed.example.test/instances/{instanceId:D}/auth/callback");
        public bool IsAuthorized { get; set; } = true;

        public ManagedElsaHandoffAuthorization Authorization => new(
            AccountId,
            OrganizationId,
            InstanceId,
            Audience,
            RedirectUri,
            new HashSet<string>([ManagedElsaHandoffDefaults.RuntimeSessionScope], StringComparer.Ordinal));

        public ValueTask<ManagedElsaHandoffAuthorization?> AuthorizeAsync(
            TrustedWorkspaceIdentity identity,
            ManagedElsaHandoffRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ManagedElsaHandoffAuthorization?>(
                IsAuthorized &&
                request.OrganizationId == OrganizationId &&
                request.InstanceId == InstanceId &&
                request.Audience == Audience &&
                request.RedirectUri == RedirectUri
                    ? Authorization
                    : null);

        public ValueTask<bool> IsStillAuthorizedAsync(
            ManagedElsaHandoffClaims claims,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(IsAuthorized && claims.OrganizationId == OrganizationId && claims.InstanceId == InstanceId);
    }

    private sealed class RecordingAuditSink : IManagedElsaHandoffAuditSink
    {
        public List<ManagedElsaHandoffAuditEvent> Events { get; } = [];

        public ValueTask RecordAsync(ManagedElsaHandoffAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
    }
}
