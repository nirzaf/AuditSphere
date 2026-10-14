using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashFlowSourceReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "financial_package_cash_flow_allocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "financial_package_cash_flow_source_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_cash_flow_source_reviews", x => x.id);
                    table.CheckConstraint("ck_financial_package_cash_source_review_values", "decision IN ('APPROVED','RETURNED') AND (decision = 'APPROVED' OR length(trim(reason)) > 0)");
                    table.ForeignKey(
                        name: "FK_financial_package_cash_flow_source_reviews_financial_packag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_client_i~",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_source_reviews_firm_id_financia~",
                table: "financial_package_cash_flow_source_reviews",
                columns: new[] { "firm_id", "financial_package_id", "reviewed_at" });

            // Source reviews are decisions: they are appended, never corrected or withdrawn.
            migrationBuilder.Sql(@"
                CREATE FUNCTION guard_financial_package_cash_source_review_history() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN RAISE EXCEPTION 'Cash-flow source review decisions are immutable history.' USING ERRCODE='23514'; END; $fn$;
                CREATE TRIGGER immutable_financial_package_cash_source_review BEFORE UPDATE OR DELETE ON financial_package_cash_flow_source_reviews
                  FOR EACH ROW EXECUTE FUNCTION guard_financial_package_cash_source_review_history();
                ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS immutable_financial_package_cash_source_review ON financial_package_cash_flow_source_reviews;
                DROP FUNCTION IF EXISTS guard_financial_package_cash_source_review_history();
                ");

            migrationBuilder.DropTable(
                name: "financial_package_cash_flow_source_reviews");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "financial_package_cash_flow_allocations");
        }
    }
}
