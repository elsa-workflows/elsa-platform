using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Agent;
using ValenceControl.Healing.Core.Configuration;
using ValenceControl.Healing.Core.Incidents;
using ValenceControl.Healing.Core.Manifests;
using ValenceControl.Healing.Core.Ownership;
using ValenceControl.Healing.Core.OpenTelemetry;
using ValenceControl.Healing.Core.Providers;
using ValenceControl.Healing.Core.Repairs;
using ValenceControl.Healing.Core.Reporting;
using ValenceControl.Healing.Core.Security;
using ValenceControl.Healing.Core.Verification;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.OpenTelemetry;
using ValenceControl.Api.Workspace.Healing;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ValenceControl.Api.Healing;

public static class HealingServiceCollectionExtensions
{
    public const string WorkersEnabledConfigurationKey = "Healing:Workers:Enabled";
    public const string TestingEnvironmentName = "Testing";

    public static ControlHealingBuilder AddControlHealing(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<HealingOptions>()
            .Bind(configuration.GetSection(HealingOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<HealingGitHubOptions>()
            .Bind(configuration.GetSection(HealingGitHubOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.WorkloadAudience), "A Healing GitHub workload audience is required.")
            .Validate(options => options.CapabilityLifetime > TimeSpan.Zero &&
                                 options.CapabilityLifetime <= TimeSpan.FromHours(1) &&
                                 options.AttemptLeaseLifetime > TimeSpan.Zero &&
                                 options.AttemptLeaseLifetime <= RepairOrchestrationService.MaximumLeaseDuration &&
                                 options.ProposalLifetime > options.AttemptLeaseLifetime &&
                                 options.ProposalLifetime <= TimeSpan.FromHours(24),
                "Healing GitHub capability, lease, and proposal lifetimes must be positive and bounded.")
            .ValidateOnStart();
        services.AddOptions<CopilotRepairProposalOptions>()
            .Bind(configuration.GetSection("Healing:ManagedInference:Copilot"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model) &&
                                 options.MaximumTurnSeconds is > 0 and <= 3_600,
                "Managed repair inference configuration is invalid.")
            .ValidateOnStart();
        services.AddOptions<HealingVerificationFailureDeliveryOptions>()
            .Bind(configuration.GetSection(HealingVerificationFailureDeliveryOptions.SectionName))
            .Validate(options => options.IsValid(),
                "Verification failure delivery requires bounded batch, lease, and retry settings; its HTTP consumer requires an HTTPS endpoint and a shared secret of at least 32 bytes.")
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<HealingOptions>, HealingOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IWorkspacePermissionContribution, HealingWorkspacePermissionContribution>());

        services.AddHealingDbContext(configuration);
        services.TryAddScoped<HealingStore>();
        services.TryAddScoped<IHealingOwnershipStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingAdministrationStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingAuditStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingIncidentStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingSignalInboxStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingTelemetrySourceStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IProviderOperationStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingEvidenceStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IRepairOrchestrationStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingStore>());
        services.TryAddScoped<IHealingMergeEvaluationStore, HealingMergeEvaluationStore>();
        services.TryAddScoped<IHumanProviderCommandStore, HealingHumanProviderCommandStore>();
        services.TryAddScoped<HealingVerificationStore>();
        services.TryAddScoped<IHealingVerificationStore>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingVerificationStore>());
        services.TryAddScoped<IRepairVerificationFailedSignalOutbox>(serviceProvider =>
            serviceProvider.GetRequiredService<HealingVerificationStore>());
        services.TryAddScoped<IHealingReportingStore, HealingReportingStore>();
        services.TryAddScoped<IHealingEvidenceSource, HealingEvidenceSource>();
        services.TryAddScoped<IHealingEvidenceElevationAuthorizer, DenyHealingEvidenceElevationAuthorizer>();
        services.TryAddScoped<HealingAuditService>();
        services.TryAddScoped<HealingConfigurationService>();
        services.TryAddScoped<ComponentManifestService>();
        services.TryAddScoped<IComponentManifestAttestationAuthority, ControlManagedComponentManifestAttestationAuthority>();
        services.TryAddScoped<SourceOwnershipService>();
        services.TryAddScoped<ComponentAttributionService>();
        services.TryAddScoped<HealingIncidentService>();
        services.TryAddScoped<HealingSignalInboxWorker>();
        services.TryAddScoped<HealingEvidenceService>();
        services.TryAddScoped<RepairOrchestrationService>();
        services.TryAddScoped<HealingRepairCoordinator>();
        services.TryAddScoped<HealingMergeService>();
        services.TryAddScoped<ITrustedDeploymentSafetyCapabilitySource, TrustedDeploymentSafetyCapabilitySource>();
        services.TryAddScoped<HealingAutoMergeCoordinator>();
        services.TryAddScoped<HumanProviderCommandService>();
        services.TryAddScoped<HealingHumanCommandCoordinator>();
        services.TryAddScoped<HealingReportingService>();
        services.TryAddScoped<HealingRepairAuthorityService>();
        services.TryAddScoped<DeploymentObservationService>();
        services.TryAddScoped<HealingVerificationService>();
        services.TryAddScoped<HealingVerificationWorker>();
        services.TryAddScoped<HealingVerificationFailureDeliveryService>();
        services.TryAddScoped<IDeploymentObservationSink>(serviceProvider =>
            serviceProvider.GetRequiredService<DeploymentObservationService>());
        services.TryAddScoped<IRepairVerificationSignalSink, HealingRepairVerificationSignalSink>();
        services.TryAddScoped<ControlDeploymentHealingObserver>();
        services.TryAddScoped(serviceProvider => new ProviderOperationService(
            serviceProvider.GetRequiredService<IProviderOperationStore>(),
            serviceProvider.GetServices<IProviderOperationHandler>(),
            serviceProvider.GetRequiredService<IOptions<HealingOptions>>().Value,
            $"provider:{Environment.MachineName}:{Guid.NewGuid():N}"));
        services.TryAddSingleton<HealingSignalNormalizer>();
        services.TryAddSingleton<HealingSignalClassifier>();
        services.TryAddSingleton<HealingFingerprintService>();
        services.TryAddScoped<IHealingSignalInboxAppender, ControlHealingSignalInboxAppender>();
        services.TryAddScoped<IHealingTelemetryScopeResolver, AuthenticatedClaimHealingTelemetryScopeResolver>();
        services.TryAddSingleton<HealingTelemetrySourceTokenService>();
        services.TryAddScoped<HealingTelemetrySourceService>();
        services.Replace(ServiceDescriptor.Scoped<IOtlpRequestAuthenticator, ControlHealingOtlpRequestAuthenticator>());
        services.AddOpenTelemetryDiagnosticsServices(options =>
            configuration.GetSection("Healing:OpenTelemetry").Bind(options));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOpenTelemetryIngestionContributor, HealingOpenTelemetryIngestionContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealingEndpointModule, WorkspaceHealingTelemetrySourceEndpointModule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealingEndpointModule, HealingReportingEndpointModule>());
        services.TryAddScoped<HealingAdministrationService>();
        services.TryAddScoped<IHealingProviderCredentialResolver, WorkspaceHealingProviderCredentialResolver>();
        services.AddHttpClient<GitHubAppTokenProvider>(client => client.BaseAddress = new Uri("https://api.github.com/"));
        services.AddHttpClient<IRepairWorkProvider, GitHubRepairWorkProvider>(client =>
            client.BaseAddress = new Uri("https://api.github.com/"));
        services.AddHttpClient<ITrustedGitHubRepositoryPublisher, GitHubHttpTrustedRepositoryPublisher>(client =>
            client.BaseAddress = new Uri("https://api.github.com/"));
        services.AddHttpClient<IRepairMergeProvider, GitHubMergeProvider>(client =>
            client.BaseAddress = new Uri("https://api.github.com/"));
        services.AddHttpClient<IGitHubRepositoryPermissionProvider, GitHubRepositoryPermissionProvider>(client =>
            client.BaseAddress = new Uri("https://api.github.com/"));
        services.AddHttpClient<HttpRepairVerificationFailureConsumer>();
        if (!string.IsNullOrWhiteSpace(configuration[$"{HealingVerificationFailureDeliveryOptions.SectionName}:Endpoint"]))
        {
            services.TryAddScoped<IRepairVerificationFailureConsumer>(serviceProvider =>
                serviceProvider.GetRequiredService<HttpRepairVerificationFailureConsumer>());
        }
        services.AddHttpClient<GitHubOidcConfigurationSigningKeyProvider>();
        services.AddHttpClient<IProviderConnectionValidator, GitHubProviderConnectionValidator>(
            client => client.BaseAddress = new Uri("https://api.github.com/"));
        services.TryAddScoped<IGitHubRepositoryAuthorizationResolver, ControlGitHubRepositoryAuthorizationResolver>();
        services.TryAddScoped<IGitHubProviderOperationLedger, ControlGitHubProviderOperationLedger>();
        services.TryAddScoped<ControlGitHubReplayStore>();
        services.TryAddScoped<IGitHubWorkloadReplayStore>(serviceProvider =>
            serviceProvider.GetRequiredService<ControlGitHubReplayStore>());
        services.TryAddScoped<IGitHubWebhookReplayStore>(serviceProvider =>
            serviceProvider.GetRequiredService<ControlGitHubReplayStore>());
        services.TryAddScoped<IGitHubOidcSigningKeyProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<GitHubOidcConfigurationSigningKeyProvider>());
        services.TryAddScoped(serviceProvider => new GitHubWorkloadIdentityValidator(
            serviceProvider.GetRequiredService<IOptions<HealingGitHubOptions>>().Value.WorkloadAudience,
            serviceProvider.GetRequiredService<IGitHubOidcSigningKeyProvider>(),
            serviceProvider.GetRequiredService<IGitHubWorkloadReplayStore>(),
            serviceProvider.GetRequiredService<TimeProvider>()));
        services.TryAddScoped(serviceProvider => new GitHubWebhookVerifier(
            serviceProvider.GetRequiredService<IGitHubWebhookReplayStore>(),
            serviceProvider.GetRequiredService<TimeProvider>()));
        services.TryAddScoped<ITrustedGitHubPublicationContextResolver, ControlTrustedGitHubPublicationContextResolver>();
        services.TryAddScoped<ITrustedPatchPublisher, TrustedGitHubPatchPublisher>();
        services.TryAddScoped<IRepairTargetInspector, ControlGitHubRepairTargetInspector>();
        services.TryAddScoped<HealingGitHubOptions>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<HealingGitHubOptions>>().Value);
        services.TryAddScoped<ControlHealingWorkloadAuthorityService>();
        services.TryAddScoped<IHealingWorkloadRequestAuthorizer, ControlHealingWorkloadRequestAuthorizer>();
        services.TryAddScoped<IManagedRepairCopilotRuntime, CopilotRepairRuntime>();
        services.TryAddScoped<IRepairProposalProvider, CopilotRepairProposalProvider>();
        services.TryAddScoped<IHealingWorkloadApi, ControlHealingWorkloadApi>();
        services.TryAddSingleton<GitHubWebhookProcessor>();
        services.TryAddScoped<ControlHealingGitHubWebhookProcessor>();
        services.TryAddScoped<IControlHealingGitHubWebhookProcessor>(serviceProvider =>
            serviceProvider.GetRequiredService<ControlHealingGitHubWebhookProcessor>());
        services.TryAddSingleton<IControlHealingGitHubWebhookProcessorRunner,
            ScopedControlHealingGitHubWebhookProcessorRunner>();
        services.TryAddScoped<IHealingVerifiedWebhookHandler, ControlHealingVerifiedWebhookHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProviderOperationHandler, GitHubUpsertWorkItemOperationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProviderOperationHandler, GitHubDispatchWorkflowOperationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProviderOperationHandler, GitHubPublishPullRequestOperationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProviderOperationHandler, GitHubRequestMergeOperationHandler>());
        services.TryAddSingleton(serviceProvider =>
            new HealingKillSwitch(serviceProvider.GetRequiredService<IOptionsMonitor<HealingOptions>>()));

        return new ControlHealingBuilder(services, configuration, environment);
    }

    public static IEndpointRouteBuilder MapControlHealingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        foreach (var module in endpoints.ServiceProvider.GetServices<IHealingEndpointModule>())
            module.MapEndpoints(endpoints);

        return endpoints;
    }

    public static Task MigrateControlHealingDatabaseAsync(
        this IServiceProvider scopedServices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);
        return scopedServices.GetRequiredService<HealingDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

