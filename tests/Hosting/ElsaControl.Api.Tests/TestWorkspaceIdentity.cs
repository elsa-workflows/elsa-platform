using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ElsaControl.Api.Tests;

internal static class TestWorkspaceIdentity
{
    public static HttpClient CreateControlIdentityClient(
        this ControlApiTestApplication app,
        string subject = "user-123",
        string? issuer = ControlApiTestApplication.TestControlIdentityIssuer,
        string? audience = ControlApiTestApplication.TestControlIdentityAudience,
        DateTimeOffset? expires = null,
        IReadOnlyDictionary<string, string>? claims = null)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(subject, issuer, audience, expires, claims));
        return client;
    }

    public static string CreateToken(
        string subject,
        string? issuer = ControlApiTestApplication.TestControlIdentityIssuer,
        string? audience = ControlApiTestApplication.TestControlIdentityAudience,
        DateTimeOffset? expires = null,
        IReadOnlyDictionary<string, string>? claims = null)
    {
        var now = DateTimeOffset.UtcNow;
        var tokenClaims = new List<Claim>();
        if (!string.IsNullOrWhiteSpace(subject))
            tokenClaims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));

        foreach (var claim in claims ?? new Dictionary<string, string>
                 {
                     ["name"] = "Ada Lovelace",
                     ["email"] = "ada@example.test"
                 })
        {
            tokenClaims.Add(new Claim(claim.Key, claim.Value));
        }

        var expiresAt = expires ?? now.AddMinutes(15);
        var notBefore = expiresAt <= now ? expiresAt.AddMinutes(-5) : now.AddMinutes(-1);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(tokenClaims),
            NotBefore = notBefore.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ControlApiTestApplication.TestControlIdentitySigningKey)),
                SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
