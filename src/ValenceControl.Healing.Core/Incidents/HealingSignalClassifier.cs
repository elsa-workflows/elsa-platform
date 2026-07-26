using ValenceControl.Healing.Abstractions;

namespace ValenceControl.Healing.Core.Incidents;

public static class HealingSignalClassificationReasonCodes
{
    public const string EligibleFailureClass = "eligible-failure-class";
    public const string ExpectedFailureClass = "expected-failure-class";
    public const string UnknownFailureClass = "unknown-failure-class";
    public const string RetryInProgress = "retry-in-progress";
    public const string AuthorizedOverride = "authorized-classification-override";
    public const string UnauthorizedOverrideIgnored = "unauthorized-classification-override-ignored";
    public const string InvalidOverrideClass = "invalid-classification-override";
}

public sealed record HealingClassificationOverride(string FailureClass, bool IsAuthorized);

public sealed record HealingSignalClassificationResult(
    bool IsEligible,
    IncidentClassification? Classification,
    string FailureClass,
    string ReasonCode,
    bool WasOverridden);

public sealed class HealingSignalClassifier
{
    private static readonly IReadOnlyDictionary<string, IncidentClassification> EligibleClasses =
        new Dictionary<string, IncidentClassification>(StringComparer.Ordinal)
        {
            [HealingFailureClasses.UnhandledRequest] = IncidentClassification.UnhandledRequest,
            [HealingFailureClasses.FatalStartup] = IncidentClassification.FatalStartup,
            [HealingFailureClasses.FatalBackground] = IncidentClassification.FatalBackground,
            [HealingFailureClasses.UnexpectedWorkflow] = IncidentClassification.UnexpectedWorkflow,
            [HealingFailureClasses.UnexpectedActivity] = IncidentClassification.UnexpectedActivity,
            [HealingFailureClasses.TransientExhausted] = IncidentClassification.TransientExhausted,
            [HealingFailureClasses.ExplicitIncident] = IncidentClassification.ExplicitIncident
        };

    private static readonly HashSet<string> ExpectedClasses = new(StringComparer.Ordinal)
    {
        HealingFailureClasses.Validation,
        HealingFailureClasses.Authorization,
        HealingFailureClasses.Cancellation,
        HealingFailureClasses.Handled,
        HealingFailureClasses.TransientRetrying
    };

    public HealingSignalClassificationResult Classify(
        NormalizedHealingSignal signal,
        HealingClassificationOverride? policyOverride = null)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (policyOverride is not null)
        {
            var overrideClass = Normalize(policyOverride.FailureClass);
            if (!policyOverride.IsAuthorized)
            {
                var original = ClassifyDefault(signal);
                return original with { ReasonCode = HealingSignalClassificationReasonCodes.UnauthorizedOverrideIgnored };
            }

            if (!EligibleClasses.TryGetValue(overrideClass, out var overrideClassification))
                return Excluded(overrideClass, HealingSignalClassificationReasonCodes.InvalidOverrideClass, wasOverridden: true);

            return new HealingSignalClassificationResult(
                true,
                overrideClassification,
                overrideClass,
                HealingSignalClassificationReasonCodes.AuthorizedOverride,
                WasOverridden: true);
        }

        return ClassifyDefault(signal);
    }

    private static HealingSignalClassificationResult ClassifyDefault(NormalizedHealingSignal signal)
    {
        if (signal.RetryState == IncidentRetryState.Retrying)
            return Excluded(signal.FailureClass, HealingSignalClassificationReasonCodes.RetryInProgress);

        var failureClass = Normalize(signal.FailureClass);
        if (failureClass == HealingFailureClasses.TransientRetrying && signal.RetryState == IncidentRetryState.Exhausted)
            failureClass = HealingFailureClasses.TransientExhausted;
        if (EligibleClasses.TryGetValue(failureClass, out var classification))
        {
            return new HealingSignalClassificationResult(
                true,
                classification,
                failureClass,
                HealingSignalClassificationReasonCodes.EligibleFailureClass,
                WasOverridden: false);
        }

        return ExpectedClasses.Contains(failureClass)
            ? Excluded(failureClass, HealingSignalClassificationReasonCodes.ExpectedFailureClass)
            : Excluded(failureClass, HealingSignalClassificationReasonCodes.UnknownFailureClass);
    }

    private static HealingSignalClassificationResult Excluded(
        string failureClass,
        string reasonCode,
        bool wasOverridden = false) =>
        new(false, null, failureClass, reasonCode, wasOverridden);

    private static string Normalize(string? failureClass) =>
        string.IsNullOrWhiteSpace(failureClass)
            ? HealingFailureClasses.Unknown
            : failureClass.Trim().ToLowerInvariant();
}
