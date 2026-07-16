using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Elsa.Platform.Healing.GitHub;

public sealed record UnifiedDiffLine(char Kind, string Text);

public sealed record UnifiedDiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<UnifiedDiffLine> Lines);

public sealed record UnifiedDiffFile(
    string? OldPath,
    string? NewPath,
    bool IsNew,
    bool IsDeleted,
    IReadOnlyList<UnifiedDiffHunk> Hunks)
{
    public string EffectivePath => NewPath ?? OldPath!;
}

public sealed record ParsedUnifiedDiff(
    IReadOnlyList<UnifiedDiffFile> Files,
    int ChangedLines,
    int SizeBytes);

public static partial class UnifiedDiffParser
{
    public static ParsedUnifiedDiff Parse(string diff, int maximumBytes = 1_048_576)
    {
        if (string.IsNullOrWhiteSpace(diff))
            throw Invalid();
        var byteCount = Encoding.UTF8.GetByteCount(diff);
        if (byteCount > maximumBytes || diff.IndexOf('\0') >= 0 || diff.Contains('\r'))
            throw Invalid();

        var lines = diff.Split('\n');
        var files = new List<UnifiedDiffFile>();
        var changedLines = 0;
        for (var index = 0; index < lines.Length - 1 || index < lines.Length && lines[index].Length > 0;)
        {
            if (!lines[index].StartsWith("diff --git ", StringComparison.Ordinal))
                throw Invalid();
            var match = DiffHeaderRegex().Match(lines[index++]);
            if (!match.Success)
                throw Invalid();
            var headerOldPath = NormalizePath(match.Groups[1].Value, "a/");
            var headerNewPath = NormalizePath(match.Groups[2].Value, "b/");
            var newFile = false;
            var deletedFile = false;
            var oldPath = headerOldPath;
            var newPath = headerNewPath;

            while (index < lines.Length && !lines[index].StartsWith("--- ", StringComparison.Ordinal))
            {
                var metadata = lines[index++];
                if (metadata.StartsWith("new file mode ", StringComparison.Ordinal))
                {
                    var mode = metadata[14..];
                    if (mode is "120000" or "160000") throw Invalid();
                    newFile = true;
                }
                else if (metadata.StartsWith("deleted file mode ", StringComparison.Ordinal))
                {
                    var mode = metadata[18..];
                    if (mode is "120000" or "160000") throw Invalid();
                    deletedFile = true;
                }
                else if (metadata.StartsWith("old mode ", StringComparison.Ordinal) ||
                         metadata.StartsWith("new mode ", StringComparison.Ordinal) ||
                         metadata.StartsWith("rename from ", StringComparison.Ordinal) ||
                         metadata.StartsWith("rename to ", StringComparison.Ordinal) ||
                         metadata.StartsWith("copy from ", StringComparison.Ordinal) ||
                         metadata.StartsWith("copy to ", StringComparison.Ordinal) ||
                         metadata.StartsWith("similarity index ", StringComparison.Ordinal) ||
                         metadata.StartsWith("dissimilarity index ", StringComparison.Ordinal) ||
                         metadata.StartsWith("Binary files ", StringComparison.Ordinal) ||
                         metadata == "GIT binary patch" || metadata.StartsWith("Submodule ", StringComparison.Ordinal))
                    throw Invalid();
                else if (metadata.StartsWith("index ", StringComparison.Ordinal))
                {
                    var mode = metadata.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    if (mode is "120000" or "160000") throw Invalid();
                }
                else
                    throw Invalid();
            }

            if (index + 1 >= lines.Length || !lines[index].StartsWith("--- ", StringComparison.Ordinal) ||
                !lines[index + 1].StartsWith("+++ ", StringComparison.Ordinal))
                throw Invalid();
            oldPath = ParseFileMarker(lines[index++], "--- ", "a/", out var oldNull);
            newPath = ParseFileMarker(lines[index++], "+++ ", "b/", out var newNull);
            newFile |= oldNull;
            deletedFile |= newNull;
            if (oldNull) oldPath = null;
            if (newNull) newPath = null;
            if (oldPath is null && newPath is null || oldPath is not null && !FixedPathEquals(oldPath, headerOldPath) ||
                newPath is not null && !FixedPathEquals(newPath, headerNewPath) ||
                !newFile && !deletedFile && !FixedPathEquals(oldPath!, newPath!))
                throw Invalid();

            var hunks = new List<UnifiedDiffHunk>();
            while (index < lines.Length && !lines[index].StartsWith("diff --git ", StringComparison.Ordinal) && lines[index].Length > 0)
            {
                var hunkMatch = HunkHeaderRegex().Match(lines[index++]);
                if (!hunkMatch.Success)
                    throw Invalid();
                var oldStart = ParseNumber(hunkMatch.Groups[1].Value);
                var oldCount = hunkMatch.Groups[2].Success ? ParseNumber(hunkMatch.Groups[2].Value) : 1;
                var newStart = ParseNumber(hunkMatch.Groups[3].Value);
                var newCount = hunkMatch.Groups[4].Success ? ParseNumber(hunkMatch.Groups[4].Value) : 1;
                var hunkLines = new List<UnifiedDiffLine>();
                var observedOld = 0;
                var observedNew = 0;
                while (index < lines.Length && !lines[index].StartsWith("@@ ", StringComparison.Ordinal) &&
                       !lines[index].StartsWith("diff --git ", StringComparison.Ordinal) && lines[index].Length > 0)
                {
                    var line = lines[index++];
                    if (line == "\\ No newline at end of file")
                        continue;
                    if (line[0] is not (' ' or '+' or '-'))
                        throw Invalid();
                    var kind = line[0];
                    hunkLines.Add(new UnifiedDiffLine(kind, line[1..]));
                    if (kind != '+') observedOld++;
                    if (kind != '-') observedNew++;
                    if (kind is '+' or '-') changedLines++;
                }
                if (observedOld != oldCount || observedNew != newCount)
                    throw Invalid();
                hunks.Add(new UnifiedDiffHunk(oldStart, oldCount, newStart, newCount, hunkLines));
            }
            if (hunks.Count == 0)
                throw Invalid();
            files.Add(new UnifiedDiffFile(oldPath, newPath, newFile, deletedFile, hunks));
            if (index < lines.Length && lines[index].Length == 0) index++;
        }

        if (files.Count == 0 || files.Select(x => x.EffectivePath).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw Invalid();
        return new ParsedUnifiedDiff(files, changedLines, byteCount);
    }

    private static string? ParseFileMarker(string line, string marker, string prefix, out bool isNull)
    {
        var value = line[marker.Length..];
        isNull = value == "/dev/null";
        return isNull ? null : NormalizePath(value, prefix);
    }

    public static string NormalizePath(string path, string? requiredPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path[0] is '"' or '\'' ||
            path.Contains('\\') || path.Contains('\0') || path.Any(char.IsControl) || Path.IsPathRooted(path))
            throw Invalid();
        if (requiredPrefix is not null)
        {
            if (!path.StartsWith(requiredPrefix, StringComparison.Ordinal)) throw Invalid();
            path = path[requiredPrefix.Length..];
        }
        var segments = path.Split('/');
        if (segments.Any(x => x.Length == 0 || x is "." or ".."))
            throw Invalid();
        return string.Join('/', segments);
    }

    private static int ParseNumber(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= 0
            ? result
            : throw Invalid();

    private static bool FixedPathEquals(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
    private static GitHubSecurityException Invalid() => new(GitHubSecurityReasonCodes.PatchInvalid);

    [GeneratedRegex("^diff --git (a/[^ ]+) (b/[^ ]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DiffHeaderRegex();

    [GeneratedRegex("^@@ -(\\d+)(?:,(\\d+))? \\+(\\d+)(?:,(\\d+))? @@(?: .*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeaderRegex();
}
