using System.Net;

namespace ElsaControl.Deployment.Abstractions.Instances;

/// <summary>
/// The persisted identity binding consumed by managed handoff. Audience is derived
/// only from the immutable instance ID; callback is derived only from a verified origin.
/// </summary>
public sealed record ElsaInstanceIdentityBinding
{
    private ElsaInstanceIdentityBinding(
        Guid instanceId,
        string audience,
        string canonicalCallbackUri,
        int bindingVersion,
        string endpointOrigin,
        DateTimeOffset changedAt)
    {
        InstanceId = instanceId;
        Audience = audience;
        CanonicalCallbackUri = canonicalCallbackUri;
        BindingVersion = bindingVersion;
        VerifiedEndpointOrigin = endpointOrigin;
        ChangedAt = changedAt;
    }

    public const string HandoffCallbackPath = "/managed-elsa/handoff/callback";

    public Guid InstanceId { get; }

    public string Audience { get; }

    public string CanonicalCallbackUri { get; }

    public int BindingVersion { get; }

    public string VerifiedEndpointOrigin { get; }

    public DateTimeOffset ChangedAt { get; }

    public static ElsaInstanceIdentityBinding Create(
        Guid instanceId,
        string verifiedEndpointOrigin,
        DateTimeOffset? changedAt = null)
    {
        EnsureInstanceId(instanceId);
        var origin = CanonicalizeOrigin(verifiedEndpointOrigin);
        return new ElsaInstanceIdentityBinding(
            instanceId,
            AudienceFor(instanceId),
            origin + HandoffCallbackPath,
            bindingVersion: 1,
            origin,
            (changedAt ?? DateTimeOffset.UtcNow).ToUniversalTime());
    }

    /// <summary>
    /// Rehydrates a persisted binding. New bindings must use <see cref="Create"/>,
    /// which always starts at version one.
    /// </summary>
    public static ElsaInstanceIdentityBinding Hydrate(
        Guid instanceId,
        string verifiedEndpointOrigin,
        int bindingVersion,
        DateTimeOffset changedAt)
    {
        EnsureInstanceId(instanceId);
        if (bindingVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(bindingVersion), "Binding version must be positive.");
        var origin = CanonicalizeOrigin(verifiedEndpointOrigin);
        return new ElsaInstanceIdentityBinding(
            instanceId,
            AudienceFor(instanceId),
            origin + HandoffCallbackPath,
            bindingVersion,
            origin,
            changedAt.ToUniversalTime());
    }

    public static string AudienceFor(Guid instanceId)
    {
        EnsureInstanceId(instanceId);
        return "urn:elsa:instance:" + instanceId.ToString("D").ToLowerInvariant();
    }

    public ElsaInstanceIdentityBinding Rotate(
        string verifiedEndpointOrigin,
        DateTimeOffset? changedAt = null)
    {
        var origin = CanonicalizeOrigin(verifiedEndpointOrigin);
        var rotatedAt = (changedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (changedAt is null && rotatedAt <= ChangedAt)
            rotatedAt = ChangedAt.AddTicks(1);
        else if (rotatedAt <= ChangedAt)
            throw new ArgumentException("Binding changes must have a strictly later timestamp.", nameof(changedAt));
        return new ElsaInstanceIdentityBinding(
            InstanceId,
            Audience,
            origin + HandoffCallbackPath,
            checked(BindingVersion + 1),
            origin,
            rotatedAt);
    }

    /// <summary>
    /// Compares every handoff binding value. In particular, old callback/version pairs
    /// and values belonging to another instance never match the current binding.
    /// </summary>
    public bool Matches(Guid instanceId, string audience, string callbackUri, int bindingVersion) =>
        instanceId == InstanceId &&
        bindingVersion == BindingVersion &&
        string.Equals(audience, Audience, StringComparison.Ordinal) &&
        string.Equals(callbackUri, CanonicalCallbackUri, StringComparison.Ordinal);

    public bool IsCurrent(Guid instanceId, string audience, string callbackUri, int bindingVersion) =>
        Matches(instanceId, audience, callbackUri, bindingVersion);

    public static string CanonicalizeCallbackUri(string verifiedEndpointOrigin) =>
        CanonicalizeOrigin(verifiedEndpointOrigin) + HandoffCallbackPath;

    private static string CanonicalizeOrigin(string? value)
    {
        if (value is null || value.Length > 2048 || value.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("A verified absolute callback origin is required.", nameof(value));

        if (uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Callback origins cannot contain user info, query or fragment components.", nameof(value));
        if (uri.AbsolutePath is not ("" or "/"))
            throw new ArgumentException("Callback origins cannot contain a path.", nameof(value));
        if (uri.Host.Contains('*', StringComparison.Ordinal))
            throw new ArgumentException("Wildcard callback hosts are not allowed.", nameof(value));

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme != Uri.UriSchemeHttps && !(scheme == Uri.UriSchemeHttp && IsLocalHost(uri.Host)))
            throw new ArgumentException("Callback origins must use HTTPS; HTTP is allowed only for localhost development.", nameof(value));

        var host = uri.IdnHost.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Callback origin host is required.", nameof(value));

        var authority = uri.HostNameType == UriHostNameType.IPv6 ? "[" + host + "]" : host;
        if (!uri.IsDefaultPort)
            authority += ":" + uri.Port;
        return scheme + "://" + authority;
    }

    private static bool IsLocalHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    private static void EnsureInstanceId(Guid instanceId)
    {
        if (instanceId == Guid.Empty)
            throw new ArgumentException("Instance ID is required.", nameof(instanceId));
    }
}
