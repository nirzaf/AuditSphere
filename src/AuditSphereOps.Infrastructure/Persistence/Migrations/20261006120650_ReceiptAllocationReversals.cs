using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptAllocationReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipt_allocation_reversals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_allocation_reversals", x => x.id);
                    table.UniqueConstraint("AK_receipt_allocation_reversals_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_receipt_allocation_reversal_review", "(status = 'PENDING_REVIEW' AND reviewed_by_user_id IS NULL AND reviewed_at IS NULL AND review_reason IS NULL) OR (status IN ('APPROVED','REJECTED') AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND length(review_reason) > 0)");
                    table.CheckConstraint("ck_receipt_allocation_reversal_values", "revision >= 1 AND amount > 0 AND length(reference) > 0 AND length(reason) > 0 AND status IN ('PENDING_REVIEW','APPROVED','REJECTED')");
                    table.ForeignKey(
                        name: "FK_receipt_allocation_reversals_allocations_firm_id_receipt_al~",
                        columns: x => new { x.firm_id, x.receipt_allocation_id },
                        principalTable: "allocations",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_allocation_reversals_users_firm_id_reviewed_by_user~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_allocation_reversals_users_firm_id_submitted_by_use~",
                        columns: x => new { x.firm_id, x.submitted_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_allocation_reversals_firm_id_receipt_allocation_id",
                table: "receipt_allocation_reversals",
                columns: new[] { "firm_id", "receipt_allocation_id" },
                unique: true,
                filter: "status = 'PENDING_REVIEW'");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_allocation_reversals_firm_id_receipt_allocation_id_~",
                table: "receipt_allocation_reversals",
                columns: new[] { "firm_id", "receipt_allocation_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_allocation_reversals_firm_id_reviewed_by_user_id",
                table: "receipt_allocation_reversals",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_allocation_reversals_firm_id_submitted_by_user_id",
                table: "receipt_allocation_reversals",
                columns: new[] { "firm_id", "submitted_by_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_allocation_reversals");
        }
    }
}
