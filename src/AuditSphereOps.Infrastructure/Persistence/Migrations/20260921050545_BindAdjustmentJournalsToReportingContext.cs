using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindAdjustmentJournalsToReportingContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_adjustment_journal_values",
                table: "adjustment_journals");

            migrationBuilder.AddColumn<string>(
                name: "basis",
                table: "adjustment_journals",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "adjustment_journals",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "period_id",
                table: "adjustment_journals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_journals_firm_id_client_id_period_id",
                table: "adjustment_journals",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_adjustment_journal_values",
                table: "adjustment_journals",
                sql: "purpose IN ('CLIENT_BOOK_CORRECTION','REPORTING_ADJUSTMENT','PRESENTATION_RECLASSIFICATION','GROUP_ONLY_ELIMINATION') AND origin IN ('AUDIT_PROPOSED','CLIENT_REQUESTED','MANAGEMENT_PROVIDED','IMPORTED') AND status IN ('Draft','Posted','ReflectedInSource','Void') AND revision >= 1 AND length(trim(journal_number)) > 0 AND length(trim(reason)) <= 4000 AND length(trim(evidence_reference)) <= 2000 AND (period_id IS NULL OR (length(trim(basis)) > 0 AND currency ~ '^[A-Z]{3}$')) AND (book_id IS NULL OR period_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_adjustment_journals_client_reporting_periods_firm_id_client~",
                table: "adjustment_journals",
                columns: new[] { "firm_id", "client_id", "period_id" },
                principalTable: "client_reporting_periods",
                principalColumns: new[] { "firm_id", "client_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_adjustment_journals_client_reporting_periods_firm_id_client~",
                table: "adjustment_journals");

            migrationBuilder.DropIndex(
                name: "IX_adjustment_journals_firm_id_client_id_period_id",
                table: "adjustment_journals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_adjustment_journal_values",
                table: "adjustment_journals");

            migrationBuilder.DropColumn(
                name: "basis",
                table: "adjustment_journals");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "adjustment_journals");

            migrationBuilder.DropColumn(
                name: "period_id",
                table: "adjustment_journals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_adjustment_journal_values",
                table: "adjustment_journals",
                sql: "purpose IN ('CLIENT_BOOK_CORRECTION','REPORTING_ADJUSTMENT','PRESENTATION_RECLASSIFICATION','GROUP_ONLY_ELIMINATION') AND origin IN ('AUDIT_PROPOSED','CLIENT_REQUESTED','MANAGEMENT_PROVIDED','IMPORTED') AND status IN ('Draft','Posted','ReflectedInSource','Void') AND revision >= 1 AND length(trim(journal_number)) > 0 AND length(trim(reason)) <= 4000 AND length(trim(evidence_reference)) <= 2000");
        }
    }
}
