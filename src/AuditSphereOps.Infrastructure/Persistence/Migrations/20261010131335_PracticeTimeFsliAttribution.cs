using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PracticeTimeFsliAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_work_tasks_firm_id_client_id_engagement_id",
                table: "work_tasks");

            migrationBuilder.DropIndex(
                name: "IX_time_entries_firm_id_client_id_engagement_id",
                table: "time_entries");

            migrationBuilder.AddColumn<string>(
                name: "fsli_code",
                table: "work_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "mapping_version_id",
                table: "work_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fsli_code",
                table: "time_entries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "mapping_version_id",
                table: "time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_firm_id_client_id_engagement_id_mapping_version_~",
                table: "work_tasks",
                columns: new[] { "firm_id", "client_id", "engagement_id", "mapping_version_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_task_fsli_snapshot",
                table: "work_tasks",
                sql: "(mapping_version_id IS NULL AND fsli_code IS NULL) OR (mapping_version_id IS NOT NULL AND client_id IS NOT NULL AND engagement_id IS NOT NULL AND fsli_code IS NOT NULL AND length(trim(fsli_code)) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_firm_id_client_id_engagement_id_mapping_versio~",
                table: "time_entries",
                columns: new[] { "firm_id", "client_id", "engagement_id", "mapping_version_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_time_entry_fsli_snapshot",
                table: "time_entries",
                sql: "(mapping_version_id IS NULL AND fsli_code IS NULL) OR (mapping_version_id IS NOT NULL AND client_id IS NOT NULL AND engagement_id IS NOT NULL AND fsli_code IS NOT NULL AND length(trim(fsli_code)) > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_time_entries_mapping_versions_firm_id_client_id_engagement_~",
                table: "time_entries",
                columns: new[] { "firm_id", "client_id", "engagement_id", "mapping_version_id" },
                principalTable: "mapping_versions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_work_tasks_mapping_versions_firm_id_client_id_engagement_id~",
                table: "work_tasks",
                columns: new[] { "firm_id", "client_id", "engagement_id", "mapping_version_id" },
                principalTable: "mapping_versions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_time_entries_mapping_versions_firm_id_client_id_engagement_~",
                table: "time_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_work_tasks_mapping_versions_firm_id_client_id_engagement_id~",
                table: "work_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_tasks_firm_id_client_id_engagement_id_mapping_version_~",
                table: "work_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_task_fsli_snapshot",
                table: "work_tasks");

            migrationBuilder.DropIndex(
                name: "IX_time_entries_firm_id_client_id_engagement_id_mapping_versio~",
                table: "time_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_time_entry_fsli_snapshot",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "fsli_code",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "mapping_version_id",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "fsli_code",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "mapping_version_id",
                table: "time_entries");

            migrationBuilder.CreateIndex(
                name: "IX_work_tasks_firm_id_client_id_engagement_id",
                table: "work_tasks",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_firm_id_client_id_engagement_id",
                table: "time_entries",
                columns: new[] { "firm_id", "client_id", "engagement_id" });
        }
    }
}
