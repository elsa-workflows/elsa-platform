using System.Collections.ObjectModel;

namespace ValenceControl.Deployment.Abstractions;

internal static class DeploymentEmpty
{
    public static IReadOnlyDictionary<string, string> StringDictionary { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}
