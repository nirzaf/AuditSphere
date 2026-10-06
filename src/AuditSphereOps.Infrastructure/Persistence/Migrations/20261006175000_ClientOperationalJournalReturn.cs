using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261006175000_ClientOperationalJournalReturn")]
public sealed class ClientOperationalJournalReturn : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
    CREATE FUNCTION protect_returned_journal_intent() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF OLD.status='RETURNED' AND ROW(NEW.period_id,NEW.journal_number,NEW.description,NEW.posting_date,NEW.currency)
        IS DISTINCT FROM ROW(OLD.period_id,OLD.journal_number,OLD.description,OLD.posting_date,OLD.currency) THEN
        RAISE EXCEPTION 'Returned journal intent needs a preserved revision before editing' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER client_operational_returned_intent BEFORE UPDATE ON client_operational_journals
      FOR EACH ROW EXECUTE FUNCTION protect_returned_journal_intent();
    CREATE FUNCTION protect_returned_journal_lines() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE journal_key uuid;
    BEGIN
      journal_key := CASE WHEN TG_OP='DELETE' THEN OLD.journal_id ELSE NEW.journal_id END;
      IF EXISTS (SELECT 1 FROM client_operational_journals j WHERE j.id=journal_key AND j.status='RETURNED') THEN
        RAISE EXCEPTION 'Returned journal lines need preserved revisions before editing' USING ERRCODE='23514';
      END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER client_operational_returned_lines BEFORE INSERT OR UPDATE OR DELETE ON client_operational_journal_lines
      FOR EACH ROW EXECUTE FUNCTION protect_returned_journal_lines();
    CREATE FUNCTION check_client_operational_return() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF OLD.status='SUBMITTED' AND NEW.status='RETURNED' AND NOT EXISTS (
        SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=NEW.firm_id AND d.client_id=NEW.client_id
          AND d.journal_id=NEW.id AND d.journal_revision=OLD.revision AND d.decision='RETURN'
          AND d.actor_user_id<>NEW.created_by_user_id AND length(trim(d.reason))>0) THEN
        RAISE EXCEPTION 'Returning a submitted native journal requires its independent review decision and reason' USING ERRCODE='23514';
      END IF;
      RETURN NULL;
    END $$;
    CREATE CONSTRAINT TRIGGER client_operational_return_review AFTER UPDATE ON client_operational_journals
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_operational_return();
    """);

  protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
    DROP TRIGGER client_operational_returned_intent ON client_operational_journals;
    DROP TRIGGER client_operational_returned_lines ON client_operational_journal_lines;
    DROP FUNCTION protect_returned_journal_intent();
    DROP FUNCTION protect_returned_journal_lines();
    DROP TRIGGER client_operational_return_review ON client_operational_journals;
    DROP FUNCTION check_client_operational_return();
    """);
}
