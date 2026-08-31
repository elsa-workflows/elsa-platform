using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Concrete adapter for the checked-in disposable Azure workload authority. The adapter keeps
/// Azure CLI/SQL command details below <see cref="IAzureProviderRunner"/> and projects every
/// successful response into bounded resource references. It never returns provider payloads,
/// command output, or resolved secret values.
/// </summary>
public sealed class AzureBicepProviderRunner : IAzureProviderRunner
{
    private const string AcrPullRoleDefinitionId = "7f951dda-4ed3-4680-a7ca-43fe172d538d";
    private const string SupportedImageRepository = "valenceruntimeimages.azurecr.io/runtime-combined";
    private const string KeyVaultSecretsUserRoleDefinitionId = "4633458b-17de-408a-b874-0445c86b69e6";
    private const string KeyVaultSecretsOfficerRoleDefinitionId = "b86a8fe4-44ce-4948-aee5-eccb2c155cd7";
    private const string TemporaryFirewallRuleName = "elsa108-bootstrap";
    private const string ProofTag = "108";
    private const string SqlConnectionSecretName = "sql-connection";
    private const string SigningKeySecretName = "identity-signing-key";
    private const string AdminPasswordSecretName = "admin-password";
    private const string AdminUsername = "proof-admin";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AzureProviderRunnerOptions _options;
    private readonly AzureProviderTargetScope _scope;
    private readonly IAzureCommandProcess _process;
    private readonly IAzureSecretResolver _secretResolver;

    public AzureBicepProviderRunner(
        AzureProviderRunnerOptions options,
        AzureProviderTargetScope scope,
        IAzureSecretResolver? secretResolver = null)
        : this(options, scope, new AzureCommandProcess(options.CommandTimeout, options.MaximumOutputCharacters), secretResolver)
    {
    }

    internal AzureBicepProviderRunner(
        AzureProviderRunnerOptions options,
        AzureProviderTargetScope scope,
        IAzureCommandProcess process,
        IAzureSecretResolver? secretResolver = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _secretResolver = secretResolver ?? new UnconfiguredAzureSecretResolver();
        _options.Validate();
        _scope.Validate();
    }

