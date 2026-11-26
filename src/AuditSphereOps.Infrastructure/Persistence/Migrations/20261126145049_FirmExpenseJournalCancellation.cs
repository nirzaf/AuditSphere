using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmExpenseJournalCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_journal_values",
                table: "firm_journals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_journal_values",
                table: "firm_journals",
                sql: "length(journal_number) > 0 AND length(source_kind) > 0 AND length(source_key) > 0 AND source_revision >= 1 AND length(posting_purpose) > 0 AND currency ~ '^[A-Z]{3}$' AND status IN ('DRAFT','REVIEW_REQUIRED','APPROVED','POSTED','CANCELLED')");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_cancelled_journal_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_TABLE_NAME = 'firm_journals' THEN
                    IF TG_OP = 'INSERT' THEN
                      IF NEW.status = 'CANCELLED' THEN
                        RAISE EXCEPTION 'Cancelled firm journals cannot be inserted.' USING ERRCODE = '55000';
                      END IF;
                    ELSIF OLD.status = 'CANCELLED' THEN
                      RAISE EXCEPTION 'Cancelled firm journals are immutable.' USING ERRCODE = '55000';
                    ELSIF TG_OP = 'UPDATE' AND NEW.status = 'CANCELLED' THEN
                      IF OLD.status NOT IN ('REVIEW_REQUIRED','APPROVED') OR
                         (to_jsonb(NEW) - 'status') IS DISTINCT FROM (to_jsonb(OLD) - 'status') THEN
                        RAISE EXCEPTION 'Only an unchanged review journal may be cancelled.' USING ERRCODE = '55000';
                      END IF;
                    END IF;
                  ELSIF TG_TABLE_NAME = 'firm_journal_lines' THEN
                    IF TG_OP = 'INSERT' THEN
                      IF EXISTS (SELECT 1 FROM firm_journals WHERE firm_id = NEW.firm_id AND id = NEW.journal_id AND status = 'CANCELLED') THEN
                        RAISE EXCEPTION 'Lines of cancelled firm journals are immutable.' USING ERRCODE = '55000';
                      END IF;
                    ELSIF TG_OP = 'UPDATE' THEN
                      IF EXISTS (SELECT 1 FROM firm_journals WHERE firm_id = OLD.firm_id AND id = OLD.journal_id AND status = 'CANCELLED') OR
                         EXISTS (SELECT 1 FROM firm_journals WHERE firm_id = NEW.firm_id AND id = NEW.journal_id AND status = 'CANCELLED') THEN
                        RAISE EXCEPTION 'Lines of cancelled firm journals are immutable.' USING ERRCODE = '55000';
                      END IF;
                    ELSIF EXISTS (SELECT 1 FROM firm_journals WHERE firm_id = OLD.firm_id AND id = OLD.journal_id AND status = 'CANCELLED') THEN
                      RAISE EXCEPTION 'Lines of cancelled firm journals are immutable.' USING ERRCODE = '55000';
                    END IF;
                  END IF;
                  IF TG_OP = 'DELETE' THEN
                    RETURN OLD;
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_firm_journals_cancelled_guard
                  BEFORE INSERT OR UPDATE OR DELETE ON firm_journals
                  FOR EACH ROW EXECUTE FUNCTION prevent_cancelled_journal_mutation();
                CREATE TRIGGER trg_firm_journal_lines_cancelled_guard
                  BEFORE INSERT OR UPDATE OR DELETE ON firm_journal_lines
                  FOR EACH ROW EXECUTE FUNCTION prevent_cancelled_journal_mutation();

                CREATE OR REPLACE FUNCTION prevent_cancelled_firm_journal_posting()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM firm_journals WHERE firm_id = NEW.firm_id AND id = NEW.journal_id AND status = 'CANCELLED') THEN
                    RAISE EXCEPTION 'Cancelled firm journals cannot be posted.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_firm_postings_reject_cancelled_journal
                  BEFORE INSERT ON firm_postings
                  FOR EACH ROW EXECUTE FUNCTION prevent_cancelled_firm_journal_posting();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_firm_postings_reject_cancelled_journal ON firm_postings;
                DROP FUNCTION IF EXISTS prevent_cancelled_firm_journal_posting();
                DROP TRIGGER IF EXISTS trg_firm_journal_lines_cancelled_guard ON firm_journal_lines;
                DROP TRIGGER IF EXISTS trg_firm_journals_cancelled_guard ON firm_journals;
                DROP FUNCTION IF EXISTS prevent_cancelled_journal_mutation();
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_journal_values",
                table: "firm_journals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_journal_values",
                table: "firm_journals",
                sql: "length(journal_number) > 0 AND length(source_kind) > 0 AND length(source_key) > 0 AND source_revision >= 1 AND length(posting_purpose) > 0 AND currency ~ '^[A-Z]{3}$' AND status IN ('DRAFT','REVIEW_REQUIRED','APPROVED','POSTED')");
        }
    }
}
