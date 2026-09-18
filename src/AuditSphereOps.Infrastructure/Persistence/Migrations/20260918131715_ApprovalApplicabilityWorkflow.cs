using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalApplicabilityWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The placeholder approval table has no decision/applicability history.
            // Do not turn existing rows into current approvals by inventing state.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM approvals) THEN
                    RAISE EXCEPTION 'Approval migration requires an explicit disposition for existing approval history.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_safety",
                table: "firm_safety_states");

            migrationBuilder.AddColumn<long>(
                name: "policy_generation",
                table: "firm_safety_states",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AlterColumn<string>(
                name: "target_kind",
                table: "approvals",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "manifest_digest",
                table: "approvals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "decision",
                table: "approvals",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "APPROVED");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_approvals_firm_id_id",
                table: "approvals",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "approval_applicabilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    current_target_revision = table.Column<long>(type: "bigint", nullable: false),
                    current_input_generation = table.Column<long>(type: "bigint", nullable: false),
                    current_policy_generation = table.Column<long>(type: "bigint", nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_applicabilities", x => x.id);
                    table.UniqueConstraint("AK_approval_applicabilities_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_approval_applicability_values", "status IN ('CURRENT','STALE','REJECTED') AND length(reason) > 0 AND current_target_revision >= 1 AND current_input_generation >= 1 AND current_policy_generation >= 1");
                    table.ForeignKey(
                        name: "FK_approval_applicabilities_approvals_firm_id_approval_id",
                        columns: x => new { x.firm_id, x.approval_id },
                        principalTable: "approvals",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_safety",
                table: "firm_safety_states",
                sql: "deployment_epoch >= 1 AND policy_generation >= 1 AND operating_mode IN ('LOCAL_ONLY','RECOVERY_QUARANTINE')");

            migrationBuilder.CreateIndex(
                name: "IX_approvals_firm_id_client_id_engagement_id",
                table: "approvals",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_approvals_firm_id_decided_by_user_id",
                table: "approvals",
                columns: new[] { "firm_id", "decided_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_approval_identity",
                table: "approvals",
                columns: new[] { "firm_id", "target_kind", "target_id", "target_revision", "input_generation", "policy_generation", "manifest_digest", "decided_by_user_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_approval_values",
                table: "approvals",
                sql: "length(target_kind) > 0 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND decision IN ('APPROVED','REJECTED')");

            migrationBuilder.CreateIndex(
                name: "ux_approval_applicability_approval",
                table: "approval_applicabilities",
                columns: new[] { "firm_id", "approval_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_approvals_engagements_firm_id_client_id_engagement_id",
                table: "approvals",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_approvals_practice_clients_firm_id_client_id",
                table: "approvals",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_approvals_users_firm_id_decided_by_user_id",
                table: "approvals",
                columns: new[] { "firm_id", "decided_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_approval_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Approval decisions are immutable historical evidence.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_approvals_append_only
                  BEFORE UPDATE OR DELETE ON approvals
                  FOR EACH ROW EXECUTE FUNCTION prevent_approval_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM approvals) OR EXISTS (SELECT 1 FROM approval_applicabilities) THEN
                    RAISE EXCEPTION 'Approval downgrade would discard historical decisions.';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_approvals_append_only ON approvals;
                DROP FUNCTION IF EXISTS prevent_approval_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_approvals_engagements_firm_id_client_id_engagement_id",
                table: "approvals");

            migrationBuilder.DropForeignKey(
                name: "FK_approvals_practice_clients_firm_id_client_id",
                table: "approvals");

            migrationBuilder.DropForeignKey(
                name: "FK_approvals_users_firm_id_decided_by_user_id",
                table: "approvals");

            migrationBuilder.DropTable(
                name: "approval_applicabilities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_safety",
                table: "firm_safety_states");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_approvals_firm_id_id",
                table: "approvals");

            migrationBuilder.DropIndex(
                name: "IX_approvals_firm_id_client_id_engagement_id",
                table: "approvals");

            migrationBuilder.DropIndex(
                name: "IX_approvals_firm_id_decided_by_user_id",
                table: "approvals");

            migrationBuilder.DropIndex(
                name: "ux_approval_identity",
                table: "approvals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_approval_values",
                table: "approvals");

            migrationBuilder.DropColumn(
                name: "policy_generation",
                table: "firm_safety_states");

            migrationBuilder.DropColumn(
                name: "decision",
                table: "approvals");

            migrationBuilder.AlterColumn<string>(
                name: "target_kind",
                table: "approvals",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "manifest_digest",
                table: "approvals",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_safety",
                table: "firm_safety_states",
                sql: "deployment_epoch >= 1 AND operating_mode IN ('LOCAL_ONLY','RECOVERY_QUARANTINE')");
        }
    }
}
