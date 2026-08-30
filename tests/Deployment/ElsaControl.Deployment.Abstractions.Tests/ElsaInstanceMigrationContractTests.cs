using ElsaControl.Deployment.Abstractions.Instances;
using Xunit;

namespace ElsaControl.Deployment.Abstractions.Tests;

public sealed class ElsaInstanceMigrationContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("3.8", "3.8.12", "4.0", "4.0.0")]
    [InlineData("3.10", "3.10.4", "4.1", "4.1.2-preview.1")]
    [InlineData("4.1", "4.1.9", "5.0", "5.0.0")]
    [InlineData("19.7", "19.7.1", "20.0", "20.0.0")]
    public void Arbitrary_release_lines_are_preserved(string sourceLine, string sourceVersion, string targetLine, string targetVersion)
    {
        var migration = Plan(sourceLine, sourceVersion, targetLine, targetVersion);

        Assert.Equal(sourceLine, migration.Source.ReleaseLine);
        Assert.Equal(sourceVersion, migration.Source.Version);
        Assert.Equal(targetLine, migration.Target.ReleaseLine);
        Assert.Equal(targetVersion, migration.Target.Version);
    }

    [Fact]
    public void Cutover_requires_health_and_makes_source_non_writable_for_thirty_days()
    {
        var migration = Plan().Advance(ElsaInstanceMigrationPhase.Preparing, Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => migration.CutOver(false,
            ElsaInstanceMigrationSourceAccess.Stopped, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => migration.CutOver(true,
            ElsaInstanceMigrationSourceAccess.Running, Now.AddMinutes(2)));

        var cutover = migration.CutOver(true, ElsaInstanceMigrationSourceAccess.ReadOnly, Now.AddMinutes(2));
        Assert.Equal(ElsaInstanceMigrationPhase.Cutover, cutover.Phase);
        Assert.Equal(ElsaInstanceMigration.MinimumSourceRetention,
            cutover.SourceRetainUntil - cutover.CutoverAt);
    }

    [Fact]
    public void Normal_release_before_deadline_is_rejected()
    {
        var retained = CutOver().RetainSource(Now.AddMinutes(3));

        Assert.Throws<InvalidOperationException>(() => retained.BeginSourceRetirement(Now.AddDays(29)));

        var retiring = retained.BeginSourceRetirement(Now.AddDays(31));
        var released = retiring.ConfirmSourceReleased(Now.AddDays(31).AddMinutes(1));
        Assert.Equal(ElsaInstanceMigrationPhase.Released, released.Phase);
        Assert.Equal(ElsaInstanceMigrationSourceAccess.Stopped, released.SourceAccess);
    }

    [Fact]
    public void Generic_phase_advance_cannot_bypass_cutover_or_source_release_proof()
    {
        var migration = Plan().Advance(ElsaInstanceMigrationPhase.Preparing, Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() =>
            migration.Advance(ElsaInstanceMigrationPhase.Cutover, Now.AddMinutes(2)));
        var retained = CutOver().RetainSource(Now.AddMinutes(3));
        Assert.Throws<InvalidOperationException>(() =>
            retained.Advance(ElsaInstanceMigrationPhase.RetiringSource, Now.AddDays(31)));
    }

    [Fact]
    public void Authorized_early_release_is_attributable_and_durable()
    {
        var accountId = Guid.NewGuid();
        var retained = CutOver().RetainSource(Now.AddMinutes(3));
        var approved = retained.ApproveEarlyRelease(accountId, Now.AddHours(1));
        var retiring = approved.BeginSourceRetirement(Now.AddDays(1));
        var released = retiring.ConfirmSourceReleased(Now.AddDays(1).AddMinutes(1));

        Assert.Equal(accountId, released.EarlyReleaseApprovedByAccountId);
        Assert.Equal(Now.AddHours(1), released.EarlyReleaseApprovedAt);
        Assert.Equal(Now.AddDays(1).AddMinutes(1), released.SourceReleasedAt);
        Assert.Throws<InvalidOperationException>(() =>
            approved.ApproveEarlyRelease(Guid.NewGuid(), Now.AddHours(2)));
    }

    [Fact]
    public void Release_references_and_start_request_are_exact_and_stable()
    {
        var migration = Plan();

        Assert.Equal(ElsaInstanceMigration.HashRequestKey("request-1"), migration.StartRequestHash);
        Assert.Throws<ArgumentException>(() => ElsaInstanceMigration.HashRequestKey("contains spaces"));
        Assert.Throws<ArgumentException>(() => ElsaInstanceMigration.HashRequestKey(new string('a', 129)));
        Assert.Equal("sha256:" + new string('a', 64), migration.Source.ManifestDigest);
        Assert.Equal("sha256:" + new string('b', 64), migration.Target.ManifestDigest);
        Assert.NotEqual(migration.Source, migration.Target);
    }

    private static ElsaInstanceMigration CutOver() => Plan()
        .Advance(ElsaInstanceMigrationPhase.Preparing, Now.AddMinutes(1))
        .CutOver(true, ElsaInstanceMigrationSourceAccess.Stopped, Now.AddMinutes(2));

    private static ElsaInstanceMigration Plan(
        string sourceLine = "3.10", string sourceVersion = "3.10.4",
        string targetLine = "4.0", string targetVersion = "4.0.1") =>
        ElsaInstanceMigration.Plan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Reference("source", sourceLine, sourceVersion, 'a'),
            Reference("target", targetLine, targetVersion, 'b'),
            ElsaInstanceMigration.HashRequestKey("request-1"), Now);

    private static ElsaInstanceMigrationReleaseReference Reference(
        string name, string releaseLine, string version, char digest) => new(
            $"{name}-plan",
            $"https://control.example/api/plans/{name}-plan",
            releaseLine,
            version,
            "sha256:" + new string(digest, 64),
            $"{name}-deployment");
}
