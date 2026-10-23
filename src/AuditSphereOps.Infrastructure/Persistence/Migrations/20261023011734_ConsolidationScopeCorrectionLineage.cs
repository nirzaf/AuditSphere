using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidationScopeCorrectionLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_component_id",
                table: "consolidation_scope_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_scope_version_id",
                table: "consolidation_scope_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_scope_versions_firm_id_group_id_supersedes_sc~",
                table: "consolidation_scope_versions",
                columns: new[] { "firm_id", "group_id", "supersedes_scope_version_id", "supersedes_component_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_consolidation_scope_supersedes_pair",
                table: "consolidation_scope_versions",
                sql: "(supersedes_scope_version_id IS NULL) = (supersedes_component_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_consolidation_scope_versions_consolidation_components_firm_~",
                table: "consolidation_scope_versions",
                columns: new[] { "firm_id", "group_id", "supersedes_scope_version_id", "supersedes_component_id" },
                principalTable: "consolidation_components",
                principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION guard_consolidation_scope_correction_lineage()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE source_period_id uuid;
                BEGIN
                  IF TG_OP = 'UPDATE' AND (
                    NEW.supersedes_scope_version_id IS DISTINCT FROM OLD.supersedes_scope_version_id OR
                    NEW.supersedes_component_id IS DISTINCT FROM OLD.supersedes_component_id) THEN
                    RAISE EXCEPTION 'Consolidation correction source identities are immutable after scope creation.'
                      USING ERRCODE = '23514';
                  END IF;

                  IF (NEW.supersedes_scope_version_id IS NULL) <> (NEW.supersedes_component_id IS NULL) THEN
                    RAISE EXCEPTION 'A consolidation correction source requires both scope and component identities.'
                      USING ERRCODE = '23514';
                  END IF;

                  IF NEW.supersedes_scope_version_id IS NOT NULL THEN
                    -- Serialize source-period changes with correction creation; the composite FK separately
                    -- proves that the selected component belongs to this exact source scope and group.
                    SELECT source.period_id INTO source_period_id
                    FROM consolidation_scope_versions AS source
                    WHERE source.firm_id = NEW.firm_id
                      AND source.group_id = NEW.group_id
                      AND source.id = NEW.supersedes_scope_version_id
                    FOR SHARE;
                    IF NOT FOUND OR source_period_id IS DISTINCT FROM NEW.period_id THEN
                      RAISE EXCEPTION 'A consolidation correction source must use the same group reporting period.'
                        USING ERRCODE = '23514';
                    END IF;
                  END IF;

                  IF TG_OP = 'UPDATE' AND NEW.period_id IS DISTINCT FROM OLD.period_id AND EXISTS (
                    SELECT 1 FROM consolidation_scope_versions AS correction
                    WHERE correction.firm_id = OLD.firm_id
                      AND correction.group_id = OLD.group_id
                      AND correction.supersedes_scope_version_id = OLD.id
                      AND correction.period_id IS DISTINCT FROM NEW.period_id) THEN
                    RAISE EXCEPTION 'A source scope period cannot invalidate a persisted correction lineage.'
                      USING ERRCODE = '23514';
                  END IF;

                  RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_consolidation_scope_correction_lineage
                  BEFORE INSERT OR UPDATE ON consolidation_scope_versions
                  FOR EACH ROW EXECUTE FUNCTION guard_consolidation_scope_correction_lineage();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE consolidation_scope_versions IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                  IF EXISTS (
                    SELECT 1 FROM consolidation_scope_versions
                    WHERE supersedes_scope_version_id IS NOT NULL OR supersedes_component_id IS NOT NULL) THEN
                    RAISE EXCEPTION 'Consolidation correction lineage downgrade would discard recorded provenance.'
                      USING ERRCODE = '55000';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_consolidation_scope_correction_lineage ON consolidation_scope_versions;
                DROP FUNCTION IF EXISTS guard_consolidation_scope_correction_lineage();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_consolidation_scope_versions_consolidation_components_firm_~",
                table: "consolidation_scope_versions");

            migrationBuilder.DropIndex(
                name: "IX_consolidation_scope_versions_firm_id_group_id_supersedes_sc~",
                table: "consolidation_scope_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_consolidation_scope_supersedes_pair",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "supersedes_component_id",
                table: "consolidation_scope_versions");

            migrationBuilder.DropColumn(
                name: "supersedes_scope_version_id",
                table: "consolidation_scope_versions");
        }
    }
}
