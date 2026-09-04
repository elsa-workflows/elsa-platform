using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Core.Plans;

namespace ElsaControl.Api.Workspace;

internal static class ElsaInstancePlanResolutionComposition
{
    public static void AddResolver(IServiceCollection services, IConfiguration configuration)
    {
        var egress = configuration["RuntimeBuilder:InstancePlans:DefaultEgress"]
            ?? ElsaInstancePlanResolutionOptions.Default.DefaultEgress;
        if (egress is not ("restricted" or "unrestricted"))
            throw new InvalidOperationException("The instance plan egress policy is unsupported.");

        // Provider support does not silently relax the platform's network default.
        // A stamp using the initial public Azure profile must opt in explicitly.
        services.AddSingleton(new ElsaInstancePlanResolutionOptions(DefaultEgress: egress));
        services.AddScoped<IElsaInstancePlanResolver, ElsaInstancePlanResolver>();
    }
}
