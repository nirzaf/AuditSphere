using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261128120000_ClientFiscalLeapDayTransitionGuard")]
public sealed class ClientFiscalLeapDayTransitionGuard : Migration
{
  // The possible-pair check remains valid for retained February 29 rows. A transition
  // guard, rather than a narrower row constraint, preserves their non-fiscal revisions.
  protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
    CREATE FUNCTION guard_client_fiscal_leap_day_transition() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF NEW.fiscal_year_start_month = 2 AND NEW.fiscal_year_start_day = 29 THEN
        IF TG_OP = 'INSERT' THEN
          RAISE EXCEPTION 'February 29 requires an approved non-leap-year rollover policy. Obtain that policy approval or choose another valid fiscal start date.'
            USING ERRCODE = '23514', CONSTRAINT = 'ck_client_fiscal_leap_day_transition';
        ELSIF ROW(OLD.fiscal_year_start_month, OLD.fiscal_year_start_day)
          IS DISTINCT FROM ROW(NEW.fiscal_year_start_month, NEW.fiscal_year_start_day) THEN
          RAISE EXCEPTION 'February 29 requires an approved non-leap-year rollover policy. Obtain that policy approval or choose another valid fiscal start date.'
            USING ERRCODE = '23514', CONSTRAINT = 'ck_client_fiscal_leap_day_transition';
        END IF;
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER trg_client_fiscal_leap_day_transition
      BEFORE INSERT OR UPDATE ON client_accounting_profiles
      FOR EACH ROW EXECUTE FUNCTION guard_client_fiscal_leap_day_transition();
    """);

  protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
    DROP TRIGGER trg_client_fiscal_leap_day_transition ON client_accounting_profiles;
    DROP FUNCTION guard_client_fiscal_leap_day_transition();
    """);
}
