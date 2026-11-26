using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

public partial class ConsolidationMatchReservations : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql(ConsolidationMatchReservationSql.Preflight);
    migrationBuilder.Sql("""
      CREATE TABLE consolidation_match_reservations (
        firm_id uuid NOT NULL,
        group_id uuid NOT NULL,
        scope_version_id uuid NOT NULL,
        intercompany_match_id uuid NOT NULL,
        consolidation_journal_id uuid NOT NULL,
        CONSTRAINT pk_consolidation_match_reservations
          PRIMARY KEY (firm_id, group_id, scope_version_id, intercompany_match_id),
        CONSTRAINT fk_consolidation_match_reservations_match_scope
          FOREIGN KEY (firm_id, group_id, scope_version_id, intercompany_match_id)
          REFERENCES intercompany_matches (firm_id, group_id, scope_version_id, id)
          ON DELETE RESTRICT,
        CONSTRAINT fk_consolidation_match_reservations_journal_scope
          FOREIGN KEY (firm_id, group_id, scope_version_id, consolidation_journal_id)
          REFERENCES consolidation_journals (firm_id, group_id, scope_version_id, id)
          ON DELETE RESTRICT
      );
      """);
    migrationBuilder.Sql("""
      CREATE INDEX ix_consolidation_match_reservations_scope_journal
      ON consolidation_match_reservations (firm_id, group_id, scope_version_id, consolidation_journal_id);
      """);
    migrationBuilder.Sql(ConsolidationMatchReservationSql.Backfill);
  }

  protected override void Down(MigrationBuilder migrationBuilder) =>
    migrationBuilder.DropTable(name: "consolidation_match_reservations");
}
