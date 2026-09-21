using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkValuationDifferencesToAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ecl_assessment_values",
                table: "ecl_assessments");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_valuation_assessments_firm_id_client_id_engagemen~",
                table: "inventory_valuation_assessments",
                newName: "IX_inventory_valuation_assessments_firm_id_client_id_engageme~1");

            migrationBuilder.AddColumn<Guid>(
                name: "proposed_journal_id",
                table: "inventory_valuation_assessments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "booked_amount",
                table: "ecl_assessments",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "proposed_journal_id",
                table: "ecl_assessments",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("UPDATE ecl_assessments SET booked_amount = management_expected_loss");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_valuation_assessments_firm_id_client_id_engagemen~",
                table: "inventory_valuation_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "proposed_journal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ecl_assessments_firm_id_client_id_engagement_id_proposed_jo~",
                table: "ecl_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "proposed_journal_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ecl_assessment_values",
                table: "ecl_assessments",
                sql: "length(trim(method)) > 0 AND length(trim(methodology_version)) > 0 AND eligible_exposure >= 0 AND probability_of_default BETWEEN 0 AND 1 AND loss_given_default BETWEEN 0 AND 1 AND booked_amount >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_ecl_assessments_adjustment_journals_firm_id_client_id_engag~",
                table: "ecl_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "proposed_journal_id" },
                principalTable: "adjustment_journals",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_valuation_assessments_adjustment_journals_firm_id~",
                table: "inventory_valuation_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "proposed_journal_id" },
                principalTable: "adjustment_journals",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ecl_assessments_adjustment_journals_firm_id_client_id_engag~",
                table: "ecl_assessments");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_valuation_assessments_adjustment_journals_firm_id~",
                table: "inventory_valuation_assessments");

            migrationBuilder.DropIndex(
                name: "IX_inventory_valuation_assessments_firm_id_client_id_engagemen~",
                table: "inventory_valuation_assessments");

            migrationBuilder.DropIndex(
                name: "IX_ecl_assessments_firm_id_client_id_engagement_id_proposed_jo~",
                table: "ecl_assessments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ecl_assessment_values",
                table: "ecl_assessments");

            migrationBuilder.DropColumn(
                name: "proposed_journal_id",
                table: "inventory_valuation_assessments");

            migrationBuilder.DropColumn(
                name: "booked_amount",
                table: "ecl_assessments");

            migrationBuilder.DropColumn(
                name: "proposed_journal_id",
                table: "ecl_assessments");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_valuation_assessments_firm_id_client_id_engageme~1",
                table: "inventory_valuation_assessments",
                newName: "IX_inventory_valuation_assessments_firm_id_client_id_engagemen~");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ecl_assessment_values",
                table: "ecl_assessments",
                sql: "length(trim(method)) > 0 AND length(trim(methodology_version)) > 0 AND eligible_exposure >= 0 AND probability_of_default BETWEEN 0 AND 1 AND loss_given_default BETWEEN 0 AND 1");
        }
    }
}
