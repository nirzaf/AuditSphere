using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveManifestRequestIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "build_request_key",
                table: "archive_manifests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "commercial_bundle_id",
                table: "archive_manifests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_request_key",
                table: "archive_manifests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_archive_manifest_build_request",
                table: "archive_manifests",
                columns: new[] { "firm_id", "archive_id", "build_request_key" },
                unique: true,
                filter: "build_request_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_archive_manifest_review_request",
                table: "archive_manifests",
                columns: new[] { "firm_id", "archive_id", "review_request_key" },
                unique: true,
                filter: "review_request_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_archive_manifest_build_request",
                table: "archive_manifests");

            migrationBuilder.DropIndex(
                name: "ux_archive_manifest_review_request",
                table: "archive_manifests");

            migrationBuilder.DropColumn(
                name: "build_request_key",
                table: "archive_manifests");

            migrationBuilder.DropColumn(
                name: "commercial_bundle_id",
                table: "archive_manifests");

            migrationBuilder.DropColumn(
                name: "review_request_key",
                table: "archive_manifests");
        }
    }
}