    public async Task<AzureProviderRunnerResult> RunAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            ValidateCommand(command);
        }
        catch (ArgumentException exception)
        {
            return Failed(command, CurrentPhase(command.Step), "azure.runner.input-invalid", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Failed(command, CurrentPhase(command.Step), "azure.runner.scope-invalid", exception.Message);
        }

        try
        {
            return command.Step switch
            {
                AzureProviderRunnerStep.Foundation => await RunFoundationAsync(command, cancellationToken),
                AzureProviderRunnerStep.AcrPull => await RunAcrPullAsync(command, cancellationToken),
                AzureProviderRunnerStep.SeedSecrets => await RunSeedSecretsAsync(command, cancellationToken),
                AzureProviderRunnerStep.SqlBootstrap => await RunSqlBootstrapAsync(command, cancellationToken),
                AzureProviderRunnerStep.Workload => await RunWorkloadAsync(command, cancellationToken),
                AzureProviderRunnerStep.Health => await RunHealthAsync(command, cancellationToken),
                AzureProviderRunnerStep.Promotion => await RunPromotionAsync(command, cancellationToken),
                AzureProviderRunnerStep.RestoreStableTraffic => await RunRestoreStableTrafficAsync(command, cancellationToken),
                AzureProviderRunnerStep.Cleanup => await RunCleanupAsync(command, cancellationToken),
                _ => Failed(command, CurrentPhase(command.Step), "azure.runner.step-invalid", "The Azure lifecycle step is not supported.")
            };
        }
        catch (OperationCanceledException)
        {
            return Uncertain(command, CurrentPhase(command.Step), "azure.runner.cancelled", "The Azure lifecycle step was interrupted before its result was confirmed.");
        }
        catch (Exception)
        {
            // Provider exceptions are deliberately value-free. A process/SDK failure may have
            // committed a remote mutation, so the durable executor must recover it explicitly.
            return Uncertain(command, CurrentPhase(command.Step), "azure.runner.uncertain", "The Azure lifecycle step failed before its external result was confirmed.");
        }
    }

    private async Task<AzureProviderRunnerResult> RunFoundationAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        var resources = command.Resources with
        {
            ResourceGroupName = _scope.ResourceGroupName,
            FoundationDeploymentId = DeploymentId(_scope.SubscriptionId, _scope.ResourceGroupName, FoundationDeploymentName(command)),
            WorkloadIdentityResourceId = ResourceId("Microsoft.ManagedIdentity", "userAssignedIdentities", $"{command.Plan.WorkloadName}-identity"),
            KeyVaultResourceId = ResourceId("Microsoft.KeyVault", "vaults", $"{command.Plan.WorkloadName}-kv"),
            KeyVaultUri = $"https://{command.Plan.WorkloadName}-kv.vault.azure.net/",
            SqlServerResourceId = ResourceId("Microsoft.Sql", "servers", $"{command.Plan.WorkloadName}-sql"),
            SqlServerFqdn = $"{command.Plan.WorkloadName}-sql.database.windows.net",
            ContainerAppsEnvironmentResourceId = ResourceId("Microsoft.App", "managedEnvironments", $"{command.Plan.WorkloadName}-aca")
        };
        var groupId = ResourceGroupId();
        var exists = await ExecuteAzAsync(command,
            ["group", "exists", "--subscription", _scope.SubscriptionId, "--name", _scope.ResourceGroupName, "--output", "tsv", "--only-show-errors"],
            ParseBooleanAsync,
            cancellationToken);
        if (!exists.Succeeded)
            return ProcessFailure(command, AzureProviderOperationPhase.FoundationSubmitted, exists, resources, mutation: false);

        if (!exists.Value!.Value)
        {
            EnsureMutationAuthority(command);
            var created = await ExecuteAzAsync<AzureCommandNoOutput>(command,
                ["group", "create", "--subscription", _scope.SubscriptionId, "--name", _scope.ResourceGroupName,
                    "--location", _scope.Location, "--tags", $"proof={ProofTag}", $"owner={_options.Owner}",
                    $"proof-name={command.Plan.WorkloadName}", $"expiry={_options.ExpiryUtc:yyyy-MM-dd}",
                    $"sqlBootstrapObjectId={_options.SqlBootstrapObjectId}", "--output", "none", "--only-show-errors"],
                static _ => AzureCommandNoOutput.Instance,
                cancellationToken);
            if (!created.Succeeded)
                return ProcessFailure(command, AzureProviderOperationPhase.FoundationSubmitted, created, resources, mutation: true);
        }
        else
        {
            var tags = await ExecuteAzAsync(command,
                ["group", "show", "--subscription", _scope.SubscriptionId, "--name", _scope.ResourceGroupName,
                    "--query", "tags", "--output", "json", "--only-show-errors"],
                ParseTagsAsync,
                cancellationToken);
            if (!tags.Succeeded || tags.Value is null)
                return ProcessFailure(command, AzureProviderOperationPhase.FoundationSubmitted, tags, resources, mutation: false);
            if (!OwnsGroup(tags.Value!.Value, command.Plan.WorkloadName))
                return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.foundation.ownership-invalid", "The target resource group is not owned by this workload.");

            EnsureMutationAuthority(command);
            var updated = await ExecuteAzAsync<AzureCommandNoOutput>(command,
                ["tag", "update", "--subscription", _scope.SubscriptionId, "--resource-id", groupId,
                    "--operation", "Merge", "--tags", $"proof={ProofTag}", $"owner={_options.Owner}",
                    $"proof-name={command.Plan.WorkloadName}", $"expiry={_options.ExpiryUtc:yyyy-MM-dd}",
                    $"sqlBootstrapObjectId={_options.SqlBootstrapObjectId}", "--output", "none", "--only-show-errors"],
                static _ => AzureCommandNoOutput.Instance,
                cancellationToken);
            if (!updated.Succeeded)
                return ProcessFailure(command, AzureProviderOperationPhase.FoundationSubmitted, updated, resources, mutation: true);

            var adminReady = await EnsureSqlBootstrapAdminForReapplyAsync(command, cancellationToken);
            if (adminReady is null)
                return Uncertain(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.foundation.sql-admin-uncertain", "The SQL bootstrap administrator could not be confirmed for reconciliation.", resources);
            if (!adminReady.Value)
                return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.foundation.sql-admin-invalid", "The existing SQL administrator does not match the governed bootstrap identity.");
        }

        var deploymentName = FoundationDeploymentName(command);
        EnsureMutationAuthority(command);
        var output = await ExecuteAzAsync(command,
            FoundationDeploymentArguments(command, deploymentName),
            ParseDeploymentOutputsAsync,
            cancellationToken);
        if (!output.Succeeded || output.Value is null)
            return ProcessFailure(command, AzureProviderOperationPhase.FoundationSubmitted, output, resources, mutation: true);

        try
        {
            resources = ProjectFoundation(output.Value!.Value, command.Plan, deploymentName);
            return Completed(command, AzureProviderOperationPhase.FoundationSubmitted, resources);
        }
        catch (ArgumentException exception)
        {
            return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.foundation.output-invalid", exception.Message, resources);
        }
    }

    private async Task<AzureProviderRunnerResult> RunAcrPullAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        var foundation = RequireFoundation(command.Resources);
        if (foundation is not null)
            return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.acr.foundation-missing", foundation);

        var registryIdResult = await ExecuteAzAsync(command,
            ["acr", "show", "--subscription", _scope.RegistrySubscriptionId, "--resource-group", _scope.RegistryResourceGroupName,
                "--name", _scope.RegistryName, "--query", "id", "--output", "tsv", "--only-show-errors"],
            ParseStringAsync,
            cancellationToken);
        if (!registryIdResult.Succeeded || string.IsNullOrWhiteSpace(registryIdResult.Value?.Value))
            return ProcessFailure(command, AzureProviderOperationPhase.FoundationSubmitted, registryIdResult, command.Resources, mutation: false);

        try
        {
            ValidateExactResourceId(registryIdResult.Value!.Value, _scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName,
                "Microsoft.ContainerRegistry", "registries", _scope.RegistryName);
        }
        catch (ArgumentException exception)
        {
            return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.acr.scope-invalid", exception.Message);
        }

        var deploymentName = AcrDeploymentName(command, command.Resources.WorkloadIdentityPrincipalId!);
        var resources = command.Resources with
        {
            RegistryResourceId = registryIdResult.Value!.Value,
            AcrPullDeploymentId = DeploymentId(_scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName, deploymentName)
        };
        EnsureMutationAuthority(command);
        var deployment = await ExecuteAzAsync(command,
            AcrDeploymentArguments(command, command.Resources.WorkloadIdentityResourceId!, command.Resources.WorkloadIdentityPrincipalId!, deploymentName),
            ParseDeploymentOutputsAsync,
            cancellationToken);
        if (!deployment.Succeeded || deployment.Value is null)
            return ProcessFailure(command, AzureProviderOperationPhase.FoundationSubmitted, deployment, resources, mutation: true);

        var roleAssignmentId = deployment.Value!.Value.String("roleAssignmentId");
        if (roleAssignmentId is null)
            return Uncertain(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.acr.output-invalid", "The ACR role assignment identity was not returned.", resources);
        try
        {
            ValidateExactRoleAssignmentId(roleAssignmentId, registryIdResult.Value!.Value);
        }
        catch (ArgumentException exception)
        {
            return Uncertain(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.acr.role-scope-invalid", exception.Message, resources);
        }

        var assignment = await WaitForRoleAssignmentAsync(command,
            registryIdResult.Value!.Value,
            command.Resources.WorkloadIdentityPrincipalId!,
            roleAssignmentId,
            cancellationToken);
        if (assignment is null)
            return Uncertain(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.acr.role-observation-uncertain", "The ACR role assignment could not be confirmed.", resources with { AcrPullRoleAssignmentId = roleAssignmentId });
        if (assignment is false)
            return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.acr.role-invalid", "The ACR role assignment did not match the governed registry scope.");

        resources = resources with
        {
            RegistryResourceId = registryIdResult.Value!.Value,
            AcrPullDeploymentId = DeploymentId(_scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName, deploymentName),
            AcrPullRoleAssignmentId = roleAssignmentId
        };
        return Completed(command, AzureProviderOperationPhase.FoundationSubmitted, resources);
    }

    private async Task<AzureProviderRunnerResult> RunSeedSecretsAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        var missing = RequireRegistry(command.Resources);
        if (missing is not null)
            return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.secrets.foundation-missing", missing);

        var vaultName = ResourceName(command.Resources.KeyVaultResourceId);
        if (vaultName is null)
            return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.secrets.vault-missing", "The Key Vault resource identity is missing.");

        (string Key, string Reference, string Name)[] secretReferences;
        try
        {
            secretReferences = (command.Plan.SecretReferences ?? EmptyReferences)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => (x.Key, Reference: x.Value, Name: MapSecretName(x.Key)))
                .ToArray();
        }
        catch (ArgumentException exception)
        {
            return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.secrets.name-invalid", exception.Message);
        }
        var changed = false;
        foreach (var (key, reference, secretName) in secretReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await ExecuteAzAsync(command,
                ["keyvault", "secret", "list", "--subscription", _scope.SubscriptionId, "--vault-name", vaultName,
                    "--query", $"[?name=='{secretName}'] | length(@)", "--output", "tsv", "--only-show-errors"],
                ParseIntegerAsync,
                cancellationToken);
            if (!existing.Succeeded || existing.Value is null)
                return ProcessFailure(command, AzureProviderOperationPhase.FoundationSubmitted, existing, command.Resources, mutation: false);
            if (existing.Value.Value == 1)
                continue;
            if (existing.Value.Value != 0)
                return Failed(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.secrets.inventory-invalid", "The secret inventory is ambiguous.");

            AzureSecretLease lease;
            try
            {
                lease = await _secretResolver.ResolveAsync(
                    new AzureSecretResolutionRequest(command.Context.WorkspaceId, key, reference, command.Resources), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Uncertain(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.secrets.cancelled", "Secret reference seeding was interrupted before completion.");
            }
            catch (Exception)
            {
                return Uncertain(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.secrets.resolve-uncertain", "An approved secret reference could not be resolved.");
            }

            await using (lease)
            {
                string directory;
                string file;
                try
                {
                    (directory, file) = await WriteTransientSecretFileAsync(lease, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return Uncertain(command, AzureProviderOperationPhase.FoundationSubmitted, "azure.secrets.cancelled", "Secret reference seeding was interrupted before completion.");
                }
                try
                {
                    EnsureMutationAuthority(command);
                    var seeded = await ExecuteAzAsync<AzureCommandNoOutput>(command,
                        ["keyvault", "secret", "set", "--subscription", _scope.SubscriptionId, "--vault-name", vaultName,
                            "--name", secretName, "--file", file, "--output", "none", "--only-show-errors"],
                        static _ => AzureCommandNoOutput.Instance,
                        cancellationToken);
                    if (!seeded.Succeeded)
                        return ProcessFailure(command, AzureProviderOperationPhase.FoundationSubmitted, seeded, command.Resources, mutation: true);
                    changed = true;
                }
                finally
                {
                    DeleteTransientSecretFile(directory, file);
                }
            }
        }

        return Completed(command, AzureProviderOperationPhase.FoundationSubmitted, command.Resources, noOp: !changed);
    }

    private async Task<AzureProviderRunnerResult> RunSqlBootstrapAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        var missing = RequireRegistry(command.Resources);
        if (missing is not null)
            return Failed(command, AzureProviderOperationPhase.FoundationReady, "azure.sql.foundation-missing", missing);
        if (command.Resources.SqlServerFqdn is null || command.Resources.WorkloadIdentityClientId is null)
            return Failed(command, AzureProviderOperationPhase.FoundationReady, "azure.sql.output-missing", "The SQL bootstrap identities are incomplete.");

        var compatibility = await ExecuteSqlCmdAsync<SafeValue<bool>>(command,
            ["-?"],
            output => new SafeValue<bool>(output.ToString().Contains("--authentication-method", StringComparison.Ordinal)),
            cancellationToken);
        if (!compatibility.Succeeded || compatibility.Value?.Value != true)
            return ProcessFailure(command, AzureProviderOperationPhase.FoundationReady, compatibility, command.Resources, mutation: false);

        EnsureMutationAuthority(command);
        var firewall = await ExecuteAzAsync<AzureCommandNoOutput>(command,
            ["sql", "server", "firewall-rule", "create", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--server", SqlServerName(command), "--name", TemporaryFirewallRuleName, "--start-ip-address", _options.SqlBootstrapIp,
                "--end-ip-address", _options.SqlBootstrapIp, "--output", "none", "--only-show-errors"],
            static _ => AzureCommandNoOutput.Instance,
            cancellationToken);
        if (!firewall.Succeeded)
            return ProcessFailure(command, AzureProviderOperationPhase.FoundationReady, firewall, command.Resources, mutation: true);

        var temporaryDirectory = string.Empty;
        var scriptPath = string.Empty;
        var firewallCleaned = false;
        try
        {
            (temporaryDirectory, scriptPath) = await WriteSqlBootstrapFileAsync(command.Resources.WorkloadIdentityClientId, command.Plan.WorkloadName, cancellationToken);
            var sqlSucceeded = false;
            for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
            {
                EnsureMutationAuthority(command);
                var bootstrap = await ExecuteSqlCmdAsync<AzureCommandNoOutput>(command,
                    ["-S", $"tcp:{command.Resources.SqlServerFqdn},1433", "-d", "Elsa", "--authentication-method", "ActiveDirectoryDefault",
                        "-i", scriptPath],
                    static _ => AzureCommandNoOutput.Instance,
                    cancellationToken);
                if (bootstrap.Succeeded)
                {
                    sqlSucceeded = true;
                    break;
                }
                if (bootstrap.Status == AzureCommandProcessStatus.Cancelled || cancellationToken.IsCancellationRequested)
                    break;
                if (bootstrap.Status == AzureCommandProcessStatus.TerminationUncertain ||
                    bootstrap.FailureKind == AzureCommandProcessFailureKind.TerminationUncertain)
                    break;
                if (attempt + 1 < _options.ObservationAttempts)
                    await Task.Delay(_options.ObservationDelay, cancellationToken);
            }

            var firewallAbsent = await DeleteAndVerifyFirewallAsync(command, SqlServerName(command), CancellationToken.None);
            if (!firewallAbsent)
                return Uncertain(command, AzureProviderOperationPhase.FoundationReady, "azure.sql.firewall-uncertain", "The temporary SQL firewall rule could not be proven absent.");
            firewallCleaned = true;
            if (!sqlSucceeded)
                return cancellationToken.IsCancellationRequested
                    ? Uncertain(command, AzureProviderOperationPhase.FoundationReady, "azure.sql.cancelled", "SQL bootstrap was interrupted before completion.")
                    : Uncertain(command, AzureProviderOperationPhase.FoundationReady, "azure.sql.bootstrap-uncertain", "SQL bootstrap did not produce a confirmed result.");

            return Completed(command, AzureProviderOperationPhase.FoundationReady, command.Resources);
        }
        finally
        {
            Exception? cleanupFailure = null;
            try
            {
                DeleteTransientSecretFile(temporaryDirectory, scriptPath);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
            try
            {
                if (!firewallCleaned && !await DeleteAndVerifyFirewallAsync(command, SqlServerName(command), CancellationToken.None))
                    throw new InvalidOperationException("The temporary SQL firewall rule could not be proven absent.");
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }
            if (cleanupFailure is not null)
                throw new InvalidOperationException("SQL bootstrap cleanup could not be proven complete.", cleanupFailure);
        }
    }

    private async Task<AzureProviderRunnerResult> RunWorkloadAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        var missing = RequireRegistry(command.Resources);
        if (missing is not null)
            return Failed(command, AzureProviderOperationPhase.WorkloadReady, "azure.workload.foundation-missing", missing);

        var stable = command.Resources.StableTrafficRevisionName;
        if (stable is null)
        {
            var stableObservation = await ResolveStableTrafficAsync(command, cancellationToken);
            if (stableObservation.Error is not null)
                return stableObservation.Error;
            stable = stableObservation.Revision;
        }

        var revision = await ResolveRevisionSuffixAsync(command, cancellationToken);
        if (revision.Error is not null)
            return revision.Error;

        var deploymentName = WorkloadDeploymentName(command);
        EnsureMutationAuthority(command);
        var output = await ExecuteAzAsync(command,
            WorkloadDeploymentArguments(command, deploymentName, revision.Suffix!, stable),
            ParseDeploymentOutputsAsync,
            cancellationToken);
        if (!output.Succeeded || output.Value is null)
            return ProcessFailure(command, AzureProviderOperationPhase.WorkloadReady, output, command.Resources, mutation: true);

        AzureProviderResourceReferences resources;
        try
        {
            resources = ProjectWorkload(output.Value!.Value, command.Resources, command.Plan, deploymentName, revision.Suffix!, stable);
        }
        catch (ArgumentException exception)
        {
            return Failed(command, AzureProviderOperationPhase.WorkloadReady, "azure.workload.output-invalid", exception.Message);
        }

        EnsureMutationAuthority(command);
        var adminRemoved = await RemoveSqlBootstrapAdminAsync(command, CancellationToken.None);
        if (!adminRemoved)
            return Uncertain(command, AzureProviderOperationPhase.WorkloadReady, "azure.sql.admin-removal-uncertain", "The temporary SQL bootstrap administrator could not be proven absent.", resources);

        return Completed(command, AzureProviderOperationPhase.WorkloadReady, resources);
    }

    private async Task<AzureProviderRunnerResult> RunHealthAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Resources.WorkloadRevisionName is null || command.Resources.WorkloadResourceId is null)
            return Failed(command, AzureProviderOperationPhase.HealthVerified, "azure.health.workload-missing", "The candidate workload identity is missing.");
        var endpointResult = await ResolveEndpointAsync(command, cancellationToken);
        if (!endpointResult.Succeeded || string.IsNullOrWhiteSpace(endpointResult.Value?.Value))
            return ProcessFailure(command, AzureProviderOperationPhase.HealthVerified, endpointResult, command.Resources, mutation: false);
        var endpoint = endpointResult.Value!.Value;

        for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
        {
            var state = await ExecuteAzAsync(command,
                ["containerapp", "revision", "show", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                    "--name", AppName(command), "--revision", command.Resources.WorkloadRevisionName,
                    "--query", "properties.healthState", "--output", "tsv", "--only-show-errors"],
                ParseStringAsync,
                cancellationToken);
            if (state.Succeeded && string.Equals(state.Value?.Value, "Healthy", StringComparison.OrdinalIgnoreCase))
            {
                AzureProviderOperationValidation.ValidateEndpoint(endpoint);
                return Completed(command, AzureProviderOperationPhase.HealthVerified, command.Resources, health: AzureProviderHealth.Healthy, endpoint: endpoint);
            }
            if (state.Status == AzureCommandProcessStatus.Cancelled || cancellationToken.IsCancellationRequested)
                return Uncertain(command, AzureProviderOperationPhase.HealthVerified, "azure.health.cancelled", "Candidate health verification was interrupted.");
            if (attempt + 1 < _options.ObservationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }

        return new AzureProviderRunnerResult(
            AzureProviderRunnerOutcome.Failed,
            AzureProviderOperationPhase.HealthVerified,
            command.Resources,
            AzureProviderHealth.Failed,
            null,
            [],
            "azure.health.unhealthy",
            "The candidate did not become healthy within the bounded observation window.");
    }

    private async Task<AzureProviderRunnerResult> RunPromotionAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Resources.WorkloadRevisionName is null || command.Resources.WorkloadResourceId is null)
            return Failed(command, AzureProviderOperationPhase.TrafficPromoted, "azure.promotion.input-missing", "The candidate revision is missing.");
        var endpointResult = await ResolveEndpointAsync(command, cancellationToken);
        if (!endpointResult.Succeeded || string.IsNullOrWhiteSpace(endpointResult.Value?.Value))
            return ProcessFailure(command, AzureProviderOperationPhase.TrafficPromoted, endpointResult, command.Resources, mutation: false);
        var endpoint = endpointResult.Value!.Value;
        AzureProviderOperationValidation.ValidateEndpoint(endpoint);
        var candidateState = await ExecuteAzAsync(command,
            ["containerapp", "revision", "show", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--name", AppName(command), "--revision", command.Resources.WorkloadRevisionName,
                "--query", "properties.{active:active,health:healthState}", "--output", "json", "--only-show-errors"],
            ParseRevisionStateAsync,
            cancellationToken);
        if (!candidateState.Succeeded || candidateState.Value is null)
            return ProcessFailure(command, AzureProviderOperationPhase.TrafficPromoted, candidateState, command.Resources, mutation: false);
        if (!candidateState.Value.Value.Active || !string.Equals(candidateState.Value.Value.Health, "Healthy", StringComparison.OrdinalIgnoreCase))
            return Failed(command, AzureProviderOperationPhase.TrafficPromoted, "azure.promotion.health-gate", "The candidate revision is not active and healthy.");

        EnsureMutationAuthority(command);
        var promoted = await ExecuteAzAsync<AzureCommandNoOutput>(command,
            ["containerapp", "ingress", "traffic", "set", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--name", AppName(command), "--revision-weight", $"{command.Resources.WorkloadRevisionName}=100", "--output", "none", "--only-show-errors"],
            static _ => AzureCommandNoOutput.Instance,
            cancellationToken);
        if (!promoted.Succeeded)
            return ProcessFailure(command, AzureProviderOperationPhase.TrafficPromoted, promoted, command.Resources, mutation: true);

        var traffic = await WaitForTrafficAsync(command, command.Resources.WorkloadRevisionName, requiredZeroRevision: null, cancellationToken);
        if (traffic is not true)
            return traffic is null
                ? Uncertain(command, AzureProviderOperationPhase.TrafficPromoted, "azure.promotion.traffic-uncertain", "Candidate traffic promotion could not be confirmed.")
                : Uncertain(command, AzureProviderOperationPhase.TrafficPromoted, "azure.promotion.traffic-invalid", "Candidate traffic did not reach the required single-revision state.");

        var health = await ExecuteCurlAsync(command, $"{endpoint.TrimEnd('/')}/health", cancellationToken);
        if (!health.Succeeded)
            return Uncertain(command, AzureProviderOperationPhase.TrafficPromoted, "azure.promotion.health-uncertain", "Candidate external health could not be confirmed.", command.Resources);

        return Completed(
            command,
            AzureProviderOperationPhase.TrafficPromoted,
            command.Resources with { StableTrafficRevisionName = command.Resources.WorkloadRevisionName },
            health: AzureProviderHealth.Healthy,
            endpoint: endpoint);
    }

    private Task<AzureCommandProcessResult<SafeValue<string>>> ResolveEndpointAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken) =>
        ExecuteAzAsync(command,
            ["containerapp", "show", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--name", AppName(command), "--query", "properties.configuration.ingress.fqdn", "--output", "tsv", "--only-show-errors"],
            output =>
            {
                var host = output.ToString().Trim();
                if (string.IsNullOrWhiteSpace(host))
                    throw new FormatException();
                var endpoint = host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? host : $"https://{host}";
                AzureProviderOperationValidation.ValidateEndpoint(endpoint);
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                    !uri.Host.EndsWith(".azurecontainerapps.io", StringComparison.OrdinalIgnoreCase) ||
                    !uri.Host.StartsWith(AppName(command) + ".", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException();
                return new SafeValue<string>(endpoint);
            },
            cancellationToken);

    private async Task<AzureProviderRunnerResult> RunRestoreStableTrafficAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        var stable = command.StableTrafficRevisionName;
        if (string.IsNullOrWhiteSpace(stable))
            return Uncertain(command, AzureProviderOperationPhase.HealthVerified, "azure.rollback.stable-missing", "No previously verified stable traffic revision is available.");
        var candidate = command.Resources.WorkloadRevisionName;
        var weights = candidate is null || string.Equals(candidate, stable, StringComparison.Ordinal)
            ? $"{stable}=100"
            : $"{stable}=100 {candidate}=0";
        EnsureMutationAuthority(command);
        var restored = await ExecuteAzAsync<AzureCommandNoOutput>(command,
            ["containerapp", "ingress", "traffic", "set", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--name", AppName(command), "--revision-weight", weights, "--output", "none", "--only-show-errors"],
            static _ => AzureCommandNoOutput.Instance,
            cancellationToken);
        if (!restored.Succeeded)
            return Uncertain(command, AzureProviderOperationPhase.HealthVerified, "azure.rollback.uncertain", "Stable traffic restoration was not confirmed.", command.Resources);

        var requiredZeroRevision = candidate is null || string.Equals(candidate, stable, StringComparison.Ordinal) ? null : candidate;
        var traffic = await WaitForTrafficAsync(command, stable, requiredZeroRevision, cancellationToken);
        if (traffic is not true)
            return Uncertain(command, AzureProviderOperationPhase.HealthVerified, "azure.rollback.uncertain", "Stable traffic restoration was not confirmed.", command.Resources);
        return Completed(command, AzureProviderOperationPhase.HealthVerified, command.Resources with { StableTrafficRevisionName = stable },
            health: AzureProviderHealth.Unknown,
            stableTrafficRestored: true,
            endpoint: null);
    }

    private async Task<AzureProviderRunnerResult> RunCleanupAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        var resources = command.Resources;
        var groupExists = await ExecuteAzAsync(command,
            ["group", "exists", "--subscription", _scope.SubscriptionId, "--name", _scope.ResourceGroupName, "--output", "tsv", "--only-show-errors"],
            ParseBooleanAsync,
            cancellationToken);
        if (!groupExists.Succeeded)
            return ProcessFailure(command, AzureProviderOperationPhase.CleanupVerified, groupExists, resources, mutation: false);

        if (groupExists.Value!.Value)
        {
            var tags = await ExecuteAzAsync(command,
                ["group", "show", "--subscription", _scope.SubscriptionId, "--name", _scope.ResourceGroupName, "--query", "tags", "--output", "json", "--only-show-errors"],
                ParseTagsAsync,
                cancellationToken);
            if (!tags.Succeeded || tags.Value is null || !OwnsGroup(tags.Value.Value, command.Plan.WorkloadName))
                return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.ownership-unverified", "The target resource group is not proven to belong to this workload.");

            var inventory = await ExecuteAzAsync(command,
                ["resource", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName, "--output", "json", "--only-show-errors"],
                ParseResourcesAsync,
                cancellationToken);
            if (!inventory.Succeeded || inventory.Value is null)
                return Uncertain(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.inventory-uncertain", "The owned resource inventory could not be confirmed.");
            if (!IsExactInventory(inventory.Value!.Value, command.Plan.WorkloadName))
                return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.ownership-unverified", "The resource inventory contains an unowned resource.");

            var identityId = ResourceId("Microsoft.ManagedIdentity", "userAssignedIdentities", $"{command.Plan.WorkloadName}-identity");
            var identityPresent = inventory.Value.Value.Any(resource => string.Equals(resource.Id, identityId, StringComparison.OrdinalIgnoreCase));
            if (resources.WorkloadIdentityPrincipalId is null && identityPresent)
            {
                var principal = await ExecuteAzAsync(command,
                    ["identity", "show", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                        "--name", $"{command.Plan.WorkloadName}-identity", "--query", "principalId", "--output", "tsv", "--only-show-errors"],
                    ParseStringAsync,
                    cancellationToken);
                if (!principal.Succeeded || string.IsNullOrWhiteSpace(principal.Value?.Value))
                    return Uncertain(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.identity-observation-uncertain", "The owned workload identity principal could not be confirmed before cleanup.");
                try
                {
                    resources = resources with
                    {
                        WorkloadIdentityPrincipalId = NormalizeGuid(principal.Value.Value, "workloadIdentityPrincipalId")
                    };
                }
                catch (ArgumentException)
                {
                    return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.identity-invalid", "The owned workload identity principal is invalid.");
                }
            }

            var vaultId = resources.KeyVaultResourceId ?? ResourceId("Microsoft.KeyVault", "vaults", $"{command.Plan.WorkloadName}-kv");
            var vaultPresent = inventory.Value.Value.Any(resource =>
                string.Equals(resource.Id, vaultId, StringComparison.OrdinalIgnoreCase));
            if (vaultPresent)
            {
                var assignments = await ExecuteAzAsync(command,
                    ["role", "assignment", "list", "--subscription", _scope.SubscriptionId, "--all",
                        "--output", "json", "--only-show-errors"],
                    ParseRoleAssignmentsAsync,
                    cancellationToken);
                if (!assignments.Succeeded || assignments.Value is null || !HasSafeVaultAssignmentsForCleanup(assignments.Value.Value, resources, command.Plan.WorkloadName))
                    return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.rbac-unverified", "The owned Key Vault role-assignment inventory is not exact.");
            }
        }

        var registryId = resources.RegistryResourceId ?? RegistryResourceId();
        var roleAssignmentId = resources.AcrPullRoleAssignmentId;
        var acrDeploymentId = resources.AcrPullDeploymentId;
        var registryGroupPresent = true;
        if (registryGroupPresent && resources.WorkloadIdentityPrincipalId is not null)
        {
            acrDeploymentId ??= DeploymentId(
                _scope.RegistrySubscriptionId,
                _scope.RegistryResourceGroupName,
                AcrDeploymentName(command, resources.WorkloadIdentityPrincipalId));
            if (roleAssignmentId is null)
            {
                var discovered = await DiscoverAcrRoleAssignmentAsync(
                    command,
                    registryId,
                    resources.WorkloadIdentityPrincipalId,
                    cancellationToken);
                if (discovered.Status == AcrRoleDiscoveryStatus.Uncertain)
                {
                    var registryGroupExists = await ExecuteAzAsync(command,
                        ["group", "exists", "--subscription", _scope.RegistrySubscriptionId, "--name", _scope.RegistryResourceGroupName,
                            "--output", "tsv", "--only-show-errors"],
                        ParseBooleanAsync,
                        cancellationToken);
                    if (!registryGroupExists.Succeeded || registryGroupExists.Value is null || registryGroupExists.Value.Value)
                        return Uncertain(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.role-observation-uncertain", "The owned ACR role assignment could not be observed before deletion.");
                    registryGroupPresent = false;
                    acrDeploymentId = null;
                }
                if (discovered.Status == AcrRoleDiscoveryStatus.Ambiguous)
                    return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.role-provenance-invalid", "The ACR role-assignment inventory is not exact for the workload identity.");
                if (registryGroupPresent)
                    roleAssignmentId = discovered.AssignmentId;
            }
        }

        if (registryGroupPresent && roleAssignmentId is not null)
        {
            try { ValidateExactRoleAssignmentId(roleAssignmentId, registryId); }
            catch (ArgumentException exception) { return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.role-scope-invalid", exception.Message); }
            if (resources.WorkloadIdentityPrincipalId is null)
                return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.role-provenance-invalid", "The owned ACR role assignment lacks the exact registry and workload identity provenance required for deletion.");
            var roleProvenance = await ValidateAcrRoleAssignmentAsync(command, roleAssignmentId,
                registryId, resources.WorkloadIdentityPrincipalId, cancellationToken);
            if (roleProvenance is null)
                return Uncertain(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.role-observation-uncertain", "The owned ACR role assignment could not be observed before deletion.");
            if (!roleProvenance.Value)
                return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.role-provenance-invalid", "The ACR role assignment does not match the exact registry, workload identity, and AcrPull role.");
            EnsureMutationAuthority(command);
            await ExecuteAzAsync<AzureCommandNoOutput>(command,
                ["role", "assignment", "delete", "--ids", roleAssignmentId, "--output", "none", "--only-show-errors"],
                static _ => AzureCommandNoOutput.Instance,
                cancellationToken);
            if (!await RoleAssignmentAbsentAsync(command, roleAssignmentId, resources.WorkloadIdentityPrincipalId, cancellationToken))
                return Uncertain(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.role-uncertain", "The owned ACR role assignment could not be proven absent.");
        }

        if (registryGroupPresent && acrDeploymentId is not null)
        {
            try
            {
                ValidateExactAcrDeploymentId(acrDeploymentId, command, resources.WorkloadIdentityPrincipalId);
            }
            catch (ArgumentException exception)
            {
                return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.deployment-scope-invalid", exception.Message);
            }
            var deployment = ResourceName(acrDeploymentId);
            if (deployment is null)
                return Failed(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.deployment-invalid", "The owned ACR deployment identity is invalid.");
            EnsureMutationAuthority(command);
            await ExecuteAzAsync<AzureCommandNoOutput>(command,
                ["deployment", "group", "delete", "--subscription", _scope.RegistrySubscriptionId, "--resource-group", _scope.RegistryResourceGroupName,
                    "--name", deployment, "--output", "none", "--only-show-errors"],
                static _ => AzureCommandNoOutput.Instance,
                cancellationToken);
            if (!await DeploymentAbsentAsync(command, deployment, cancellationToken))
                return Uncertain(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.deployment-uncertain", "The owned ACR deployment record could not be proven absent.");
        }

        if (groupExists.Value!.Value)
        {
            EnsureMutationAuthority(command);
            await ExecuteAzAsync<AzureCommandNoOutput>(command,
                ["group", "delete", "--subscription", _scope.SubscriptionId, "--name", _scope.ResourceGroupName, "--yes", "--no-wait", "--output", "none", "--only-show-errors"],
                static _ => AzureCommandNoOutput.Instance,
                cancellationToken);
            if (!await ResourceGroupAbsentAsync(command, cancellationToken))
                return Uncertain(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.group-uncertain", "The owned resource group could not be proven absent.");
        }

        var vaultName = $"{command.Plan.WorkloadName}-kv";
        var exactVaultId = resources.KeyVaultResourceId ?? ResourceId("Microsoft.KeyVault", "vaults", vaultName);
        if (!await PurgeAndVerifyVaultAsync(command, vaultName, exactVaultId, cancellationToken))
            return Uncertain(command, AzureProviderOperationPhase.CleanupVerified, "azure.cleanup.vault-uncertain", "The owned Key Vault could not be proven absent.");

        return new AzureProviderRunnerResult(
            AzureProviderRunnerOutcome.Completed,
            AzureProviderOperationPhase.CleanupVerified,
            new AzureProviderResourceReferences(),
            AzureProviderHealth.Unknown,
            null,
            [],
            "azure.cleanup.completed",
            "Exact owned-resource cleanup was verified.",
            OwnedResourcesAbsent: true);
    }

    private async Task<AzureCommandProcessResult<T>> ExecuteAzAsync<T>(
        AzureProviderRunnerCommand command,
        IReadOnlyList<string> arguments,
        AzureCommandOutputProjector<T> projector,
        CancellationToken cancellationToken)
        where T : AzureCommandSafeOutput
    {
        _options.ValidateExecutionAuthority(command.Context, _scope);
        var request = new AzureCommandProcessRequest(
            _options.AzureCliPath,
            arguments.Select(AzureCommandArgument.Safe).ToArray(),
            workingDirectory: _options.TemplateRoot);
        return await _process.ExecuteAsync(request, projector, cancellationToken);
    }

    private async Task<AzureCommandProcessResult<T>> ExecuteSqlCmdAsync<T>(
        AzureProviderRunnerCommand command,
        IReadOnlyList<string> arguments,
        AzureCommandOutputProjector<T> projector,
        CancellationToken cancellationToken)
        where T : AzureCommandSafeOutput
    {
        _options.ValidateExecutionAuthority(command.Context, _scope);
        var request = new AzureCommandProcessRequest(
            _options.SqlCmdPath,
            arguments.Select(AzureCommandArgument.Safe).ToArray(),
            workingDirectory: _options.TemplateRoot);
        return await _process.ExecuteAsync(request, projector, cancellationToken);
    }

    private async Task<AzureCommandProcessResult<AzureCommandNoOutput>> ExecuteCurlAsync(AzureProviderRunnerCommand command, string endpoint, CancellationToken cancellationToken)
    {
        _options.ValidateExecutionAuthority(command.Context, _scope);
        var request = new AzureCommandProcessRequest(
            _options.CurlPath,
            new[] { "--fail", "--silent", "--show-error", "--retry", "30", "--retry-all-errors", "--retry-delay", "5", "--max-time", "10", endpoint }
                .Select(AzureCommandArgument.Safe).ToArray());
        return await _process.ExecuteAsync<AzureCommandNoOutput>(request, static _ => AzureCommandNoOutput.Instance, cancellationToken);
    }

    private async Task<bool?> WaitForRoleAssignmentAsync(AzureProviderRunnerCommand command, string registryId, string principalId, string expectedAssignmentId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
        {
            var list = await ExecuteAzAsync(command,
                ["role", "assignment", "list", "--subscription", _scope.RegistrySubscriptionId, "--all",
                    "--assignee-object-id", principalId,
                    "--output", "json", "--only-show-errors"],
                ParseRoleAssignmentsAsync,
                cancellationToken);
            if (!list.Succeeded)
            {
                if (list.Status == AzureCommandProcessStatus.Cancelled || cancellationToken.IsCancellationRequested)
                    return null;
            }
            else
            {
                var exact = list.Value!.Value.Where(x => string.Equals(x.Id, expectedAssignmentId, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (exact.Length == 1)
                    return string.Equals(exact[0].Scope, registryId, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(exact[0].PrincipalId, principalId, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(RoleDefinitionId(exact[0].RoleDefinitionId), AcrPullRoleDefinitionId, StringComparison.OrdinalIgnoreCase);
                if (exact.Length > 1)
                    return false;
            }
            if (attempt + 1 < _options.ObservationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }
        return null;
    }

    private async Task<(string? Revision, AzureProviderRunnerResult? Error)> ResolveStableTrafficAsync(AzureProviderRunnerCommand command, CancellationToken cancellationToken)
    {
        var count = await ExecuteAzAsync(command,
            ["resource", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName, "--resource-type", "Microsoft.App/containerApps",
                "--query", "[?name=='" + AppName(command) + "'] | length(@)", "--output", "tsv", "--only-show-errors"],
            ParseIntegerAsync,
            cancellationToken);
        if (!count.Succeeded)
            return (null, Uncertain(command, AzureProviderOperationPhase.WorkloadReady, "azure.traffic.observation-uncertain", "Existing workload traffic could not be observed."));
        if (count.Value!.Value == 0)
            return (null, null);
        if (count.Value.Value != 1)
            return (null, Failed(command, AzureProviderOperationPhase.WorkloadReady, "azure.traffic.ambiguous", "Expected exactly one governed Container App."));

        var traffic = await ExecuteAzAsync(command,
            ["containerapp", "show", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName, "--name", AppName(command),
                "--query", "properties.configuration.ingress.traffic", "--output", "json", "--only-show-errors"],
            ParseTrafficAsync,
            cancellationToken);
        if (!traffic.Succeeded || traffic.Value is null)
            return (null, Uncertain(command, AzureProviderOperationPhase.WorkloadReady, "azure.traffic.observation-uncertain", "Existing workload traffic could not be observed."));
        var stable = traffic.Value!.Value.SingleOrDefault(x => x.Weight == 100);
        if (stable is null || traffic.Value.Value.Sum(x => x.Weight) != 100 || string.IsNullOrWhiteSpace(stable.RevisionName))
            return (null, Failed(command, AzureProviderOperationPhase.WorkloadReady, "azure.traffic.ambiguous", "Existing workload traffic has no single 100% revision."));
        var state = await ExecuteAzAsync(command,
            ["containerapp", "revision", "show", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--name", AppName(command), "--revision", stable.RevisionName, "--query", "properties.{active:active,health:healthState}",
                "--output", "json", "--only-show-errors"],
            ParseRevisionStateAsync,
            cancellationToken);
        if (!state.Succeeded || state.Value is null)
            return (null, Uncertain(command, AzureProviderOperationPhase.WorkloadReady, "azure.traffic.observation-uncertain", "Existing stable revision health could not be observed."));
        return state.Value!.Value.Active && string.Equals(state.Value.Value.Health, "Healthy", StringComparison.OrdinalIgnoreCase)
            ? (stable.RevisionName, null)
            : (null, Failed(command, AzureProviderOperationPhase.WorkloadReady, "azure.traffic.unhealthy", "Existing stable traffic is not active and healthy."));
    }

    private async Task<(string? Suffix, AzureProviderRunnerResult? Error)> ResolveRevisionSuffixAsync(AzureProviderRunnerCommand command, CancellationToken cancellationToken)
    {
        var baseSuffix = command.Plan.Fingerprint[..24];
        var count = await ExecuteAzAsync(command,
            ["resource", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName, "--resource-type", "Microsoft.App/containerApps",
                "--query", "[?name=='" + AppName(command) + "'] | length(@)", "--output", "tsv", "--only-show-errors"],
            ParseIntegerAsync,
            cancellationToken);
        if (!count.Succeeded)
            return (null, Uncertain(command, AzureProviderOperationPhase.WorkloadReady, "azure.revision.observation-uncertain", "Existing workload revisions could not be observed."));
        if (count.Value!.Value == 0)
            return (baseSuffix, null);
        if (count.Value.Value != 1)
            return (null, Failed(command, AzureProviderOperationPhase.WorkloadReady, "azure.revision.ambiguous", "Expected exactly one governed Container App."));

        var current = await ExecuteAzAsync(command,
            ["resource", "show", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--resource-type", "Microsoft.App/containerApps", "--name", AppName(command), "--query", "properties.template.revisionSuffix",
                "--output", "tsv", "--only-show-errors"],
            ParseStringAsync,
            cancellationToken);
        if (!current.Succeeded)
            return (null, Uncertain(command, AzureProviderOperationPhase.WorkloadReady, "azure.revision.observation-uncertain", "Existing workload revisions could not be observed."));
        if (IsRevisionSuffixForPlan(current.Value?.Value, baseSuffix))
            return (current.Value!.Value, null);

        var names = await ExecuteAzAsync(command,
            ["containerapp", "revision", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--name", AppName(command), "--query", "[].name", "--output", "json", "--only-show-errors"],
            ParseStringArrayAsync,
            cancellationToken);
        if (!names.Succeeded || names.Value is null)
            return (null, Uncertain(command, AzureProviderOperationPhase.WorkloadReady, "azure.revision.observation-uncertain", "Existing workload revisions could not be observed."));
        for (var ordinal = 0; ordinal < 1000; ordinal++)
        {
            var candidate = ordinal == 0 ? baseSuffix : $"{baseSuffix}-r{ordinal}";
            if (!names.Value!.Value.Contains($"{AppName(command)}--{candidate}", StringComparer.OrdinalIgnoreCase))
                return (candidate, null);
        }
        return (null, Failed(command, AzureProviderOperationPhase.WorkloadReady, "azure.revision.exhausted", "No deterministic workload revision suffix was available."));
    }

    private async Task<bool?> WaitForTrafficAsync(
        AzureProviderRunnerCommand command,
        string desiredRevision,
        string? requiredZeroRevision,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
        {
            var traffic = await ExecuteAzAsync(command,
                ["containerapp", "show", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                    "--name", AppName(command), "--query", "properties.configuration.ingress.traffic", "--output", "json", "--only-show-errors"],
                ParseTrafficAsync,
                cancellationToken);
            if (traffic.Succeeded && traffic.Value is not null && traffic.Value.Value.Sum(x => x.Weight) == 100)
            {
                var desiredEntries = traffic.Value.Value.Where(x => string.Equals(x.RevisionName, desiredRevision, StringComparison.OrdinalIgnoreCase)).ToArray();
                var nonDesired = traffic.Value.Value.Any(x => !string.Equals(x.RevisionName, desiredRevision, StringComparison.OrdinalIgnoreCase) && x.Weight != 0);
                var requiredZero = requiredZeroRevision is null ||
                    traffic.Value.Value.Count(x => string.Equals(x.RevisionName, requiredZeroRevision, StringComparison.OrdinalIgnoreCase) && x.Weight == 0) == 1;
                if (desiredEntries.Length == 1 && desiredEntries[0].Weight == 100 && !nonDesired && requiredZero)
                    return true;
            }
            if (traffic.Status == AzureCommandProcessStatus.Cancelled || cancellationToken.IsCancellationRequested)
                return null;
            if (attempt + 1 < _options.ObservationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }
        return false;
    }

    private async Task<bool> DeleteAndVerifyFirewallAsync(AzureProviderRunnerCommand command, string serverName, CancellationToken cancellationToken)
    {
        EnsureMutationAuthority(command);
        await ExecuteAzAsync<AzureCommandNoOutput>(command,
            ["sql", "server", "firewall-rule", "delete", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--server", serverName, "--name", TemporaryFirewallRuleName, "--output", "none", "--only-show-errors"],
            static _ => AzureCommandNoOutput.Instance,
            cancellationToken);
        for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
        {
            var list = await ExecuteAzAsync(command,
                ["sql", "server", "firewall-rule", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                    "--server", serverName, "--output", "json", "--only-show-errors"],
                ParseFirewallRulesAsync,
                cancellationToken);
            if (list.Succeeded && list.Value is not null && !list.Value.Value.Any(x => string.Equals(x.Name, TemporaryFirewallRuleName, StringComparison.Ordinal)))
                return true;
            if (attempt + 1 < _options.ObservationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }
        return false;
    }

    private async Task<bool?> EnsureSqlBootstrapAdminForReapplyAsync(
        AzureProviderRunnerCommand command,
        CancellationToken cancellationToken)
    {
        var server = $"{command.Plan.WorkloadName}-sql";
        var count = await ExecuteAzAsync(command,
            ["sql", "server", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--query", $"[?name=='{server}'] | length(@)", "--output", "tsv", "--only-show-errors"],
            ParseIntegerAsync,
            cancellationToken);
        if (!count.Succeeded || count.Value is null)
            return null;
        if (count.Value.Value == 0)
            return true;
        if (count.Value.Value != 1)
            return false;

        var admins = await ExecuteAzAsync(command,
            ["sql", "server", "ad-admin", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--server", server, "--output", "json", "--only-show-errors"],
            ParseAdminsAsync,
            cancellationToken);
        if (!admins.Succeeded || admins.Value is null)
            return null;
        if (admins.Value.Value.Count > 1)
            return false;
        if (admins.Value.Value.Count == 0)
        {
            EnsureMutationAuthority(command);
            var created = await ExecuteAzAsync<AzureCommandNoOutput>(command,
                ["sql", "server", "ad-admin", "create", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                    "--server", server, "--display-name", _options.SqlBootstrapLogin, "--object-id", _options.SqlBootstrapObjectId,
                    "--output", "none", "--only-show-errors"],
                static _ => AzureCommandNoOutput.Instance,
                cancellationToken);
            if (!created.Succeeded)
                return null;
            admins = await ExecuteAzAsync(command,
                ["sql", "server", "ad-admin", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                    "--server", server, "--output", "json", "--only-show-errors"],
                ParseAdminsAsync,
                cancellationToken);
            if (!admins.Succeeded || admins.Value is null)
                return null;
        }

        if (admins.Value.Value.Count != 1 ||
            !string.Equals(admins.Value.Value[0].Login, _options.SqlBootstrapLogin, StringComparison.Ordinal) ||
            !string.Equals(admins.Value.Value[0].Sid, _options.SqlBootstrapObjectId, StringComparison.OrdinalIgnoreCase))
            return false;

        EnsureMutationAuthority(command);
        var enabled = await ExecuteAzAsync<AzureCommandNoOutput>(command,
            ["sql", "server", "ad-only-auth", "enable", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--name", server, "--output", "none", "--only-show-errors"],
            static _ => AzureCommandNoOutput.Instance,
            cancellationToken);
        return enabled.Succeeded ? true : null;
    }

    private async Task<bool> RemoveSqlBootstrapAdminAsync(AzureProviderRunnerCommand command, CancellationToken cancellationToken)
    {
        var workloadName = command.Plan.WorkloadName;
        var server = $"{workloadName}-sql";
        var count = await ExecuteAzAsync(command,
            ["sql", "server", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--query", "[?name=='" + server + "'] | length(@)", "--output", "tsv", "--only-show-errors"],
            ParseIntegerAsync,
            cancellationToken);
        if (!count.Succeeded || count.Value!.Value == 0)
            return count.Succeeded && count.Value!.Value == 0;
        if (count.Value.Value != 1)
            return false;
        var admins = await ExecuteAzAsync(command,
            ["sql", "server", "ad-admin", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--server", server, "--output", "json", "--only-show-errors"],
            ParseAdminsAsync,
            cancellationToken);
        if (!admins.Succeeded || admins.Value is null || admins.Value.Value.Count == 0)
            return admins.Succeeded && admins.Value is not null && admins.Value.Value.Count == 0;
        if (admins.Value.Value.Count != 1 || !string.Equals(admins.Value.Value[0].Login, _options.SqlBootstrapLogin, StringComparison.Ordinal) ||
            !string.Equals(admins.Value.Value[0].Sid, _options.SqlBootstrapObjectId, StringComparison.OrdinalIgnoreCase))
            return false;
        EnsureMutationAuthority(command);
        var disabled = await ExecuteAzAsync<AzureCommandNoOutput>(command,
            ["sql", "server", "ad-only-auth", "disable", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--name", server, "--output", "none", "--only-show-errors"],
            static _ => AzureCommandNoOutput.Instance,
            cancellationToken);
        if (!disabled.Succeeded)
            return false;
        EnsureMutationAuthority(command);
        await ExecuteAzAsync<AzureCommandNoOutput>(command,
            ["sql", "server", "ad-admin", "delete", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                "--server", server, "--output", "none", "--only-show-errors"],
            static _ => AzureCommandNoOutput.Instance,
            cancellationToken);
        for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
        {
            var remaining = await ExecuteAzAsync(command,
                ["sql", "server", "ad-admin", "list", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
                    "--server", server, "--query", "length(@)", "--output", "tsv", "--only-show-errors"],
                ParseIntegerAsync,
                cancellationToken);
            if (remaining.Succeeded && remaining.Value?.Value == 0)
                return true;
            if (attempt + 1 < _options.ObservationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }
        return false;
    }

    private async Task<bool> RoleAssignmentAbsentAsync(
        AzureProviderRunnerCommand command,
        string assignmentId,
        string principalId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
        {
            var list = await ExecuteAzAsync(command,
                ["role", "assignment", "list", "--subscription", _scope.RegistrySubscriptionId, "--all",
                    "--assignee-object-id", principalId,
                    "--output", "json", "--only-show-errors"],
                ParseRoleAssignmentsAsync,
                cancellationToken);
            if (list.Succeeded && list.Value is not null && !list.Value.Value.Any(x => string.Equals(x.Id, assignmentId, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (attempt + 1 < _options.ObservationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }
        return false;
    }

    private async Task<AcrRoleDiscovery> DiscoverAcrRoleAssignmentAsync(
        AzureProviderRunnerCommand command,
        string registryId,
        string principalId,
        CancellationToken cancellationToken)
    {
        var list = await ExecuteAzAsync(command,
            ["role", "assignment", "list", "--subscription", _scope.RegistrySubscriptionId, "--all",
                "--assignee-object-id", principalId,
                "--output", "json", "--only-show-errors"],
            ParseRoleAssignmentsAsync,
            cancellationToken);
        if (!list.Succeeded || list.Value is null)
            return new(AcrRoleDiscoveryStatus.Uncertain, null);

        var exact = list.Value.Value.Where(assignment =>
            string.Equals(assignment.Scope, registryId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(assignment.PrincipalId, principalId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(RoleDefinitionId(assignment.RoleDefinitionId), AcrPullRoleDefinitionId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 0)
            return new(AcrRoleDiscoveryStatus.Absent, null);
        if (exact.Length != 1 || string.IsNullOrWhiteSpace(exact[0].Id))
            return new(AcrRoleDiscoveryStatus.Ambiguous, null);
        try
        {
            ValidateExactRoleAssignmentId(exact[0].Id!, registryId);
            return new(AcrRoleDiscoveryStatus.Exact, exact[0].Id);
        }
        catch (ArgumentException)
        {
            return new(AcrRoleDiscoveryStatus.Ambiguous, null);
        }
    }

    private async Task<bool?> ValidateAcrRoleAssignmentAsync(
        AzureProviderRunnerCommand command,
        string assignmentId,
        string registryId,
        string principalId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
        {
            var list = await ExecuteAzAsync(command,
                ["role", "assignment", "list", "--subscription", _scope.RegistrySubscriptionId, "--all",
                    "--assignee-object-id", principalId,
                    "--output", "json", "--only-show-errors"],
                ParseRoleAssignmentsAsync,
                cancellationToken);
            if (list.Succeeded && list.Value is not null)
            {
                var exact = list.Value.Value.Where(x => string.Equals(x.Id, assignmentId, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (exact.Length == 0)
                    return true;
                if (exact.Length != 1)
                    return false;
                return string.Equals(exact[0].Scope, registryId, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(exact[0].PrincipalId, principalId, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(RoleDefinitionId(exact[0].RoleDefinitionId), AcrPullRoleDefinitionId, StringComparison.OrdinalIgnoreCase);
            }
            if (list.Status == AzureCommandProcessStatus.Cancelled || cancellationToken.IsCancellationRequested)
                return null;
            if (attempt + 1 < _options.ObservationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }
        return null;
    }

    private async Task<bool> DeploymentAbsentAsync(AzureProviderRunnerCommand command, string deploymentName, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
        {
            var list = await ExecuteAzAsync(command,
                ["deployment", "group", "list", "--subscription", _scope.RegistrySubscriptionId, "--resource-group", _scope.RegistryResourceGroupName,
                    "--output", "json", "--only-show-errors"],
                ParseDeploymentsAsync,
                cancellationToken);
            if (list.Succeeded && list.Value is not null && !list.Value.Value.Any(x => string.Equals(x.Name, deploymentName, StringComparison.Ordinal)))
                return true;
            if (attempt + 1 < _options.ObservationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }
        return false;
    }

    private async Task<bool> ResourceGroupAbsentAsync(AzureProviderRunnerCommand command, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.ObservationAttempts; attempt++)
        {
            var exists = await ExecuteAzAsync(command,
                ["group", "exists", "--subscription", _scope.SubscriptionId, "--name", _scope.ResourceGroupName, "--output", "tsv", "--only-show-errors"],
                ParseBooleanAsync,
                cancellationToken);
            if (exists.Succeeded && exists.Value?.Value == false)
                return true;
            if (attempt + 1 < _options.ObservationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }
        return false;
    }

    private async Task<bool> PurgeAndVerifyVaultAsync(
        AzureProviderRunnerCommand command,
        string vaultName,
        string exactVaultId,
        CancellationToken cancellationToken)
    {
        var purgeRequested = false;
        var consecutiveAbsenceObservations = 0;
        var observationAttempts = Math.Max(2, _options.ObservationAttempts);
        for (var attempt = 0; attempt < observationAttempts; attempt++)
        {
            var deleted = await ExecuteAzAsync(command,
                ["keyvault", "list-deleted", "--subscription", _scope.SubscriptionId, "--resource-type", "vault", "--output", "json", "--only-show-errors"],
                ParseDeletedVaultsAsync,
                cancellationToken);
            if (!deleted.Succeeded || deleted.Value is null)
            {
                consecutiveAbsenceObservations = 0;
                if (attempt + 1 < observationAttempts)
                    await Task.Delay(_options.ObservationDelay, cancellationToken);
                continue;
            }
            var candidates = deleted.Value!.Value.Where(x => string.Equals(x.Name, vaultName, StringComparison.Ordinal) &&
                                                       string.Equals(x.EffectiveLocation, _scope.Location, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0)
            {
                consecutiveAbsenceObservations++;
                if (consecutiveAbsenceObservations >= 2)
                    return true;
                if (attempt + 1 < observationAttempts)
                    await Task.Delay(_options.ObservationDelay, cancellationToken);
                continue;
            }
            if (candidates.Any(candidate => string.IsNullOrWhiteSpace(candidate.EffectiveVaultId)))
                return false;
            var matches = candidates.Where(candidate =>
                string.Equals(candidate.EffectiveVaultId, exactVaultId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0)
            {
                consecutiveAbsenceObservations++;
                if (consecutiveAbsenceObservations >= 2)
                    return true;
                if (attempt + 1 < observationAttempts)
                    await Task.Delay(_options.ObservationDelay, cancellationToken);
                continue;
            }
            if (matches.Length != 1)
                return false;
            consecutiveAbsenceObservations = 0;
            if (!purgeRequested)
            {
                EnsureMutationAuthority(command);
                await ExecuteAzAsync<AzureCommandNoOutput>(command,
                    ["keyvault", "purge", "--subscription", _scope.SubscriptionId, "--name", vaultName, "--location", _scope.Location,
                        "--output", "none", "--only-show-errors"],
                    static _ => AzureCommandNoOutput.Instance,
                    cancellationToken);
                purgeRequested = true;
            }
            if (attempt + 1 < observationAttempts)
                await Task.Delay(_options.ObservationDelay, cancellationToken);
        }
        return false;
    }

    private IReadOnlyList<string> FoundationDeploymentArguments(AzureProviderRunnerCommand command, string deploymentName) =>
        ["deployment", "group", "create", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
            "--name", deploymentName, "--template-file", Path.Combine(_options.TemplateRoot, "main.bicep"), "--parameters",
            $"proofName={command.Plan.WorkloadName}", $"location={_scope.Location}", $"imageRepository={command.Plan.ImageRepository}", $"imageDigest={command.Plan.ImageDigest}",
            $"registryName={_scope.RegistryName}", $"registrySubscriptionId={_scope.RegistrySubscriptionId}",
            $"registryResourceGroupName={_scope.RegistryResourceGroupName}", $"sqlBootstrapObjectId={_options.SqlBootstrapObjectId}",
            $"sqlBootstrapLogin={_options.SqlBootstrapLogin}", $"expiryUtc={_options.ExpiryUtc:yyyy-MM-dd}", $"owner={_options.Owner}",
            $"sqlConnectionSecretName={SqlConnectionSecretName}", $"signingKeySecretName={SigningKeySecretName}",
            $"adminPasswordSecretName={AdminPasswordSecretName}", $"adminUsername={AdminUsername}",
            $"elsaVersion={command.Plan.ElsaVersion}",
            $"templateFingerprint={command.Context.TemplateFingerprint}", "deployWorkload=false", "--query", "properties.outputs", "--output", "json", "--only-show-errors"];

    private IReadOnlyList<string> AcrDeploymentArguments(AzureProviderRunnerCommand command, string identityId, string principalId, string deploymentName) =>
        ["deployment", "group", "create", "--subscription", _scope.RegistrySubscriptionId, "--resource-group", _scope.RegistryResourceGroupName,
            "--name", deploymentName, "--template-file", Path.Combine(_options.TemplateRoot, "acr-pull-role.bicep"), "--parameters",
            $"registryName={_scope.RegistryName}", $"workloadIdentityId={identityId}", $"workloadPrincipalId={principalId}",
            "--query", "properties.outputs", "--output", "json", "--only-show-errors"];

    private IReadOnlyList<string> WorkloadDeploymentArguments(AzureProviderRunnerCommand command, string deploymentName, string revision, string? stable) =>
        ["deployment", "group", "create", "--subscription", _scope.SubscriptionId, "--resource-group", _scope.ResourceGroupName,
            "--name", deploymentName, "--template-file", Path.Combine(_options.TemplateRoot, "main.bicep"), "--parameters",
            $"proofName={command.Plan.WorkloadName}", $"location={_scope.Location}", $"imageRepository={command.Plan.ImageRepository}", $"imageDigest={command.Plan.ImageDigest}",
            $"registryName={_scope.RegistryName}", $"registrySubscriptionId={_scope.RegistrySubscriptionId}",
            $"registryResourceGroupName={_scope.RegistryResourceGroupName}", $"sqlBootstrapObjectId={_options.SqlBootstrapObjectId}",
            $"sqlBootstrapLogin={_options.SqlBootstrapLogin}", $"expiryUtc={_options.ExpiryUtc:yyyy-MM-dd}", $"owner={_options.Owner}",
            $"sqlConnectionSecretName={SqlConnectionSecretName}", $"signingKeySecretName={SigningKeySecretName}",
            $"adminPasswordSecretName={AdminPasswordSecretName}", $"adminUsername={AdminUsername}",
            $"elsaVersion={command.Plan.ElsaVersion}",
            $"templateFingerprint={command.Context.TemplateFingerprint}", "deployWorkload=true", $"workloadRevisionSuffix={revision}",
            $"stableTrafficRevisionName={stable ?? string.Empty}", "--query", "properties.outputs", "--output", "json", "--only-show-errors"];

    private void ValidateCommand(AzureProviderRunnerCommand command)
    {
        if (command.Context.WorkspaceId == Guid.Empty || command.Context.OperationId == Guid.Empty)
            throw new ArgumentException("The Azure execution context identity is required.", nameof(command));
        if (!string.Equals(command.Plan.WorkloadName, command.Context.TargetKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Azure plan target does not match its execution context.", nameof(command));
        if (!IsSafeWorkloadName(command.Plan.WorkloadName) ||
            !IsFingerprint(command.Plan.Fingerprint) ||
            !string.Equals(command.Plan.Fingerprint, command.Context.PlanFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(command.Context.TemplateFingerprint, _options.ComputeTemplateAuthorityFingerprint(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Azure plan identity is not bound to the durable execution context.", nameof(command));
        if (!string.Equals(command.Plan.Location, _scope.Location, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Azure plan location is outside the configured target scope.", nameof(command));
        var scopeFingerprint = _options.ComputeProviderScopeFingerprint(_scope);
        if (string.IsNullOrWhiteSpace(command.Context.ProviderScopeFingerprint) ||
            !string.Equals(command.Context.ProviderScopeFingerprint, scopeFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Azure operation is not bound to the configured target scope.");
        if (!string.Equals(command.Plan.Topology, AzureWorkloadPlanTranslator.SupportedTopology, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(command.Plan.Isolation, AzureWorkloadPlanTranslator.SupportedIsolation, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(command.Plan.ReleaseLine, AzureWorkloadPlanTranslator.SupportedReleaseLine, StringComparison.OrdinalIgnoreCase) ||
            !BelongsToReleaseLine(command.Plan.ReleaseLine, command.Plan.ElsaVersion) ||
            !string.Equals(command.Plan.ImageRepository, SupportedImageRepository, StringComparison.Ordinal) ||
            !AzureWorkloadPlanTranslator.IsSupportedLocation(command.Plan.Location) ||
            command.Plan.ImageDigest.Length != 64 || !command.Plan.ImageDigest.All(Uri.IsHexDigit) ||
            !AzureProviderOperationValidation.IsSafeImmutableEvidenceReference(command.Plan.ReleaseManifestReference, command.Plan.ReleaseManifestDigest) ||
            !AzureProviderOperationValidation.IsSafeImmutableEvidenceReference(command.Plan.ReleaseManifestSignatureReference, command.Plan.ReleaseManifestSignatureDigest) ||
            !AzureProviderOperationValidation.IsSafeSecretReferences(command.Plan.SecretReferences))
            throw new ArgumentException("The Azure workload plan is outside the governed provider profile.", nameof(command));
        AzureProviderOperationValidation.ValidateReferences(command.Resources);
        ValidateExactPersistedReferences(command);
    }

    /// <summary>
    /// Rebinds all mutable provider authority immediately before a remote mutation. This closes
    /// the gap between admission/observation and a later command if an executable, template or
    /// scope option is replaced while a durable operation is running.
    /// </summary>
    private void EnsureMutationAuthority(AzureProviderRunnerCommand command)
    {
        ValidateCommand(command);
        if (!string.Equals(command.Context.TemplateFingerprint, _options.ComputeTemplateAuthorityFingerprint(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The checked-in Azure template authority changed while the operation was running.");
    }

    private void ValidateExactPersistedReferences(AzureProviderRunnerCommand command)
    {
        var resources = command.Resources;
        if (resources.ResourceGroupName is not null && !string.Equals(resources.ResourceGroupName, _scope.ResourceGroupName, StringComparison.Ordinal))
            throw new ArgumentException("The persisted resource group is outside the exact configured scope.", nameof(command));
        if (resources.WorkloadIdentityResourceId is not null)
            ValidateExactResourceId(resources.WorkloadIdentityResourceId, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.ManagedIdentity", "userAssignedIdentities", $"{command.Plan.WorkloadName}-identity");
        if (resources.KeyVaultResourceId is not null)
            ValidateExactResourceId(resources.KeyVaultResourceId, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.KeyVault", "vaults", $"{command.Plan.WorkloadName}-kv");
        if (resources.SqlServerResourceId is not null)
            ValidateExactResourceId(resources.SqlServerResourceId, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.Sql", "servers", $"{command.Plan.WorkloadName}-sql");
        if (resources.ContainerAppsEnvironmentResourceId is not null)
            ValidateExactResourceId(resources.ContainerAppsEnvironmentResourceId, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.App", "managedEnvironments", $"{command.Plan.WorkloadName}-aca");
        if (resources.WorkloadResourceId is not null)
            ValidateExactResourceId(resources.WorkloadResourceId, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.App", "containerApps", $"{command.Plan.WorkloadName}-app");
        if (resources.FoundationDeploymentId is not null)
            ValidateExactDeploymentId(resources.FoundationDeploymentId, _scope.SubscriptionId, _scope.ResourceGroupName);
        if (resources.WorkloadDeploymentId is not null)
            ValidateExactDeploymentId(resources.WorkloadDeploymentId, _scope.SubscriptionId, _scope.ResourceGroupName);
        if (resources.RegistryResourceId is not null)
            ValidateExactResourceId(resources.RegistryResourceId, _scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName, "Microsoft.ContainerRegistry", "registries", _scope.RegistryName);
        if (resources.AcrPullDeploymentId is not null)
        {
            ValidateExactDeploymentId(resources.AcrPullDeploymentId, _scope.RegistrySubscriptionId, _scope.RegistryResourceGroupName);
            if (resources.WorkloadIdentityPrincipalId is not null)
                ValidateExactAcrDeploymentId(resources.AcrPullDeploymentId, command, resources.WorkloadIdentityPrincipalId);
        }
        if (resources.AcrPullRoleAssignmentId is not null)
            ValidateExactRoleAssignmentId(resources.AcrPullRoleAssignmentId, resources.RegistryResourceId);
    }

    private AzureProviderResourceReferences ProjectFoundation(DeploymentOutputs outputs, AzureWorkloadPlan plan, string deploymentName)
    {
        var resources = new AzureProviderResourceReferences(
            ResourceGroupName: Required(outputs.String("resourceGroupName"), "resourceGroupName"),
            FoundationDeploymentId: DeploymentId(_scope.SubscriptionId, _scope.ResourceGroupName, deploymentName),
            WorkloadIdentityResourceId: Required(outputs.String("workloadIdentityId"), "workloadIdentityId"),
            WorkloadIdentityClientId: NormalizeGuid(Required(outputs.String("workloadIdentityClientId"), "workloadIdentityClientId"), "workloadIdentityClientId"),
            WorkloadIdentityPrincipalId: NormalizeGuid(Required(outputs.String("workloadIdentityPrincipalId"), "workloadIdentityPrincipalId"), "workloadIdentityPrincipalId"),
            KeyVaultResourceId: Required(outputs.String("keyVaultId"), "keyVaultId"),
            KeyVaultUri: Required(outputs.String("keyVaultUri"), "keyVaultUri"),
            SqlServerResourceId: Required(outputs.String("sqlServerId"), "sqlServerId"),
            SqlServerFqdn: Required(outputs.String("sqlServerFqdn"), "sqlServerFqdn"),
            ContainerAppsEnvironmentResourceId: Required(outputs.String("containerAppsEnvironmentId"), "containerAppsEnvironmentId"));
        AzureProviderOperationValidation.ValidateReferences(resources);
        ValidateExactResourceId(resources.WorkloadIdentityResourceId!, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.ManagedIdentity", "userAssignedIdentities", $"{plan.WorkloadName}-identity");
        ValidateExactResourceId(resources.KeyVaultResourceId!, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.KeyVault", "vaults", $"{plan.WorkloadName}-kv");
        ValidateExactResourceId(resources.SqlServerResourceId!, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.Sql", "servers", $"{plan.WorkloadName}-sql");
        ValidateExactResourceId(resources.ContainerAppsEnvironmentResourceId!, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.App", "managedEnvironments", $"{plan.WorkloadName}-aca");
        if (!string.Equals(resources.ResourceGroupName, _scope.ResourceGroupName, StringComparison.Ordinal))
            throw new ArgumentException("The foundation returned an unexpected resource group.");
        return resources;
    }

    private AzureProviderResourceReferences ProjectWorkload(DeploymentOutputs outputs, AzureProviderResourceReferences foundation, AzureWorkloadPlan plan, string deploymentName, string revision, string? stable)
    {
        var resourceId = Required(outputs.String("containerAppId"), "containerAppId");
        ValidateExactResourceId(resourceId, _scope.SubscriptionId, _scope.ResourceGroupName, "Microsoft.App", "containerApps", $"{plan.WorkloadName}-app");
        var endpoint = Required(outputs.String("containerAppEndpoint"), "containerAppEndpoint");
        AzureProviderOperationValidation.ValidateEndpoint(endpoint);
        var resources = foundation with
        {
            WorkloadDeploymentId = DeploymentId(_scope.SubscriptionId, _scope.ResourceGroupName, deploymentName),
            WorkloadResourceId = resourceId,
            WorkloadRevisionName = $"{plan.WorkloadName}-app--{revision}",
            StableTrafficRevisionName = stable,
        };
        AzureProviderOperationValidation.ValidateReferences(resources);
        return resources;
    }

    private bool IsExactInventory(IReadOnlyList<AzureResource> resources, string workload)
    {
        var resourceGroupId = ResourceGroupId();
        var resourceGroupPrefix = resourceGroupId + "/providers/";
        var roots = new[]
        {
            $"{resourceGroupId}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{workload}-identity",
            $"{resourceGroupId}/providers/Microsoft.KeyVault/vaults/{workload}-kv",
            $"{resourceGroupId}/providers/Microsoft.Sql/servers/{workload}-sql",
            $"{resourceGroupId}/providers/Microsoft.OperationalInsights/workspaces/{workload}-logs",
            $"{resourceGroupId}/providers/Microsoft.App/managedEnvironments/{workload}-aca",
            $"{resourceGroupId}/providers/Microsoft.App/containerApps/{workload}-app"
        };
        foreach (var resource in resources)
        {
            var id = resource.Id?.ToLowerInvariant();
            if (id is null || string.IsNullOrWhiteSpace(resource.Type))
                return false;
            if (resource.Type?.Equals("Microsoft.Authorization/roleAssignments", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!id.StartsWith(resourceGroupPrefix.ToLowerInvariant(), StringComparison.Ordinal) ||
                    !id.Contains($"/providers/microsoft.keyvault/vaults/{workload}-kv/providers/microsoft.authorization/roleassignments/", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(ResourceName(resource.Id), "D", out _))
                    return false;
                continue;
            }
            if (!id.StartsWith(resourceGroupPrefix.ToLowerInvariant(), StringComparison.Ordinal) ||
                !roots.Any(root => id.Equals(root.ToLowerInvariant(), StringComparison.Ordinal) || id.StartsWith(root.ToLowerInvariant() + "/", StringComparison.Ordinal)))
                return false;
        }
        return true;
    }

    private bool HasSafeVaultAssignmentsForCleanup(IReadOnlyList<RoleAssignment> assignments, AzureProviderResourceReferences resources, string workload)
    {
        var vault = resources.KeyVaultResourceId ?? ResourceId("Microsoft.KeyVault", "vaults", $"{workload}-kv");
        var owned = assignments.Where(x => string.Equals(x.Scope, vault, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (owned.Length > 2)
            return false;
        var users = owned.Where(x => string.Equals(RoleDefinitionId(x.RoleDefinitionId), KeyVaultSecretsUserRoleDefinitionId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var officers = owned.Where(x => string.Equals(RoleDefinitionId(x.RoleDefinitionId), KeyVaultSecretsOfficerRoleDefinitionId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (users.Length + officers.Length != owned.Length || users.Length > 1 || officers.Length > 1)
            return false;
        if (users.Length == 1 && (resources.WorkloadIdentityPrincipalId is null ||
            !string.Equals(users[0].PrincipalId, resources.WorkloadIdentityPrincipalId, StringComparison.OrdinalIgnoreCase)))
            return false;
        return officers.Length == 0 || string.Equals(officers[0].PrincipalId, _options.SqlBootstrapObjectId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? RequireFoundation(AzureProviderResourceReferences resources) =>
        resources.ResourceGroupName is null || resources.FoundationDeploymentId is null || resources.WorkloadIdentityResourceId is null ||
        resources.WorkloadIdentityClientId is null || resources.WorkloadIdentityPrincipalId is null || resources.KeyVaultResourceId is null ||
        resources.KeyVaultUri is null || resources.SqlServerResourceId is null || resources.SqlServerFqdn is null || resources.ContainerAppsEnvironmentResourceId is null
            ? "The persisted foundation resource references are incomplete."
            : null;

    private static string? RequireRegistry(AzureProviderResourceReferences resources) =>
        RequireFoundation(resources) ?? (resources.RegistryResourceId is null || resources.AcrPullDeploymentId is null || resources.AcrPullRoleAssignmentId is null
            ? "The persisted registry resource references are incomplete."
            : null);

    private static AzureProviderRunnerResult ProcessFailure<T>(AzureProviderRunnerCommand command, AzureProviderOperationPhase phase, AzureCommandProcessResult<T> result, AzureProviderResourceReferences resources, bool mutation)
        where T : AzureCommandSafeOutput =>
        result.Status is AzureCommandProcessStatus.TerminationUncertain || result.FailureKind is AzureCommandProcessFailureKind.TerminationUncertain
            ? Uncertain(command, phase, "azure.step.termination-uncertain", "The Azure lifecycle process could not be proven terminated, so the external result requires recovery.", resources)
            : result.Status == AzureCommandProcessStatus.Cancelled || result.FailureKind == AzureCommandProcessFailureKind.Cancelled
                ? Uncertain(command, phase, "azure.step.cancelled", "The Azure lifecycle step was interrupted before its result was confirmed.", resources)
            : mutation
                ? Uncertain(command, phase, "azure.step.uncertain", "The Azure lifecycle step failed before its external result was confirmed.", resources)
                : Failed(command, phase, "azure.step.failed", "The Azure lifecycle observation failed.", resources);

    private static AzureProviderRunnerResult Completed(AzureProviderRunnerCommand command, AzureProviderOperationPhase phase, AzureProviderResourceReferences resources, bool noOp = false, AzureProviderHealth health = AzureProviderHealth.Unknown, string? endpoint = null, bool stableTrafficRestored = false) =>
        new(noOp ? AzureProviderRunnerOutcome.NoOp : AzureProviderRunnerOutcome.Completed, phase, resources, health, endpoint, [], noOp ? "azure.step.no-op" : "azure.step.completed", noOp ? "The Azure lifecycle step was already converged." : "The Azure lifecycle step completed.", StableTrafficRestored: stableTrafficRestored);

    private static AzureProviderRunnerResult Failed(AzureProviderRunnerCommand command, AzureProviderOperationPhase phase, string code, string message, AzureProviderResourceReferences? resources = null) =>
        new(AzureProviderRunnerOutcome.Failed, phase, resources ?? command.Resources, AzureProviderHealth.Unknown, null, [], code, message);

    private static AzureProviderRunnerResult Uncertain(AzureProviderRunnerCommand command, AzureProviderOperationPhase phase, string code, string message, AzureProviderResourceReferences? resources = null) =>
        new(AzureProviderRunnerOutcome.Uncertain, phase, resources ?? command.Resources, AzureProviderHealth.Unknown, null, [], code, message);

    private static AzureProviderOperationPhase CurrentPhase(AzureProviderRunnerStep step) => step switch
    {
        AzureProviderRunnerStep.Foundation or AzureProviderRunnerStep.AcrPull or AzureProviderRunnerStep.SeedSecrets => AzureProviderOperationPhase.FoundationSubmitted,
        AzureProviderRunnerStep.SqlBootstrap => AzureProviderOperationPhase.FoundationReady,
        AzureProviderRunnerStep.Workload => AzureProviderOperationPhase.WorkloadReady,
        AzureProviderRunnerStep.Health => AzureProviderOperationPhase.HealthVerified,
        AzureProviderRunnerStep.Promotion => AzureProviderOperationPhase.TrafficPromoted,
        AzureProviderRunnerStep.RestoreStableTraffic => AzureProviderOperationPhase.HealthVerified,
        AzureProviderRunnerStep.Cleanup => AzureProviderOperationPhase.CleanupVerified,
        _ => AzureProviderOperationPhase.Planned
    };

    private string ResourceGroupId() => $"/subscriptions/{_scope.SubscriptionId}/resourceGroups/{_scope.ResourceGroupName}";

    private string ResourceId(string provider, string type, string name) =>
        $"{ResourceGroupId()}/providers/{provider}/{type}/{name}";

    private string RegistryResourceId() =>
        $"/subscriptions/{_scope.RegistrySubscriptionId}/resourceGroups/{_scope.RegistryResourceGroupName}/providers/Microsoft.ContainerRegistry/registries/{_scope.RegistryName}";
    private static string AppName(AzureProviderRunnerCommand command) => $"{command.Plan.WorkloadName}-app";
    private static string SqlServerName(AzureProviderRunnerCommand command) => $"{command.Plan.WorkloadName}-sql";
    private static string FoundationDeploymentName(AzureProviderRunnerCommand command) => $"elsa108-{command.Plan.WorkloadName}-{command.Plan.Fingerprint[..12]}-foundation";
    private static string WorkloadDeploymentName(AzureProviderRunnerCommand command) => $"elsa108-{command.Plan.WorkloadName}-{command.Plan.Fingerprint[..12]}-workload";
    private string AcrDeploymentName(AzureProviderRunnerCommand command, string principalId) => $"elsa108-{command.Plan.WorkloadName}-{ShortHash($"{_scope.SubscriptionId}/{_scope.ResourceGroupName}/{principalId}/{_scope.RegistrySubscriptionId}/{_scope.RegistryResourceGroupName}/{_scope.RegistryName}")}-acr";
    private static string ShortHash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
    private static string DeploymentId(string subscription, string resourceGroup, string name) => $"/subscriptions/{subscription}/resourceGroups/{resourceGroup}/providers/Microsoft.Resources/deployments/{name}";
    private static string Required(string? value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"The Azure output {name} is missing.");
    private static string? ResourceName(string? resourceId) => resourceId?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
    private static string? RoleDefinitionId(string? id) => id?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
    private static bool IsRevisionSuffixForPlan(string? value, string baseSuffix)
    {
        if (value is null || string.Equals(value, baseSuffix, StringComparison.OrdinalIgnoreCase))
            return value is not null;
        if (!value.StartsWith(baseSuffix + "-r", StringComparison.OrdinalIgnoreCase))
            return false;
        var ordinal = value[(baseSuffix.Length + 2)..];
        return ordinal.Length > 0 && ordinal.All(char.IsAsciiDigit) &&
               int.TryParse(ordinal, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and < 1000;
    }
    private static bool IsFingerprint(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool BelongsToReleaseLine(string releaseLine, string version) =>
        string.Equals(version, releaseLine, StringComparison.OrdinalIgnoreCase) ||
        version.StartsWith(releaseLine + ".", StringComparison.OrdinalIgnoreCase) ||
        version.StartsWith(releaseLine + "-", StringComparison.OrdinalIgnoreCase);
    private static bool IsSafeWorkloadName(string? value) => value is not null && value.Length is >= 3 and <= 16 &&
        char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1]) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    private static string MapSecretName(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        var mapped = normalized switch
        {
            "database:connectionstring" or "database:connection-string" or "sql-connection" => "sql-connection",
            "identity:signingkey" or "identity:signing-key" or "identity-signing-key" => "identity-signing-key",
            "admin:password" or "admin-password" => "admin-password",
            _ => normalized.Replace(':', '-').Replace('_', '-')
        };
        if (mapped.Length is 0 or > 127 || mapped.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("The secret reference key cannot be mapped to a governed Azure secret name.", nameof(key));
        return mapped;
    }

    private static void ValidateExactResourceId(string id, string subscription, string group, string provider, string type, string name)
    {
        var expected = $"/subscriptions/{subscription}/resourceGroups/{group}/providers/{provider}/{type}/{name}";
        if (!string.Equals(id, expected, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Azure resource identity is outside the exact configured scope.");
    }

    private static void ValidateExactDeploymentId(string id, string subscription, string group)
    {
        var prefix = $"/subscriptions/{subscription}/resourceGroups/{group}/providers/Microsoft.Resources/deployments/";
        var name = ResourceName(id);
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(name) ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            throw new ArgumentException("The Azure deployment identity is outside the exact configured scope.");
    }

    private void ValidateExactAcrDeploymentId(string id, AzureProviderRunnerCommand command, string? principalId)
    {
        if (principalId is null || !string.Equals(ResourceName(id), AcrDeploymentName(command, principalId), StringComparison.Ordinal))
            throw new ArgumentException("The ACR deployment identity is not the deterministic deployment for this workload identity.");
    }

    private static string NormalizeGuid(string value, string name) =>
        Guid.TryParseExact(value, "D", out var guid)
            ? guid.ToString("D", CultureInfo.InvariantCulture)
            : throw new ArgumentException($"The Azure output {name} is not a canonical identity.");

    private static void ValidateExactRoleAssignmentId(string id, string? registryId)
    {
        if (registryId is null || !id.StartsWith(registryId + "/providers/Microsoft.Authorization/roleAssignments/", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(ResourceName(id), "D", out _))
            throw new ArgumentException("The Azure role assignment is outside the exact registry scope.");
    }

    private bool OwnsGroup(IReadOnlyDictionary<string, string> tags, string workload) =>
        tags.TryGetValue("proof", out var proof) && proof == ProofTag && tags.TryGetValue("owner", out var owner) && string.Equals(owner, _options.Owner, StringComparison.Ordinal) &&
        tags.TryGetValue("proof-name", out var name) && string.Equals(name, workload, StringComparison.Ordinal) &&
        tags.TryGetValue("sqlBootstrapObjectId", out var bootstrap) && string.Equals(bootstrap, _options.SqlBootstrapObjectId, StringComparison.Ordinal) &&
        tags.TryGetValue("expiry", out var expiry) && string.Equals(expiry, _options.ExpiryUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static SafeValue<bool> ParseBooleanAsync(ReadOnlyMemory<char> output) =>
        bool.TryParse(output.ToString().Trim(), out var value) ? new SafeValue<bool>(value) : throw new FormatException();
    private static SafeValue<string> ParseStringAsync(ReadOnlyMemory<char> output) => new(output.ToString().Trim());

    private static SafeValue<T> ParseJson<T>(ReadOnlyMemory<char> output) => new(JsonSerializer.Deserialize<T>(output.Span, JsonOptions) ?? throw new JsonException());
    private static SafeValue<DeploymentOutputs> ParseDeploymentOutputsAsync(ReadOnlyMemory<char> output) => ParseJson<DeploymentOutputs>(output);
    private static SafeValue<IReadOnlyDictionary<string, string>> ParseTagsAsync(ReadOnlyMemory<char> output) => new(new ReadOnlyDictionary<string, string>(ParseJson<Dictionary<string, string>>(output).Value));
    private static SafeValue<IReadOnlyList<RoleAssignment>> ParseRoleAssignmentsAsync(ReadOnlyMemory<char> output) => new(ParseJson<List<RoleAssignment>>(output).Value);
    private static SafeValue<IReadOnlyList<AzureResource>> ParseResourcesAsync(ReadOnlyMemory<char> output) => new(ParseJson<List<AzureResource>>(output).Value);
    private static SafeValue<IReadOnlyList<FirewallRule>> ParseFirewallRulesAsync(ReadOnlyMemory<char> output) => new(ParseJson<List<FirewallRule>>(output).Value);
    private static SafeValue<IReadOnlyList<DeploymentRecord>> ParseDeploymentsAsync(ReadOnlyMemory<char> output) => new(ParseJson<List<DeploymentRecord>>(output).Value);
    private static SafeValue<IReadOnlyList<DeletedVault>> ParseDeletedVaultsAsync(ReadOnlyMemory<char> output) => new(ParseJson<List<DeletedVault>>(output).Value);
    private static SafeValue<IReadOnlyList<AdminRecord>> ParseAdminsAsync(ReadOnlyMemory<char> output) => new(ParseJson<List<AdminRecord>>(output).Value);
    private static SafeValue<IReadOnlyList<TrafficEntry>> ParseTrafficAsync(ReadOnlyMemory<char> output) => new(ParseJson<List<TrafficEntry>>(output).Value);
    private static SafeValue<RevisionState> ParseRevisionStateAsync(ReadOnlyMemory<char> output) => ParseJson<RevisionState>(output);
    private static SafeValue<IReadOnlyList<string>> ParseStringArrayAsync(ReadOnlyMemory<char> output) => new(ParseJson<List<string>>(output).Value);
    private static SafeValue<int> ParseIntegerAsync(ReadOnlyMemory<char> output) =>
        int.TryParse(output.ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? new SafeValue<int>(value) : throw new FormatException();

    private static async Task<(string Directory, string File)> WriteTransientSecretFileAsync(AzureSecretLease lease, CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-azure-").FullName;
        var file = Path.Combine(directory, "value");
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using var stream = new FileStream(file, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 4096,
                Options = FileOptions.SequentialScan
            });
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(lease.Value, cancellationToken);
            await writer.FlushAsync(cancellationToken);
            return (directory, file);
        }
        catch
        {
            DeleteTransientSecretFile(directory, file);
            throw;
        }
    }

    private async Task<(string Directory, string File)> WriteSqlBootstrapFileAsync(string clientId, string workload, CancellationToken cancellationToken)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(_options.TemplateRoot, "sql-bootstrap.sql"), cancellationToken);
        source = source.Replace("__WORKLOAD_IDENTITY_NAME__", $"{workload}-identity", StringComparison.Ordinal)
            .Replace("__WORKLOAD_IDENTITY_CLIENT_ID__", clientId, StringComparison.Ordinal);
        var directory = Directory.CreateTempSubdirectory("elsa-azure-").FullName;
        var file = Path.Combine(directory, "sql-bootstrap.sql");
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await File.WriteAllTextAsync(file, source, new UTF8Encoding(false), cancellationToken);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return (directory, file);
        }
        catch
        {
            DeleteTransientSecretFile(directory, file);
            throw;
        }
    }

    private static void DeleteTransientSecretFile(string directory, string file)
    {
        // Cleanup failure is not best-effort: a secret-bearing file that cannot be proven absent
        // makes the provider outcome uncertain and must reach durable recovery via RunAsync.
        if (!string.IsNullOrEmpty(file) && File.Exists(file))
            File.Delete(file);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: false);
    }

    private sealed class DeploymentOutputs : Dictionary<string, OutputValue>
    {
        public string? String(string name) => TryGetValue(name, out var value) ? value.Value : null;
    }

    private sealed class OutputValue
    {
        [JsonPropertyName("value")] public string? Value { get; set; }
    }

    private sealed class RoleAssignment
    {
        public string? Id { get; set; }
        public string? Scope { get; set; }
        public string? PrincipalId { get; set; }
        public string? RoleDefinitionId { get; set; }
    }

    private enum AcrRoleDiscoveryStatus { Absent, Exact, Ambiguous, Uncertain }
    private sealed record AcrRoleDiscovery(AcrRoleDiscoveryStatus Status, string? AssignmentId);

    private sealed class AzureResource
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
    }

    private sealed class FirewallRule { public string? Name { get; set; } }
    private sealed class DeploymentRecord { public string? Name { get; set; } }
    private sealed class DeletedVault
    {
        public string? Name { get; set; }
        public string? Location { get; set; }
        public string? VaultId { get; set; }
        [JsonPropertyName("properties")] public DeletedVaultProperties? Properties { get; set; }
        [JsonIgnore] public string? EffectiveLocation => Properties?.Location ?? Location;
        [JsonIgnore] public string? EffectiveVaultId => Properties?.VaultId ?? VaultId;
    }
    private sealed class DeletedVaultProperties { public string? Location { get; set; } public string? VaultId { get; set; } }
    private sealed class AdminRecord { public string? Login { get; set; } public string? Sid { get; set; } }
    private sealed class TrafficEntry { public string? RevisionName { get; set; } public int Weight { get; set; } }
    private sealed class RevisionState { public bool Active { get; set; } public string? Health { get; set; } }
    private sealed class SafeValue<T>(T value) : AzureCommandSafeOutput
    {
        public T Value { get; } = value;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyReferences = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}

/// <summary>Compatibility name for hosts that refer to the adapter as the Azure provider runner.</summary>
public sealed class AzureProviderRunner : IAzureProviderRunner
{
    private readonly AzureBicepProviderRunner _inner;

    public AzureProviderRunner(AzureProviderRunnerOptions options, AzureProviderTargetScope scope, IAzureSecretResolver? secretResolver = null) =>
        _inner = new AzureBicepProviderRunner(options, scope, secretResolver);

    internal AzureProviderRunner(AzureProviderRunnerOptions options, AzureProviderTargetScope scope, IAzureCommandProcess process, IAzureSecretResolver? secretResolver = null) =>
        _inner = new AzureBicepProviderRunner(options, scope, process, secretResolver);

    public Task<AzureProviderRunnerResult> RunAsync(AzureProviderRunnerCommand command, CancellationToken cancellationToken = default) =>
        _inner.RunAsync(command, cancellationToken);
}
