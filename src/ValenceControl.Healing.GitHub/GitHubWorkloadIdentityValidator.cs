using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ValenceControl.Healing.GitHub;

public sealed record GitHubWorkloadIdentityExpectation(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid AttemptId,
    string OneTimeNonceHash,
    string Subject,
    string RepositoryId,
    string RepositoryOwner,
    string RepositoryName,
    string WorkflowRef,
    string WorkflowRevision,
    string SourceRef,
    string SourceRevision,
    string Phase = "initial",
    Guid? ProposalId = null,
    IReadOnlySet<string>? Scopes = null);

public sealed record VerifiedGitHubWorkloadIdentity(
    Guid AttemptId,
    string Subject,
    string RepositoryId,
    string Repository,
    string WorkflowRef,
    string WorkflowRevision,
    string SourceRef,
    string SourceRevision,
    string RunId,
    int RunAttempt,
    string ActorId,
    string JwtId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

public sealed record GitHubWorkloadIdentityValidationResult(
    bool Succeeded,
    string ReasonCode,
    VerifiedGitHubWorkloadIdentity? Identity)
{
    public static GitHubWorkloadIdentityValidationResult Valid(VerifiedGitHubWorkloadIdentity identity) =>
        new(true, "github-workload-identity-valid", identity);

    public static GitHubWorkloadIdentityValidationResult Invalid(string reasonCode) =>
        new(false, reasonCode, null);
}

public interface IGitHubOidcSigningKeyProvider
{
    ValueTask<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken = default);
    void RequestRefresh();
}

public interface IGitHubWorkloadReplayStore
{
    /// <summary>Atomically appends the exchange. Returns false when either JWT ID or nonce was accepted previously.</summary>
    ValueTask<bool> TryAcceptAsync(
        GitHubWorkloadReplayRecord exchange,
        CancellationToken cancellationToken = default);
}

