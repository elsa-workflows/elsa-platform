using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.OpenTelemetry;
using Microsoft.AspNetCore.Http;

namespace ValenceControl.Healing.OpenTelemetry.Tests;

public sealed class ControlHealingOtlpRequestAuthenticatorTests
{
    [Fact]
    public async Task Active_token_establishes_only_the_server_owned_scope_and_stable_source_identity()
    {
        var fixture = CreateActiveSource();

        var result = await AuthenticateAsync(fixture.Authenticator, fixture.Credential.Token);

        Assert.True(result.Accepted);
        Assert.True(result.Context.IsAuthenticated);
        Assert.Equal($"control-otel-source:{fixture.Source.Id:D}", result.Context.SourceIdentity);
        Assert.Equivalent(new Dictionary<string, string>
        {
            [HealingTelemetryScopeClaims.WorkspaceId] = fixture.Source.WorkspaceId.ToString("D"),
            [HealingTelemetryScopeClaims.ApplicationId] = fixture.Source.ApplicationId.ToString("D"),
            [HealingTelemetryScopeClaims.EnvironmentId] = fixture.Source.EnvironmentId.ToString("D")
        }, result.Context.Claims);
        Assert.Equal("1", result.Context.Metadata["valence.control.telemetry-source.credential-version"]);
    }

    [Fact]
    public async Task Malformed_unknown_wrong_and_revoked_tokens_are_indistinguishable_rejections()
    {
        var fixture = CreateActiveSource();
        var unknown = fixture.Tokens.Issue(Guid.NewGuid()).Token;
        var wrong = fixture.Tokens.Issue(fixture.Source.Id).Token;

        var malformedResult = await AuthenticateAsync(fixture.Authenticator, "not-a-source-token");
        var unknownResult = await AuthenticateAsync(fixture.Authenticator, unknown);
        var wrongResult = await AuthenticateAsync(fixture.Authenticator, wrong);
        fixture.Source.Status = HealingTelemetrySourceStatus.Revoked;
        var revokedResult = await AuthenticateAsync(fixture.Authenticator, fixture.Credential.Token);

        Assert.All(new[] { malformedResult, unknownResult, wrongResult, revokedResult }, result =>
            Assert.True(!result.Accepted && ReferenceEquals(result.Context, OpenTelemetryIngestionContext.Untrusted)));
    }

    [Fact]
    public async Task Rotation_immediately_invalidates_the_previous_token_and_preserves_source_scope()
    {
        var fixture = CreateActiveSource();
        var rotated = fixture.Tokens.Issue(fixture.Source.Id);

        fixture.Source.CredentialSalt = rotated.Salt;
        fixture.Source.CredentialHash = rotated.Hash;
        fixture.Source.CredentialVersion = 2;

        var previousResult = await AuthenticateAsync(fixture.Authenticator, fixture.Credential.Token);
        var rotatedResult = await AuthenticateAsync(fixture.Authenticator, rotated.Token);

        Assert.False(previousResult.Accepted);
        Assert.True(rotatedResult.Accepted);
        Assert.Equal($"control-otel-source:{fixture.Source.Id:D}", rotatedResult.Context.SourceIdentity);
        Assert.Equal("2", rotatedResult.Context.Metadata["valence.control.telemetry-source.credential-version"]);
        Assert.Equal(fixture.Source.WorkspaceId.ToString("D"), rotatedResult.Context.Claims[HealingTelemetryScopeClaims.WorkspaceId]);
    }

    [Fact]
    public void Token_contains_a_256_bit_random_secret_and_verification_is_bound_to_its_salt()
    {
        var tokens = new HealingTelemetrySourceTokenService();
        var first = tokens.Issue(Guid.NewGuid());
        var second = tokens.Issue(Guid.NewGuid());

        Assert.True(tokens.TryParse(first.Token, out _, out var secret));
        Assert.Equal(32, secret.Count());
        Assert.Equal(32, first.Salt.Count());
        Assert.NotEqual(first.Salt, second.Salt);
        Assert.Equal(32, first.Hash.Count());
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.True(tokens.Verify(secret, first.Salt, first.Hash));
        Assert.False(tokens.Verify(secret, second.Salt, first.Hash));
    }

    private static async Task<Elsa.Diagnostics.OpenTelemetry.Ingestion.OtlpRequestAuthenticationResult> AuthenticateAsync(
        ControlHealingOtlpRequestAuthenticator authenticator,
        string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[HealingTelemetrySourceTokenService.HeaderName] = token;
        return await authenticator.AuthenticateAsync(context);
    }

    private static Fixture CreateActiveSource()
    {
        var tokens = new HealingTelemetrySourceTokenService();
        var sourceId = Guid.NewGuid();
        var credential = tokens.Issue(sourceId);
        var source = new HealingTelemetrySource
        {
            Id = sourceId,
            WorkspaceId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            EnvironmentId = Guid.NewGuid(),
            Name = "Orders production",
            CredentialSalt = credential.Salt,
            CredentialHash = credential.Hash,
            CredentialVersion = 1,
            Status = HealingTelemetrySourceStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var store = new InMemoryTelemetrySourceStore(source);
        return new(source, credential, tokens, new ControlHealingOtlpRequestAuthenticator(store, tokens));
    }

    private sealed record Fixture(
        HealingTelemetrySource Source,
        HealingTelemetrySourceCredential Credential,
        HealingTelemetrySourceTokenService Tokens,
        ControlHealingOtlpRequestAuthenticator Authenticator);

    private sealed class InMemoryTelemetrySourceStore(params HealingTelemetrySource[] sources) : IHealingTelemetrySourceStore
    {
        private readonly List<HealingTelemetrySource> _sources = [.. sources];

        public ValueTask<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);

        public ValueTask<HealingTelemetrySource> AddTelemetrySourceAsync(HealingTelemetrySource source, CancellationToken cancellationToken = default)
        {
            _sources.Add(source);
            return ValueTask.FromResult(source);
        }

        public ValueTask<IReadOnlyList<HealingTelemetrySource>> ListTelemetrySourcesAsync(Guid workspaceId, Guid applicationId, Guid environmentId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingTelemetrySource>>(_sources.Where(x =>
                x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.EnvironmentId == environmentId).ToList());

        public ValueTask<HealingTelemetrySource?> GetTelemetrySourceAsync(Guid workspaceId, Guid applicationId, Guid environmentId, Guid sourceId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_sources.SingleOrDefault(x => x.WorkspaceId == workspaceId && x.ApplicationId == applicationId && x.EnvironmentId == environmentId && x.Id == sourceId));

        public ValueTask<HealingTelemetrySource?> GetActiveTelemetrySourceForAuthenticationAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_sources.SingleOrDefault(x => x.Id == sourceId && x.Status == HealingTelemetrySourceStatus.Active));

        public ValueTask<HealingTelemetrySource?> RotateTelemetrySourceAsync(Guid workspaceId, Guid applicationId, Guid environmentId, Guid sourceId, byte[] expectedVersion, byte[] credentialSalt, byte[] credentialHash, DateTimeOffset rotatedAt, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<HealingTelemetrySource?>(null);

        public ValueTask<HealingTelemetrySource?> RevokeTelemetrySourceAsync(Guid workspaceId, Guid applicationId, Guid environmentId, Guid sourceId, byte[] expectedVersion, DateTimeOffset revokedAt, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<HealingTelemetrySource?>(null);
    }
}
