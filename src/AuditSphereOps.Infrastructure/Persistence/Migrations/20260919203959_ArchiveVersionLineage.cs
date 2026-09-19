using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveVersionLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "predecessor_manifest_id",
                table: "archive_manifests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by_manifest_id",
                table: "archive_manifests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_archive_manifests_firm_id_predecessor_manifest_id",
                table: "archive_manifests",
                columns: new[] { "firm_id", "predecessor_manifest_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_manifests_firm_id_superseded_by_manifest_id",
                table: "archive_manifests",
                columns: new[] { "firm_id", "superseded_by_manifest_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_manifests_lineage_no_self_ref",
                table: "archive_manifests",
                sql: "(predecessor_manifest_id IS NULL OR predecessor_manifest_id <> id) AND (superseded_by_manifest_id IS NULL OR superseded_by_manifest_id <> id)");

            migrationBuilder.AddForeignKey(
                name: "FK_archive_manifests_archive_manifests_firm_id_predecessor_man~",
                table: "archive_manifests",
                columns: new[] { "firm_id", "predecessor_manifest_id" },
                principalTable: "archive_manifests",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_archive_manifests_archive_manifests_firm_id_superseded_by_m~",
                table: "archive_manifests",
                columns: new[] { "firm_id", "superseded_by_manifest_id" },
                principalTable: "archive_manifests",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Evidence-safe downgrade: refuse if any version lineage exists.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM archive_manifests WHERE predecessor_manifest_id IS NOT NULL OR superseded_by_manifest_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'Archive version lineage downgrade would discard immutable version lineage history.' USING ERRCODE = '55000';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_archive_manifests_archive_manifests_firm_id_predecessor_man~",
                table: "archive_manifests");

            migrationBuilder.DropForeignKey(
                name: "FK_archive_manifests_archive_manifests_firm_id_superseded_by_m~",
                table: "archive_manifests");

            migrationBuilder.DropIndex(
                name: "IX_archive_manifests_firm_id_predecessor_manifest_id",
                table: "archive_manifests");

            migrationBuilder.DropIndex(
                name: "IX_archive_manifests_firm_id_superseded_by_manifest_id",
                table: "archive_manifests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_manifests_lineage_no_self_ref",
                table: "archive_manifests");

            migrationBuilder.DropColumn(
                name: "predecessor_manifest_id",
                table: "archive_manifests");

            migrationBuilder.DropColumn(
                name: "superseded_by_manifest_id",
                table: "archive_manifests");
        }
    }
}
