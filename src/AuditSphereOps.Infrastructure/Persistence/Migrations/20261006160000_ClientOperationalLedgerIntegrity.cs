using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261006160000_ClientOperationalLedgerIntegrity")]
public sealed class ClientOperationalLedgerIntegrity : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql("""
      DO $$ BEGIN
        IF EXISTS (
          SELECT 1 FROM client_operational_journals j LEFT JOIN client_operational_journal_lines l
            ON l.firm_id=j.firm_id AND l.client_id=j.client_id AND l.journal_id=j.id
          WHERE j.status IN ('SUBMITTED','POSTED') GROUP BY j.id
          HAVING count(l.id) NOT BETWEEN 2 AND 100 OR coalesce(sum(l.debit),0)<=0
            OR coalesce(sum(l.debit),0)<>coalesce(sum(l.credit),0)) OR EXISTS (
          SELECT 1 FROM client_operational_journals j WHERE j.status='POSTED' AND NOT EXISTS (
            SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=j.firm_id AND d.client_id=j.client_id
              AND d.journal_id=j.id AND d.journal_revision=j.revision-1 AND d.decision='APPROVE'
              AND d.actor_user_id=j.posted_by_user_id AND d.actor_user_id<>j.created_by_user_id)) THEN
          RAISE EXCEPTION 'Existing native journal integrity must be resolved before installing protection' USING ERRCODE='23514';
        END IF;
      END $$;
      CREATE FUNCTION protect_client_operational_journal() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE p client_reporting_periods%ROWTYPE;
      BEGIN
        IF TG_OP = 'INSERT' THEN
          IF NEW.status <> 'DRAFT' OR NEW.revision <> 1 THEN
            RAISE EXCEPTION 'Native journals must begin as draft revision one' USING ERRCODE = '23514';
          END IF;
          SELECT * INTO p FROM client_reporting_periods
            WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.period_id FOR UPDATE;
          IF NOT FOUND OR p.status='CLOSED' OR NEW.posting_date NOT BETWEEN p.start_date AND p.end_date OR p.currency<>NEW.currency THEN
            RAISE EXCEPTION 'Native draft needs an open matching period' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END IF;
        IF OLD.status = 'POSTED' THEN
          RAISE EXCEPTION 'Posted native journal is immutable; prepare a linked correction' USING ERRCODE = '23514';
        END IF;
        IF TG_OP = 'DELETE' THEN
          IF OLD.status <> 'DRAFT' THEN
            RAISE EXCEPTION 'Submitted journal history cannot be deleted' USING ERRCODE = '23514';
          END IF;
          RETURN OLD;
        END IF;
        IF ROW(NEW.id, NEW.firm_id, NEW.client_id, NEW.created_by_user_id, NEW.created_at)
          IS DISTINCT FROM ROW(OLD.id, OLD.firm_id, OLD.client_id, OLD.created_by_user_id, OLD.created_at) THEN
          RAISE EXCEPTION 'Native journal ownership and creator are immutable' USING ERRCODE = '23514';
        END IF;
        IF OLD.status = 'SUBMITTED' AND
          ROW(NEW.period_id, NEW.journal_number, NEW.description, NEW.posting_date, NEW.currency, NEW.submitted_at)
          IS DISTINCT FROM ROW(OLD.period_id, OLD.journal_number, OLD.description, OLD.posting_date, OLD.currency, OLD.submitted_at) THEN
          RAISE EXCEPTION 'Submitted native journal intent is frozen' USING ERRCODE = '23514';
        END IF;
        IF OLD.status = 'SUBMITTED' AND NEW.status=OLD.status AND NEW.revision<>OLD.revision THEN
          RAISE EXCEPTION 'Submitted revision is frozen until a reviewed transition' USING ERRCODE='23514';
        END IF;
        IF NEW.status IS DISTINCT FROM OLD.status AND NOT (
          (OLD.status IN ('DRAFT','RETURNED') AND NEW.status = 'SUBMITTED') OR
          (OLD.status = 'SUBMITTED' AND NEW.status IN ('RETURNED','POSTED'))) THEN
          RAISE EXCEPTION 'Invalid native journal state transition' USING ERRCODE = '23514';
        END IF;
        IF NEW.status IS DISTINCT FROM OLD.status AND NEW.revision <> OLD.revision + 1 THEN
          RAISE EXCEPTION 'Native journal transition needs the next revision' USING ERRCODE = '23514';
        END IF;
        IF NEW.status = 'SUBMITTED' AND NEW.submitted_at IS NULL THEN
          RAISE EXCEPTION 'Submission needs its recorded timestamp' USING ERRCODE = '23514';
        END IF;
        IF NEW.status IN ('SUBMITTED','POSTED') THEN
          SELECT * INTO p FROM client_reporting_periods
            WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id AND id = NEW.period_id FOR UPDATE;
          IF NOT FOUND OR p.status = 'CLOSED' OR NEW.posting_date NOT BETWEEN p.start_date AND p.end_date
            OR p.currency <> NEW.currency OR (NEW.status='POSTED' AND NEW.posted_by_user_id = NEW.created_by_user_id) THEN
            RAISE EXCEPTION 'Native posting needs an open matching period and independent reviewer' USING ERRCODE = '23514';
          END IF;
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER client_operational_journal_protected BEFORE INSERT OR UPDATE OR DELETE
        ON client_operational_journals FOR EACH ROW EXECUTE FUNCTION protect_client_operational_journal();

      CREATE FUNCTION check_native_journals_before_period_close() RETURNS trigger LANGUAGE plpgsql AS $$
      BEGIN
        IF NEW.status='CLOSED' AND OLD.status IS DISTINCT FROM NEW.status AND EXISTS (
          SELECT 1 FROM client_operational_journals j WHERE j.firm_id=NEW.firm_id AND j.client_id=NEW.client_id
            AND j.period_id=NEW.id AND j.status<>'POSTED') THEN
          RAISE EXCEPTION 'Unposted native journals block period close' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER native_journals_period_close_guard BEFORE UPDATE ON client_reporting_periods
        FOR EACH ROW EXECUTE FUNCTION check_native_journals_before_period_close();

      CREATE FUNCTION protect_client_operational_line() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE j client_operational_journals%ROWTYPE;
      BEGIN
        IF TG_OP = 'UPDATE' AND ROW(NEW.id, NEW.firm_id, NEW.client_id, NEW.journal_id)
          IS DISTINCT FROM ROW(OLD.id, OLD.firm_id, OLD.client_id, OLD.journal_id) THEN
          RAISE EXCEPTION 'Native journal lines cannot change ownership' USING ERRCODE = '23514';
        END IF;
        SELECT * INTO j FROM client_operational_journals
          WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.journal_id ELSE NEW.journal_id END FOR UPDATE;
        IF NOT FOUND OR j.status NOT IN ('DRAFT','RETURNED') THEN
          RAISE EXCEPTION 'Native lines are frozen after submission' USING ERRCODE = '23514';
        END IF;
        IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER client_operational_lines_protected BEFORE INSERT OR UPDATE OR DELETE
        ON client_operational_journal_lines FOR EACH ROW EXECUTE FUNCTION protect_client_operational_line();

      CREATE FUNCTION protect_client_operational_decision() RETURNS trigger LANGUAGE plpgsql AS $$
      BEGIN
        RAISE EXCEPTION 'Native journal review decisions are append only' USING ERRCODE = '23514';
      END $$;
      CREATE TRIGGER client_operational_decisions_append_only BEFORE UPDATE OR DELETE
        ON client_operational_journal_decisions FOR EACH ROW EXECUTE FUNCTION protect_client_operational_decision();

      CREATE FUNCTION check_client_operational_journal() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE j client_operational_journals%ROWTYPE; journal_key uuid; n bigint; debit_total numeric; credit_total numeric;
      BEGIN
        IF TG_TABLE_NAME = 'client_operational_journals' THEN
          journal_key := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
        ELSE
          journal_key := CASE WHEN TG_OP = 'DELETE' THEN OLD.journal_id ELSE NEW.journal_id END;
        END IF;
        SELECT * INTO j FROM client_operational_journals WHERE id = journal_key;
        IF NOT FOUND OR j.status NOT IN ('SUBMITTED','POSTED') THEN RETURN NULL; END IF;
        SELECT count(*), coalesce(sum(debit),0), coalesce(sum(credit),0) INTO n,debit_total,credit_total
          FROM client_operational_journal_lines WHERE firm_id=j.firm_id AND client_id=j.client_id AND journal_id=j.id;
        IF n NOT BETWEEN 2 AND 100 OR debit_total <= 0 OR debit_total <> credit_total THEN
          RAISE EXCEPTION 'Native submitted or posted journal must balance' USING ERRCODE = '23514';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM client_accounting_profiles p WHERE p.firm_id=j.firm_id AND p.client_id=j.client_id
          AND p.source_mode='NATIVE_BOOKKEEPING' AND p.functional_currency=j.currency) OR
          (SELECT count(*) FROM client_chart_versions c WHERE c.firm_id=j.firm_id AND c.client_id=j.client_id
            AND c.status='APPROVED' AND c.effective_from<=j.posting_date AND (c.effective_to IS NULL OR c.effective_to>=j.posting_date))<>1 OR EXISTS (
          SELECT 1 FROM client_operational_journal_lines l LEFT JOIN client_accounts a
            ON a.firm_id=l.firm_id AND a.client_id=l.client_id AND a.id=l.client_account_id
          LEFT JOIN client_chart_versions c ON c.id=a.chart_version_id AND c.firm_id=a.firm_id AND c.client_id=a.client_id
          WHERE l.journal_id=j.id AND (a.id IS NULL OR NOT a.is_posting OR a.status<>'ACTIVE'
            OR a.account_code<>l.account_code OR a.account_name<>l.account_name OR c.status<>'APPROVED'
            OR c.effective_from>j.posting_date OR c.effective_to<j.posting_date)) THEN
          RAISE EXCEPTION 'Native journal needs its matching profile and unique approved posting chart' USING ERRCODE = '23514';
        END IF;
        IF j.status = 'POSTED' AND NOT EXISTS (
          SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=j.firm_id AND d.client_id=j.client_id
            AND d.journal_id=j.id AND d.journal_revision=j.revision-1 AND d.decision='APPROVE'
            AND d.actor_user_id=j.posted_by_user_id AND d.actor_user_id<>j.created_by_user_id) THEN
          RAISE EXCEPTION 'Native posting needs an independent decision for its exact submitted revision' USING ERRCODE = '23514';
        END IF;
        RETURN NULL;
      END $$;
      CREATE CONSTRAINT TRIGGER client_operational_journal_balanced AFTER INSERT OR UPDATE
        ON client_operational_journals DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_operational_journal();
      CREATE CONSTRAINT TRIGGER client_operational_lines_balanced AFTER INSERT OR UPDATE OR DELETE
        ON client_operational_journal_lines DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_operational_journal();
      CREATE CONSTRAINT TRIGGER client_operational_decision_balanced AFTER INSERT
        ON client_operational_journal_decisions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_operational_journal();
      """);
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql("""
      DROP TRIGGER client_operational_decision_balanced ON client_operational_journal_decisions;
      DROP TRIGGER client_operational_lines_balanced ON client_operational_journal_lines;
      DROP TRIGGER client_operational_journal_balanced ON client_operational_journals;
      DROP TRIGGER client_operational_decisions_append_only ON client_operational_journal_decisions;
      DROP TRIGGER client_operational_lines_protected ON client_operational_journal_lines;
      DROP TRIGGER client_operational_journal_protected ON client_operational_journals;
      DROP TRIGGER native_journals_period_close_guard ON client_reporting_periods;
      DROP FUNCTION check_client_operational_journal();
      DROP FUNCTION protect_client_operational_decision();
      DROP FUNCTION protect_client_operational_line();
      DROP FUNCTION protect_client_operational_journal();
      DROP FUNCTION check_native_journals_before_period_close();
      """);
  }
}
