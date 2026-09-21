using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsolidationRollForwardLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "opening_run_hash",
                table: "consolidation_scope_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "opening_translation_manifest_hash",
                table: "consolidation_scope_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "opening_translation_reserve",
                table: "consolidation_scope_versions",
                type: "numeric(19,6)",
                precision: 20,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "prior_scope_version_id",
                table: "consolidation_scope_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recurring_elimination_manifest",
                table: "consolidation_scope_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "opening_run_hash",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "opening_translation_manifest_hash",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "opening_translation_reserve",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "prior_scope_version_id",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "recurring_elimination_manifest",
                table: "consolidation_scope_versions");
        }
    }
}
