using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261007170000_ClientOperationalPostingSnapshot")]
public sealed partial class ClientOperationalPostingSnapshot : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.AddColumn<long>(
      name: "posting_sequence",
      table: "client_operational_journals",
      type: "bigint",
      nullable: true);

    migrationBuilder.Sql("CREATE SEQUENCE client_operational_posting_sequence AS bigint START WITH 1;");
    migrationBuilder.Sql("""
      ALTER TABLE client_operational_journals DISABLE TRIGGER client_operational_journal_protected;
      ALTER TABLE client_operational_journals DISABLE TRIGGER client_operational_journal_balanced;
      WITH numbered AS (
        SELECT id, row_number() OVER (ORDER BY posted_at, id) AS posting_sequence
        FROM client_operational_journals WHERE status='POSTED'
      )
      UPDATE client_operational_journals journal
      SET posting_sequence=numbered.posting_sequence
      FROM numbered WHERE journal.id=numbered.id;
      """);
    migrationBuilder.Sql("""
      SELECT setval('client_operational_posting_sequence',
        coalesce((SELECT max(posting_sequence) FROM client_operational_journals), 1),
        EXISTS (SELECT 1 FROM client_operational_journals WHERE posting_sequence IS NOT NULL));
      ALTER TABLE client_operational_journals ENABLE TRIGGER client_operational_journal_balanced;
      ALTER TABLE client_operational_journals ENABLE TRIGGER client_operational_journal_protected;
      """, suppressTransaction: true);

    migrationBuilder.Sql("""
      CREATE INDEX ix_client_operational_journal_posting_snapshot
        ON client_operational_journals(firm_id,client_id,period_id,posting_sequence) WHERE status='POSTED';
      CREATE FUNCTION assign_client_operational_posting_sequence() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
      BEGIN
        IF TG_OP='INSERT' THEN
          IF NEW.posting_sequence IS NOT NULL THEN
            RAISE EXCEPTION 'Native posting sequence is database assigned' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END IF;
        IF NEW.posting_sequence IS DISTINCT FROM OLD.posting_sequence THEN
          RAISE EXCEPTION 'Native posting sequence is immutable' USING ERRCODE='23514';
        END IF;
        IF NEW.status='POSTED' AND OLD.status<>'POSTED' THEN
          -- Serialize allocation through transaction commit so a later visible posting
          -- can never receive a sequence below an already observed snapshot high-water mark.
          PERFORM pg_advisory_xact_lock(726524055781::bigint);
          NEW.posting_sequence := nextval(format('%I.client_operational_posting_sequence', TG_TABLE_SCHEMA)::regclass);
        ELSIF NEW.status<>'POSTED' AND NEW.posting_sequence IS NOT NULL THEN
          RAISE EXCEPTION 'Unposted native journal cannot have a posting sequence' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER z_client_operational_posting_sequence
        BEFORE INSERT OR UPDATE ON client_operational_journals
      FOR EACH ROW EXECUTE FUNCTION assign_client_operational_posting_sequence();
      """);
    migrationBuilder.Sql("""
      ALTER TABLE client_operational_journals
        DROP CONSTRAINT ck_client_operational_journal_values;
      ALTER TABLE client_operational_journals ADD CONSTRAINT ck_client_operational_journal_values
        CHECK (length(trim(journal_number)) > 0 AND length(trim(description)) > 0
          AND currency ~ '^[A-Z]{3}$' AND revision >= 1
          AND status IN ('DRAFT','SUBMITTED','RETURNED','APPROVED','POSTED')
          AND ((status='POSTED' AND posted_by_user_id IS NOT NULL AND posted_at IS NOT NULL AND posting_sequence>0)
            OR (status<>'POSTED' AND posting_sequence IS NULL)));
      """);
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql("""
      DROP TRIGGER z_client_operational_posting_sequence ON client_operational_journals;
      DROP FUNCTION assign_client_operational_posting_sequence();
      ALTER TABLE client_operational_journals DROP CONSTRAINT ck_client_operational_journal_values;
      ALTER TABLE client_operational_journals ADD CONSTRAINT ck_client_operational_journal_values
        CHECK (length(trim(journal_number)) > 0 AND length(trim(description)) > 0
          AND currency ~ '^[A-Z]{3}$' AND revision >= 1
          AND status IN ('DRAFT','SUBMITTED','RETURNED','APPROVED','POSTED')
          AND ((status='POSTED' AND posted_by_user_id IS NOT NULL AND posted_at IS NOT NULL) OR status<>'POSTED'));
      DROP INDEX ix_client_operational_journal_posting_snapshot;
      DROP SEQUENCE client_operational_posting_sequence;
      """);
    migrationBuilder.DropColumn(name: "posting_sequence", table: "client_operational_journals");
  }
}
