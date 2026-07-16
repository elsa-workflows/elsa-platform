using System.Security.Cryptography;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.OpenTelemetry;
using Elsa.Platform.Healing.Core.Security;
using Elsa.Platform.Healing.Abstractions;

namespace Elsa.Platform.Healing.OpenTelemetry;

public sealed record HealingTelemetrySourceCredential(
    string Token,
    byte[] Salt,
    byte[] Hash);

public sealed record HealingTelemetrySourceCredentialResult(
    HealingTelemetrySource Source,
    string Token);

/// <summary>Creates and verifies high-entropy, one-time-disclosed source credentials.</summary>
public sealed class HealingTelemetrySourceTokenService
{
    public const string HeaderName = "X-Elsa-Healing-Source-Token";
    public const string TokenPrefix = "elsa_otlp_v1";
    private const int SecretByteCount = 32;
    private const int SaltByteCount = 32;

    public HealingTelemetrySourceCredential Issue(Guid sourceId)
    {
        if (sourceId == Guid.Empty)
            throw new ArgumentException("A telemetry source identifier is required.", nameof(sourceId));

        var secret = RandomNumberGenerator.GetBytes(SecretByteCount);
        var salt = RandomNumberGenerator.GetBytes(SaltByteCount);
        try
        {
            var hash = Hash(secret, salt);
            return new HealingTelemetrySourceCredential(
                $"{TokenPrefix}.{sourceId:N}.{Base64UrlEncode(secret)}",
                salt,
                hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public bool TryParse(string? token, out Guid sourceId, out byte[] secret)
    {
        sourceId = Guid.Empty;
        secret = [];
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 ||
            !parts[0].Equals(TokenPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(parts[1], "N", out sourceId) ||
            sourceId == Guid.Empty ||
            !TryBase64UrlDecode(parts[2], out secret))
        {
            sourceId = Guid.Empty;
            secret = [];
            return false;
        }

        if (secret.Length != SecretByteCount)
        {
            CryptographicOperations.ZeroMemory(secret);
            sourceId = Guid.Empty;
            secret = [];
            return false;
        }

        return true;
    }

    public bool Verify(byte[] secret, byte[] salt, byte[] expectedHash)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(expectedHash);
        if (secret.Length != SecretByteCount || salt.Length != SaltByteCount || expectedHash.Length != 32)
            return false;

        var actualHash = Hash(secret, salt);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualHash);
        }
    }

    private static byte[] Hash(byte[] secret, byte[] salt) => HMACSHA256.HashData(salt, secret);

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, out byte[] result)
    {
        result = [];
        if (value.Length == 0 || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            return false;

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => "!"
        };
        try
        {
            result = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Coordinates source registration while ensuring plaintext tokens never cross the persistence boundary.</summary>
public sealed class HealingTelemetrySourceService(
    IHealingTelemetrySourceStore store,
    HealingTelemetrySourceTokenService tokens,
    HealingAuditService auditService,
    TimeProvider timeProvider)
{
    public async ValueTask<HealingTelemetrySourceCredentialResult> CreateAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        string name,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(workspaceId, applicationId, environmentId);
        name = ValidateName(name);
        ValidateAuditContext(actorId, correlationId);
        var sourceId = Guid.NewGuid();
        var credential = tokens.Issue(sourceId);
        var source = new HealingTelemetrySource
        {
            Id = sourceId,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            EnvironmentId = environmentId,
            Name = name,
            CredentialSalt = credential.Salt,
            CredentialHash = credential.Hash,
            CredentialVersion = 1,
            Status = HealingTelemetrySourceStatus.Active,
            CreatedAt = timeProvider.GetUtcNow()
        };
        return await store.ExecuteInTransactionAsync<HealingTelemetrySourceCredentialResult>(async transactionCancellationToken =>
        {
            var persisted = await store.AddTelemetrySourceAsync(source, transactionCancellationToken);
            await AuditAsync(persisted, "telemetry-source-created", "active", actorId, correlationId, transactionCancellationToken);
            return new HealingTelemetrySourceCredentialResult(persisted, credential.Token);
        }, cancellationToken);
    }

    public ValueTask<IReadOnlyList<HealingTelemetrySource>> ListAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(workspaceId, applicationId, environmentId);
        return store.ListTelemetrySourcesAsync(workspaceId, applicationId, environmentId, cancellationToken);
    }

    public async ValueTask<HealingTelemetrySourceCredentialResult?> RotateAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(workspaceId, applicationId, environmentId);
        ValidateAuditContext(actorId, correlationId);
        if (sourceId == Guid.Empty)
            return null;
        return await store.ExecuteInTransactionAsync<HealingTelemetrySourceCredentialResult?>(async transactionCancellationToken =>
        {
            var current = await store.GetTelemetrySourceAsync(
                workspaceId, applicationId, environmentId, sourceId, transactionCancellationToken);
            if (current is null || current.Status != HealingTelemetrySourceStatus.Active)
                return null;
            var credential = tokens.Issue(sourceId);
            var source = await store.RotateTelemetrySourceAsync(
                workspaceId, applicationId, environmentId, sourceId,
                current.Version,
                credential.Salt, credential.Hash, timeProvider.GetUtcNow(), transactionCancellationToken);
            if (source is null)
                return null;
            await AuditAsync(source, "telemetry-source-rotated", "active", actorId, correlationId, transactionCancellationToken);
            return new HealingTelemetrySourceCredentialResult(source, credential.Token);
        }, cancellationToken);
    }

    public async ValueTask<HealingTelemetrySource?> RevokeAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(workspaceId, applicationId, environmentId);
        ValidateAuditContext(actorId, correlationId);
        if (sourceId == Guid.Empty)
            return null;
        return await store.ExecuteInTransactionAsync<HealingTelemetrySource?>(async transactionCancellationToken =>
        {
            var current = await store.GetTelemetrySourceAsync(
                workspaceId, applicationId, environmentId, sourceId, transactionCancellationToken);
            if (current is null || current.Status == HealingTelemetrySourceStatus.Revoked)
                return current;
            var source = await store.RevokeTelemetrySourceAsync(
                workspaceId, applicationId, environmentId, sourceId,
                current.Version, timeProvider.GetUtcNow(), transactionCancellationToken);
            if (source is not null)
                await AuditAsync(source, "telemetry-source-revoked", "revoked", actorId, correlationId, transactionCancellationToken);
            return source;
        }, cancellationToken);
    }

    private static void ValidateScope(Guid workspaceId, Guid applicationId, Guid environmentId)
    {
        if (workspaceId == Guid.Empty || applicationId == Guid.Empty || environmentId == Guid.Empty)
            throw new ArgumentException("Workspace, application, and environment identifiers are required.");
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A telemetry source name is required.", nameof(name));
        name = name.Trim();
        return name.Length <= 256
            ? name
            : throw new ArgumentException("The telemetry source name cannot exceed 256 characters.", nameof(name));
    }

    private static void ValidateAuditContext(string actorId, Guid correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (correlationId == Guid.Empty)
            throw new ArgumentException("An audit correlation identifier is required.", nameof(correlationId));
    }

    private ValueTask<HealingAuditEvent> AuditAsync(
        HealingTelemetrySource source,
        string eventType,
        string status,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        auditService.AppendAsync(new HealingAuditWrite(
            source.WorkspaceId,
            "telemetry-source",
            source.Id,
            eventType,
            "succeeded",
            HealingActorTypes.Human,
            actorId,
            correlationId,
            null,
            null,
            null,
            null,
            new Dictionary<string, string?> { ["status"] = status }), cancellationToken);
}
