using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeneralLedgerCompletenessCalculationVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_gl_completeness_bridge_input",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_gl_completeness_bridge_values",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.AddColumn<int>(
                name: "calculation_version",
                table: "general_ledger_completeness_bridges",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "calculation_version",
                table: "general_ledger_completeness_bridges",
                type: "integer",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ux_gl_completeness_bridge_input",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "engagement_id", "trial_balance_dataset_id", "import_batch_id", "calculation_version" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_gl_completeness_bridge_values",
                table: "general_ledger_completeness_bridges",
                sql: "trial_balance_hash ~ '^[0-9a-f]{64}$' AND general_ledger_hash ~ '^[0-9a-f]{64}$' AND account_residual_digest ~ '^[0-9a-f]{64}$' AND calculation_version IN (1,2) AND trial_balance_account_count > 0 AND general_ledger_account_count > 0 AND matched_account_count >= 0 AND mismatched_account_count >= 0 AND absolute_residual >= 0 AND coverage_start <= coverage_end AND length(trim(evidence_reference)) > 0 AND status IN ('RECONCILED','UNRECONCILED','APPROVED','REJECTED') AND ((status IN ('APPROVED','REJECTED') AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL) OR status IN ('RECONCILED','UNRECONCILED'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE general_ledger_completeness_bridges IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM general_ledger_completeness_bridges WHERE calculation_version <> 1) THEN
                    RAISE EXCEPTION 'Version 2 completeness proofs prevent rollback without losing their calculation identity.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ux_gl_completeness_bridge_input",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_gl_completeness_bridge_values",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "calculation_version",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.CreateIndex(
                name: "ux_gl_completeness_bridge_input",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "engagement_id", "trial_balance_dataset_id", "import_batch_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_gl_completeness_bridge_values",
                table: "general_ledger_completeness_bridges",
                sql: "trial_balance_hash ~ '^[0-9a-f]{64}$' AND general_ledger_hash ~ '^[0-9a-f]{64}$' AND account_residual_digest ~ '^[0-9a-f]{64}$' AND trial_balance_account_count > 0 AND general_ledger_account_count > 0 AND matched_account_count >= 0 AND mismatched_account_count >= 0 AND absolute_residual >= 0 AND coverage_start <= coverage_end AND length(trim(evidence_reference)) > 0 AND status IN ('RECONCILED','UNRECONCILED','APPROVED','REJECTED') AND ((status IN ('APPROVED','REJECTED') AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL) OR status IN ('RECONCILED','UNRECONCILED'))");
        }
    }
}
