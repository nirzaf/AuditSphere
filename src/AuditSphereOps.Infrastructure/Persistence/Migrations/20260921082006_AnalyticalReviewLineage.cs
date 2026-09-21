using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticalReviewLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_analytical_review_values",
                table: "analytical_reviews");

            migrationBuilder.AddColumn<string>(
                name: "corroboration_reference",
                table: "journal_risk_flags",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "management_explanation",
                table: "journal_risk_flags",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "selected_for_testing",
                table: "journal_risk_flags",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "analytical_reviews",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "QAR");

            migrationBuilder.AddColumn<string>(
                name: "input_snapshot_json",
                table: "analytical_reviews",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "movement_flags",
                table: "analytical_reviews",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "seasonality_explanation",
                table: "analytical_reviews",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
UPDATE analytical_reviews AS a
SET currency = COALESCE(NULLIF(p.currency, ''), 'QAR')
FROM client_reporting_periods AS p
WHERE p.id = a.period_id AND p.firm_id = a.firm_id AND p.client_id = a.client_id;
");

            migrationBuilder.AddCheckConstraint(
                name: "ck_analytical_review_values",
                table: "analytical_reviews",
                sql: "length(trim(area)) > 0 AND length(trim(measure)) > 0 AND length(trim(currency)) = 3 AND length(trim(denominator_basis)) > 0 AND length(trim(formula_version)) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_analytical_review_values",
                table: "analytical_reviews");

            migrationBuilder.DropColumn(
                name: "corroboration_reference",
                table: "journal_risk_flags");

            migrationBuilder.DropColumn(
                name: "management_explanation",
                table: "journal_risk_flags");

            migrationBuilder.DropColumn(
                name: "selected_for_testing",
                table: "journal_risk_flags");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "analytical_reviews");

            migrationBuilder.DropColumn(
                name: "input_snapshot_json",
                table: "analytical_reviews");

            migrationBuilder.DropColumn(
                name: "movement_flags",
                table: "analytical_reviews");

            migrationBuilder.DropColumn(
                name: "seasonality_explanation",
                table: "analytical_reviews");

            migrationBuilder.AddCheckConstraint(
                name: "ck_analytical_review_values",
                table: "analytical_reviews",
                sql: "length(trim(area)) > 0 AND length(trim(measure)) > 0 AND length(trim(denominator_basis)) > 0 AND length(trim(formula_version)) > 0");
        }
    }
}
