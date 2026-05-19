using System.Security.Cryptography;
using System.Text;

namespace Elsa.Catalog.Api.Authentication;

public sealed class AdminApiKeyValidator(IConfiguration configuration)
{
    public bool IsValid(string? suppliedApiKey)
    {
        var configuredApiKey = configuration[ApiKeyAuthenticationDefaults.ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredApiKey) || string.IsNullOrWhiteSpace(suppliedApiKey))
            return false;

        var configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedApiKey);
        using var hmac = new HMACSHA256(configuredBytes);
        var configuredHash = hmac.ComputeHash(configuredBytes);
        var suppliedHash = hmac.ComputeHash(suppliedBytes);
        return CryptographicOperations.FixedTimeEquals(configuredHash, suppliedHash);
    }
}
