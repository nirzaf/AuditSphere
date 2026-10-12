using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskPlanReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_task_plan_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_version = table.Column<int>(type: "integer", nullable: false),
                    engagement_generation = table.Column<long>(type: "bigint", nullable: false),
                    milestone_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    milestone_plan_revision = table.Column<long>(type: "bigint", nullable: true),
                    schedule_basis_date = table.Column<DateOnly>(type: "date", nullable: true),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    task_count = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_task_plan_receipts", x => x.id);
                    table.UniqueConstraint("AK_workflow_task_plan_receipts_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_task_plan_receipt_values", "template_version >= 1 AND engagement_generation >= 1 AND (milestone_plan_id IS NULL) = (milestone_plan_revision IS NULL) AND (milestone_plan_id IS NULL) = (schedule_basis_date IS NULL) AND (milestone_plan_revision IS NULL OR milestone_plan_revision >= 1) AND idempotency_key <> '00000000-0000-0000-0000-000000000000' AND request_digest ~ '^[0-9a-f]{64}$' AND task_count BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "FK_workflow_task_plan_receipts_engagements_firm_id_client_id_e~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_plan_receipts_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_plan_receipts_workflow_template_revisions_fir~",
                        columns: x => new { x.firm_id, x.template_id, x.template_revision_id },
                        principalTable: "workflow_template_revisions",
                        principalColumns: new[] { "firm_id", "template_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_plan_receipts_firm_id_client_id_engagement_id",
                table: "workflow_task_plan_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_plan_receipts_firm_id_created_by_user_id",
                table: "workflow_task_plan_receipts",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_plan_receipts_firm_id_template_id_template_re~",
                table: "workflow_task_plan_receipts",
                columns: new[] { "firm_id", "template_id", "template_revision_id" });

            migrationBuilder.CreateIndex(
                name: "UX_workflow_task_plan_receipts_engagement_revision",
                table: "workflow_task_plan_receipts",
                columns: new[] { "firm_id", "engagement_id", "template_revision_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_workflow_task_plan_receipts_idempotency",
                table: "workflow_task_plan_receipts",
                columns: new[] { "firm_id", "engagement_id", "idempotency_key" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_workflow_task_plan_receipt_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'workflow task plan receipts are append-only'; END;
                $$;
                CREATE TRIGGER trg_workflow_task_plan_receipt_append_only
                  BEFORE UPDATE OR DELETE ON workflow_task_plan_receipts
                  FOR EACH ROW EXECUTE FUNCTION prevent_workflow_task_plan_receipt_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_workflow_task_plan_receipt_append_only ON workflow_task_plan_receipts; DROP FUNCTION IF EXISTS prevent_workflow_task_plan_receipt_mutation();");
            migrationBuilder.DropTable(
                name: "workflow_task_plan_receipts");
        }
    }
}
