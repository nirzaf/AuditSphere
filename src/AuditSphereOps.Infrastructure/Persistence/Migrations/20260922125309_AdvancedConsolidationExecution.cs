using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvancedConsolidationExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_advanced_consolidation_method_schedules_firm_id_group_id_sc~",
                table: "advanced_consolidation_method_schedules");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_advanced_method_schedules_scope_id",
                table: "advanced_consolidation_method_schedules",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "id" });

            migrationBuilder.CreateTable(
                name: "advanced_consolidation_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_revision = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    framework = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    engine_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_manifest_json = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    input_manifest_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    comparative_statement_json = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    comparative_statement_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    current_statement_json = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    current_statement_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    output_manifest = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    output_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    comparative_signed_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    current_signed_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_advanced_consolidation_executions", x => x.id);
                    table.CheckConstraint("ck_advanced_consolidation_execution_values", "group_revision >= 1 AND method IN ('FOREIGN_CURRENCY_RESERVE_V1','ACQUISITION_NCI_V1','OWNERSHIP_CHANGE_V1','NESTED_GROUP_V1','ASSET_TRANSFER_ELIMINATION_V1') AND length(trim(framework)) > 0 AND length(trim(engine_version)) > 0 AND length(trim(output_manifest)) > 0 AND input_manifest_digest ~ '^[0-9a-f]{64}$' AND comparative_statement_digest ~ '^[0-9a-f]{64}$' AND current_statement_digest ~ '^[0-9a-f]{64}$' AND output_digest ~ '^[0-9a-f]{64}$' AND status IN ('VERIFIED','APPROVED')");
                    table.ForeignKey(
                        name: "FK_advanced_consolidation_executions_advanced_consolidation_me~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.schedule_id },
                        principalTable: "advanced_consolidation_method_schedules",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_advanced_consolidation_executions_client_groups_firm_id_gro~",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_advanced_consolidation_executions_consolidation_scope_versi~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id },
                        principalTable: "consolidation_scope_versions",
                        principalColumns: new[] { "firm_id", "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_advanced_consolidation_executions_users_firm_id_created_by_~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_advanced_consolidation_executions_firm_id_created_by_user_id",
                table: "advanced_consolidation_executions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_advanced_consolidation_executions_firm_id_group_id_scope_ve~",
                table: "advanced_consolidation_executions",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "schedule_id" });

            migrationBuilder.CreateIndex(
                name: "ux_advanced_consolidation_execution",
                table: "advanced_consolidation_executions",
                columns: new[] { "firm_id", "scope_version_id", "method", "input_manifest_digest" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "advanced_consolidation_executions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_advanced_method_schedules_scope_id",
                table: "advanced_consolidation_method_schedules");

            migrationBuilder.CreateIndex(
                name: "IX_advanced_consolidation_method_schedules_firm_id_group_id_sc~",
                table: "advanced_consolidation_method_schedules",
                columns: new[] { "firm_id", "group_id", "scope_version_id" });
        }
    }
}
