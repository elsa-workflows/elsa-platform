using Elsa.Platform.Weaver.Core.Safety;

namespace Elsa.Platform.Weaver.Core.Tools;

public sealed class WeaverWorkspaceTools(WeaverRedactionService redaction)
{
    public WeaverWorkspaceContextSummary GetCurrentContext(string? routePath, IReadOnlyDictionary<string, string> context)
    {
        var items = context
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new WeaverWorkspaceContextItem(x.Key, redaction.Redact(x.Value).Value))
            .ToList();
        var summary = items.Count == 0
            ? "No page entity context was provided."
            : string.Join(", ", items.Select(item => $"{item.Name}={item.Value}"));

        return new WeaverWorkspaceContextSummary(routePath, items, summary);
    }
}

public sealed record WeaverWorkspaceContextSummary(
    string? RoutePath,
    IReadOnlyList<WeaverWorkspaceContextItem> Items,
    string Summary);

public sealed record WeaverWorkspaceContextItem(string Name, string Value);
