using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EqrDecisionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_eqr_case_values",
                table: "eqr_cases");

            migrationBuilder.AddColumn<Guid>(
                name: "current_decision_id",
                table: "eqr_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "current_decision_revision",
                table: "eqr_cases",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "eqr_cases",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "eqr_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    eqr_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    findings_discussed = table.Column<bool>(type: "boolean", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_facts_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reviewed_report_deliverable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_report_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    request_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    previous_decision_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eqr_decisions", x => x.id);
                    table.CheckConstraint("ck_eqr_decision_values", "revision > 0 AND status IN ('IN_PROGRESS','CONCURRED','CHANGES_REQUESTED') AND length(trim(request_key)) > 0 AND reviewed_facts_digest ~ '^[0-9a-f]{64}$' AND (reviewed_report_sha256 IS NULL OR reviewed_report_sha256 ~ '^[0-9a-f]{64}$') AND ((status = 'CONCURRED' AND findings_discussed) OR status <> 'CONCURRED') AND ((status = 'CHANGES_REQUESTED' AND notes IS NOT NULL AND length(trim(notes)) > 0) OR status <> 'CHANGES_REQUESTED')");
                    table.ForeignKey(
                        name: "FK_eqr_decisions_engagements_firm_id_client_id_engagement_id",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_eqr_decisions_eqr_cases_eqr_case_id",
                        column: x => x.eqr_case_id,
                        principalTable: "eqr_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_eqr_decisions_users_firm_id_decided_by_user_id",
                        columns: x => new { x.firm_id, x.decided_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_eqr_case_values",
                table: "eqr_cases",
                sql: "status IN ('PENDING','IN_PROGRESS','CONCURRED','CHANGES_REQUESTED') AND revision >= 0 AND current_decision_revision >= 0 AND (current_decision_id IS NULL OR current_decision_revision = revision) AND ((status = 'CONCURRED' AND concurrence_date IS NOT NULL AND completed_at IS NOT NULL)   OR status <> 'CONCURRED')");

            migrationBuilder.CreateIndex(
                name: "ix_eqr_decision_engagement_decided",
                table: "eqr_decisions",
                columns: new[] { "firm_id", "engagement_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "IX_eqr_decisions_eqr_case_id",
                table: "eqr_decisions",
                column: "eqr_case_id");

            migrationBuilder.CreateIndex(
                name: "IX_eqr_decisions_firm_id_client_id_engagement_id",
                table: "eqr_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_eqr_decisions_firm_id_decided_by_user_id",
                table: "eqr_decisions",
                columns: new[] { "firm_id", "decided_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_eqr_decision_request",
                table: "eqr_decisions",
                columns: new[] { "firm_id", "engagement_id", "request_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_eqr_decision_revision",
                table: "eqr_decisions",
                columns: new[] { "firm_id", "eqr_case_id", "revision" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION guard_eqr_decision_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'EQR decisions are append-only.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_eqr_decisions_append_only
                  BEFORE UPDATE OR DELETE ON eqr_decisions
                  FOR EACH ROW EXECUTE FUNCTION guard_eqr_decision_mutation();
                CREATE FUNCTION reject_eqr_decision_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'EQR decision history cannot be truncated.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_eqr_decisions_no_truncate
                  BEFORE TRUNCATE ON eqr_decisions
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_eqr_decision_truncate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE eqr_decisions IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM eqr_decisions) THEN
                    RAISE EXCEPTION 'Cannot remove retained EQR decision history.' USING ERRCODE = '55000';
                  END IF;
                END;
                $$;
                DROP TRIGGER IF EXISTS trg_eqr_decisions_no_truncate ON eqr_decisions;
                DROP TRIGGER IF EXISTS trg_eqr_decisions_append_only ON eqr_decisions;
                DROP FUNCTION IF EXISTS reject_eqr_decision_truncate();
                DROP FUNCTION IF EXISTS guard_eqr_decision_mutation();
                """);
            migrationBuilder.DropTable(
                name: "eqr_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_eqr_case_values",
                table: "eqr_cases");

            migrationBuilder.DropColumn(
                name: "current_decision_id",
                table: "eqr_cases");

            migrationBuilder.DropColumn(
                name: "current_decision_revision",
                table: "eqr_cases");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "eqr_cases");

            migrationBuilder.AddCheckConstraint(
                name: "ck_eqr_case_values",
                table: "eqr_cases",
                sql: "status IN ('PENDING','IN_PROGRESS','CONCURRED','CHANGES_REQUESTED') AND ((status = 'CONCURRED' AND concurrence_date IS NOT NULL AND completed_at IS NOT NULL)   OR status <> 'CONCURRED')");
        }
    }
}
