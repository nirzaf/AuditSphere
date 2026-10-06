using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmExpenseCreationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_expense_values",
                table: "firm_expenses");

            migrationBuilder.AddColumn<string>(
                name: "create_request_hash",
                table: "firm_expenses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "create_request_id",
                table: "firm_expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_expenses_firm_id_prepared_by_user_id_create_request_id",
                table: "firm_expenses",
                columns: new[] { "firm_id", "prepared_by_user_id", "create_request_id" },
                unique: true,
                filter: "create_request_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_expense_values",
                table: "firm_expenses",
                sql: "category IN ('RENT','SALARIES','PETTY_CASH','UTILITIES','OTHER') AND status IN ('DRAFT','SUBMITTED','APPROVED','POSTED','REJECTED') AND amount > 0 AND currency ~ '^[A-Z]{3}$' AND length(evidence_sha256) = 64 AND octet_length(evidence_content) BETWEEN 1 AND 5242880 AND (reviewed_by_user_id IS NULL OR reviewed_by_user_id <> prepared_by_user_id) AND ((status = 'POSTED') = (posting_id IS NOT NULL)) AND ((create_request_id IS NULL AND create_request_hash IS NULL) OR (create_request_id IS NOT NULL AND create_request_hash ~ '^[0-9a-f]{64}$'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_firm_expenses_firm_id_prepared_by_user_id_create_request_id",
                table: "firm_expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_expense_values",
                table: "firm_expenses");

            migrationBuilder.DropColumn(
                name: "create_request_hash",
                table: "firm_expenses");

            migrationBuilder.DropColumn(
                name: "create_request_id",
                table: "firm_expenses");

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_expense_values",
                table: "firm_expenses",
                sql: "category IN ('RENT','SALARIES','PETTY_CASH','UTILITIES','OTHER') AND status IN ('DRAFT','SUBMITTED','APPROVED','POSTED','REJECTED') AND amount > 0 AND currency ~ '^[A-Z]{3}$' AND length(evidence_sha256) = 64 AND octet_length(evidence_content) BETWEEN 1 AND 5242880 AND (reviewed_by_user_id IS NULL OR reviewed_by_user_id <> prepared_by_user_id) AND ((status = 'POSTED') = (posting_id IS NOT NULL))");
        }
    }
}
