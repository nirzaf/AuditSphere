using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationProofEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_reconciliation_proofs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    formula_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    gl_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    items_signed_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    residual = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    is_reconciled = table.Column<bool>(type: "boolean", nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    item_manifest_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_reconciliation_proofs", x => x.id);
                    table.CheckConstraint("ck_accounting_reconciliation_proof_values", "length(trim(formula_version)) > 0 AND item_manifest_digest ~ '^[0-9a-f]{64}$' AND item_count >= 0");
                    table.ForeignKey(
                        name: "FK_accounting_reconciliation_proofs_accounting_reconciliations~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.reconciliation_id },
                        principalTable: "accounting_reconciliations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_reconciliation_proofs_engagements_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounting_reconciliation_proof",
                table: "accounting_reconciliation_proofs",
                columns: new[] { "firm_id", "reconciliation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_reconciliation_proofs_firm_id_client_id_engageme~",
                table: "accounting_reconciliation_proofs",
                columns: new[] { "firm_id", "client_id", "engagement_id", "reconciliation_id" });

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_accounting_reconciliation_proofs_append_only
                  BEFORE UPDATE OR DELETE ON accounting_reconciliation_proofs
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_accounting_reconciliation_proofs_append_only ON accounting_reconciliation_proofs;
                """);

            migrationBuilder.DropTable(
                name: "accounting_reconciliation_proofs");
        }
    }
}
