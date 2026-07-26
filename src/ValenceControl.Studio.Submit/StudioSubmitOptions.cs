namespace ValenceControl.Studio.Submit;

public sealed record StudioSubmitOptions
{
    public Uri? ControlEndpoint { get; init; }

    public Guid? WorkspaceId { get; init; }

    public Guid? ApplicationId { get; init; }

    public Guid? EnvironmentId { get; init; }

    public string? RevisionLabel { get; init; }

    public string? RevisionCommit { get; init; }

    public StudioSubmitAuthenticationMode AuthenticationMode { get; init; } = StudioSubmitAuthenticationMode.ControlBearerToken;

    public string? CredentialReference { get; init; }

    public StudioPublishSeparationPolicy PublishSeparationPolicy { get; init; } = StudioPublishSeparationPolicy.HideDirectPublish;

    public string ProducerName { get; init; } = "Elsa Studio";

    public string? ProducerVersion { get; init; }

    public string PayloadProvider { get; init; } = "producer-managed";

    public string PayloadUriScheme { get; init; } = "studio";

    public string? RuntimeVersionRange { get; init; }

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = ["loom.recipe.apply"];
}

public enum StudioSubmitAuthenticationMode
{
    ControlBearerToken,
    ProviderCredentialReference
}

public enum StudioPublishSeparationPolicy
{
    HideDirectPublish,
    DisableDirectPublish,
    ShowDirectPublishAsSeparateAction
}
