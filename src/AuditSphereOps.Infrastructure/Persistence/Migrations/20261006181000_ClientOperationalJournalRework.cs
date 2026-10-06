using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AuditSphereOps.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261006181000_ClientOperationalJournalRework")]
public sealed class ClientOperationalJournalRework : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION protect_client_operational_journal() RETURNS trigger LANGUAGE plpgsql AS $$
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
          (OLD.status = 'RETURNED' AND NEW.status = 'DRAFT') OR
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
    CREATE OR REPLACE FUNCTION protect_returned_journal_intent() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF OLD.status='RETURNED' AND NEW.status='DRAFT' THEN
        IF ROW(NEW.period_id,NEW.journal_number,NEW.currency) IS DISTINCT FROM ROW(OLD.period_id,OLD.journal_number,OLD.currency)
          OR NEW.revision<>OLD.revision+1 OR NOT EXISTS (
          SELECT 1 FROM client_operational_journal_snapshots s JOIN client_operational_journal_decisions d
            ON d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.journal_id=s.journal_id AND d.journal_revision=s.journal_revision
          WHERE s.firm_id=OLD.firm_id AND s.client_id=OLD.client_id AND s.journal_id=OLD.id
            AND s.journal_revision=OLD.revision-1 AND d.decision='RETURN' AND d.actor_user_id<>OLD.created_by_user_id
            AND ((s.snapshot_json->'journal') - 'status' - 'revision' - 'submitted_at' - 'posted_by_user_id' - 'posted_at')
              = (to_jsonb(OLD) - 'status' - 'revision' - 'submitted_at' - 'posted_by_user_id' - 'posted_at')
            AND s.snapshot_json->'lines' = (SELECT jsonb_agg(to_jsonb(l) ORDER BY l.line_number)
              FROM client_operational_journal_lines l WHERE l.firm_id=OLD.firm_id AND l.client_id=OLD.client_id AND l.journal_id=OLD.id)) THEN
          RAISE EXCEPTION 'Rework needs the matching immutable submitted content and return decision' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END IF;
      IF OLD.status='RETURNED' AND ROW(NEW.period_id,NEW.journal_number,NEW.description,NEW.posting_date,NEW.currency)
        IS DISTINCT FROM ROW(OLD.period_id,OLD.journal_number,OLD.description,OLD.posting_date,OLD.currency) THEN
        RAISE EXCEPTION 'Returned journal intent needs a preserved revision before editing' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $$;
    """);
  protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION protect_client_operational_journal() RETURNS trigger LANGUAGE plpgsql AS $$
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
    CREATE OR REPLACE FUNCTION protect_returned_journal_intent() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF OLD.status='RETURNED' AND ROW(NEW.period_id,NEW.journal_number,NEW.description,NEW.posting_date,NEW.currency)
        IS DISTINCT FROM ROW(OLD.period_id,OLD.journal_number,OLD.description,OLD.posting_date,OLD.currency) THEN
        RAISE EXCEPTION 'Returned journal intent needs a preserved revision before editing' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $$;
    """);
}
