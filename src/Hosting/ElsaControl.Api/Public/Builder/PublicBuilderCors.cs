namespace ElsaControl.Api.Public.Builder;

/// <summary>
/// Cross-origin access for the public Runtime Builder API. Origins are configuration-driven so a host
/// can add or retire a front end (such as the Elsa Hub configurator) without a code change.
/// </summary>
public static class PublicBuilderCors
{
    public const string PolicyName = "PublicBuilderClients";
    public const string AllowedOriginsConfigurationKey = "Cors:BuilderClientOrigins";
}
