using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdjustmentSourceBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_trial_balance_datasets_firm_id_engagement_id",
                table: "trial_balance_datasets");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "adjustment_journals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "adjustment_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    result_hash = table.Column<string>(type: "text", nullable: true),
                    applied_debits = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    applied_credits_abs = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    applied_journal_count = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adjustment_plans", x => x.id);
                    table.CheckConstraint("ck_plan_result", "status = 'Draft' OR (result_hash IS NOT NULL AND result_hash ~ '^[0-9a-f]{64}$')");
                    table.CheckConstraint("ck_plan_status", "status IN ('Draft','Finalized')");
                    table.ForeignKey(
                        name: "FK_adjustment_plans_engagements_firm_id_engagement_id",
                        columns: x => new { x.firm_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_adjustment_plans_trial_balance_datasets_base_dataset_id",
                        column: x => x.base_dataset_id,
                        principalTable: "trial_balance_datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_source_reconciliations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_journal_number = table.Column<string>(type: "text", nullable: false),
                    journal_revision = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    evidence = table.Column<string>(type: "text", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_source_reconciliations", x => x.id);
                    table.CheckConstraint("ck_reconciliation_evidence", "(state IN ('REFLECTED','PARTIALLY_REFLECTED') AND length(evidence) > 0 AND length(evidence) <= 2000) OR (state NOT IN ('REFLECTED','PARTIALLY_REFLECTED') AND length(evidence) <= 2000)");
                    table.CheckConstraint("ck_reconciliation_number", "length(logical_journal_number) > 0 AND length(logical_journal_number) <= 32");
                    table.CheckConstraint("ck_reconciliation_revision", "journal_revision >= 1");
                    table.CheckConstraint("ck_reconciliation_state", "state IN ('UNKNOWN','NOT_REFLECTED','REFLECTED','PARTIALLY_REFLECTED','NOT_APPLICABLE')");
                    table.ForeignKey(
                        name: "FK_journal_source_reconciliations_engagements_firm_id_engageme~",
                        columns: x => new { x.firm_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_source_reconciliations_trial_balance_datasets_base_~",
                        column: x => x.base_dataset_id,
                        principalTable: "trial_balance_datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "adjustment_plan_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_journal_number = table.Column<string>(type: "text", nullable: false),
                    journal_revision = table.Column<long>(type: "bigint", nullable: false),
                    layer = table.Column<string>(type: "text", nullable: false),
                    reflection_state = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adjustment_plan_lines", x => x.id);
                    table.CheckConstraint("ck_planline_reflection", "reflection_state IN ('UNKNOWN','NOT_REFLECTED','REFLECTED','PARTIALLY_REFLECTED','NOT_APPLICABLE')");
                    table.CheckConstraint("ck_planline_revision", "journal_revision >= 1");
                    table.CheckConstraint("ck_planline_shape", "length(logical_journal_number) > 0 AND length(logical_journal_number) <= 32 AND length(layer) > 0 AND length(layer) <= 32");
                    table.ForeignKey(
                        name: "FK_adjustment_plan_lines_adjustment_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "adjustment_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_dataset_firm_engagement_hash",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "engagement_id", "sha256_hex" },
                unique: true,
                filter: "length(sha256_hex) > 0");

            migrationBuilder.CreateIndex(
                name: "ux_planline_plan_journal_layer",
                table: "adjustment_plan_lines",
                columns: new[] { "plan_id", "logical_journal_number", "layer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_plans_base_dataset_id",
                table: "adjustment_plans",
                column: "base_dataset_id");

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_plans_firm_id_engagement_id",
                table: "adjustment_plans",
                columns: new[] { "firm_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_source_reconciliations_base_dataset_id",
                table: "journal_source_reconciliations",
                column: "base_dataset_id");

            migrationBuilder.CreateIndex(
                name: "ux_reconciliation_base_journal",
                table: "journal_source_reconciliations",
                columns: new[] { "firm_id", "engagement_id", "base_dataset_id", "logical_journal_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adjustment_plan_lines");

            migrationBuilder.DropTable(
                name: "journal_source_reconciliations");

            migrationBuilder.DropTable(
                name: "adjustment_plans");

            migrationBuilder.DropIndex(
                name: "ux_dataset_firm_engagement_hash",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "adjustment_journals");

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_datasets_firm_id_engagement_id",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "engagement_id" });
        }
    }
}
