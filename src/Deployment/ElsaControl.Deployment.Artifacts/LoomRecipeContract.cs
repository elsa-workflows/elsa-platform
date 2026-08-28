namespace ElsaControl.Deployment.Artifacts;

/// <summary>
/// The shared wire contract for <see cref="ArtifactTypeIds.ElsaLoomRecipe"/> artifact payloads.
/// The Studio producer serializes recipes against these names/values and the runtime applier
/// deserializes against the same constants, so the two sides cannot drift silently.
/// </summary>
public static class LoomRecipeContract
{
    /// <summary>The only recipe body schema version this contract describes.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Step type that upserts (and optionally publishes) a single workflow definition.</summary>
    public const string WorkflowDefinitionUpsertStep = "workflowDefinition.upsert";

    // Recipe body property names.
    public const string SchemaVersionProperty = "schemaVersion";
    public const string StepsProperty = "steps";

    // Step property names.
    public const string StepTypeProperty = "type";
    public const string StepPayloadProperty = "payload";
}
