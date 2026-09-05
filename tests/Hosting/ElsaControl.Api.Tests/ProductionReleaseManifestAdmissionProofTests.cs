using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElsaControl.Api.Authentication;
using ElsaControl.Api.ReleaseCatalog;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseCatalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace ElsaControl.Api.Tests;

/// <summary>
/// Opt-in production-composition proof for the release admission boundary. The ordinary test
/// run never reads the proof configuration or contacts a registry; the custom fact marks this
/// test skipped unless an operator explicitly enables it.
/// </summary>
public sealed class ProductionReleaseManifestAdmissionProofTests
{
    private const string Gate = "ELSA_CONTROL_LIVE_RELEASE_ADMISSION_PROOF";
    private const string ConfigurationPathVariable = "ELSA_CONTROL_LIVE_RELEASE_ADMISSION_CONFIG";
    private const string EnvironmentApiKeyVariable = "ELSA_CONTROL_LIVE_RELEASE_ADMISSION_API_KEY";
    private const string StandardEnvironmentApiKeyVariable = "Authentication__ApiKey";
    private const string ProofConfigurationSection = "ReleaseCatalog:AdmissionProof";
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private const int MaximumBundleBytes = 256 * 1024;
    private const int MinimumTimeoutSeconds = 30;
    private const int MaximumTimeoutSeconds = 1_800;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions FixtureJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [ProductionReleaseManifestAdmissionFact]
    public async Task Production_composition_admits_replays_and_rejects_without_catalog_mutation()
    {
        ProductionReleaseManifestAdmissionApplication? application = null;

        try
        {
            var inputs = LoadInputs();
            application = new ProductionReleaseManifestAdmissionApplication(inputs.ContentRoot);
            using var timeout = new CancellationTokenSource(inputs.Timeout);
            var cancellationToken = timeout.Token;
            var client = application.CreateClient();
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, inputs.ApiKey);

            using (var health = await client.GetAsync("/health", cancellationToken))
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            var before = await SnapshotAsync(application.Services, cancellationToken);
            Assert.DoesNotContain(before, identity => identity.ManifestDigest == inputs.Publication.Digest);

            using var first = await client.PostControlJsonAsync(
                "/api/admin/release-catalog/manifests",
                inputs.Request,
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var firstResult = await ReadAdmissionAsync(first, cancellationToken);
            Assert.Equal(GovernedReleaseCatalogWriteStatus.Stored, firstResult.Status);
            Assert.NotEmpty(firstResult.Entries);
            AssertIdentities(firstResult.Entries, inputs);

            var afterFirst = await SnapshotAsync(application.Services, cancellationToken);
            var admitted = afterFirst
                .Where(identity => identity.ManifestDigest == inputs.Publication.Digest)
                .ToArray();
            Assert.NotEmpty(admitted);
            Assert.All(admitted, identity => AssertExpectedIdentity(identity, inputs));

            using var replay = await client.PostControlJsonAsync(
                "/api/admin/release-catalog/manifests",
                inputs.Request,
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            var replayResult = await ReadAdmissionAsync(replay, cancellationToken);
            Assert.Equal(GovernedReleaseCatalogWriteStatus.Unchanged, replayResult.Status);
            Assert.Equal(firstResult.Entries.Count, replayResult.Entries.Count);
            AssertIdentities(replayResult.Entries, inputs);

            var afterReplay = await SnapshotAsync(application.Services, cancellationToken);
            Assert.Equal(afterFirst, afterReplay);

            using var tamperedPayload = await client.PostControlJsonAsync(
                "/api/admin/release-catalog/manifests",
                inputs.Request with { Payload = Tamper(inputs.Request.Payload!) },
                cancellationToken);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, tamperedPayload.StatusCode);
            var afterTamperedPayload = await SnapshotAsync(application.Services, cancellationToken);
            Assert.Equal(afterFirst, afterTamperedPayload);

            using var mutableReference = await client.PostControlJsonAsync(
                "/api/admin/release-catalog/manifests",
                inputs.Request with
                {
                    Reference = inputs.Request.Reference![..inputs.Request.Reference!.IndexOf('@')] + ":mutable"
                },
                cancellationToken);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, mutableReference.StatusCode);
            var afterMutableReference = await SnapshotAsync(application.Services, cancellationToken);
            Assert.Equal(afterFirst, afterMutableReference);

            await application.DisposeAsync();
            application = null;
            foreach (var rejectedPolicy in new[]
            {
                new Dictionary<string, string?>
                {
                    ["ReleaseCatalog:Admission:ExpectedSignatureSubject"] = "https://github.com/unapproved/producer/.github/workflows/release.yml@refs/heads/main"
                },
                new Dictionary<string, string?>
                {
                    ["ReleaseCatalog:Admission:ExpectedOidcIssuer"] = "https://issuer.invalid"
                }
            })
            {
                await using var rejectedApplication = new ProductionReleaseManifestAdmissionApplication(inputs.ContentRoot, rejectedPolicy);
                using var rejectedClient = rejectedApplication.CreateClient();
                rejectedClient.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, inputs.ApiKey);
                using var rejection = await rejectedClient.PostControlJsonAsync(
                    "/api/admin/release-catalog/manifests", inputs.Request, cancellationToken);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, rejection.StatusCode);
                Assert.Equal(afterFirst, await SnapshotAsync(rejectedApplication.Services, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            throw new XunitException("The opt-in production release-admission proof timed out.");
        }
        catch (Exception)
        {
            // Do not surface HTTP bodies, configuration, API keys, transport errors or raw
            // verifier diagnostics through the test failure. The operator can inspect the
            // host's separately retained, value-free test-run status.
            throw new XunitException("The opt-in production release-admission proof failed.");
        }
        finally
        {
            if (application is not null)
            {
                try
                {
                    await application.DisposeAsync();
                }
                catch (Exception)
                {
                    // Preserve the value-free proof result.
                }
            }
        }
    }

