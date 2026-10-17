using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliationCorrectionJournalLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_accounting_reconciliation_items_firm_id_client_id_engagemen~",
                table: "accounting_reconciliation_items",
                newName: "IX_accounting_reconciliation_items_firm_id_client_id_engageme~1");

            migrationBuilder.AddColumn<Guid>(
                name: "linked_journal_id",
                table: "accounting_reconciliation_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "linked_journal_revision",
                table: "accounting_reconciliation_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_reconciliation_items_firm_id_client_id_engagemen~",
                table: "accounting_reconciliation_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "linked_journal_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounting_reconciliation_item_journal_link",
                table: "accounting_reconciliation_items",
                sql: "(linked_journal_id IS NULL AND linked_journal_revision IS NULL) OR (linked_journal_id IS NOT NULL AND linked_journal_revision > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_reconciliation_items_adjustment_journals_firm_id~",
                table: "accounting_reconciliation_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "linked_journal_id" },
                principalTable: "adjustment_journals",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounting_reconciliation_items_adjustment_journals_firm_id~",
                table: "accounting_reconciliation_items");

            migrationBuilder.DropIndex(
                name: "IX_accounting_reconciliation_items_firm_id_client_id_engagemen~",
                table: "accounting_reconciliation_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounting_reconciliation_item_journal_link",
                table: "accounting_reconciliation_items");

            migrationBuilder.DropColumn(
                name: "linked_journal_id",
                table: "accounting_reconciliation_items");

            migrationBuilder.DropColumn(
                name: "linked_journal_revision",
                table: "accounting_reconciliation_items");

            migrationBuilder.RenameIndex(
                name: "IX_accounting_reconciliation_items_firm_id_client_id_engageme~1",
                table: "accounting_reconciliation_items",
                newName: "IX_accounting_reconciliation_items_firm_id_client_id_engagemen~");
        }
    }
}
