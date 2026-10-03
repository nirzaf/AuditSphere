using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeJournalManagementEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION enforce_management_decision_revision() RETURNS trigger AS $$
                DECLARE parent adjustment_journals%ROWTYPE;
                BEGIN
                  SELECT * INTO parent FROM adjustment_journals WHERE id=NEW.journal_id FOR UPDATE;
                  IF parent.id IS NULL OR parent.status<>'Draft' OR parent.revision<>NEW.journal_revision OR
                     parent.firm_id<>NEW.firm_id OR parent.client_id<>NEW.client_id OR parent.engagement_id<>NEW.engagement_id THEN
                    RAISE EXCEPTION 'Management evidence requires the exact current draft scope and revision';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER adjustment_journal_management_decision_revision_bound
                  BEFORE INSERT ON adjustment_journal_management_decisions
                  FOR EACH ROW EXECUTE FUNCTION enforce_management_decision_revision();
                CREATE TRIGGER adjustment_journal_management_decisions_append_only
                  BEFORE UPDATE OR DELETE ON adjustment_journal_management_decisions
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_management_journal_revision() RETURNS trigger AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM adjustment_journal_management_decisions
                    WHERE journal_id=OLD.id AND journal_revision=OLD.revision)
                    AND ROW(OLD.firm_id,OLD.client_id,OLD.engagement_id,OLD.revision,OLD.base_dataset_id,OLD.journal_number,OLD.purpose,OLD.period_id,OLD.book_id,
                      OLD.basis,OLD.currency,OLD.origin,OLD.reason,OLD.evidence_reference,OLD.supersedes_journal_id,
                      OLD.reversal_of_journal_id,OLD.created_by_user_id,OLD.created_at)
                    IS DISTINCT FROM ROW(NEW.firm_id,NEW.client_id,NEW.engagement_id,NEW.revision,NEW.base_dataset_id,NEW.journal_number,NEW.purpose,NEW.period_id,NEW.book_id,
                      NEW.basis,NEW.currency,NEW.origin,NEW.reason,NEW.evidence_reference,NEW.supersedes_journal_id,
                      NEW.reversal_of_journal_id,NEW.created_by_user_id,NEW.created_at) THEN
                    RAISE EXCEPTION 'Retained management decision freezes the exact journal revision';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER adjustment_journal_management_revision_frozen
                  BEFORE UPDATE ON adjustment_journals FOR EACH ROW EXECUTE FUNCTION enforce_management_journal_revision();
                CREATE OR REPLACE FUNCTION enforce_adjustment_line_frozen() RETURNS trigger AS $$
                DECLARE parent adjustment_journals%ROWTYPE;
                BEGIN
                  IF TG_OP IN ('DELETE','UPDATE') THEN
                    SELECT * INTO parent FROM adjustment_journals WHERE id=OLD.journal_id FOR SHARE;
                    IF parent.id IS NULL OR parent.status NOT IN ('Draft','Returned') OR EXISTS
                      (SELECT 1 FROM adjustment_journal_management_decisions WHERE journal_id=parent.id AND journal_revision=parent.revision) THEN
                      RAISE EXCEPTION 'adjustment journal % lines are frozen', OLD.journal_id;
                    END IF;
                  END IF;
                  IF TG_OP IN ('INSERT','UPDATE') THEN
                    SELECT * INTO parent FROM adjustment_journals WHERE id=NEW.journal_id FOR SHARE;
                    IF parent.id IS NULL OR parent.status NOT IN ('Draft','Returned') OR EXISTS
                      (SELECT 1 FROM adjustment_journal_management_decisions WHERE journal_id=parent.id AND journal_revision=parent.revision) THEN
                      RAISE EXCEPTION 'adjustment journal % lines are frozen', NEW.journal_id;
                    END IF;
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_adjustment_journal_action",
                table: "adjustment_journal_actions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_adjustment_journal_action",
                table: "adjustment_journal_actions",
                sql: "((action='CREATE' AND old_revision=0 AND old_status='NOT_CREATED' AND new_revision=1 AND new_status='Draft') OR (action IN ('UPDATE','SUBMIT','RETURN','POST','REVERSE') AND old_revision >= 1) OR (action='MANAGEMENT' AND old_revision >= 1 AND old_revision=new_revision AND old_status='Draft' AND new_status='Draft')) AND actor_epoch >= 1 AND new_revision >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000 AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM adjustment_journal_actions WHERE action='MANAGEMENT') OR
                     EXISTS (SELECT 1 FROM adjustment_journal_management_decisions) THEN
                    RAISE EXCEPTION 'Retained management evidence prevents migration rollback';
                  END IF;
                END $$;
                DROP TRIGGER adjustment_journal_management_decision_revision_bound ON adjustment_journal_management_decisions;
                DROP FUNCTION enforce_management_decision_revision();
                DROP TRIGGER adjustment_journal_management_decisions_append_only ON adjustment_journal_management_decisions;
                DROP TRIGGER adjustment_journal_management_revision_frozen ON adjustment_journals;
                DROP FUNCTION enforce_management_journal_revision();
                CREATE OR REPLACE FUNCTION enforce_adjustment_line_frozen() RETURNS trigger AS $$
                DECLARE parent_state text;
                BEGIN
                  IF TG_OP IN ('DELETE','UPDATE') THEN
                    SELECT status INTO parent_state FROM adjustment_journals WHERE id=OLD.journal_id FOR SHARE;
                    IF parent_state IS NULL OR parent_state NOT IN ('Draft','Returned') THEN
                      RAISE EXCEPTION 'adjustment journal % lines are frozen', OLD.journal_id;
                    END IF;
                  END IF;
                  IF TG_OP IN ('INSERT','UPDATE') THEN
                    SELECT status INTO parent_state FROM adjustment_journals WHERE id=NEW.journal_id FOR SHARE;
                    IF parent_state IS NULL OR parent_state NOT IN ('Draft','Returned') THEN
                      RAISE EXCEPTION 'adjustment journal % lines are frozen', NEW.journal_id;
                    END IF;
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_adjustment_journal_action",
                table: "adjustment_journal_actions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_adjustment_journal_action",
                table: "adjustment_journal_actions",
                sql: "((action='CREATE' AND old_revision=0 AND old_status='NOT_CREATED' AND new_revision=1 AND new_status='Draft') OR (action IN ('UPDATE','SUBMIT','RETURN','POST','REVERSE') AND old_revision >= 1)) AND actor_epoch >= 1 AND new_revision >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000 AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000");
        }
    }
}
