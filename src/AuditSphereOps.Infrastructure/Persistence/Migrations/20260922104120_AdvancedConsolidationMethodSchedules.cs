using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvancedConsolidationMethodSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "advanced_consolidation_method_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_revision = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    framework = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_manifest_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    source_manifest_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_snapshot_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    input_snapshot_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_advanced_consolidation_method_schedules", x => x.id);
                    table.CheckConstraint("ck_advanced_method_schedule_values", "group_revision >= 1 AND method IN ('FOREIGN_CURRENCY_RESERVE_V1','ACQUISITION_NCI_V1','OWNERSHIP_CHANGE_V1','NESTED_GROUP_V1','ASSET_TRANSFER_ELIMINATION_V1') AND length(trim(framework)) > 0 AND length(trim(source_manifest_json)) > 0 AND source_manifest_digest ~ '^[0-9a-f]{64}$' AND length(trim(input_snapshot_json)) > 0 AND input_snapshot_digest ~ '^[0-9a-f]{64}$' AND status IN ('SUBMITTED','APPROVED')");
                    table.ForeignKey(
                        name: "FK_advanced_consolidation_method_schedules_client_groups_firm_~",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_advanced_consolidation_method_schedules_consolidation_scope~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id },
                        principalTable: "consolidation_scope_versions",
                        principalColumns: new[] { "firm_id", "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_advanced_consolidation_method_schedules_users_firm_id_creat~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_advanced_consolidation_method_schedules_firm_id_created_by_~",
                table: "advanced_consolidation_method_schedules",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_advanced_consolidation_method_schedules_firm_id_group_id_sc~",
                table: "advanced_consolidation_method_schedules",
                columns: new[] { "firm_id", "group_id", "scope_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_advanced_method_schedule_input",
                table: "advanced_consolidation_method_schedules",
                columns: new[] { "firm_id", "scope_version_id", "method", "input_snapshot_digest" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "advanced_consolidation_method_schedules");
        }
    }
}
