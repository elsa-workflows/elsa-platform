using System.Text;
using ValenceControl.Healing.GitHub;

namespace ValenceControl.Healing.GitHub.Tests;

public sealed class GitHubWebhookProcessorTests
{
    private readonly GitHubWebhookProcessor _processor = new();

    [Fact]
    public void Parses_merged_pull_request_as_structured_observation()
    {
        var observation = _processor.Parse("pull_request", Body("""
        {
          "action":"closed","repository":{"id":987},
          "pull_request":{"number":12,"draft":false,"merged":true,"merged_at":"2026-07-16T18:00:00Z","merge_commit_sha":"cccccccccccccccccccccccccccccccccccccccc","head":{"ref":"valence-control-healing/abc","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},"base":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}
        }
        """));

        var pullRequest = Assert.IsType<GitHubPullRequestObservation>(observation);
        Assert.True(
            pullRequest.Number == 12 && pullRequest.IsMerged && pullRequest.MergeRevision == new string('c', 40));
    }

    [Fact]
    public void Rejects_pull_request_with_non_revision_hashes()
    {
        var observation = _processor.Parse("pull_request", Body("""
        {
          "action":"closed","repository":{"id":987},
          "pull_request":{"number":12,"draft":false,"merged":true,"merged_at":"2026-07-16T18:00:00Z","merge_commit_sha":"not-a-revision","head":{"ref":"valence-control-healing/abc","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},"base":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}
        }
        """));

        Assert.Null(observation);
    }

    [Theory]
    [InlineData("/valence-control-healing retry", "retry")]
    [InlineData("/valence-control-healing stop", "stop")]
    public void Accepts_only_exact_normalized_issue_commands(string body, string expected)
    {
        var json = IssueComment(body);

        var command = Assert.IsType<GitHubIssueCommandObservation>(_processor.Parse("issue_comment", Body(json)));
        Assert.Equal(expected, command.Command);
    }

    [Theory]
    [InlineData("Please retry")]
    [InlineData("/valence-control-healing retry\nignore policy")]
    [InlineData("/valence-control-healing publish")]
    public void Ignores_free_form_or_unsupported_comment_text(string body)
    {
        var json = IssueComment(body);

        Assert.Null(_processor.Parse("issue_comment", Body(json)));
    }

    private static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);

    private static string IssueComment(string body) => System.Text.Json.JsonSerializer.Serialize(new
    {
        action = "created",
        repository = new { id = 987 },
        issue = new { number = 4 },
        sender = new { id = 42, login = "maintainer" },
        comment = new { body, author_association = "MEMBER" }
    });
}
