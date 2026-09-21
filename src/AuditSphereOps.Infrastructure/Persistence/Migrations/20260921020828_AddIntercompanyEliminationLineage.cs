using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntercompanyEliminationLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_intercompany_match_values",
                table: "intercompany_matches");

            migrationBuilder.AddColumn<string>(
                name: "buyer_taxonomy_code",
                table: "intercompany_matches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "seller_taxonomy_code",
                table: "intercompany_matches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "intercompany_match_id",
                table: "consolidation_run_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("UPDATE intercompany_matches SET seller_taxonomy_code = account_nature, buyer_taxonomy_code = account_nature WHERE seller_taxonomy_code = '' OR buyer_taxonomy_code = '';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_intercompany_match_values",
                table: "intercompany_matches",
                sql: "length(trim(account_nature)) > 0 AND length(trim(seller_taxonomy_code)) > 0 AND length(trim(buyer_taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$'");

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_run_lines_firm_id_group_id_scope_version_id_i~",
                table: "consolidation_run_lines",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "intercompany_match_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_consolidation_run_lines_intercompany_matches_firm_id_group_~",
                table: "consolidation_run_lines",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "intercompany_match_id" },
                principalTable: "intercompany_matches",
                principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_consolidation_run_lines_intercompany_matches_firm_id_group_~",
                table: "consolidation_run_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_intercompany_match_values",
                table: "intercompany_matches");

            migrationBuilder.DropIndex(
                name: "IX_consolidation_run_lines_firm_id_group_id_scope_version_id_i~",
                table: "consolidation_run_lines");

            migrationBuilder.DropColumn(
                name: "buyer_taxonomy_code",
                table: "intercompany_matches");

            migrationBuilder.DropColumn(
                name: "seller_taxonomy_code",
                table: "intercompany_matches");

            migrationBuilder.DropColumn(
                name: "intercompany_match_id",
                table: "consolidation_run_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_intercompany_match_values",
                table: "intercompany_matches",
                sql: "length(trim(account_nature)) > 0 AND currency ~ '^[A-Z]{3}$'");
        }
    }
}
