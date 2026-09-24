using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalTechnicalStatesAndReturnReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_adjustment_journal_values",
                table: "adjustment_journals");

            migrationBuilder.AddColumn<string>(
                name: "return_reason",
                table: "adjustment_journals",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_adjustment_journal_values",
                table: "adjustment_journals",
                sql: "purpose IN ('CLIENT_BOOK_CORRECTION','REPORTING_ADJUSTMENT','PRESENTATION_RECLASSIFICATION','GROUP_ONLY_ELIMINATION') AND origin IN ('AUDIT_PROPOSED','CLIENT_REQUESTED','MANAGEMENT_PROVIDED','IMPORTED') AND status IN ('Draft','Submitted','Returned','Posted','ReflectedInSource','Void') AND revision >= 1 AND length(trim(journal_number)) > 0 AND length(trim(reason)) <= 4000 AND length(trim(evidence_reference)) <= 2000 AND (period_id IS NULL OR (length(trim(basis)) > 0 AND currency ~ '^[A-Z]{3}$')) AND (book_id IS NULL OR period_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_adjustment_journal_values",
                table: "adjustment_journals");

            migrationBuilder.DropColumn(
                name: "return_reason",
                table: "adjustment_journals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_adjustment_journal_values",
                table: "adjustment_journals",
                sql: "purpose IN ('CLIENT_BOOK_CORRECTION','REPORTING_ADJUSTMENT','PRESENTATION_RECLASSIFICATION','GROUP_ONLY_ELIMINATION') AND origin IN ('AUDIT_PROPOSED','CLIENT_REQUESTED','MANAGEMENT_PROVIDED','IMPORTED') AND status IN ('Draft','Posted','ReflectedInSource','Void') AND revision >= 1 AND length(trim(journal_number)) > 0 AND length(trim(reason)) <= 4000 AND length(trim(evidence_reference)) <= 2000 AND (period_id IS NULL OR (length(trim(basis)) > 0 AND currency ~ '^[A-Z]{3}$')) AND (book_id IS NULL OR period_id IS NOT NULL)");
        }
    }
}
