using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Ownership;
using FluentAssertions;

namespace ValenceControl.Healing.GitHub.Tests;

public sealed class GitHubProviderConnectionValidatorTests
{
    [Fact]
    public async Task Validation_mints_repository_narrowed_installation_token_and_returns_immutable_repository_id()
    {
        using var rsa = RSA.Create(2048);
        var credential = JsonSerializer.Serialize(new
        {
            appId = "12345",
            privateKeyPem = rsa.ExportRSAPrivateKeyPem()
        });
        var handler = new RecordingHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/access_tokens", StringComparison.Ordinal))
            {
                request.Headers.Authorization!.Scheme.Should().Be("Bearer");
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization.Parameter);
                jwt.Issuer.Should().Be("12345");
                jwt.Header.Alg.Should().Be("RS256");
                var body = await request.Content!.ReadAsStringAsync();
                using var document = JsonDocument.Parse(body);
                document.RootElement.GetProperty("repositories")[0].GetString().Should().Be("claims");
                document.RootElement.GetProperty("permissions").GetProperty("metadata").GetString().Should().Be("read");
                return Json(HttpStatusCode.Created, new
                {
                    token = "installation-token",
                    expires_at = DateTimeOffset.UtcNow.AddMinutes(30)
                });
            }

            request.RequestUri.AbsolutePath.Should().Be("/repos/acme/claims");
            request.Headers.Authorization!.Parameter.Should().Be("installation-token");
            return Json(HttpStatusCode.OK, new { id = 987654321L, full_name = "acme/claims" });
        });
        using var tokenClient = Client(handler);
        using var validationClient = Client(handler);
        var validator = new GitHubProviderConnectionValidator(
            validationClient,
            new GitHubAppTokenProvider(tokenClient),
            new StaticCredentialResolver(credential));

        var result = await validator.ValidateAsync(Connection());

        result.Should().Be(ProviderConnectionValidationResult.Valid("987654321"));
        handler.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task Validation_fails_closed_without_a_resolvable_structured_credential()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("GitHub must not be called."));
        using var tokenClient = Client(handler);
        using var validationClient = Client(handler);
        var validator = new GitHubProviderConnectionValidator(
            validationClient,
            new GitHubAppTokenProvider(tokenClient),
            new StaticCredentialResolver(null));

        var result = await validator.ValidateAsync(Connection());

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(HealingOwnershipReasonCodes.ProviderValidationFailed);
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task Validation_rejects_a_repository_identity_mismatch()
    {
        using var rsa = RSA.Create(2048);
        var credential = JsonSerializer.Serialize(new { appId = 12345, privateKeyPem = rsa.ExportRSAPrivateKeyPem() });
        var handler = new RecordingHandler(request => Task.FromResult(
            request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.Created, new { token = "installation-token", expires_at = DateTimeOffset.UtcNow.AddMinutes(30) })
                : Json(HttpStatusCode.OK, new { id = 1L, full_name = "acme/other" })));
        using var tokenClient = Client(handler);
        using var validationClient = Client(handler);
        var validator = new GitHubProviderConnectionValidator(
            validationClient,
            new GitHubAppTokenProvider(tokenClient),
            new StaticCredentialResolver(credential));

        var result = await validator.ValidateAsync(Connection());

        result.ReasonCode.Should().Be(HealingOwnershipReasonCodes.ProviderRepositoryMismatch);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Validation_fails_closed_when_github_is_unavailable(bool failDuringTokenExchange)
    {
        using var rsa = RSA.Create(2048);
        var credential = JsonSerializer.Serialize(new { appId = 12345, privateKeyPem = rsa.ExportRSAPrivateKeyPem() });
        var handler = new RecordingHandler(request =>
        {
            if (failDuringTokenExchange || request.Method == HttpMethod.Get)
                throw new HttpRequestException("GitHub is unavailable.");

            return Task.FromResult(Json(HttpStatusCode.Created, new
            {
                token = "installation-token",
                expires_at = DateTimeOffset.UtcNow.AddMinutes(30)
            }));
        });
        using var tokenClient = Client(handler);
        using var validationClient = Client(handler);
        var validator = new GitHubProviderConnectionValidator(
            validationClient,
            new GitHubAppTokenProvider(tokenClient),
            new StaticCredentialResolver(credential));

        var result = await validator.ValidateAsync(Connection());

        result.ReasonCode.Should().Be(HealingOwnershipReasonCodes.ProviderValidationFailed);
        handler.RequestCount.Should().Be(failDuringTokenExchange ? 1 : 2);
    }

    private static ProviderConnection Connection() => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        Provider = "GitHub",
        InstallationId = "42",
        RepositoryProviderId = "pending",
        RepositoryOwner = "acme",
        RepositoryName = "claims",
        CredentialReference = $"credential://{Guid.NewGuid():D}",
        Status = ProviderConnectionStatus.PendingValidation,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static HttpClient Client(HttpMessageHandler handler) => new(handler, disposeHandler: false)
    {
        BaseAddress = new Uri("https://api.github.com/")
    };

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object value) => new(statusCode)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class StaticCredentialResolver(string? credential) : IHealingProviderCredentialResolver
    {
        public ValueTask<string?> ResolveAsync(Guid workspaceId, string credentialReference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(credential);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return response(request);
        }
    }
}