public sealed class ControlHealingBuilder
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    internal ControlHealingBuilder(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        Services = services;
        _configuration = configuration;
        _environment = environment;
    }

    public IServiceCollection Services { get; }

    public ControlHealingBuilder AddHostedWorker<TWorker>() where TWorker : class, IHostedService
        => AddHostedWorkerCore<TWorker>(stageEnabledConfigurationKey: null);

    public ControlHealingBuilder AddHostedWorker<TWorker>(string stageEnabledConfigurationKey)
        where TWorker : class, IHostedService
        => AddHostedWorkerCore<TWorker>(stageEnabledConfigurationKey);

    private ControlHealingBuilder AddHostedWorkerCore<TWorker>(string? stageEnabledConfigurationKey)
        where TWorker : class, IHostedService
    {
        if (!_environment.IsEnvironment(HealingServiceCollectionExtensions.TestingEnvironmentName) &&
            _configuration.GetValue(HealingServiceCollectionExtensions.WorkersEnabledConfigurationKey, false) &&
            (stageEnabledConfigurationKey is null || _configuration.GetValue(stageEnabledConfigurationKey, true)))
        {
            Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TWorker>());
        }

        return this;
    }

    public ControlHealingBuilder AddEndpointModule<TModule>() where TModule : class, IHealingEndpointModule
        => AddEndpointModuleCore<TModule>(stageEnabledConfigurationKey: null);

    public ControlHealingBuilder AddEndpointModule<TModule>(string stageEnabledConfigurationKey)
        where TModule : class, IHealingEndpointModule
        => AddEndpointModuleCore<TModule>(stageEnabledConfigurationKey);

    private ControlHealingBuilder AddEndpointModuleCore<TModule>(string? stageEnabledConfigurationKey)
        where TModule : class, IHealingEndpointModule
    {
        if (stageEnabledConfigurationKey is null || _configuration.GetValue(stageEnabledConfigurationKey, true))
            Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealingEndpointModule, TModule>());
        return this;
    }
}

public interface IHealingEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

internal sealed class HealingWorkspacePermissionContribution : IWorkspacePermissionContribution
{
    public IReadOnlySet<string> All => HealingPermissions.All;
    public IReadOnlySet<string> OwnerDefaults => HealingPermissions.All;
}

internal sealed class DenyHealingEvidenceElevationAuthorizer : IHealingEvidenceElevationAuthorizer
{
    public ValueTask<EvidenceElevationAuthorization> AuthorizeAsync(
        EvidenceElevationAuthorizationRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(EvidenceElevationAuthorization.Denied("evidence-elevation-requires-explicit-authorization"));
}
