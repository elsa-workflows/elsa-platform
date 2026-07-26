using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace ValenceControl.Healing.GitHub.Tests;

public sealed class GitHubWorkloadIdentityValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private const string Audience = "valence-control-healing";
    private const string Nonce = "nonce-with-at-least-thirty-two-characters";

    [Fact]
    public async Task Valid_identity_binds_repository_workflow_run_and_one_time_nonce()
    {
        using var rsa = RSA.Create(2048);
        var replay = new ReplayStore();
        var validator = Validator(rsa, replay);

        var result = await validator.ValidateAsync(Token(rsa), Nonce, Expected());

        result.Succeeded.Should().BeTrue();
        result.Identity.Should().Match<VerifiedGitHubWorkloadIdentity>(x =>
            x.RepositoryId == "987" && x.RunId == "123456" && x.RunAttempt == 2 && x.ActorId == "99");
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("repository")]
    [InlineData("workflow")]
    [InlineData("source-sha")]
    public async Task Mismatched_trust_claims_fail_closed(string mismatch)
    {
        using var rsa = RSA.Create(2048);
        var issuer = mismatch == "issuer" ? "https://attacker.invalid" : GitHubWorkloadIdentityValidator.GitHubIssuer;
        var audience = mismatch == "audience" ? "another-service" : Audience;
        var overrides = mismatch switch
        {
            "repository" => new Dictionary<string, string> { ["repository_id"] = "111" },
            "workflow" => new Dictionary<string, string> { ["workflow_sha"] = new string('d', 40) },
            "source-sha" => new Dictionary<string, string> { ["sha"] = new string('e', 40) },
            _ => null
        };
        var validator = Validator(rsa, new ReplayStore());

        var result = await validator.ValidateAsync(Token(rsa, issuer, audience, overrides), Nonce, Expected());

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(GitHubSecurityReasonCodes.IdentityInvalid);
    }

    [Fact]
    public async Task Wrong_nonce_fails_before_consuming_replay_state()
    {
        using var rsa = RSA.Create(2048);
        var replay = new ReplayStore();
        var validator = Validator(rsa, replay);
        var token = Token(rsa);

        var wrong = await validator.ValidateAsync(token, "a-different-one-time-nonce-value-here", Expected());
        var valid = await validator.ValidateAsync(token, Nonce, Expected());

        wrong.ReasonCode.Should().Be(GitHubSecurityReasonCodes.IdentityInvalid);
        valid.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Jwt_id_and_nonce_are_single_use()
    {
        using var rsa = RSA.Create(2048);
        var validator = Validator(rsa, new ReplayStore());

        var first = await validator.ValidateAsync(Token(rsa), Nonce, Expected());
        var replay = await validator.ValidateAsync(Token(rsa), Nonce, Expected());

        first.Succeeded.Should().BeTrue();
        replay.ReasonCode.Should().Be(GitHubSecurityReasonCodes.IdentityReplay);
    }

    [Fact]
    public async Task Unsigned_or_wrongly_signed_assertion_is_rejected()
    {
        using var trusted = RSA.Create(2048);
        using var attacker = RSA.Create(2048);
        var validator = Validator(trusted, new ReplayStore());

        var result = await validator.ValidateAsync(Token(attacker), Nonce, Expected());

        result.ReasonCode.Should().Be(GitHubSecurityReasonCodes.IdentityInvalid);
    }

    [Fact]
    public async Task Unknown_signing_key_requests_one_discovery_refresh_and_revalidates()
    {
        using var oldKey = RSA.Create(2048);
        using var currentKey = RSA.Create(2048);
        var provider = new RotatingKeyProvider(
            new RsaSecurityKey(oldKey.ExportParameters(false)),
            new RsaSecurityKey(currentKey.ExportParameters(false)));
        var validator = new GitHubWorkloadIdentityValidator(
            Audience, provider, new ReplayStore(), new FixedTimeProvider(Now));

        var result = await validator.ValidateAsync(Token(currentKey), Nonce, Expected());

        result.Succeeded.Should().BeTrue();
        provider.RefreshCount.Should().Be(1);
    }

    private static GitHubWorkloadIdentityValidator Validator(RSA rsa, IGitHubWorkloadReplayStore replay) => new(
        Audience, new KeyProvider(new RsaSecurityKey(rsa.ExportParameters(false))), replay, new FixedTimeProvider(Now));

    private static GitHubWorkloadIdentityExpectation Expected() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Hash(Nonce), "repo:acme/app:ref:refs/heads/main",
        "987", "acme", "app", "acme/app/.github/workflows/heal.yml@refs/heads/main", new string('b', 40),
        "refs/heads/main", new string('c', 40));

    private static string Token(
        RSA rsa,
        string issuer = GitHubWorkloadIdentityValidator.GitHubIssuer,
        string audience = Audience,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sub"] = "repo:acme/app:ref:refs/heads/main",
            ["repository_id"] = "987",
            ["repository"] = "acme/app",
            ["workflow_ref"] = "acme/app/.github/workflows/heal.yml@refs/heads/main",
            ["workflow_sha"] = new string('b', 40),
            ["ref"] = "refs/heads/main",
            ["sha"] = new string('c', 40),
            ["run_id"] = "123456",
            ["run_attempt"] = "2",
            ["actor_id"] = "99",
            ["jti"] = "single-use-jti"
        };
        if (overrides is not null)
            foreach (var (key, value) in overrides) values[key] = value;
        var claims = values.Select(x => new Claim(x.Key, x.Value)).Append(
            new Claim("iat", Now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
        var token = new JwtSecurityToken(
            issuer, audience, claims, Now.AddMinutes(-1).UtcDateTime,
            Now.AddMinutes(5).UtcDateTime,
            new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class KeyProvider(SecurityKey key) : IGitHubOidcSigningKeyProvider
    {
        public ValueTask<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<SecurityKey>>([key]);
        public void RequestRefresh() { }
    }

    private sealed class ReplayStore : IGitHubWorkloadReplayStore
    {
        private readonly HashSet<string> _values = new(StringComparer.Ordinal);
        public ValueTask<bool> TryAcceptAsync(GitHubWorkloadReplayRecord exchange, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_values.Add("jti:" + exchange.JwtId) && _values.Add("nonce:" + exchange.NonceHash));
    }

    private sealed class RotatingKeyProvider(SecurityKey oldKey, SecurityKey currentKey) : IGitHubOidcSigningKeyProvider
    {
        public int RefreshCount { get; private set; }
        public ValueTask<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<SecurityKey>>([RefreshCount == 0 ? oldKey : currentKey]);
        public void RequestRefresh() => RefreshCount++;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
