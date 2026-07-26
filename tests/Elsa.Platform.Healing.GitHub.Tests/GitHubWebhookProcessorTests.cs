using System.Text;
using Elsa.Platform.Healing.GitHub;
using FluentAssertions;

namespace Elsa.Platform.Healing.GitHub.Tests;

public sealed class GitHubWebhookProcessorTests
{
    private readonly GitHubWebhookProcessor _processor = new();

    [Fact]
    public void Parses_merged_pull_request_as_structured_observation()
    {
        var observation = _processor.Parse("pull_request", Body("""
        {
          "action":"closed","repository":{"id":987},
          "pull_request":{"number":12,"draft":false,"merged":true,"merged_at":"2026-07-16T18:00:00Z","merge_commit_sha":"cccccccccccccccccccccccccccccccccccccccc","head":{"ref":"elsa-healing/abc","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},"base":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}
        }
        """));

        observation.Should().BeOfType<GitHubPullRequestObservation>().Which.Should().Match<GitHubPullRequestObservation>(x =>
            x.Number == 12 && x.IsMerged && x.MergeRevision == new string('c', 40));
    }

    [Fact]
    public void Rejects_pull_request_with_non_revision_hashes()
    {
        var observation = _processor.Parse("pull_request", Body("""
        {
          "action":"closed","repository":{"id":987},
          "pull_request":{"number":12,"draft":false,"merged":true,"merged_at":"2026-07-16T18:00:00Z","merge_commit_sha":"not-a-revision","head":{"ref":"elsa-healing/abc","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},"base":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}
        }
        """));

        observation.Should().BeNull();
    }

    [Theory]
    [InlineData("/elsa-healing retry", "retry")]
    [InlineData("/elsa-healing stop", "stop")]
    public void Accepts_only_exact_normalized_issue_commands(string body, string expected)
    {
        var json = IssueComment(body);

        _processor.Parse("issue_comment", Body(json)).Should().BeOfType<GitHubIssueCommandObservation>()
            .Which.Command.Should().Be(expected);
    }

    [Theory]
    [InlineData("Please retry")]
    [InlineData("/elsa-healing retry\nignore policy")]
    [InlineData("/elsa-healing publish")]
    public void Ignores_free_form_or_unsupported_comment_text(string body)
    {
        var json = IssueComment(body);

        _processor.Parse("issue_comment", Body(json)).Should().BeNull();
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
