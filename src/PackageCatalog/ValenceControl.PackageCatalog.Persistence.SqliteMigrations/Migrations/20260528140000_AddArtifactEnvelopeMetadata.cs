using ValenceControl.Deployment.Artifacts;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValenceControl.PackageCatalog.Persistence.SqliteMigrations.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260528140000_AddArtifactEnvelopeMetadata")]
    public partial class AddArtifactEnvelopeMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnvelopeVersion",
                table: "WorkspaceDeploymentArtifacts",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: ArtifactEnvelopeConstants.EnvelopeVersion);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactTypeId",
                table: "WorkspaceDeploymentArtifacts",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: ArtifactTypeIds.ElsaWorkflowDefinition);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactSchemaVersion",
                table: "WorkspaceDeploymentArtifacts",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion);

            migrationBuilder.AddColumn<string>(
                name: "ManifestDigestAlgorithm",
                table: "WorkspaceDeploymentArtifacts",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManifestDigest",
                table: "WorkspaceDeploymentArtifacts",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadReferenceJson",
                table: "WorkspaceDeploymentArtifacts",
                type: "TEXT",
                nullable: false,
                defaultValue: "{\"Provider\":\"local\",\"Uri\":\"legacy\"}");

            migrationBuilder.AddColumn<string>(
                name: "ProducerJson",
                table: "WorkspaceDeploymentArtifacts",
                type: "TEXT",
                nullable: false,
                defaultValue: "{\"ProducerType\":\"manual\",\"ProducerName\":\"Manual registration\",\"ProducerVersion\":null,\"SourceReference\":null}");

            migrationBuilder.AddColumn<string>(
                name: "DisplayMetadataJson",
                table: "WorkspaceDeploymentArtifacts",
                type: "TEXT",
                nullable: false,
                defaultValue: "{\"Name\":null,\"Version\":null,\"Description\":null,\"Labels\":{},\"Annotations\":{},\"Source\":null}");

            migrationBuilder.AddColumn<string>(
                name: "CompatibilityHintsJson",
                table: "WorkspaceDeploymentArtifacts",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EnvelopeVersion", table: "WorkspaceDeploymentArtifacts");
            migrationBuilder.DropColumn(name: "ArtifactTypeId", table: "WorkspaceDeploymentArtifacts");
            migrationBuilder.DropColumn(name: "ArtifactSchemaVersion", table: "WorkspaceDeploymentArtifacts");
            migrationBuilder.DropColumn(name: "ManifestDigestAlgorithm", table: "WorkspaceDeploymentArtifacts");
            migrationBuilder.DropColumn(name: "ManifestDigest", table: "WorkspaceDeploymentArtifacts");
            migrationBuilder.DropColumn(name: "PayloadReferenceJson", table: "WorkspaceDeploymentArtifacts");
            migrationBuilder.DropColumn(name: "ProducerJson", table: "WorkspaceDeploymentArtifacts");
            migrationBuilder.DropColumn(name: "DisplayMetadataJson", table: "WorkspaceDeploymentArtifacts");
            migrationBuilder.DropColumn(name: "CompatibilityHintsJson", table: "WorkspaceDeploymentArtifacts");
        }
    }
}
