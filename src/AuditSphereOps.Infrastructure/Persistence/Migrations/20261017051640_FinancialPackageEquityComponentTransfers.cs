using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialPackageEquityComponentTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_package_equity_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_line_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    destination_line_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_equity_transfers", x => x.id);
                    table.CheckConstraint("ck_financial_package_equity_transfer_values", "length(trim(transfer_reference)) > 0 AND length(trim(source_line_code)) > 0 AND length(trim(destination_line_code)) > 0 AND lower(source_line_code) <> lower(destination_line_code) AND amount > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0");
                    table.ForeignKey(
                        name: "FK_financial_package_equity_transfers_financial_packages_firm_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_equity_transfers_firm_id_client_id_engage~",
                table: "financial_package_equity_transfers",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_equity_transfer_reference",
                table: "financial_package_equity_transfers",
                columns: new[] { "firm_id", "financial_package_id", "transfer_reference" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_financial_package_equity_transfers_append_only
                  BEFORE UPDATE OR DELETE ON financial_package_equity_transfers
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_package_support_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_financial_package_equity_transfers_append_only ON financial_package_equity_transfers;");
            migrationBuilder.DropTable(
                name: "financial_package_equity_transfers");
        }
    }
}
