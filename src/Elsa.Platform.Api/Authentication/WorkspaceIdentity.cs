using System.Net;
using System.Net.Sockets;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.Api.Authentication;

public interface IWorkspaceIdentityReader
{
    ValueTask<TrustedWorkspaceIdentity?> ReadAsync(HttpContext context);
}

public sealed class TrustedHeaderWorkspaceIdentityReader(IConfiguration configuration) : IWorkspaceIdentityReader
{
    public const string EnabledConfigurationKey = "Authentication:WorkspaceTrustedHeaders:Enabled";
    public const string AllowedProxyNetworksConfigurationKey = "Authentication:WorkspaceTrustedHeaders:AllowedProxyNetworks";
    public const string IssuerHeader = "X-Catalog-Identity-Issuer";
    public const string SubjectHeader = "X-Catalog-Identity-Subject";
    public const string EmailHeader = "X-Catalog-Identity-Email";
    public const string NameHeader = "X-Catalog-Identity-Name";

    public ValueTask<TrustedWorkspaceIdentity?> ReadAsync(HttpContext context)
    {
        if (!configuration.GetValue<bool>(EnabledConfigurationKey))
            return ValueTask.FromResult<TrustedWorkspaceIdentity?>(null);

        if (!IsTrustedProxy(context.Connection.RemoteIpAddress))
            return ValueTask.FromResult<TrustedWorkspaceIdentity?>(null);

        var request = context.Request;
        var issuer = request.Headers[IssuerHeader].FirstOrDefault();
        var subject = request.Headers[SubjectHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            return ValueTask.FromResult<TrustedWorkspaceIdentity?>(null);

        return ValueTask.FromResult<TrustedWorkspaceIdentity?>(new TrustedWorkspaceIdentity(
            issuer,
            subject,
            request.Headers[NameHeader].FirstOrDefault(),
            request.Headers[EmailHeader].FirstOrDefault()));
    }

    private bool IsTrustedProxy(IPAddress? remoteIpAddress)
    {
        if (remoteIpAddress is null)
            return false;

        var address = Normalize(remoteIpAddress);
        foreach (var entry in ConfiguredProxyNetworks())
        {
            if (MatchesNetwork(address, entry))
                return true;
        }

        return false;
    }

    private IEnumerable<string> ConfiguredProxyNetworks()
    {
        var values = configuration.GetSection(AllowedProxyNetworksConfigurationKey).Get<string[]>();
        if (values is { Length: > 0 })
            return values.SelectMany(SplitNetworkEntries);

        return SplitNetworkEntries(configuration[AllowedProxyNetworksConfigurationKey]);
    }

    private static IEnumerable<string> SplitNetworkEntries(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x));

    private static bool MatchesNetwork(IPAddress address, string entry)
    {
        var parts = entry.Split('/', 2, StringSplitOptions.TrimEntries);
        if (!IPAddress.TryParse(parts[0], out var network))
            return false;

        network = Normalize(network);
        if (network.AddressFamily != address.AddressFamily)
            return false;

        if (parts.Length == 1)
            return network.Equals(address);

        var bitLength = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (!int.TryParse(parts[1], out var prefixLength) || prefixLength < 0 || prefixLength > bitLength)
            return false;

        return Contains(network, address, prefixLength);
    }

    private static bool Contains(IPAddress network, IPAddress address, int prefixLength)
    {
        var networkBytes = network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (networkBytes[i] != addressBytes[i])
                return false;
        }

        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xff << (8 - remainingBits));
        return (networkBytes[fullBytes] & mask) == (addressBytes[fullBytes] & mask);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

public static class WorkspaceIdentityHttpContextExtensions
{
    public static IResult UnauthorizedWorkspaceIdentity() =>
        Results.Problem(
            title: "Trusted workspace identity is required.",
            statusCode: StatusCodes.Status401Unauthorized);
}
