using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditCompletionGoingConcernCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_going_concern_assessments_firm_id_client_id_engagement_id",
                table: "going_concern_assessments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_going_concern_assessments_firm_id_client_id_engagement_id_id",
                table: "going_concern_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "audit_completion_going_concern_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    going_concern_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    input_json = table.Column<string>(type: "character varying(120000)", maxLength: 120000, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_completion_going_concern_command_receipts", x => x.id);
                    table.CheckConstraint("ck_audit_completion_going_concern_command_receipt_values", "actor_epoch >= 0 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND going_concern_assessment_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND action IN ('PREPARE','REVIEW') AND length(input_json) BETWEEN 1 AND 120000 AND length(preview_json) BETWEEN 1 AND 1000000 AND length(result_json) BETWEEN 1 AND 1000000 AND jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object' AND jsonb_typeof(result_json::jsonb) = 'object'");
                    table.ForeignKey(
                        name: "FK_audit_completion_going_concern_command_receipts_engagements~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_completion_going_concern_command_receipts_going_conce~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.going_concern_assessment_id },
                        principalTable: "going_concern_assessments",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_completion_going_concern_command_receipts_users_firm_~",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_completion_going_concern_command_receipts_firm_id_cli~",
                table: "audit_completion_going_concern_command_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "going_concern_assessment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_completion_going_concern_receipt_engagement_history",
                table: "audit_completion_going_concern_command_receipts",
                columns: new[] { "firm_id", "engagement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_completion_going_concern_receipt_actor_request",
                table: "audit_completion_going_concern_command_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION reject_audit_completion_gc_receipt_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$ BEGIN
                  RAISE EXCEPTION 'Going-concern command receipts are append-only.' USING ERRCODE = '55000';
                END $$;
                CREATE TRIGGER trg_audit_completion_gc_receipts_immutable
                  BEFORE UPDATE OR DELETE ON audit_completion_going_concern_command_receipts
                  FOR EACH ROW EXECUTE FUNCTION reject_audit_completion_gc_receipt_mutation();
                CREATE TRIGGER trg_audit_completion_gc_receipts_no_truncate
                  BEFORE TRUNCATE ON audit_completion_going_concern_command_receipts
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_audit_completion_gc_receipt_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE audit_completion_going_concern_command_receipts IN ACCESS EXCLUSIVE MODE; DO $$ BEGIN IF EXISTS (SELECT 1 FROM audit_completion_going_concern_command_receipts) THEN RAISE EXCEPTION 'Going-concern receipt history is append-only; downgrade refused.' USING ERRCODE = '55000'; END IF; END $$;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_completion_gc_receipts_immutable ON audit_completion_going_concern_command_receipts; DROP TRIGGER IF EXISTS trg_audit_completion_gc_receipts_no_truncate ON audit_completion_going_concern_command_receipts; DROP FUNCTION IF EXISTS reject_audit_completion_gc_receipt_mutation();");
            migrationBuilder.DropTable(
                name: "audit_completion_going_concern_command_receipts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_going_concern_assessments_firm_id_client_id_engagement_id_id",
                table: "going_concern_assessments");

            migrationBuilder.CreateIndex(
                name: "IX_going_concern_assessments_firm_id_client_id_engagement_id",
                table: "going_concern_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id" });
        }
    }
}
