using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeneralLedgerCompletenessBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "general_ledger_completeness_bridges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trial_balance_dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trial_balance_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    general_ledger_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    account_residual_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    trial_balance_account_count = table.Column<int>(type: "integer", nullable: false),
                    general_ledger_account_count = table.Column<int>(type: "integer", nullable: false),
                    matched_account_count = table.Column<int>(type: "integer", nullable: false),
                    mismatched_account_count = table.Column<int>(type: "integer", nullable: false),
                    absolute_residual = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    coverage_start = table.Column<DateOnly>(type: "date", nullable: false),
                    coverage_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_general_ledger_completeness_bridges", x => x.id);
                    table.UniqueConstraint("ak_gl_completeness_bridges_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_gl_completeness_bridge_values", "trial_balance_hash ~ '^[0-9a-f]{64}$' AND general_ledger_hash ~ '^[0-9a-f]{64}$' AND account_residual_digest ~ '^[0-9a-f]{64}$' AND trial_balance_account_count > 0 AND general_ledger_account_count > 0 AND matched_account_count >= 0 AND mismatched_account_count >= 0 AND absolute_residual >= 0 AND coverage_start <= coverage_end AND length(trim(evidence_reference)) > 0 AND status IN ('RECONCILED','UNRECONCILED','APPROVED','REJECTED') AND ((status IN ('APPROVED','REJECTED') AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL) OR status IN ('RECONCILED','UNRECONCILED'))");
                    table.ForeignKey(
                        name: "FK_general_ledger_completeness_bridges_client_reporting_books_~",
                        columns: x => new { x.firm_id, x.client_id, x.book_id },
                        principalTable: "client_reporting_books",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_general_ledger_completeness_bridges_client_reporting_period~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_general_ledger_completeness_bridges_engagements_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_general_ledger_completeness_bridges_source_import_batches_f~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.import_batch_id },
                        principalTable: "source_import_batches",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_general_ledger_completeness_bridges_trial_balance_datasets_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.trial_balance_dataset_id },
                        principalTable: "trial_balance_datasets",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_general_ledger_completeness_bridges_users_firm_id_created_b~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_general_ledger_completeness_bridges_users_firm_id_reviewed_~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_general_ledger_completeness_bridges_firm_id_client_id_book_~",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "book_id" });

            migrationBuilder.CreateIndex(
                name: "IX_general_ledger_completeness_bridges_firm_id_client_id_engag~",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "engagement_id", "import_batch_id" });

            migrationBuilder.CreateIndex(
                name: "IX_general_ledger_completeness_bridges_firm_id_client_id_perio~",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_general_ledger_completeness_bridges_firm_id_created_by_user~",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_general_ledger_completeness_bridges_firm_id_reviewed_by_use~",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_gl_completeness_bridge_input",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "engagement_id", "trial_balance_dataset_id", "import_batch_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "general_ledger_completeness_bridges");
        }
    }
}
