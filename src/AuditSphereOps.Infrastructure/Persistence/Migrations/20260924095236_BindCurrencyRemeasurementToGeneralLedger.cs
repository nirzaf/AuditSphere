using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindCurrencyRemeasurementToGeneralLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_general_ledger_line_id",
                table: "currency_remeasurement_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_gl_line_digest",
                table: "currency_remeasurement_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_gl_lines_scope_id",
                table: "general_ledger_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_remeasurement_items_firm_id_client_id_engagement_~1",
                table: "currency_remeasurement_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_general_ledger_line_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_currency_remeasurement_gl_source",
                table: "currency_remeasurement_items",
                sql: "(source_general_ledger_line_id IS NULL AND source_gl_line_digest = '') OR (source_general_ledger_line_id IS NOT NULL AND source_gl_line_digest ~ '^[0-9a-f]{64}$')");

            migrationBuilder.AddForeignKey(
                name: "FK_currency_remeasurement_items_general_ledger_lines_firm_id_c~",
                table: "currency_remeasurement_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_general_ledger_line_id" },
                principalTable: "general_ledger_lines",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION prevent_general_ledger_source_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Imported general-ledger source records are append-only.' USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_general_ledger_transactions_append_only
                  BEFORE UPDATE OR DELETE ON general_ledger_transactions
                  FOR EACH ROW EXECUTE FUNCTION prevent_general_ledger_source_mutation();
                CREATE TRIGGER trg_general_ledger_lines_append_only
                  BEFORE UPDATE OR DELETE ON general_ledger_lines
                  FOR EACH ROW EXECUTE FUNCTION prevent_general_ledger_source_mutation();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_general_ledger_lines_append_only ON general_ledger_lines;
                DROP TRIGGER IF EXISTS trg_general_ledger_transactions_append_only ON general_ledger_transactions;
                DROP FUNCTION IF EXISTS prevent_general_ledger_source_mutation();
            ");
            migrationBuilder.DropForeignKey(
                name: "FK_currency_remeasurement_items_general_ledger_lines_firm_id_c~",
                table: "currency_remeasurement_items");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_gl_lines_scope_id",
                table: "general_ledger_lines");

            migrationBuilder.DropIndex(
                name: "IX_currency_remeasurement_items_firm_id_client_id_engagement_~1",
                table: "currency_remeasurement_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_currency_remeasurement_gl_source",
                table: "currency_remeasurement_items");

            migrationBuilder.DropColumn(
                name: "source_general_ledger_line_id",
                table: "currency_remeasurement_items");

            migrationBuilder.DropColumn(
                name: "source_gl_line_digest",
                table: "currency_remeasurement_items");
        }
    }
}
