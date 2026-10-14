using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseArtifactPin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pinned_artifact_id",
                table: "releases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pinned_artifact_sha256_hex",
                table: "releases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pinned_artifact_version",
                table: "releases",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pinned_artifact_id",
                table: "releases");

            migrationBuilder.DropColumn(
                name: "pinned_artifact_sha256_hex",
                table: "releases");

            migrationBuilder.DropColumn(
                name: "pinned_artifact_version",
                table: "releases");
        }
    }
}
