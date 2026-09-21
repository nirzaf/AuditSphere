using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntercompanyMatchReviewModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_intercompany_match_values",
                table: "intercompany_matches");

            migrationBuilder.AddColumn<string>(
                name: "match_group_reference",
                table: "intercompany_matches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "match_mode",
                table: "intercompany_matches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ONE_TO_ONE");

            migrationBuilder.AddColumn<bool>(
                name: "outside_perimeter_review",
                table: "intercompany_matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_intercompany_match_values",
                table: "intercompany_matches",
                sql: "length(trim(account_nature)) > 0 AND match_mode IN ('ONE_TO_ONE','GROUPED') AND (match_mode <> 'GROUPED' OR length(trim(match_group_reference)) > 0) AND length(trim(seller_taxonomy_code)) > 0 AND length(trim(buyer_taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_intercompany_match_values",
                table: "intercompany_matches");

            migrationBuilder.DropColumn(
                name: "match_group_reference",
                table: "intercompany_matches");

            migrationBuilder.DropColumn(
                name: "match_mode",
                table: "intercompany_matches");

            migrationBuilder.DropColumn(
                name: "outside_perimeter_review",
                table: "intercompany_matches");

            migrationBuilder.AddCheckConstraint(
                name: "ck_intercompany_match_values",
                table: "intercompany_matches",
                sql: "length(trim(account_nature)) > 0 AND length(trim(seller_taxonomy_code)) > 0 AND length(trim(buyer_taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$'");
        }
    }
}
