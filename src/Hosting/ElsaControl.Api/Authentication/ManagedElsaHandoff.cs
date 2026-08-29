using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ElsaControl.PackageCatalog.Core.Accounts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ElsaControl.Api.Authentication;

public static class ManagedElsaHandoffDefaults
{
    public const string ConfigurationSection = "ManagedElsa:Handoff";
    public const string RuntimeSessionScope = "runtime:session";
    public const string TokenType = "elsa-handoff+jwt";
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(5);
}

public sealed class ManagedElsaHandoffOptions
{
    public bool Enabled { get; init; }
    public string Issuer { get; init; } = "https://cloud.elsaworkflows.io";
    public TimeSpan TokenLifetime { get; init; } = ManagedElsaHandoffDefaults.DefaultLifetime;
}

public sealed class ManagedElsaHandoffConfigurationValidator(
    IHostEnvironment environment,
    IOptions<ManagedElsaHandoffOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var validated = ManagedElsaHandoffIssuer.ValidateOptions(options.Value);
        if (environment.IsProduction() && validated.Enabled)
            throw new InvalidOperationException(
                "ManagedElsa:Handoff:Enabled cannot be enabled in Production until the ephemeral prototype key ring is replaced with a configured rotating key provider.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The authorization result is deliberately supplied by the Elsa Instance boundary.
/// It must be derived from current Control identity and current organization/instance
/// membership; it must not be populated from browser-provided account or role claims.
/// </summary>
public sealed record ManagedElsaHandoffAuthorization(
    Guid AccountId,
    Guid OrganizationId,
    Guid InstanceId,
    string Audience,
    Uri RedirectUri,
    IReadOnlySet<string> Scopes);

public sealed record ManagedElsaHandoffRequest(
    Guid OrganizationId,
    Guid InstanceId,
    string Audience,
    Uri RedirectUri,
    IReadOnlySet<string>? Scopes = null)
{
    public IReadOnlySet<string> RequestedScopes => Scopes is { Count: > 0 }
        ? Scopes
        : new HashSet<string>([ManagedElsaHandoffDefaults.RuntimeSessionScope], StringComparer.Ordinal);
}

public interface IManagedElsaHandoffAuthorizer
{
    ValueTask<ManagedElsaHandoffAuthorization?> AuthorizeAsync(
        TrustedWorkspaceIdentity identity,
        ManagedElsaHandoffRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<bool> IsStillAuthorizedAsync(
        ManagedElsaHandoffClaims claims,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The production authorizer is intentionally not guessed before the Elsa Instance
/// aggregate exists. Register an instance-aware implementation before enabling the
/// endpoint; the default keeps the endpoint fail-closed.
/// </summary>
public sealed class UnconfiguredManagedElsaHandoffAuthorizer : IManagedElsaHandoffAuthorizer
{
    public ValueTask<ManagedElsaHandoffAuthorization?> AuthorizeAsync(
        TrustedWorkspaceIdentity identity,
        ManagedElsaHandoffRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ManagedElsaHandoffAuthorization?>(null);

    public ValueTask<bool> IsStillAuthorizedAsync(
        ManagedElsaHandoffClaims claims,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}

public sealed class ManagedElsaHandoffKeyRing : IDisposable
{
    private readonly IReadOnlyDictionary<string, RsaSecurityKey> _keys;

    public ManagedElsaHandoffKeyRing(string activeKeyId, RSA activeKey, IEnumerable<(string KeyId, RSA Key)>? validationKeys = null)
    {
        if (string.IsNullOrWhiteSpace(activeKeyId))
            throw new ArgumentException("An active key id is required.", nameof(activeKeyId));

        ArgumentNullException.ThrowIfNull(activeKey);
        var keys = (validationKeys ?? []).ToList();
        if (!keys.Any(x => string.Equals(x.KeyId, activeKeyId, StringComparison.Ordinal)))
            keys.Add((activeKeyId, activeKey));

        _keys = keys
            .Select(x => new RsaSecurityKey(x.Key) { KeyId = x.KeyId })
            .ToDictionary(x => x.KeyId!, StringComparer.Ordinal);
        ActiveKeyId = activeKeyId;
    }

    public string ActiveKeyId { get; }

    public SigningCredentials ActiveSigningCredentials =>
        new(_keys[ActiveKeyId], SecurityAlgorithms.RsaSha256);

    public IReadOnlyCollection<SecurityKey> ValidationKeys => _keys.Values.Cast<SecurityKey>().ToArray();

    public static ManagedElsaHandoffKeyRing CreateEphemeral() =>
        new("prototype", RSA.Create(2048));

    public void Dispose()
    {
        foreach (var key in _keys.Values)
            key.Rsa?.Dispose();
    }
}

public sealed record ManagedElsaHandoffIssueResult(
    string Token,
    string TokenType,
    string KeyId,
    string Audience,
    Uri RedirectUri,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Jti);

public sealed record ManagedElsaHandoffClaims(
    string Jti,
    Guid AccountId,
    string ControlIssuer,
    string ControlSubject,
    Guid OrganizationId,
    Guid InstanceId,
    string Audience,
    Uri RedirectUri,
    IReadOnlySet<string> Scopes,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt)
{
    public TrustedWorkspaceIdentity ToTrustedWorkspaceIdentity() =>
        new(ControlIssuer, ControlSubject, null, null);
}

public enum ManagedElsaHandoffRedeemFailure
{
    InvalidToken,
    Replay,
    AuthorizationRevoked
}

public sealed record ManagedElsaHandoffRedeemResult(
    ManagedElsaHandoffClaims? Claims,
    ManagedElsaHandoffRedeemFailure? Failure)
{
    public bool Succeeded => Claims is not null && Failure is null;

    public static ManagedElsaHandoffRedeemResult Success(ManagedElsaHandoffClaims claims) => new(claims, null);

    public static ManagedElsaHandoffRedeemResult Denied(ManagedElsaHandoffRedeemFailure failure) => new(null, failure);
}

public interface IManagedElsaHandoffReplayStore
{
    ValueTask<bool> TryConsumeAsync(string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
}

public sealed class InMemoryManagedElsaHandoffReplayStore(TimeProvider timeProvider) : IManagedElsaHandoffReplayStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);

    public ValueTask<bool> TryConsumeAsync(
        string jti,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var item in _consumed)
        {
            if (item.Value <= now)
                _consumed.TryRemove(item.Key, out _);
        }

        return ValueTask.FromResult(_consumed.TryAdd(jti, expiresAt));
    }
}

public sealed record ManagedElsaHandoffAuditEvent(
    string Action,
    string Jti,
    Guid? AccountId,
    Guid? OrganizationId,
    Guid? InstanceId,
    string? Audience,
    DateTimeOffset OccurredAt);

public interface IManagedElsaHandoffAuditSink
{
    ValueTask RecordAsync(ManagedElsaHandoffAuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public sealed class NullManagedElsaHandoffAuditSink : IManagedElsaHandoffAuditSink
{
    public ValueTask RecordAsync(ManagedElsaHandoffAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}

public sealed class ManagedElsaHandoffIssuer(
    IOptions<ManagedElsaHandoffOptions> options,
    ManagedElsaHandoffKeyRing keyRing,
    TimeProvider timeProvider)
{
    private readonly ManagedElsaHandoffOptions _options = ValidateOptions(options.Value);

    public ManagedElsaHandoffIssueResult Issue(
        TrustedWorkspaceIdentity identity,
        ManagedElsaHandoffRequest request,
        ManagedElsaHandoffAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorization);

        if (!string.Equals(request.Audience, authorization.Audience, StringComparison.Ordinal) ||
            request.OrganizationId != authorization.OrganizationId ||
            request.InstanceId != authorization.InstanceId ||
            request.RedirectUri != authorization.RedirectUri ||
            !IsSafeRedirectUri(request.RedirectUri) ||
            string.IsNullOrWhiteSpace(identity.Issuer) ||
            string.IsNullOrWhiteSpace(identity.Subject) ||
            !request.RequestedScopes.SetEquals([ManagedElsaHandoffDefaults.RuntimeSessionScope]) ||
            !request.RequestedScopes.All(authorization.Scopes.Contains))
            throw new InvalidOperationException("The handoff authorization does not match the requested target.");

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(_options.TokenLifetime);
        var jti = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, authorization.AccountId.ToString("D")),
            new("control_iss", identity.Issuer),
            new("control_sub", identity.Subject),
            new("org_id", authorization.OrganizationId.ToString("D")),
            new("instance_id", authorization.InstanceId.ToString("D")),
            new("redirect_uri", authorization.RedirectUri.OriginalString),
            new("scope", string.Join(' ', request.RequestedScopes.Order(StringComparer.Ordinal))),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = authorization.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            TokenType = ManagedElsaHandoffDefaults.TokenType,
            SigningCredentials = keyRing.ActiveSigningCredentials
        };
        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(handler.CreateToken(descriptor));

        return new ManagedElsaHandoffIssueResult(
            token,
            ManagedElsaHandoffDefaults.TokenType,
            keyRing.ActiveKeyId,
            authorization.Audience,
            authorization.RedirectUri,
            now,
            expiresAt,
            jti);
    }

    internal static ManagedElsaHandoffOptions ValidateOptions(ManagedElsaHandoffOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer) || !Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("ManagedElsa:Handoff:Issuer must be an absolute HTTPS URI.");
        if (options.TokenLifetime <= TimeSpan.Zero || options.TokenLifetime > ManagedElsaHandoffDefaults.MaximumLifetime)
            throw new InvalidOperationException($"ManagedElsa:Handoff:TokenLifetime must be between 1 second and {ManagedElsaHandoffDefaults.MaximumLifetime}.");

        return options;
    }

    internal static bool IsSafeRedirectUri(Uri redirectUri) =>
        redirectUri.IsAbsoluteUri &&
        (redirectUri.Scheme == Uri.UriSchemeHttps ||
         (redirectUri.Scheme == Uri.UriSchemeHttp &&
          (redirectUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || redirectUri.Host.Equals("127.0.0.1", StringComparison.Ordinal)))) &&
        !redirectUri.Fragment.Any() &&
        !redirectUri.UserInfo.Any();
}

public sealed class ManagedElsaHandoffRedeemer(
    IOptions<ManagedElsaHandoffOptions> options,
    ManagedElsaHandoffKeyRing keyRing,
    IManagedElsaHandoffReplayStore replayStore,
    IManagedElsaHandoffAuthorizer authorizer,
    TimeProvider timeProvider,
    IManagedElsaHandoffAuditSink auditSink)
{
    private readonly ManagedElsaHandoffOptions _options = ManagedElsaHandoffIssuer.ValidateOptions(options.Value);

    public async Task<ManagedElsaHandoffRedeemResult> RedeemAsync(
        string token,
        string expectedAudience,
        Uri expectedRedirectUri,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(expectedAudience) ||
            !ManagedElsaHandoffIssuer.IsSafeRedirectUri(expectedRedirectUri))
            return await InvalidAsync(cancellationToken);

        ManagedElsaHandoffClaims claims;
        try
        {
            claims = ValidateToken(token, expectedAudience, expectedRedirectUri);
        }
        catch (SecurityTokenException)
        {
            return await InvalidAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            return await InvalidAsync(cancellationToken);
        }

        if (!await replayStore.TryConsumeAsync(claims.Jti, claims.ExpiresAt, cancellationToken))
        {
            await auditSink.RecordAsync(new ManagedElsaHandoffAuditEvent(
                "redeem.replay_rejected",
                claims.Jti,
                claims.AccountId,
                claims.OrganizationId,
                claims.InstanceId,
                claims.Audience,
                timeProvider.GetUtcNow()), cancellationToken);
            return ManagedElsaHandoffRedeemResult.Denied(ManagedElsaHandoffRedeemFailure.Replay);
        }

        if (!await authorizer.IsStillAuthorizedAsync(claims, cancellationToken))
        {
            await auditSink.RecordAsync(new ManagedElsaHandoffAuditEvent(
                "redeem.authorization_revoked",
                claims.Jti,
                claims.AccountId,
                claims.OrganizationId,
                claims.InstanceId,
                claims.Audience,
                timeProvider.GetUtcNow()), cancellationToken);
            return ManagedElsaHandoffRedeemResult.Denied(ManagedElsaHandoffRedeemFailure.AuthorizationRevoked);
        }

        await auditSink.RecordAsync(new ManagedElsaHandoffAuditEvent(
            "redeem.succeeded",
            claims.Jti,
            claims.AccountId,
            claims.OrganizationId,
            claims.InstanceId,
            claims.Audience,
            timeProvider.GetUtcNow()), cancellationToken);
        return ManagedElsaHandoffRedeemResult.Success(claims);
    }

    private ManagedElsaHandoffClaims ValidateToken(string token, string expectedAudience, Uri expectedRedirectUri)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keyRing.ValidationKeys,
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = expectedAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };
        var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
        if (validatedToken is not JwtSecurityToken jwt ||
            !string.Equals(jwt.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
            throw new SecurityTokenException("Only RS256 handoff tokens are accepted.");

        var jti = RequiredClaim(principal, JwtRegisteredClaimNames.Jti);
        var subject = RequiredGuid(principal, JwtRegisteredClaimNames.Sub);
        var controlIssuer = RequiredClaim(principal, "control_iss");
        var controlSubject = RequiredClaim(principal, "control_sub");
        var organizationId = RequiredGuid(principal, "org_id");
        var instanceId = RequiredGuid(principal, "instance_id");
        var audience = RequiredClaim(principal, JwtRegisteredClaimNames.Aud);
        var redirectUri = new Uri(RequiredClaim(principal, "redirect_uri"), UriKind.Absolute);
        var scopes = RequiredClaim(principal, "scope")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        if (scopes.Count != 1 || !scopes.Contains(ManagedElsaHandoffDefaults.RuntimeSessionScope) ||
            !redirectUri.Equals(expectedRedirectUri) ||
            !ManagedElsaHandoffIssuer.IsSafeRedirectUri(redirectUri))
            throw new SecurityTokenException("The handoff token is not bound to the requested target.");

        var issuedAt = NumericDateClaim(principal, JwtRegisteredClaimNames.Iat);
        var expiresAt = NumericDateClaim(principal, JwtRegisteredClaimNames.Exp);
        return new ManagedElsaHandoffClaims(
            jti,
            subject,
            controlIssuer,
            controlSubject,
            organizationId,
            instanceId,
            audience,
            redirectUri,
            scopes,
            issuedAt,
            expiresAt);
    }

    private async Task<ManagedElsaHandoffRedeemResult> InvalidAsync(CancellationToken cancellationToken)
    {
        await auditSink.RecordAsync(new ManagedElsaHandoffAuditEvent(
            "redeem.invalid",
            "",
            null,
            null,
            null,
            null,
            timeProvider.GetUtcNow()), cancellationToken);
        return ManagedElsaHandoffRedeemResult.Denied(ManagedElsaHandoffRedeemFailure.InvalidToken);
    }

    private static string RequiredClaim(ClaimsPrincipal principal, string claimType) =>
        principal.FindFirst(claimType)?.Value is { Length: > 0 } value
            ? value
            : throw new SecurityTokenException($"Required handoff claim '{claimType}' is missing.");

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(RequiredClaim(principal, claimType), out var value)
            ? value
            : throw new SecurityTokenException($"Handoff claim '{claimType}' is not a GUID.");

    private static DateTimeOffset NumericDateClaim(ClaimsPrincipal principal, string claimType) =>
        long.TryParse(RequiredClaim(principal, claimType), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : throw new SecurityTokenException($"Handoff claim '{claimType}' is not a numeric date.");
}

public sealed class ManagedElsaHandoffService(
    IWorkspaceIdentityReader identityReader,
    IManagedElsaHandoffAuthorizer authorizer,
    ManagedElsaHandoffIssuer issuer,
    ManagedElsaHandoffRedeemer redeemer,
    IManagedElsaHandoffAuditSink auditSink,
    TimeProvider timeProvider)
{
    public async Task<ManagedElsaHandoffIssueResult?> IssueAsync(
        HttpContext context,
        ManagedElsaHandoffRequest request,
        CancellationToken cancellationToken = default)
    {
        var identity = await identityReader.ReadAsync(context);
        if (identity is null)
            return null;

        var authorization = await authorizer.AuthorizeAsync(identity, request, cancellationToken);
        if (authorization is null)
        {
            await auditSink.RecordAsync(new ManagedElsaHandoffAuditEvent(
                "issue.authorization_denied",
                "",
                null,
                request.OrganizationId,
                request.InstanceId,
                request.Audience,
                timeProvider.GetUtcNow()), cancellationToken);
            return null;
        }

        var result = issuer.Issue(identity, request, authorization);
        await auditSink.RecordAsync(new ManagedElsaHandoffAuditEvent(
            "issue.succeeded",
            result.Jti,
            authorization.AccountId,
            authorization.OrganizationId,
            authorization.InstanceId,
            authorization.Audience,
            result.IssuedAt), cancellationToken);
        return result;
    }

    public Task<ManagedElsaHandoffRedeemResult> RedeemAsync(
        string token,
        string expectedAudience,
        Uri expectedRedirectUri,
        CancellationToken cancellationToken = default) =>
        redeemer.RedeemAsync(token, expectedAudience, expectedRedirectUri, cancellationToken);
}

public sealed record ManagedElsaHandoffSession(
    string SessionId,
    Guid AccountId,
    Guid OrganizationId,
    Guid InstanceId,
    IReadOnlySet<string> Scopes,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Local prototype for the managed runtime's session boundary. A production runtime
/// should back this with its own HttpOnly/SameSite session cookie and revoke it on
/// logout; the handoff JWT is never used as the runtime's long-lived bearer token.
/// </summary>
public sealed class InMemoryManagedElsaSessionStore(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, ManagedElsaHandoffSession> _sessions = new(StringComparer.Ordinal);

    public ManagedElsaHandoffSession Create(ManagedElsaHandoffClaims claims)
    {
        var session = new ManagedElsaHandoffSession(
            claims.Jti,
            claims.AccountId,
            claims.OrganizationId,
            claims.InstanceId,
            claims.Scopes,
            claims.ExpiresAt);
        _sessions[session.SessionId] = session;
        return session;
    }

    public bool IsActive(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) && session.ExpiresAt > timeProvider.GetUtcNow();

    public bool Revoke(string sessionId) => _sessions.TryRemove(sessionId, out _);
}
