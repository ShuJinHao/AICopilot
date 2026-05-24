using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    /// <inheritdoc />
    public partial class AddPilotAuthorizationHardeningM21 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "business_scope",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "credential_owner",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "department",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "execution_window_end",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "execution_window_start",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pilot_owner",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "post_run_audit_archive_format",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rollback_window_end",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rollback_window_start",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "secret_reference_name_hash",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "secret_storage_mode",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signed_approval_ref",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pilot_authorization_submissions_expires_at",
                schema: "aigateway",
                table: "pilot_authorization_submissions",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pilot_authorization_submissions_expires_at",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "business_scope",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "credential_owner",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "department",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "execution_window_end",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "execution_window_start",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "pilot_owner",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "post_run_audit_archive_format",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "rollback_window_end",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "rollback_window_start",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "secret_reference_name_hash",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "secret_storage_mode",
                schema: "aigateway",
                table: "pilot_authorization_submissions");

            migrationBuilder.DropColumn(
                name: "signed_approval_ref",
                schema: "aigateway",
                table: "pilot_authorization_submissions");
        }
    }
}
