using ValenceControl.Deployment.Abstractions;
using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.Resources;
using FluentAssertions;

namespace ValenceControl.Deployment.Engine.Tests;

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

        result.Status.Should().Be(DeploymentStatus.Validated);
        result.Diagnostics.Should().BeEmpty();
        _handler.ValidatedResources.Should().ContainSingle().Which.Should().Be(resource);
    }

    [Fact]
    public async Task ValidateAsyncReportsUnsupportedResourceType()
    {
        var resource = DeploymentEngineTestFixtures.Resource("recipe", "seed");
        var engine = CreateEngine(_handler);

        var result = await engine.ValidateAsync(new TestArtifactReader(resource), _target);

        result.Status.Should().Be(DeploymentStatus.ValidationFailed);
        result.Diagnostics.Should().ContainSingle(x =>
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

        result.Status.Should().Be(DeploymentStatus.ValidationFailed);
        result.Diagnostics.Should().ContainSingle(x =>
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

        result.Status.Should().Be(DeploymentStatus.ValidationFailed);
        result.Diagnostics.Should().Contain(diagnostic);
        _handler.ApplyChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsyncReportsValidationExceptionsWithValidationCode()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.ValidationException = new InvalidOperationException("Validation exploded.");
        var engine = CreateEngine(_handler);

        var result = await engine.ValidateAsync(new TestArtifactReader(resource), _target);

        result.Status.Should().Be(DeploymentStatus.ValidationFailed);
        result.Diagnostics.Should().ContainSingle(x =>
            x.Code == DeploymentEngineDiagnosticCodes.ValidateFailed &&
            x.ResourceId == resource.Id);
    }

    private static DeploymentEngine CreateEngine(params RecordingResourceHandler[] handlers) =>
        new(handlers, options: DeploymentEngineTestFixtures.StableOptions());
}
