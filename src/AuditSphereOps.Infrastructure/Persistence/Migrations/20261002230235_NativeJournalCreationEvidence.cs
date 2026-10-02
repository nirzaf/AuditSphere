using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeJournalCreationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_adjustment_journal_action",
                table: "adjustment_journal_actions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_adjustment_journal_action",
                table: "adjustment_journal_actions",
                sql: "((action='CREATE' AND old_revision=0 AND old_status='NOT_CREATED' AND new_revision=1 AND new_status='Draft') OR (action IN ('UPDATE','SUBMIT','RETURN','POST','REVERSE') AND old_revision >= 1)) AND actor_epoch >= 1 AND new_revision >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000 AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM adjustment_journal_actions WHERE action='CREATE') THEN
                    RAISE EXCEPTION 'Retained journal creation evidence prevents migration rollback';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_adjustment_journal_action",
                table: "adjustment_journal_actions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_adjustment_journal_action",
                table: "adjustment_journal_actions",
                sql: "action IN ('UPDATE','SUBMIT','RETURN','POST','REVERSE') AND actor_epoch >= 1 AND old_revision >= 1 AND new_revision >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000 AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000");
        }
    }
}
