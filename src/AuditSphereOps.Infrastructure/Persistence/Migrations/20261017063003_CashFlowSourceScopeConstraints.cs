using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashFlowSourceScopeConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_package_cash_flow_allocations_financial_package_c~",
                table: "financial_package_cash_flow_allocations");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_client_i~",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_lines_firm_id_client_id_engagem~",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_allocations_cash_flow_line_id",
                table: "financial_package_cash_flow_allocations");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_client_id_e~",
                table: "financial_package_cash_flow_allocations");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_gl_lines_source_identity",
                table: "general_ledger_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "import_batch_id", "transaction_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_financial_package_cash_source_reviews_package_scope_id",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_financial_package_cash_flow_lines_package_scope_id",
                table: "financial_package_cash_flow_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_client_~1",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "previous_review_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_client_i~",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_import_batch_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_reviewed~",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_client_id_~1",
                table: "financial_package_cash_flow_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_import_batch_id", "general_ledger_transaction_id", "general_ledger_line_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_client_id_e~",
                table: "financial_package_cash_flow_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "cash_flow_line_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_created_by_~",
                table: "financial_package_cash_flow_allocations",
                columns: new[] { "firm_id", "created_by_user_id" });

            // Older allocation rows received a zero-GUID placeholder before creator identity was persisted.
            // NOT VALID preserves those immutable legacy rows but enforces real firm-scoped users for all new allocations.
            migrationBuilder.Sql(@"ALTER TABLE financial_package_cash_flow_allocations
                ADD CONSTRAINT fk_cash_alloc_creator FOREIGN KEY (firm_id, created_by_user_id)
                REFERENCES users (firm_id, id) ON DELETE RESTRICT NOT VALID;");

            migrationBuilder.AddForeignKey(
                name: "fk_cash_alloc_gl_source",
                table: "financial_package_cash_flow_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_import_batch_id", "general_ledger_transaction_id", "general_ledger_line_id" },
                principalTable: "general_ledger_lines",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "import_batch_id", "transaction_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cash_alloc_movement_scoped",
                table: "financial_package_cash_flow_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "cash_flow_line_id" },
                principalTable: "financial_package_cash_flow_lines",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cash_review_previous",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "previous_review_id" },
                principalTable: "financial_package_cash_flow_source_reviews",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cash_review_reviewer",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "reviewed_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cash_review_source_batch",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_import_batch_id" },
                principalTable: "source_import_batches",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cash_alloc_creator",
                table: "financial_package_cash_flow_allocations");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_alloc_gl_source",
                table: "financial_package_cash_flow_allocations");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_alloc_movement_scoped",
                table: "financial_package_cash_flow_allocations");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_review_previous",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_review_reviewer",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_review_source_batch",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_gl_lines_source_identity",
                table: "general_ledger_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_financial_package_cash_source_reviews_package_scope_id",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_client_~1",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_client_i~",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_reviewed~",
                table: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_financial_package_cash_flow_lines_package_scope_id",
                table: "financial_package_cash_flow_lines");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_client_id_~1",
                table: "financial_package_cash_flow_allocations");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_client_id_e~",
                table: "financial_package_cash_flow_allocations");

            migrationBuilder.DropIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_created_by_~",
                table: "financial_package_cash_flow_allocations");

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_client_i~",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_lines_firm_id_client_id_engagem~",
                table: "financial_package_cash_flow_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_allocations_cash_flow_line_id",
                table: "financial_package_cash_flow_allocations",
                column: "cash_flow_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_client_id_e~",
                table: "financial_package_cash_flow_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_financial_package_cash_flow_allocations_financial_package_c~",
                table: "financial_package_cash_flow_allocations",
                column: "cash_flow_line_id",
                principalTable: "financial_package_cash_flow_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
