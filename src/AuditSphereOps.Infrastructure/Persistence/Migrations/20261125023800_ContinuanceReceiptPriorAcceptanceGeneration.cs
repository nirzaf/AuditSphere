using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261125023800_ContinuanceReceiptPriorAcceptanceGeneration")]
public sealed class ContinuanceReceiptPriorAcceptanceGeneration : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(AllowRetainedPriorGeneration);

  protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(RequireSameGeneration);

  private const string AllowRetainedPriorGeneration = """
    DO $migration$
    DECLARE definition text := pg_get_functiondef('public.enforce_assessment_command_receipt()'::regprocedure);
    BEGIN
      IF position('AND prior.generation=NEW.generation AND prior.decision IN' IN definition) = 0 THEN
        RAISE EXCEPTION 'Expected assessment receipt guard definition was not found';
      END IF;
      EXECUTE replace(definition,
        'AND prior.generation=NEW.generation AND prior.decision IN',
        'AND prior.generation<=NEW.generation AND prior.decision IN');
    END $migration$;
    """;

  private const string RequireSameGeneration = """
    DO $migration$
    DECLARE definition text := pg_get_functiondef('public.enforce_assessment_command_receipt()'::regprocedure);
    BEGIN
      IF position('AND prior.generation<=NEW.generation AND prior.decision IN' IN definition) = 0 THEN
        RAISE EXCEPTION 'Expected continuance receipt guard definition was not found';
      END IF;
      EXECUTE replace(definition,
        'AND prior.generation<=NEW.generation AND prior.decision IN',
        'AND prior.generation=NEW.generation AND prior.decision IN');
    END $migration$;
    """;
}
