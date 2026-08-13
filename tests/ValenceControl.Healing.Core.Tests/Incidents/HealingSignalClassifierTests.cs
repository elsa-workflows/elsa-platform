using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core.Incidents;

namespace ValenceControl.Healing.Core.Tests.Incidents;

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

        Assert.True(first.Succeeded);
        var normalized = first.Signal!;
        Assert.Equal(TimeSpan.Zero, normalized.OccurredAt.Offset);
        Assert.Equal("checkout-api", normalized.ServiceName);
        Assert.Equal(new NormalizedHealingFrame("Acme.Checkout", "Acme.Checkout.OrderHandler", "HandleAsync"), Assert.Single(normalized.Frames));
        Assert.Equal(normalized.OccurrenceKey, retry.Signal!.OccurrenceKey);
        Assert.NotEqual(normalized.OccurrenceKey, otherResource.Signal!.OccurrenceKey);
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

        Assert.Equal(first.OccurrenceKey, replay.OccurrenceKey);
        Assert.NotEqual(first.OccurrenceKey, otherApplication.OccurrenceKey);
    }

    [Theory]
    [InlineData("2.0", HealingSignalNormalizationReasonCodes.UnsupportedProfileVersion)]
    [InlineData("1", HealingSignalNormalizationReasonCodes.UnsupportedProfileVersion)]
    [InlineData("1.0", HealingSignalNormalizationReasonCodes.ServiceNameRequired, "")]
    public void NormalizeRejectsNonConformingSignals(string version, string expectedReason, string? serviceName = "checkout-api")
    {
        var result = _normalizer.Normalize(Signal() with { ProfileVersion = version, ServiceName = serviceName });

        Assert.False(result.Succeeded);
        Assert.Null(result.Signal);
        Assert.Contains(expectedReason, result.ReasonCodes);
    }

    [Fact]
    public void NormalizeRejectsMissingEvidenceWithoutThrowing()
    {
        var result = _normalizer.Normalize(Signal() with { Evidence = null! });

        Assert.False(result.Succeeded);
        Assert.Contains(HealingSignalNormalizationReasonCodes.EvidenceRequired, result.ReasonCodes);
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

        Assert.True(result.IsEligible);
        Assert.Equal(classification, result.Classification);
        Assert.Equal(HealingSignalClassificationReasonCodes.EligibleFailureClass, result.ReasonCode);
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

        Assert.False(result.IsEligible);
        Assert.Null(result.Classification);
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

        Assert.False(result.IsEligible);
        Assert.Equal(HealingSignalClassificationReasonCodes.RetryInProgress, result.ReasonCode);
    }

    [Fact]
    public void OnlyAuthorizedPolicyCanRemapAnExcludedFailure()
    {
        var normalized = _normalizer.Normalize(Signal() with { FailureClass = HealingFailureClasses.Validation }).Signal!;

        var unauthorized = _classifier.Classify(normalized,
            new HealingClassificationOverride(HealingFailureClasses.UnhandledRequest, IsAuthorized: false));
        var authorized = _classifier.Classify(normalized,
            new HealingClassificationOverride(HealingFailureClasses.UnhandledRequest, IsAuthorized: true));

        Assert.False(unauthorized.IsEligible);
        Assert.Equal(HealingSignalClassificationReasonCodes.UnauthorizedOverrideIgnored, unauthorized.ReasonCode);
        Assert.True(authorized.IsEligible);
        Assert.Equal(IncidentClassification.UnhandledRequest, authorized.Classification);
        Assert.Equal(HealingSignalClassificationReasonCodes.AuthorizedOverride, authorized.ReasonCode);
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

        Assert.Equal(HealingFingerprintService.CurrentVersion, first.Version);
        Assert.StartsWith("sha256:", first.Value);
        Assert.Equal(first, second);
        Assert.NotEqual(first.Value, otherRepository.Value);
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

        Assert.True(result.Succeeded);
        Assert.Equal(
            [new NormalizedHealingFrame(null, "Acme.Checkout.OrderHandler", "HandleAsync"), new NormalizedHealingFrame(null, "Acme.Api.Controllers.OrderController", "Post")],
            result.Signal!.Frames);
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

        Assert.Equal(4_096, Assert.IsType<string>(normalized.Source.Exception.Message).Length);
        Assert.Equal(64, normalized.Frames.Count());
        Assert.True(normalized.Source.Evidence.IsTruncated);
        Assert.All(["exception.message:truncated", "exception.frames:truncated"], value => Assert.Contains(value, normalized.Source.Evidence.OmittedFields));
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
