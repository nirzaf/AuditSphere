using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecoverySessionQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_safety",
                table: "firm_safety_states");

            migrationBuilder.AddColumn<long>(
                name: "recovery_epoch",
                table: "firm_safety_states",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "recovery_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    restore_point = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    external_epoch = table.Column<long>(type: "bigint", nullable: false),
                    reconciliation_scope = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    findings = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_restart_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_sessions", x => x.id);
                    table.UniqueConstraint("AK_recovery_sessions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_recovery_session_values", "external_epoch >= 1 AND length(trim(restore_point)) > 0 AND length(trim(reconciliation_scope)) > 0 AND length(trim(findings)) > 0 AND ((approved_restart_at IS NULL AND approved_by_user_id IS NULL) OR (approved_restart_at IS NOT NULL AND approved_by_user_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_recovery_sessions_firm_safety_states_firm_id",
                        column: x => x.firm_id,
                        principalTable: "firm_safety_states",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recovery_sessions_users_firm_id_approved_by_user_id",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_safety",
                table: "firm_safety_states",
                sql: "deployment_epoch >= 1 AND recovery_epoch >= 0 AND policy_generation >= 1 AND operating_mode IN ('LOCAL_ONLY','RECOVERY_QUARANTINE')");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_sessions_firm_id_approved_by_user_id",
                table: "recovery_sessions",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_sessions_firm_id_created_at",
                table: "recovery_sessions",
                columns: new[] { "firm_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recovery_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_safety",
                table: "firm_safety_states");

            migrationBuilder.DropColumn(
                name: "recovery_epoch",
                table: "firm_safety_states");

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_safety",
                table: "firm_safety_states",
                sql: "deployment_epoch >= 1 AND policy_generation >= 1 AND operating_mode IN ('LOCAL_ONLY','RECOVERY_QUARANTINE')");
        }
    }
}
