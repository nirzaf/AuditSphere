using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveStructuredExportAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE archive_structured_exports IN ACCESS EXCLUSIVE MODE;

                CREATE FUNCTION guard_archive_structured_export_insert() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE parent_status text;
                DECLARE parent_version bigint;
                BEGIN
                  -- Keep the same parent lock order as ReviewManifestAsync: archive, then manifest.
                  PERFORM 1 FROM archives
                  WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id
                    AND engagement_id = NEW.engagement_id AND id = NEW.archive_id
                  FOR SHARE;
                  IF NOT FOUND THEN
                    RAISE EXCEPTION 'Structured archive export parent is unavailable.' USING ERRCODE = '55000';
                  END IF;

                  SELECT status, version INTO parent_status, parent_version
                  FROM archive_manifests
                  WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id
                    AND engagement_id = NEW.engagement_id AND archive_id = NEW.archive_id
                    AND id = NEW.archive_manifest_id
                  FOR SHARE;
                  IF NOT FOUND OR parent_status IS DISTINCT FROM 'BUILT' OR parent_version IS DISTINCT FROM NEW.version THEN
                    RAISE EXCEPTION 'Structured archive exports require their exact built manifest.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_archive_structured_export_insert
                  BEFORE INSERT ON archive_structured_exports
                  FOR EACH ROW EXECUTE FUNCTION guard_archive_structured_export_insert();

                CREATE FUNCTION guard_archive_structured_export_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Structured archive exports are retained immutable evidence.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_archive_structured_export_mutation
                  BEFORE UPDATE OR DELETE ON archive_structured_exports
                  FOR EACH ROW EXECUTE FUNCTION guard_archive_structured_export_mutation();

                CREATE FUNCTION reject_archive_structured_export_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Structured archive exports cannot be truncated.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_archive_structured_export_truncate
                  BEFORE TRUNCATE ON archive_structured_exports
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_archive_structured_export_truncate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE archive_structured_exports IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM archive_structured_exports) THEN
                    RAISE EXCEPTION 'Cannot remove structured-export integrity guards while retained structured exports exist.' USING ERRCODE = '55000';
                  END IF;
                END;
                $$;
                DROP TRIGGER IF EXISTS trg_archive_structured_export_truncate ON archive_structured_exports;
                DROP TRIGGER IF EXISTS trg_archive_structured_export_mutation ON archive_structured_exports;
                DROP TRIGGER IF EXISTS trg_archive_structured_export_insert ON archive_structured_exports;
                DROP FUNCTION IF EXISTS reject_archive_structured_export_truncate();
                DROP FUNCTION IF EXISTS guard_archive_structured_export_mutation();
                DROP FUNCTION IF EXISTS guard_archive_structured_export_insert();
                """);
        }
    }
}
