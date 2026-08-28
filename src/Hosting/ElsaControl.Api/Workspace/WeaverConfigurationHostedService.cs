using ElsaControl.Weaver.Core.Configuration;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Workspace;

public sealed class WeaverConfigurationHostedService(
    IOptions<WeaverOptions> options,
    ILogger<WeaverConfigurationHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var weaverOptions = options.Value;
        if (weaverOptions.IsAvailable(out var disabledReason))
        {
            logger.LogInformation(
                "Weaver is enabled with provider mode {ProviderMode} and model {Model}.",
                weaverOptions.ProviderMode,
                weaverOptions.Model);
            return Task.CompletedTask;
        }

        logger.LogInformation("Weaver is unavailable: {Reason}", disabledReason);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
