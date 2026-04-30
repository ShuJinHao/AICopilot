using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class Phase44OnsiteAttestationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "onsite_confirmation_expires_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "onsite_confirmed_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "onsite_confirmed_by",
                table: "sessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_onsite_attestation",
                table: "approval_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "onsite_confirmation_expires_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "onsite_confirmed_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "onsite_confirmed_by",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "requires_onsite_attestation",
                table: "approval_policies");
        }
    }
}
