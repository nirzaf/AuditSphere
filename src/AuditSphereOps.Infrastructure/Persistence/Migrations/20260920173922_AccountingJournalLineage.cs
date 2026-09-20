using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

public partial class AccountingJournalLineage : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.DropCheckConstraint("ck_audit_difference_values", "audit_differences");

    migrationBuilder.AddColumn<string>("correction_state", "audit_differences", type: "character varying(40)", maxLength: 40, nullable: true);
    migrationBuilder.AddColumn<string>("journal_impact_hash", "audit_differences", type: "character varying(64)", maxLength: 64, nullable: true);
    migrationBuilder.AddColumn<string>("journal_impact_json", "audit_differences", type: "character varying(100000)", maxLength: 100000, nullable: true);
    migrationBuilder.AddColumn<Guid>("proposed_journal_id", "audit_differences", type: "uuid", nullable: true);
    migrationBuilder.AddColumn<long>("proposed_journal_revision", "audit_differences", type: "bigint", nullable: true);
    migrationBuilder.AddColumn<Guid>("source_reflection_reconciliation_id", "audit_differences", type: "uuid", nullable: true);
    migrationBuilder.AddColumn<Guid>("verified_adjusted_snapshot_id", "audit_differences", type: "uuid", nullable: true);

    migrationBuilder.AddColumn<Guid>("book_id", "adjustment_journals", type: "uuid", nullable: true);
    migrationBuilder.AddColumn<string>("evidence_reference", "adjustment_journals", type: "character varying(2000)", maxLength: 2000, nullable: false, defaultValue: "LEGACY_RECORD");
    migrationBuilder.AddColumn<string>("origin", "adjustment_journals", type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "AUDIT_PROPOSED");
    migrationBuilder.AddColumn<string>("purpose", "adjustment_journals", type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "REPORTING_ADJUSTMENT");
    migrationBuilder.AddColumn<string>("reason", "adjustment_journals", type: "character varying(4000)", maxLength: 4000, nullable: false, defaultValue: "LEGACY_ADJUSTMENT");
    migrationBuilder.AddColumn<Guid>("reversal_of_journal_id", "adjustment_journals", type: "uuid", nullable: true);
    migrationBuilder.AddColumn<Guid>("supersedes_journal_id", "adjustment_journals", type: "uuid", nullable: true);

    migrationBuilder.AddUniqueConstraint("AK_journal_source_reconciliations_scope_id", "journal_source_reconciliations",
      new[] { "firm_id", "client_id", "engagement_id", "id" });
    migrationBuilder.AddUniqueConstraint("AK_adjustment_journals_scope_id", "adjustment_journals",
      new[] { "firm_id", "client_id", "engagement_id", "id" });

    migrationBuilder.CreateTable(
      name: "adjustment_journal_management_decisions",
      columns: table => new
      {
        id = table.Column<Guid>(type: "uuid", nullable: false),
        firm_id = table.Column<Guid>(type: "uuid", nullable: false),
        client_id = table.Column<Guid>(type: "uuid", nullable: false),
        engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
        journal_id = table.Column<Guid>(type: "uuid", nullable: false),
        journal_revision = table.Column<long>(type: "bigint", nullable: false),
        decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
        evidence_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
        evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
        decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
        decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
      },
      constraints: table =>
      {
        table.PrimaryKey("PK_adjustment_journal_management_decisions", x => x.id);
        table.CheckConstraint("ck_adjustment_journal_management_decision_values",
          "decision IN ('ACCEPTED','REJECTED','PARTIAL') AND evidence_mode IN ('SIGNED_IN','OFFLINE') AND length(trim(evidence_reference)) > 0 AND length(trim(evidence_reference)) <= 2000 AND journal_revision >= 1");
        table.ForeignKey("FK_adjustment_journal_management_decisions_adjustment_journals~",
          x => new { x.firm_id, x.client_id, x.engagement_id, x.journal_id }, "adjustment_journals",
          new[] { "firm_id", "client_id", "engagement_id", "id" }, onDelete: ReferentialAction.Restrict);
        table.ForeignKey("FK_adjustment_journal_management_decisions_users_firm_id_decid~",
          x => new { x.firm_id, x.decided_by_user_id }, "users", new[] { "firm_id", "id" }, onDelete: ReferentialAction.Restrict);
      });

    migrationBuilder.CreateIndex("IX_audit_differences_firm_id_client_id_engagement_id_proposed_~", "audit_differences",
      new[] { "firm_id", "client_id", "engagement_id", "proposed_journal_id" });
    migrationBuilder.CreateIndex("IX_audit_differences_firm_id_client_id_engagement_id_source_re~", "audit_differences",
      new[] { "firm_id", "client_id", "engagement_id", "source_reflection_reconciliation_id" });
    migrationBuilder.CreateIndex("IX_audit_differences_firm_id_client_id_engagement_id_verified_~", "audit_differences",
      new[] { "firm_id", "client_id", "engagement_id", "verified_adjusted_snapshot_id" });
    migrationBuilder.AddCheckConstraint("ck_audit_difference_values", "audit_differences",
      "length(trim(account_area)) > 0 AND length(trim(difference_type)) > 0 AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND amount <> 0 AND input_generation > 0 AND status IN ('OPEN','MANAGEMENT_RESPONDED','EVALUATED','CORRECTED','VERIFIED_REFLECTED') AND (correction_state IS NULL OR correction_state IN ('PROPOSED','AGREED','REJECTED','APPLIED_IN_REPORTING','REPORTED_POSTED_EXTERNALLY','VERIFIED_REFLECTED')) AND (NOT corrected OR correction_state IS NULL OR (correction_state = 'VERIFIED_REFLECTED' AND proposed_journal_id IS NOT NULL AND source_reflection_reconciliation_id IS NOT NULL AND verified_adjusted_snapshot_id IS NOT NULL))");

    migrationBuilder.CreateIndex("IX_adjustment_journals_firm_id_client_id_book_id", "adjustment_journals",
      new[] { "firm_id", "client_id", "book_id" });
    migrationBuilder.AddCheckConstraint("ck_adjustment_journal_values", "adjustment_journals",
      "purpose IN ('CLIENT_BOOK_CORRECTION','REPORTING_ADJUSTMENT','PRESENTATION_RECLASSIFICATION','GROUP_ONLY_ELIMINATION') AND origin IN ('AUDIT_PROPOSED','CLIENT_REQUESTED','MANAGEMENT_PROVIDED','IMPORTED') AND status IN ('Draft','Posted','ReflectedInSource','Void') AND revision >= 1 AND length(trim(journal_number)) > 0 AND length(trim(reason)) <= 4000 AND length(trim(evidence_reference)) <= 2000");
    migrationBuilder.CreateIndex("IX_adjustment_journal_management_decisions_firm_id_decided_by_~", "adjustment_journal_management_decisions",
      new[] { "firm_id", "decided_by_user_id" });
    migrationBuilder.CreateIndex("ux_adjustment_journal_management_decision_revision", "adjustment_journal_management_decisions",
      new[] { "firm_id", "client_id", "engagement_id", "journal_id", "journal_revision" }, unique: true);

    migrationBuilder.AddForeignKey("FK_adjustment_journals_client_reporting_books_firm_id_client_i~", "adjustment_journals",
      columns: new[] { "firm_id", "client_id", "book_id" }, principalTable: "client_reporting_books",
      principalColumns: new[] { "firm_id", "client_id", "id" }, onDelete: ReferentialAction.Restrict);
    migrationBuilder.AddForeignKey("FK_audit_differences_adjusted_tb_snapshots_firm_id_client_id_e~", "audit_differences",
      columns: new[] { "firm_id", "client_id", "engagement_id", "verified_adjusted_snapshot_id" }, principalTable: "adjusted_tb_snapshots",
      principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" }, onDelete: ReferentialAction.Restrict);
    migrationBuilder.AddForeignKey("FK_audit_differences_adjustment_journals_firm_id_client_id_eng~", "audit_differences",
      columns: new[] { "firm_id", "client_id", "engagement_id", "proposed_journal_id" }, principalTable: "adjustment_journals",
      principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" }, onDelete: ReferentialAction.Restrict);
    migrationBuilder.AddForeignKey("FK_audit_differences_journal_source_reconciliations_firm_id_cl~", "audit_differences",
      columns: new[] { "firm_id", "client_id", "engagement_id", "source_reflection_reconciliation_id" }, principalTable: "journal_source_reconciliations",
      principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" }, onDelete: ReferentialAction.Restrict);
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.DropForeignKey("FK_adjustment_journals_client_reporting_books_firm_id_client_i~", "adjustment_journals");
    migrationBuilder.DropForeignKey("FK_audit_differences_adjusted_tb_snapshots_firm_id_client_id_e~", "audit_differences");
    migrationBuilder.DropForeignKey("FK_audit_differences_adjustment_journals_firm_id_client_id_eng~", "audit_differences");
    migrationBuilder.DropForeignKey("FK_audit_differences_journal_source_reconciliations_firm_id_cl~", "audit_differences");
    migrationBuilder.DropIndex("IX_adjustment_journal_management_decisions_firm_id_decided_by_~", "adjustment_journal_management_decisions");
    migrationBuilder.DropIndex("ux_adjustment_journal_management_decision_revision", "adjustment_journal_management_decisions");
    migrationBuilder.DropTable("adjustment_journal_management_decisions");
    migrationBuilder.DropUniqueConstraint("AK_journal_source_reconciliations_scope_id", "journal_source_reconciliations");
    migrationBuilder.DropUniqueConstraint("AK_adjustment_journals_scope_id", "adjustment_journals");
    migrationBuilder.DropIndex("IX_audit_differences_firm_id_client_id_engagement_id_proposed_~", "audit_differences");
    migrationBuilder.DropIndex("IX_audit_differences_firm_id_client_id_engagement_id_source_re~", "audit_differences");
    migrationBuilder.DropIndex("IX_audit_differences_firm_id_client_id_engagement_id_verified_~", "audit_differences");
    migrationBuilder.DropIndex("IX_adjustment_journals_firm_id_client_id_book_id", "adjustment_journals");
    migrationBuilder.DropCheckConstraint("ck_audit_difference_values", "audit_differences");
    migrationBuilder.DropCheckConstraint("ck_adjustment_journal_values", "adjustment_journals");
    migrationBuilder.AddCheckConstraint("ck_audit_difference_values", "audit_differences",
      "length(trim(account_area)) > 0 AND length(trim(difference_type)) > 0 AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND amount <> 0 AND input_generation > 0 AND status IN ('OPEN','MANAGEMENT_RESPONDED','EVALUATED','CORRECTED') AND ((corrected AND correction_reference IS NOT NULL AND status = 'CORRECTED') OR NOT corrected)");
    migrationBuilder.DropColumn("correction_state", "audit_differences");
    migrationBuilder.DropColumn("journal_impact_hash", "audit_differences");
    migrationBuilder.DropColumn("journal_impact_json", "audit_differences");
    migrationBuilder.DropColumn("proposed_journal_id", "audit_differences");
    migrationBuilder.DropColumn("proposed_journal_revision", "audit_differences");
    migrationBuilder.DropColumn("source_reflection_reconciliation_id", "audit_differences");
    migrationBuilder.DropColumn("verified_adjusted_snapshot_id", "audit_differences");
    migrationBuilder.DropColumn("book_id", "adjustment_journals");
    migrationBuilder.DropColumn("evidence_reference", "adjustment_journals");
    migrationBuilder.DropColumn("origin", "adjustment_journals");
    migrationBuilder.DropColumn("purpose", "adjustment_journals");
    migrationBuilder.DropColumn("reason", "adjustment_journals");
    migrationBuilder.DropColumn("reversal_of_journal_id", "adjustment_journals");
    migrationBuilder.DropColumn("supersedes_journal_id", "adjustment_journals");
  }
}
