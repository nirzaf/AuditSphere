using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportingTaxonomyOverlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reporting_taxonomy_values",
                table: "reporting_taxonomy_versions");

            migrationBuilder.AddColumn<Guid>(
                name: "base_taxonomy_version_id",
                table: "reporting_taxonomy_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "overlay_scope",
                table: "reporting_taxonomy_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "BASE");

            migrationBuilder.CreateIndex(
                name: "IX_reporting_taxonomy_versions_firm_id_base_taxonomy_version_id",
                table: "reporting_taxonomy_versions",
                columns: new[] { "firm_id", "base_taxonomy_version_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_reporting_taxonomy_values",
                table: "reporting_taxonomy_versions",
                sql: "length(trim(code)) > 0 AND length(trim(framework)) > 0 AND length(trim(name)) > 0 AND (overlay_scope = 'BASE' OR overlay_scope LIKE 'INDUSTRY:%' OR overlay_scope LIKE 'GROUP:%') AND (effective_to IS NULL OR effective_from <= effective_to)");

            migrationBuilder.AddForeignKey(
                name: "FK_reporting_taxonomy_versions_reporting_taxonomy_versions_fir~",
                table: "reporting_taxonomy_versions",
                columns: new[] { "firm_id", "base_taxonomy_version_id" },
                principalTable: "reporting_taxonomy_versions",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reporting_taxonomy_versions_reporting_taxonomy_versions_fir~",
                table: "reporting_taxonomy_versions");

            migrationBuilder.DropIndex(
                name: "IX_reporting_taxonomy_versions_firm_id_base_taxonomy_version_id",
                table: "reporting_taxonomy_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reporting_taxonomy_values",
                table: "reporting_taxonomy_versions");

            migrationBuilder.DropColumn(
                name: "base_taxonomy_version_id",
                table: "reporting_taxonomy_versions");

            migrationBuilder.DropColumn(
                name: "overlay_scope",
                table: "reporting_taxonomy_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reporting_taxonomy_values",
                table: "reporting_taxonomy_versions",
                sql: "length(trim(code)) > 0 AND length(trim(framework)) > 0 AND length(trim(name)) > 0 AND (effective_to IS NULL OR effective_from <= effective_to)");
        }
    }
}
