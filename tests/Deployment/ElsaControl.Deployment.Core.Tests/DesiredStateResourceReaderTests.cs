using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Abstractions.Resources;
using ElsaControl.Deployment.Core.Workspace;
using Xunit;

namespace ElsaControl.Deployment.Core.Tests;

public sealed class DesiredStateResourceReaderTests
{
    [Fact]
    public void Reads_supported_records_into_typed_resources_without_reparsing_payloads()
    {
        var resources = DesiredStateResourceReader.Read("""
            {"records":[
              {"kind":"Workflow","name":"Payment Retry","payload":{"version":8}},
              {"kind":"SecretReference","name":"Payment API","payload":{"reference":"kv://payments/api"}},
              {"kind":"Unknown","name":"Ignored","payload":{}}
            ]}
            """);

        Assert.Equal(3, resources.Count);
        var workflow = Assert.Single(resources, x => x.Name == "Payment Retry");
        Assert.Equal("Workflow", workflow.Kind);
        Assert.Equal(DesiredStateRecordKind.Workflow, workflow.KnownKind);
        Assert.Equal(new DeploymentResourceId("Workflow", "Payment Retry"), workflow.Resource.Id);
        Assert.Equal("{\"version\":8}", workflow.Payload.GetRawText());
        Assert.Equal(new ArtifactDigest("sha256", "9e7feab6660f23cb63046272f63f9bf3dd2a83f92d5af0962878464a8c038783"), workflow.Resource.DesiredStateHash);
        var unknown = Assert.Single(resources, x => x.Name == "Ignored");
        Assert.Null(unknown.KnownKind);
        Assert.Equal(new DeploymentResourceId("Unknown", "Ignored"), unknown.Resource.Id);
    }

    [Fact]
    public void Treats_json_object_property_order_and_whitespace_as_the_same_resource_state()
    {
        var source = Assert.Single(DesiredStateResourceReader.Read("""
            {"records":[{"kind":"Workflow","name":"Payment Retry","payload":{"version":8,"enabled":true}}]}
            """));
        var target = Assert.Single(DesiredStateResourceReader.Read("""
            { "records": [ { "name": "Payment Retry", "payload": { "enabled": true, "version": 8 }, "kind": "Workflow" } ] }
            """));

        Assert.Equal(source.Resource.DesiredStateHash, target.Resource.DesiredStateHash);
    }

    [Fact]
    public void Supports_the_legacy_root_array_shape()
    {
        var resources = DesiredStateResourceReader.Read("""
            [{"kind":"Feature","name":"Retries","payload":{"enabled":true}}]
            """);

        var resource = Assert.Single(resources);
        Assert.Equal(new DeploymentResourceId("Feature", "Retries"), resource.Resource.Id);
    }

    [Fact]
    public void Preserves_legacy_empty_results_for_malformed_or_non_array_input()
    {
        Assert.Empty(DesiredStateResourceReader.Read("not json"));
        Assert.Empty(DesiredStateResourceReader.Read("{\"records\":{}}"));
        Assert.Empty(DesiredStateResourceReader.Read("{\"records\":[{\"kind\":\"Workflow\"}]}"));
    }

    [Fact]
    public void Reads_artifact_reference_records()
    {
        var resources = DesiredStateResourceReader.Read("""
            {"records":[{"kind":"ArtifactReference","name":"Artifact","payload":{"artifactId":"artifact-1"}}]}
            """);

        Assert.Single(resources);
    }
}