    private static async Task<AdminReleaseCatalogAdmissionResponse> ReadAdmissionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var admission = await response.Content.ReadControlJsonAsync<AdminReleaseCatalogAdmissionResponse>(cancellationToken);
        return admission ?? throw new InvalidDataException();
    }

    private static void AssertIdentities(
        IReadOnlyList<ReleaseCatalogEntryResponse> entries,
        ProofInputs inputs)
    {
        Assert.All(entries, entry =>
        {
            Assert.Equal(inputs.Publication.Reference, entry.ManifestReference);
            Assert.Equal(inputs.Publication.Digest, entry.ManifestDigest);
            Assert.Equal(inputs.Publication.PayloadDigest, entry.PayloadDigest);
            Assert.Equal(inputs.Publication.SignatureEvidence.Reference, entry.SignatureEvidenceReference);
            Assert.Equal(inputs.Publication.SignatureEvidence.Digest, entry.SignatureEvidenceDigest);
            Assert.Equal(inputs.RegistryClass, entry.RegistryClass);
        });
    }

    private static void AssertExpectedIdentity(CatalogIdentity identity, ProofInputs inputs)
    {
        Assert.Equal(inputs.Publication.Reference, identity.ManifestReference);
        Assert.Equal(inputs.Publication.Digest, identity.ManifestDigest);
        Assert.Equal(inputs.Publication.PayloadDigest, identity.PayloadDigest);
        Assert.Equal(inputs.Publication.SignatureEvidence.Reference, identity.SignatureEvidenceReference);
        Assert.Equal(inputs.Publication.SignatureEvidence.Digest, identity.SignatureEvidenceDigest);
        Assert.Equal(inputs.RegistryClass, identity.RegistryClass);
    }

    private static async Task<IReadOnlyList<CatalogIdentity>> SnapshotAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGovernedReleaseCatalogStore>();
        var entries = await store.QueryAsync(new GovernedReleaseCatalogQuery(), cancellationToken);
        return entries
            .Select(entry => new CatalogIdentity(
                entry.ManifestReference,
                entry.ManifestDigest,
                entry.PayloadDigest,
                entry.SignatureEvidenceReference,
                entry.SignatureEvidenceDigest,
                entry.RegistryClass,
                entry.Topology.Id,
                entry.Distribution.Id,
                entry.Distribution.Generation,
                entry.Distribution.ReleaseLine,
                entry.Distribution.ReleaseVersion,
                entry.Distribution.Channel,
                entry.Distribution.ProducerLifecycle,
                entry.CatalogLifecycle,
                entry.Distribution.SourceRepository,
                entry.Distribution.SourceCommit,
                entry.Distribution.SourceRunId,
                entry.ComponentDeclarations?.Digest,
                JsonSerializer.Serialize(entry, FixtureJsonOptions)))
            .OrderBy(identity => identity.ManifestDigest, StringComparer.Ordinal)
            .ThenBy(identity => identity.TopologyId, StringComparer.Ordinal)
            .ToArray();
    }

    private static ProofInputs LoadInputs()
    {
        var contentRoot = RequiredAbsoluteDirectory(Environment.GetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_ELSACONTROL_API"));
        var configurationPath = RequiredAbsoluteFile(Environment.GetEnvironmentVariable(ConfigurationPathVariable));
        var expectedConfigurationPath = Path.GetFullPath(Path.Combine(contentRoot, "appsettings.Production.json"));
        if (!string.Equals(configurationPath, expectedConfigurationPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ProofConfigurationException();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.Production.json", optional: false, reloadOnChange: false)
            .Build();

        if (!configuration.GetValue<bool>("ReleaseCatalog:Verification:Enabled") ||
            configuration.GetValue<bool>("Deployment:AzureProvider:WorkerEnabled") ||
            configuration.GetValue<bool>("Deployment:ElsaInstanceLifecycle:Enabled") ||
            configuration.GetValue<bool>("Billing:Lifecycle:Enabled"))
            throw new ProofConfigurationException();

        ValidateIsolatedSqliteDatabase(configuration);
        var publicationPath = RequiredAbsoluteFile(configuration[$"{ProofConfigurationSection}:PublicationPath"]);
        var payloadPath = RequiredAbsoluteFile(configuration[$"{ProofConfigurationSection}:PayloadPath"]);
        var subjectPath = RequiredAbsoluteFile(configuration[$"{ProofConfigurationSection}:SubjectPath"]);
        var signatureEvidencePath = RequiredAbsoluteFile(configuration[$"{ProofConfigurationSection}:SignatureEvidencePath"]);

        var publication = DeserializePublication(publicationPath);
        var payloadBytes = ReadBoundedFile(payloadPath, MaximumManifestBytes);
        var subjectBytes = ReadBoundedFile(subjectPath, MaximumManifestBytes);
        var signatureEvidenceBytes = ReadBoundedFile(signatureEvidencePath, MaximumBundleBytes);
        var payload = StrictUtf8.GetString(payloadBytes);

        if (!string.Equals(publication.ArtifactType, ReleaseRegistryProtocol.ReleaseArtifactType, StringComparison.Ordinal) ||
            !ReleaseRegistryProtocol.IsDigest(publication.Digest) ||
            !ReleaseRegistryProtocol.IsDigest(publication.PayloadDigest) ||
            !ReleaseRegistryProtocol.IsDigest(publication.SignatureEvidence.Digest) ||
            string.IsNullOrWhiteSpace(publication.Reference) ||
            string.IsNullOrWhiteSpace(publication.SignatureEvidence.Reference) ||
            !string.Equals(Digest(subjectBytes), publication.Digest, StringComparison.Ordinal) ||
            !string.Equals(Digest(payloadBytes), publication.PayloadDigest, StringComparison.Ordinal) ||
            !string.Equals(Digest(signatureEvidenceBytes), publication.SignatureEvidence.Digest, StringComparison.Ordinal))
            throw new ProofConfigurationException();

        var apiKey = configuration[ApiKeyAuthenticationDefaults.ConfigurationKey];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable(StandardEnvironmentApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable(EnvironmentApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(char.IsControl))
            throw new ProofConfigurationException();

        var registryClass = configuration[$"{ReleaseCatalogAdmissionOptions.ConfigurationSection}:RegistryClass"]?.Trim();
        if (string.IsNullOrWhiteSpace(registryClass) || registryClass.Any(char.IsControl))
            throw new ProofConfigurationException();

        var timeoutSeconds = ParseTimeout(configuration[$"{ProofConfigurationSection}:TimeoutSeconds"]);
        return new(
            contentRoot,
            apiKey,
            registryClass,
            publication,
            new AdminReleaseManifestIngestionRequest(publication.Reference, publication.Digest, payload),
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static ReleaseManifestPublication DeserializePublication(string path)
    {
        var publication = JsonSerializer.Deserialize<ReleaseManifestPublication>(
            File.ReadAllBytes(path), FixtureJsonOptions);
        return publication is { SignatureEvidence: not null }
            ? publication
            : throw new ProofConfigurationException();
    }

    private static byte[] ReadBoundedFile(string path, int maximumBytes)
    {
        var length = new FileInfo(path).Length;
        if (length is <= 0 || length > maximumBytes)
            throw new ProofConfigurationException();

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes.Length > maximumBytes || bytes.LongLength != length)
            throw new ProofConfigurationException();
        return bytes;
    }

    private static string RequiredAbsoluteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ProofConfigurationException();
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath) || IsReparsePoint(fullPath))
            throw new ProofConfigurationException();
        return fullPath;
    }

    private static string RequiredAbsoluteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ProofConfigurationException();
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) || IsReparsePoint(fullPath))
            throw new ProofConfigurationException();
        return fullPath;
    }

    private static bool IsReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static void ValidateIsolatedSqliteDatabase(IConfiguration configuration)
    {
        if (!string.Equals(configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase))
            throw new ProofConfigurationException();

        var connectionString = configuration.GetConnectionString("Catalog");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ProofConfigurationException();

        var connection = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = connection.DataSource?.Trim();
        if (string.IsNullOrWhiteSpace(dataSource) ||
            connection.Mode == SqliteOpenMode.Memory ||
            dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(dataSource))
            throw new ProofConfigurationException();

        var databasePath = Path.GetFullPath(dataSource);
        if (File.Exists(databasePath) || File.Exists(databasePath + "-wal") || File.Exists(databasePath + "-shm"))
            throw new ProofConfigurationException();
    }

    private static int ParseTimeout(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? 300
            : int.TryParse(value, out var seconds) && seconds is >= MinimumTimeoutSeconds and <= MaximumTimeoutSeconds
                ? seconds
                : throw new ProofConfigurationException();

    private static string Tamper(string payload) => payload + "\n";

    private static string Digest(ReadOnlySpan<byte> bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class ProductionReleaseManifestAdmissionApplication(
        string contentRoot,
        IReadOnlyDictionary<string, string?>? policyOverrides = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseContentRoot(contentRoot);
            if (policyOverrides is not null)
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(policyOverrides));
        }
    }

    private sealed record ProofInputs(
        string ContentRoot,
        string ApiKey,
        string RegistryClass,
        ReleaseManifestPublication Publication,
        AdminReleaseManifestIngestionRequest Request,
        TimeSpan Timeout);

    private sealed record ReleaseManifestPublication(
        string ArtifactType,
        string Reference,
        string Digest,
        string PayloadDigest,
        SignatureEvidencePublication SignatureEvidence);

    private sealed record SignatureEvidencePublication(string Reference, string Digest);

    private sealed record CatalogIdentity(
        string ManifestReference,
        string ManifestDigest,
        string PayloadDigest,
        string SignatureEvidenceReference,
        string SignatureEvidenceDigest,
        string RegistryClass,
        string TopologyId,
        string DistributionId,
        string DistributionGeneration,
        string ReleaseLine,
        string ReleaseVersion,
        string Channel,
        string ProducerLifecycle,
        string CatalogLifecycle,
        string SourceRepository,
        string SourceCommit,
        string SourceRunId,
        string? ComponentDeclarationsDigest,
        string ProjectionFingerprint);

    private sealed class ProofConfigurationException : Exception;

    private sealed class ProductionReleaseManifestAdmissionFactAttribute : FactAttribute
    {
        public ProductionReleaseManifestAdmissionFactAttribute()
        {
            if (!IsEnabled())
                Skip = $"{Gate} is not enabled; the production release-admission proof was explicitly skipped.";
        }

        private static bool IsEnabled() =>
            string.Equals(Environment.GetEnvironmentVariable(Gate), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable(Gate), "true", StringComparison.OrdinalIgnoreCase);
    }
}
