using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialStatementComposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_statement_compositions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    presentation_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_calculation_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    manifest_digest = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    pdf_artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pdf_sha256_hex = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    word_artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    word_sha256_hex = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    excel_artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    excel_sha256_hex = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    selected_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_statement_compositions", x => x.id);
                    table.CheckConstraint("ck_financial_statement_composition_values", "length(manifest_digest) = 64 AND length(pdf_sha256_hex) = 64 AND length(word_sha256_hex) = 64 AND length(excel_sha256_hex) = 64");
                });

            migrationBuilder.CreateIndex(
                name: "ux_financial_statement_compositions_revision",
                table: "financial_statement_compositions",
                columns: new[] { "firm_id", "presentation_revision_id" },
                unique: true);

            // Append-only: a composition is history, so updates and deletes are refused. The trigger follows the table and its index.
            migrationBuilder.Sql(@"
                CREATE FUNCTION guard_financial_statement_composition_history() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN RAISE EXCEPTION 'Financial statement compositions are immutable history.' USING ERRCODE='23514'; END; $fn$;
                CREATE TRIGGER immutable_financial_statement_composition BEFORE UPDATE OR DELETE ON financial_statement_compositions
                  FOR EACH ROW EXECUTE FUNCTION guard_financial_statement_composition_history();
                ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS immutable_financial_statement_composition ON financial_statement_compositions;
                DROP FUNCTION IF EXISTS guard_financial_statement_composition_history();
                ");
            migrationBuilder.DropTable(
                name: "financial_statement_compositions");
        }
    }
}
