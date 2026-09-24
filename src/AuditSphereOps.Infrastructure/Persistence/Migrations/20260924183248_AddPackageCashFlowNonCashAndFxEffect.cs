using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageCashFlowNonCashAndFxEffect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_non_cash",
                table: "financial_package_cash_flow_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "financial_package_fx_effects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_pair = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_fx_effects", x => x.id);
                    table.CheckConstraint("ck_financial_package_fx_effect_values", "length(trim(currency_pair)) > 0 AND length(trim(evidence_reference)) > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_financial_package_fx_effects_financial_packages_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_fx_effects_firm_id_client_id_engagement_i~",
                table: "financial_package_fx_effects",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_fx_effect_identity",
                table: "financial_package_fx_effects",
                columns: new[] { "firm_id", "financial_package_id", "currency_pair", "evidence_reference" });

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_financial_package_fx_effects_append_only
                  BEFORE UPDATE OR DELETE ON financial_package_fx_effects
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_financial_package_fx_effects_append_only ON financial_package_fx_effects;
                """);

            migrationBuilder.DropTable(
                name: "financial_package_fx_effects");

            migrationBuilder.DropColumn(
                name: "is_non_cash",
                table: "financial_package_cash_flow_lines");
        }
    }
}
