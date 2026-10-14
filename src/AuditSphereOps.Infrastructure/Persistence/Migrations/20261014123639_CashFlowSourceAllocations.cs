using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashFlowSourceAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_package_cash_flow_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_flow_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    general_ledger_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    general_ledger_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_line_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_cash_flow_allocations", x => x.id);
                    table.CheckConstraint("ck_financial_package_cash_allocation_values", "signed_amount <> 0 AND currency ~ '^[A-Z]{3}$' AND source_line_hash ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_financial_package_cash_flow_allocations_financial_package_c~",
                        column: x => x.cash_flow_line_id,
                        principalTable: "financial_package_cash_flow_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_package_cash_flow_allocations_financial_packages_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_allocations_cash_flow_line_id",
                table: "financial_package_cash_flow_allocations",
                column: "cash_flow_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_client_id_e~",
                table: "financial_package_cash_flow_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_allocations_firm_id_general_led~",
                table: "financial_package_cash_flow_allocations",
                columns: new[] { "firm_id", "general_ledger_line_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_cash_allocation_movement_source",
                table: "financial_package_cash_flow_allocations",
                columns: new[] { "firm_id", "financial_package_id", "cash_flow_line_id", "general_ledger_line_id" },
                unique: true);

            // Source allocations are part of the sealed package's lineage: they can be added with the package and never changed.
            migrationBuilder.Sql(@"
                CREATE FUNCTION guard_financial_package_cash_allocation_history() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN RAISE EXCEPTION 'Cash-flow source allocations are immutable package lineage.' USING ERRCODE='23514'; END; $fn$;
                CREATE TRIGGER immutable_financial_package_cash_allocation BEFORE UPDATE OR DELETE ON financial_package_cash_flow_allocations
                  FOR EACH ROW EXECUTE FUNCTION guard_financial_package_cash_allocation_history();
                ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS immutable_financial_package_cash_allocation ON financial_package_cash_flow_allocations;
                DROP FUNCTION IF EXISTS guard_financial_package_cash_allocation_history();
                ");

            migrationBuilder.DropTable(
                name: "financial_package_cash_flow_allocations");
        }
    }
}
