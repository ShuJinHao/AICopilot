using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext;

[DbContext(typeof(AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
[Migration("20260722170000_AddArtifactEvidenceSetDigest")]
public partial class AddArtifactEvidenceSetDigest : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "evidence_set_digest",
            schema: "aigateway",
            table: "artifacts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "evidence_set_digest",
            schema: "aigateway",
            table: "artifacts");
    }
}
