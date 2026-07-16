using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core.Incidents;
using FluentAssertions;

namespace Elsa.Platform.Healing.Core.Tests.Incidents;

public sealed class HealingSignalClassifierTests
{
    private readonly HealingSignalNormalizer _normalizer = new();
    private readonly HealingSignalClassifier _classifier = new();
    private readonly HealingFingerprintService _fingerprints = new();

    [Fact]
    public void NormalizeAcceptsCompatibleMinorVersionAndCreatesStableOccurrenceIdentity()
    {
        var signal = Signal() with { ProfileVersion = "1.7", OccurrenceId = null };

        var first = _normalizer.Normalize(signal);
        var retry = _normalizer.Normalize(signal with
        {
            Exception = signal.Exception with { Message = "volatile request 987" }
        });
        var otherResource = _normalizer.Normalize(signal with { ResourceIdentity = "service:checkout:blue" });

        first.Succeeded.Should().BeTrue();
        var normalized = first.Signal!;
        normalized.OccurredAt.Offset.Should().Be(TimeSpan.Zero);
        normalized.ServiceName.Should().Be("checkout-api");
        normalized.Frames.Should().ContainSingle().Which.Should().Be(
            new NormalizedHealingFrame("Acme.Checkout", "Acme.Checkout.OrderHandler", "HandleAsync"));
        retry.Signal!.OccurrenceKey.Should().Be(normalized.OccurrenceKey);
        otherResource.Signal!.OccurrenceKey.Should().NotBe(normalized.OccurrenceKey);
    }

    [Fact]
    public void ExplicitOccurrenceIdentityIsApplicationScopedAndIgnoresTelemetryVolatility()
    {
        var signal = Signal() with { OccurrenceId = " retry-42 " };

        var first = _normalizer.Normalize(signal).Signal!;
        var replay = _normalizer.Normalize(signal with
        {
            EnvironmentId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid(),
            Trace = new("cccccccccccccccccccccccccccccccc", "dddddddddddddddd"),
            OccurredAt = signal.OccurredAt.AddMinutes(5)
        }).Signal!;
        var otherApplication = _normalizer.Normalize(signal with { ApplicationId = Guid.NewGuid() }).Signal!;

        replay.OccurrenceKey.Should().Be(first.OccurrenceKey);
        otherApplication.OccurrenceKey.Should().NotBe(first.OccurrenceKey);
    }

    [Theory]
    [InlineData("2.0", HealingSignalNormalizationReasonCodes.UnsupportedProfileVersion)]
    [InlineData("1", HealingSignalNormalizationReasonCodes.UnsupportedProfileVersion)]
    [InlineData("1.0", HealingSignalNormalizationReasonCodes.ServiceNameRequired, "")]
    public void NormalizeRejectsNonConformingSignals(string version, string expectedReason, string? serviceName = "checkout-api")
    {
        var result = _normalizer.Normalize(Signal() with { ProfileVersion = version, ServiceName = serviceName });

        result.Succeeded.Should().BeFalse();
        result.Signal.Should().BeNull();
        result.ReasonCodes.Should().Contain(expectedReason);
    }

    [Fact]
    public void NormalizeRejectsMissingEvidenceWithoutThrowing()
    {
        var result = _normalizer.Normalize(Signal() with { Evidence = null! });

        result.Succeeded.Should().BeFalse();
        result.ReasonCodes.Should().Contain(HealingSignalNormalizationReasonCodes.EvidenceRequired);
    }

    [Theory]
    [InlineData(HealingFailureClasses.UnhandledRequest, IncidentClassification.UnhandledRequest)]
    [InlineData(HealingFailureClasses.FatalStartup, IncidentClassification.FatalStartup)]
    [InlineData(HealingFailureClasses.FatalBackground, IncidentClassification.FatalBackground)]
    [InlineData(HealingFailureClasses.UnexpectedWorkflow, IncidentClassification.UnexpectedWorkflow)]
    [InlineData(HealingFailureClasses.UnexpectedActivity, IncidentClassification.UnexpectedActivity)]
    [InlineData(HealingFailureClasses.TransientExhausted, IncidentClassification.TransientExhausted)]
    [InlineData(HealingFailureClasses.ExplicitIncident, IncidentClassification.ExplicitIncident)]
    public void CuratedEligibleFailureClassesAreAccepted(string failureClass, IncidentClassification classification)
    {
        var normalized = _normalizer.Normalize(Signal() with { FailureClass = failureClass }).Signal!;

        var result = _classifier.Classify(normalized);

        result.IsEligible.Should().BeTrue();
        result.Classification.Should().Be(classification);
        result.ReasonCode.Should().Be(HealingSignalClassificationReasonCodes.EligibleFailureClass);
    }

    [Theory]
    [InlineData(HealingFailureClasses.Validation)]
    [InlineData(HealingFailureClasses.Authorization)]
    [InlineData(HealingFailureClasses.Cancellation)]
    [InlineData(HealingFailureClasses.Handled)]
    [InlineData(HealingFailureClasses.TransientRetrying)]
    [InlineData(HealingFailureClasses.Unknown)]
    public void ExpectedAndUnknownFailureClassesAreExcluded(string failureClass)
    {
        var normalized = _normalizer.Normalize(Signal() with { FailureClass = failureClass }).Signal!;

        var result = _classifier.Classify(normalized);

        result.IsEligible.Should().BeFalse();
        result.Classification.Should().BeNull();
    }

