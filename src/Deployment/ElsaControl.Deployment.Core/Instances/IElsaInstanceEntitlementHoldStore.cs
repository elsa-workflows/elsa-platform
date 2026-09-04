namespace ElsaControl.Deployment.Core.Instances;

/// <summary>Durable boundary for holding a queued provider mutation after entitlement loss.</summary>
public interface IElsaInstanceEntitlementHoldStore
{
    /// <summary>
    /// Performs the durable authorization CAS immediately before provider submission.
    /// Queued work is moved to <c>EntitlementHeld</c> when denied; held work is
    /// returned to <c>Queued</c> when entitlement is restored. The transaction is
    /// the authorization linearization point.
    /// </summary>
    Task<ElsaInstanceCommercialGateDecision> AuthorizeProviderSubmissionAsync(
        Guid workspaceId,
        Guid instanceId,
        Guid operationId,
        DateTimeOffset authorizedAt,
        CancellationToken cancellationToken = default);

}
