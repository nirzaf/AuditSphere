using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableForeignOperationTranslation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "applied_rate",
                table: "translation_results",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "translation_results",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_user_id",
                table: "translation_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "translation_results",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateOnly>(
                name: "rate_date",
                table: "translation_results",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rate_type",
                table: "translation_results",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_package_hash",
                table: "translation_results",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "exchange_rate_set_version_id",
                table: "consolidation_scope_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "translation_policy_version_id",
                table: "consolidation_scope_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "translation_rate_date",
                table: "consolidation_scope_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "translation_rate_type",
                table: "consolidation_scope_versions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "source_line_id",
                table: "consolidation_run_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_scope_versions_firm_id_exchange_rate_set_vers~",
                table: "consolidation_scope_versions",
                columns: new[] { "firm_id", "exchange_rate_set_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_scope_versions_firm_id_translation_policy_ver~",
                table: "consolidation_scope_versions",
                columns: new[] { "firm_id", "translation_policy_version_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_consolidation_scope_versions_exchange_rate_set_versions_fir~",
                table: "consolidation_scope_versions",
                columns: new[] { "firm_id", "exchange_rate_set_version_id" },
                principalTable: "exchange_rate_set_versions",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_consolidation_scope_versions_translation_policy_versions_fi~",
                table: "consolidation_scope_versions",
                columns: new[] { "firm_id", "translation_policy_version_id" },
                principalTable: "translation_policy_versions",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_consolidation_scope_versions_exchange_rate_set_versions_fir~",
                table: "consolidation_scope_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_consolidation_scope_versions_translation_policy_versions_fi~",
                table: "consolidation_scope_versions");

            migrationBuilder.DropIndex(
                name: "IX_consolidation_scope_versions_firm_id_exchange_rate_set_vers~",
                table: "consolidation_scope_versions");

            migrationBuilder.DropIndex(
                name: "IX_consolidation_scope_versions_firm_id_translation_policy_ver~",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "applied_rate",
                table: "translation_results");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "translation_results");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                table: "translation_results");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "translation_results");

            migrationBuilder.DropColumn(
                name: "rate_date",
                table: "translation_results");

            migrationBuilder.DropColumn(
                name: "rate_type",
                table: "translation_results");

            migrationBuilder.DropColumn(
                name: "source_package_hash",
                table: "translation_results");

            migrationBuilder.DropColumn(
                name: "exchange_rate_set_version_id",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "translation_policy_version_id",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "translation_rate_date",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "translation_rate_type",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "source_line_id",
                table: "consolidation_run_lines");
        }
    }
}