    [Fact]
    public void RetryingSignalIsExcludedEvenWhenItsFailureClassWouldOtherwiseBeEligible()
    {
        var normalized = _normalizer.Normalize(Signal() with
        {
            FailureClass = HealingFailureClasses.FatalBackground,
            RetryState = HealingRetryStates.Retrying
        }).Signal!;

        var result = _classifier.Classify(normalized);

        result.IsEligible.Should().BeFalse();
        result.ReasonCode.Should().Be(HealingSignalClassificationReasonCodes.RetryInProgress);
    }

    [Fact]
    public void OnlyAuthorizedPolicyCanRemapAnExcludedFailure()
    {
        var normalized = _normalizer.Normalize(Signal() with { FailureClass = HealingFailureClasses.Validation }).Signal!;

        var unauthorized = _classifier.Classify(normalized,
            new HealingClassificationOverride(HealingFailureClasses.UnhandledRequest, IsAuthorized: false));
        var authorized = _classifier.Classify(normalized,
            new HealingClassificationOverride(HealingFailureClasses.UnhandledRequest, IsAuthorized: true));

        unauthorized.IsEligible.Should().BeFalse();
        unauthorized.ReasonCode.Should().Be(HealingSignalClassificationReasonCodes.UnauthorizedOverrideIgnored);
        authorized.IsEligible.Should().BeTrue();
        authorized.Classification.Should().Be(IncidentClassification.UnhandledRequest);
        authorized.ReasonCode.Should().Be(HealingSignalClassificationReasonCodes.AuthorizedOverride);
    }

    [Fact]
    public void FingerprintExcludesVolatileEvidenceAndNormalizesStackTraceFrames()
    {
        var originalSignal = Signal();
        var original = _normalizer.Normalize(originalSignal).Signal!;
        var volatileVariant = _normalizer.Normalize(originalSignal with
        {
            EnvironmentId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid(),
            OccurredAt = originalSignal.OccurredAt.AddHours(1),
            Exception = originalSignal.Exception with
            {
                Message = "Order 999 failed",
                Frames = [new("Acme.Checkout", "Acme.Checkout.OrderHandler", "HandleAsync", "/agent/_work/9/OrderHandler.cs", 404)]
            },
            Trace = new("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", "ffffffffffffffff")
        }).Signal!;

        var first = _fingerprints.Compute(original, ["package:Acme.Checkout"], "github:acme/checkout");
        var second = _fingerprints.Compute(volatileVariant, ["package:Acme.Checkout"], "github:acme/checkout");
        var otherRepository = _fingerprints.Compute(original, ["package:Acme.Checkout"], "github:acme/orders");

        first.Version.Should().Be(HealingFingerprintService.CurrentVersion);
        first.Value.Should().StartWith("sha256:");
        second.Should().Be(first);
        otherRepository.Value.Should().NotBe(first.Value);
    }

    [Fact]
    public void NormalizerExtractsStableFramesFromDotNetStackTraceWhenStructuredFramesAreAbsent()
    {
        var signal = Signal() with
        {
            Exception = new(
                "System.InvalidOperationException",
                "message",
                "   at Acme.Checkout.OrderHandler.HandleAsync(Order order) in /src/OrderHandler.cs:line 42\n--- End of stack trace from previous location ---\n   at Acme.Api.Controllers.OrderController.Post() in C:\\src\\OrderController.cs:line 15",
                [])
        };

        var result = _normalizer.Normalize(signal);

        result.Succeeded.Should().BeTrue();
        result.Signal!.Frames.Should().Equal(
            new NormalizedHealingFrame(null, "Acme.Checkout.OrderHandler", "HandleAsync"),
            new NormalizedHealingFrame(null, "Acme.Api.Controllers.OrderController", "Post"));
    }

    [Fact]
    public void NormalizerMarksEvidenceWhenItAppliesSafetyBounds()
    {
        var signal = Signal();
        var frames = Enumerable.Range(0, 70)
            .Select(index => new HealingExceptionFrame("Acme.Checkout", $"Acme.Type{index}", "Run", null, null))
            .ToArray();

        var normalized = _normalizer.Normalize(signal with
        {
            Exception = signal.Exception with { Message = new string('x', 5_000), Frames = frames }
        }).Signal!;

        normalized.Source.Exception.Message.Should().HaveLength(4_096);
        normalized.Frames.Should().HaveCount(64);
        normalized.Source.Evidence.IsTruncated.Should().BeTrue();
        normalized.Source.Evidence.OmittedFields.Should().Contain([
            "exception.message:truncated",
            "exception.frames:truncated"
        ]);
    }

    private static HealingSignal Signal() => new(
        HealingContractVersions.SignalProfile,
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        new DateTimeOffset(2026, 7, 16, 12, 30, 0, TimeSpan.FromHours(2)),
        " checkout.order ",
        HealingFailureClasses.UnhandledRequest,
        HealingRetryStates.None,
        new HealingExceptionEvidence(
            " System.InvalidOperationException ",
            "Order 123 failed",
            null,
            [new(" Acme.Checkout ", " Acme.Checkout.OrderHandler ", " HandleAsync ", "/src/OrderHandler.cs", 42)]),
        new HealingEvidenceMetadata(true, false, []),
        SourceRevision: "abc123",
        ComponentManifestDigest: "sha256:manifest",
        Trace: new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb"),
        ServiceName: " checkout-api ",
        ResourceIdentity: " service:checkout:green ",
        Severity: "error");
}
