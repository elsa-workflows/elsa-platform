using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Core.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Tests;

public sealed class ManagedElsaHandoffTests
{
    private const string CodeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string WrongCodeVerifier = "aBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
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
                authorizer.RedirectUri.OriginalString,
                authorizer.CodeChallenge));

        Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        var issued = (await issue.Content.ReadControlJsonAsync<ManagedElsaHandoffIssueResponse>())!;
        Assert.Equal(ManagedElsaHandoffDefaults.TokenType, issued.TokenType);
        Assert.Equal(authorizer.Audience, issued.Audience);
        Assert.Equal(authorizer.RedirectUri.OriginalString, issued.RedirectUri);

        var redeem = await app.CreateClient().PostControlJsonAsync(
            "/api/managed-elsa/handoff/redeem",
            new ManagedElsaHandoffRedeemRequest(issued.Token, authorizer.Audience, authorizer.RedirectUri.OriginalString, CodeVerifier));

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
                authorizer.RedirectUri.OriginalString,
                authorizer.CodeChallenge));

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
        Assert.Contains(fixture.Audit.Events, audit => audit.Action == "redeem.succeeded");
        Assert.Contains(fixture.Audit.Events, audit => audit.Action == "redeem.replay_rejected");
    }

    [Fact]
    public async Task Concurrent_redeemers_allow_exactly_one_success()
    {
        using var fixture = CreateFixture();
        var token = fixture.Issue();

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => fixture.RedeemAsync(token)));

        Assert.Single(results, result => result.Succeeded);
        Assert.Equal(31, results.Count(result => result.Failure == ManagedElsaHandoffRedeemFailure.Replay));
        Assert.Equal(31, fixture.Audit.Events.Count(x => x.Action == "redeem.replay_rejected"));
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        using var fixture = CreateFixture();

        var result = await fixture.RedeemAsync(fixture.Issue(), expectedAudience: "urn:elsa:instance:other");

        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, result.Failure);
    }

    [Fact]
    public async Task Missing_or_wrong_verifier_is_rejected()
    {
        using var fixture = CreateFixture();
        var token = fixture.Issue();

        var missing = await fixture.RedeemAsync(token, codeVerifier: "");
        var wrong = await fixture.RedeemAsync(fixture.Issue(), codeVerifier: WrongCodeVerifier);

        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, missing.Failure);
        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, wrong.Failure);
        Assert.Equal(2, fixture.Audit.Events.Count(audit => audit.Action == "redeem.invalid"));
    }

    [Fact]
    public async Task Malformed_verifier_is_rejected_before_hashing()
    {
        using var fixture = CreateFixture();

        var result = await fixture.RedeemAsync(fixture.Issue(), codeVerifier: "too-short");

        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, result.Failure);
        Assert.Contains(fixture.Audit.Events, audit => audit.Action == "redeem.invalid");
    }

    [Fact]
    public void Issued_token_contains_the_required_type_and_known_key_id()
    {
        using var fixture = CreateFixture();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(fixture.Issue());

        Assert.Equal(ManagedElsaHandoffDefaults.TokenType, jwt.Header.Typ);
        Assert.Equal("prototype", jwt.Header.Kid);
        Assert.Equal(
            fixture.Authorizer.BindingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            jwt.Claims.Single(x => x.Type == "binding_version").Value);
    }

    [Fact]
    public async Task Wrong_token_type_and_unknown_key_id_are_rejected()
    {
        using var fixture = CreateFixture();
        var wrongType = fixture.IssueWithTokenType("JWT");
        using var unknownKeyRing = ManagedElsaHandoffKeyRing.CreateEphemeral();
        var alternateIssuer = new ManagedElsaHandoffIssuer(
            Options.Create(new ManagedElsaHandoffOptions
            {
                Enabled = true,
                Issuer = "https://cloud.example.test",
                TokenLifetime = TimeSpan.FromMinutes(1)
            }),
            unknownKeyRing,
            fixture.Clock);
        var unknownKey = alternateIssuer.Issue(
            new TrustedWorkspaceIdentity("https://idp.example.test", "subject", "User", "user@example.test"),
            fixture.Request,
            fixture.Authorizer.Authorization).Token;

        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, (await fixture.RedeemAsync(wrongType)).Failure);
        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, (await fixture.RedeemAsync(unknownKey)).Failure);
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
        Assert.Contains(fixture.Audit.Events, audit => audit.Action == "redeem.authorization_revoked");
    }

    [Fact]
    public async Task Rotated_instance_binding_invalidates_an_issued_handoff()
    {
        using var fixture = CreateFixture();
        var token = fixture.Issue();
        fixture.Authorizer.BindingVersion++;

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
    public void Issue_rejects_uri_normalization_differences()
    {
        using var fixture = CreateFixture();
        var request = fixture.Request with
        {
            RedirectUri = new Uri(fixture.Authorizer.RedirectUri.OriginalString.Replace(
                "https://managed.example.test/",
                "https://managed.example.test:443/"))
        };

        Assert.Throws<InvalidOperationException>(() => fixture.Issue(request));
    }

    [Fact]
    public async Task Redeem_rejects_uri_normalization_differences()
    {
        using var fixture = CreateFixture();
        var normalizedDifferent = new Uri($"https://managed.example.test:443/instances/{fixture.Authorizer.InstanceId:D}/auth/callback");

        var result = await fixture.RedeemAsync(fixture.Issue(), expectedRedirectUri: normalizedDifferent);

        Assert.Equal(ManagedElsaHandoffRedeemFailure.InvalidToken, result.Failure);
    }

    [Fact]
    public void Key_ring_rejects_validation_key_that_duplicates_active_id()
    {
        using var active = RSA.Create(2048);
        using var duplicate = RSA.Create(2048);

        Assert.Throws<ArgumentException>(() => new ManagedElsaHandoffKeyRing(
            "active",
            active,
            [("active", duplicate)]));
    }

    [Fact]
    public void Configured_key_ring_supports_active_and_previous_key_overlap()
    {
        using var active = RSA.Create(2048);
        using var previous = RSA.Create(2048);
        var options = new ManagedElsaHandoffOptions
        {
            ActiveKeyId = "active-2026-09",
            ActivePrivateKeyPem = active.ExportRSAPrivateKeyPem(),
            PreviousPublicKeys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["previous-2026-08"] = previous.ExportRSAPublicKeyPem()
            }
        };

        using var keyRing = ManagedElsaHandoffKeyRing.CreateConfigured(options);

        Assert.Equal("active-2026-09", keyRing.ActiveKeyId);
        Assert.True(keyRing.ContainsKey("active-2026-09"));
        Assert.True(keyRing.ContainsKey("previous-2026-08"));
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
        return new HandoffFixture(clock, authorizer, issuer, redeemer, keyRing, audit);
    }

    private sealed class HandoffFixture(
        TestTimeProvider clock,
        FakeHandoffAuthorizer authorizer,
        ManagedElsaHandoffIssuer issuer,
        ManagedElsaHandoffRedeemer redeemer,
        ManagedElsaHandoffKeyRing keyRing,
        RecordingAuditSink audit) : IDisposable
    {
        public TestTimeProvider Clock { get; } = clock;
        public FakeHandoffAuthorizer Authorizer { get; } = authorizer;
        private ManagedElsaHandoffIssuer Issuer { get; } = issuer;
        private ManagedElsaHandoffRedeemer Redeemer { get; } = redeemer;
        private ManagedElsaHandoffKeyRing KeyRing { get; } = keyRing;
        public RecordingAuditSink Audit { get; } = audit;

        public ManagedElsaHandoffRequest Request => new(
            Authorizer.OrganizationId,
            Authorizer.InstanceId,
            Authorizer.Audience,
            Authorizer.RedirectUri,
            Authorizer.CodeChallenge);

        public string Issue() => Issuer.Issue(
            new TrustedWorkspaceIdentity("https://idp.example.test", "subject", "User", "user@example.test"),
            Request,
            Authorizer.Authorization).Token;

        public string Issue(ManagedElsaHandoffRequest request) => Issuer.Issue(
            new TrustedWorkspaceIdentity("https://idp.example.test", "subject", "User", "user@example.test"),
            request,
            Authorizer.Authorization).Token;

        public string IssueWithTokenType(string tokenType)
        {
            var handler = new JwtSecurityTokenHandler();
            var source = handler.ReadJwtToken(Issue());
            return handler.WriteToken(handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Issuer = source.Issuer,
                Audience = source.Audiences.Single(),
                Subject = new System.Security.Claims.ClaimsIdentity(source.Claims),
                IssuedAt = source.ValidFrom,
                NotBefore = source.ValidFrom,
                Expires = source.ValidTo,
                TokenType = tokenType,
                SigningCredentials = KeyRing.ActiveSigningCredentials
            }));
        }

        public Task<ManagedElsaHandoffRedeemResult> RedeemAsync(
            string token,
            string? expectedAudience = null,
            Uri? expectedRedirectUri = null,
            string? codeVerifier = null) =>
            Redeemer.RedeemAsync(
                token,
                expectedAudience ?? Authorizer.Audience,
                expectedRedirectUri ?? Authorizer.RedirectUri,
                codeVerifier ?? CodeVerifier);

        public void Dispose() => KeyRing.Dispose();
    }

    private sealed class FakeHandoffAuthorizer(Guid organizationId, Guid instanceId) : IManagedElsaHandoffAuthorizer
    {
        public Guid OrganizationId { get; } = organizationId;
        public Guid InstanceId { get; } = instanceId;
        public Guid AccountId { get; } = Guid.NewGuid();
        public string Audience { get; } = $"urn:elsa:instance:{instanceId:D}";
        public Uri RedirectUri { get; } = new($"https://managed.example.test/instances/{instanceId:D}/auth/callback");
        public string CodeChallenge { get; } = ManagedElsaHandoffIssuer.CreateCodeChallenge(CodeVerifier);
        public int BindingVersion { get; set; } = 7;
        public bool IsAuthorized { get; set; } = true;

        public ManagedElsaHandoffAuthorization Authorization => new(
            AccountId,
            OrganizationId,
            InstanceId,
            Audience,
            RedirectUri,
            CodeChallenge,
            new HashSet<string>([ManagedElsaHandoffDefaults.RuntimeSessionScope], StringComparer.Ordinal),
            BindingVersion);

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
            ValueTask.FromResult(IsAuthorized && claims.OrganizationId == OrganizationId && claims.InstanceId == InstanceId &&
                                 claims.BindingVersion == BindingVersion);
    }

    private sealed class RecordingAuditSink : IManagedElsaHandoffAuditSink
    {
        public ConcurrentQueue<ManagedElsaHandoffAuditEvent> Events { get; } = new();

        public ValueTask RecordAsync(ManagedElsaHandoffAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Enqueue(auditEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
    }
}
