namespace Elsa.Platform.Studio.Submit;

public sealed record StudioSubmitOptions
{
    public Uri? PlatformEndpoint { get; init; }

    public Guid? WorkspaceId { get; init; }

    public StudioSubmitAuthenticationMode AuthenticationMode { get; init; } = StudioSubmitAuthenticationMode.PlatformBearerToken;

    public string? CredentialReference { get; init; }

    public StudioPublishSeparationPolicy PublishSeparationPolicy { get; init; } = StudioPublishSeparationPolicy.HideDirectPublish;

    public string ProducerName { get; init; } = "Elsa Studio";

    public string? ProducerVersion { get; init; }

    public string PayloadProvider { get; init; } = "producer-managed";

    public string PayloadUriScheme { get; init; } = "studio";

    public string? RuntimeVersionRange { get; init; }

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = ["workflow-definition.apply"];
}

public enum StudioSubmitAuthenticationMode
{
    PlatformBearerToken,
    ProviderCredentialReference
}

public enum StudioPublishSeparationPolicy
{
    HideDirectPublish,
    DisableDirectPublish,
    ShowDirectPublishAsSeparateAction
}