public sealed record GitHubWorkloadReplayRecord(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid AttemptId,
    string Issuer,
    string Audience,
    string Subject,
    string RepositoryProviderId,
    string RepositoryOwner,
    string RepositoryName,
    string WorkflowReference,
    string WorkflowRevision,
    string SourceReference,
    string SourceRevision,
    string WorkflowRunId,
    int WorkflowRunAttempt,
    string ActorId,
    string JwtId,
    string NonceHash,
    string Phase,
    Guid? ProposalId,
    IReadOnlySet<string> Scopes,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

public sealed class GitHubWorkloadIdentityValidator(
    string audience,
    IGitHubOidcSigningKeyProvider signingKeyProvider,
    IGitHubWorkloadReplayStore replayStore,
    TimeProvider? timeProvider = null)
{
    public const string GitHubIssuer = "https://token.actions.githubusercontent.com";
    private const int MaximumAssertionLength = 16_384;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<GitHubWorkloadIdentityValidationResult> ValidateAsync(
        string identityAssertion,
        string oneTimeNonce,
        GitHubWorkloadIdentityExpectation expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (string.IsNullOrWhiteSpace(audience) || string.IsNullOrWhiteSpace(identityAssertion) ||
            identityAssertion.Length > MaximumAssertionLength || expected.WorkspaceId == Guid.Empty ||
            expected.ApplicationId == Guid.Empty || expected.AttemptId == Guid.Empty ||
            string.IsNullOrWhiteSpace(oneTimeNonce))
            return GitHubWorkloadIdentityValidationResult.Invalid(GitHubSecurityReasonCodes.IdentityInvalid);

        var nonceHash = Hash(oneTimeNonce);
        if (!FixedEquals(nonceHash, expected.OneTimeNonceHash))
            return GitHubWorkloadIdentityValidationResult.Invalid(GitHubSecurityReasonCodes.IdentityInvalid);

        IReadOnlyCollection<SecurityKey> keys;
        try
        {
            keys = await signingKeyProvider.GetSigningKeysAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return GitHubWorkloadIdentityValidationResult.Invalid(GitHubSecurityReasonCodes.IdentityInvalid);
        }
        if (keys.Count == 0)
            return GitHubWorkloadIdentityValidationResult.Invalid(GitHubSecurityReasonCodes.IdentityInvalid);

        var now = _timeProvider.GetUtcNow();
        (ClaimsPrincipal? Principal, SecurityToken? Token, bool KeyNotFound) Validate(
            IReadOnlyCollection<SecurityKey> signingKeys)
        {
            try
            {
                var parameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = signingKeys,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidTypes = ["JWT"],
                    ValidateIssuer = true,
                    ValidIssuer = GitHubIssuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    RequireAudience = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    LifetimeValidator = (notBefore, expires, _, _) =>
                        expires is not null && expires.Value > now.UtcDateTime &&
                        (notBefore is null || notBefore.Value <= now.UtcDateTime.AddSeconds(30))
                };
                var principal = new JwtSecurityTokenHandler
                {
                    MapInboundClaims = false,
                    MaximumTokenSizeInBytes = MaximumAssertionLength
                }.ValidateToken(identityAssertion, parameters, out var token);
                return (principal, token, false);
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                return (null, null, true);
            }
            catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
            {
                return (null, null, false);
            }
        }

        var validation = Validate(keys);
        if (validation.KeyNotFound)
        {
            signingKeyProvider.RequestRefresh();
            try
            {
                keys = await signingKeyProvider.GetSigningKeysAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return GitHubWorkloadIdentityValidationResult.Invalid(GitHubSecurityReasonCodes.IdentityInvalid);
            }
            validation = Validate(keys);
        }

        if (validation.Principal is null || validation.Token is not JwtSecurityToken jwt || jwt.Header.Alg != SecurityAlgorithms.RsaSha256)
            return GitHubWorkloadIdentityValidationResult.Invalid(GitHubSecurityReasonCodes.IdentityInvalid);

        var claims = ReadRequiredClaims(validation.Principal);
        if (claims is null ||
            !FixedEquals(claims.Subject!, expected.Subject) ||
            !FixedEquals(claims.RepositoryId!, expected.RepositoryId) ||
            !FixedEquals(claims.Repository!, $"{expected.RepositoryOwner}/{expected.RepositoryName}") ||
            !FixedEquals(claims.WorkflowRef!, expected.WorkflowRef) ||
            !FixedEquals(claims.WorkflowRevision!, expected.WorkflowRevision) ||
            !FixedEquals(claims.SourceRef!, expected.SourceRef) ||
            !FixedEquals(claims.SourceRevision!, expected.SourceRevision) ||
            !long.TryParse(claims.RunId, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !int.TryParse(claims.RunAttemptText, NumberStyles.None, CultureInfo.InvariantCulture, out var runAttempt) || runAttempt < 1 ||
            !long.TryParse(claims.ActorId, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !long.TryParse(claims.IssuedAtText, NumberStyles.None, CultureInfo.InvariantCulture, out var issuedAtSeconds) ||
            jwt.ValidTo <= now.UtcDateTime || jwt.ValidFrom > now.UtcDateTime.AddSeconds(30))
            return GitHubWorkloadIdentityValidationResult.Invalid(GitHubSecurityReasonCodes.IdentityInvalid);

        var expiresAt = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
        if (issuedAt > now.AddSeconds(30) || issuedAt < now.AddMinutes(-10) || expiresAt - issuedAt > TimeSpan.FromMinutes(10))
            return GitHubWorkloadIdentityValidationResult.Invalid(GitHubSecurityReasonCodes.IdentityInvalid);
        var replayRecord = new GitHubWorkloadReplayRecord(
            expected.WorkspaceId, expected.ApplicationId, expected.AttemptId, GitHubIssuer, audience,
            claims.Subject!, claims.RepositoryId!, expected.RepositoryOwner, expected.RepositoryName,
            claims.WorkflowRef!, claims.WorkflowRevision!, claims.SourceRef!, claims.SourceRevision!, claims.RunId!,
            runAttempt, claims.ActorId!, claims.JwtId!, nonceHash, expected.Phase, expected.ProposalId,
            expected.Scopes ?? new HashSet<string>(StringComparer.Ordinal), issuedAt, expiresAt);
        if (!await replayStore.TryAcceptAsync(replayRecord, cancellationToken))
            return GitHubWorkloadIdentityValidationResult.Invalid(GitHubSecurityReasonCodes.IdentityReplay);

        return GitHubWorkloadIdentityValidationResult.Valid(new VerifiedGitHubWorkloadIdentity(
            expected.AttemptId, claims.Subject!, claims.RepositoryId!, claims.Repository!, claims.WorkflowRef!,
            claims.WorkflowRevision!, claims.SourceRef!, claims.SourceRevision!, claims.RunId!, runAttempt,
            claims.ActorId!, claims.JwtId!, issuedAt, expiresAt));
    }

    private static RequiredClaims? ReadRequiredClaims(ClaimsPrincipal principal)
    {
        string? One(string type)
        {
            var values = principal.FindAll(type).Select(x => x.Value).ToArray();
            return values.Length == 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : null;
        }

        var values = new RequiredClaims(
            One("sub"), One("repository_id"), One("repository"), One("workflow_ref"),
            One("workflow_sha"), One("ref"), One("sha"), One("run_id"), One("run_attempt"),
            One("actor_id"), One("jti"), One("iat"));
        return values.HasAll ? values : null;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private sealed record RequiredClaims(
        string? Subject,
        string? RepositoryId,
        string? Repository,
        string? WorkflowRef,
        string? WorkflowRevision,
        string? SourceRef,
        string? SourceRevision,
        string? RunId,
        string? RunAttemptText,
        string? ActorId,
        string? JwtId,
        string? IssuedAtText)
    {
        public bool HasAll =>
            Subject is { Length: > 0 } && RepositoryId is { Length: > 0 } && Repository is { Length: > 0 } &&
            WorkflowRef is { Length: > 0 } && WorkflowRevision is { Length: > 0 } &&
            SourceRef is { Length: > 0 } && SourceRevision is { Length: > 0 } &&
            RunId is { Length: > 0 } && RunAttemptText is { Length: > 0 } &&
            ActorId is { Length: > 0 } && JwtId is { Length: > 0 } && IssuedAtText is { Length: > 0 };
    }
}
