using ElsaControl.Deployment.Abstractions;
using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Resources;

namespace ElsaControl.Deployment.Engine.Tests;

public class DeploymentEngineValidationTests
{
    private readonly TestTarget _target = new();
    private readonly RecordingResourceHandler _handler = new();

    [Fact]
    public async Task ValidateAsyncReturnsValidatedForSupportedResources()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var engine = CreateEngine(_handler);

        var result = await engine.ValidateAsync(new TestArtifactReader(resource), _target);

        Assert.Equal(DeploymentStatus.Validated, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(resource, Assert.Single(_handler.ValidatedResources));
    }

    [Fact]
    public async Task ValidateAsyncReportsUnsupportedResourceType()
    {
        var resource = DeploymentEngineTestFixtures.Resource("recipe", "seed");
        var engine = CreateEngine(_handler);

        var result = await engine.ValidateAsync(new TestArtifactReader(resource), _target);

        Assert.Equal(DeploymentStatus.ValidationFailed, result.Status);
        Assert.Single(result.Diagnostics, x =>
            x.Code == DeploymentEngineDiagnosticCodes.HandlerMissing &&
            x.ResourceId == resource.Id);
    }

    [Fact]
    public async Task ValidateAsyncRejectsDuplicateResourceIdentities()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var duplicate = DeploymentEngineTestFixtures.Resource();
        var engine = CreateEngine(_handler);

        var result = await engine.ValidateAsync(new TestArtifactReader(resource, duplicate), _target);

        Assert.Equal(DeploymentStatus.ValidationFailed, result.Status);
        Assert.Single(result.Diagnostics, x =>
            x.Code == DeploymentEngineDiagnosticCodes.ResourceDuplicate &&
            x.ResourceId == resource.Id);
    }

    [Fact]
    public async Task ValidateAsyncAggregatesHandlerDiagnostics()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var diagnostic = new DeploymentDiagnostic("variable.invalid", DeploymentDiagnosticSeverity.Error, "Invalid variable.", resource.Id);
        _handler.ValidationDiagnostics = [diagnostic];
        var engine = CreateEngine(_handler);

        var result = await engine.ValidateAsync(new TestArtifactReader(resource), _target);

        Assert.Equal(DeploymentStatus.ValidationFailed, result.Status);
        Assert.Contains(diagnostic, result.Diagnostics);
        Assert.Empty(_handler.ApplyChanges);
    }

    [Fact]
    public async Task ValidateAsyncReportsValidationExceptionsWithValidationCode()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.ValidationException = new InvalidOperationException("Validation exploded.");
        var engine = CreateEngine(_handler);

        var result = await engine.ValidateAsync(new TestArtifactReader(resource), _target);

        Assert.Equal(DeploymentStatus.ValidationFailed, result.Status);
        Assert.Single(result.Diagnostics, x =>
            x.Code == DeploymentEngineDiagnosticCodes.ValidateFailed &&
            x.ResourceId == resource.Id);
    }

    private static DeploymentEngine CreateEngine(params RecordingResourceHandler[] handlers) =>
        new(handlers, options: DeploymentEngineTestFixtures.StableOptions());
}
