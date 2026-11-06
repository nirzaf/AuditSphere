using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveAudienceAndReviewedManifestIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_structured_export_values",
                table: "archive_structured_exports");
            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_structured_export_values",
                table: "archive_structured_exports",
                sql: "version >= 1 AND schema IN ('records-export.v1','records-export.v2') AND length(trim(payload_json)) > 0 AND content_hash ~ '^[0-9a-f]{64}$' AND byte_count > 0");

            migrationBuilder.Sql("""
                CREATE FUNCTION guard_archive_manifest_entry_insert() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE parent_status text;
                BEGIN
                  SELECT status INTO parent_status
                  FROM archive_manifests
                  WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id
                    AND engagement_id = NEW.engagement_id AND id = NEW.archive_manifest_id
                  FOR SHARE;
                  IF parent_status IS NULL THEN
                    RAISE EXCEPTION 'Archive manifest parent is unavailable.' USING ERRCODE = '55000';
                  END IF;
                  IF parent_status <> 'BUILT' THEN
                    RAISE EXCEPTION 'Reviewed archive manifests cannot accept new entries.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_archive_manifest_entry_insert
                  BEFORE INSERT ON archive_manifest_entries
                  FOR EACH ROW EXECUTE FUNCTION guard_archive_manifest_entry_insert();

                CREATE FUNCTION guard_archive_manifest_entry_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Archive manifest entries are append-only after build.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_archive_manifest_entry_mutation
                  BEFORE UPDATE OR DELETE ON archive_manifest_entries
                  FOR EACH ROW EXECUTE FUNCTION guard_archive_manifest_entry_mutation();

                CREATE FUNCTION reject_archive_manifest_entry_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Archive manifest entries cannot be truncated.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_archive_manifest_entry_truncate
                  BEFORE TRUNCATE ON archive_manifest_entries
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_archive_manifest_entry_truncate();

                CREATE FUNCTION guard_archive_manifest_review_state() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'Archive manifests are retained evidence.' USING ERRCODE = '55000';
                  END IF;
                  IF OLD.status = 'REVIEWED' THEN
                    RAISE EXCEPTION 'Reviewed archive manifests are immutable.' USING ERRCODE = '55000';
                  END IF;
                  IF NEW.status NOT IN ('BUILT','REVIEWED') THEN
                    RAISE EXCEPTION 'Unsupported archive manifest state transition.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_archive_manifest_review_state
                  BEFORE UPDATE OR DELETE ON archive_manifests
                  FOR EACH ROW EXECUTE FUNCTION guard_archive_manifest_review_state();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE archives IN ACCESS EXCLUSIVE MODE;
                LOCK TABLE archive_manifest_entries IN ACCESS EXCLUSIVE MODE;
                LOCK TABLE archive_manifests IN ACCESS EXCLUSIVE MODE;
                LOCK TABLE archive_structured_exports IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM archive_manifests) THEN
                    RAISE EXCEPTION 'Cannot remove archive-manifest integrity guards while retained manifests exist.' USING ERRCODE = '55000';
                  END IF;
                  IF EXISTS (SELECT 1 FROM archive_structured_exports WHERE schema = 'records-export.v2') THEN
                    RAISE EXCEPTION 'Cannot remove engagement-scoped archive schemas while v2 payloads remain.' USING ERRCODE = '55000';
                  END IF;
                END;
                $$;
                DROP TRIGGER IF EXISTS trg_archive_manifest_review_state ON archive_manifests;
                DROP TRIGGER IF EXISTS trg_archive_manifest_entry_truncate ON archive_manifest_entries;
                DROP TRIGGER IF EXISTS trg_archive_manifest_entry_mutation ON archive_manifest_entries;
                DROP TRIGGER IF EXISTS trg_archive_manifest_entry_insert ON archive_manifest_entries;
                DROP FUNCTION IF EXISTS guard_archive_manifest_review_state();
                DROP FUNCTION IF EXISTS reject_archive_manifest_entry_truncate();
                DROP FUNCTION IF EXISTS guard_archive_manifest_entry_mutation();
                DROP FUNCTION IF EXISTS guard_archive_manifest_entry_insert();
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_structured_export_values",
                table: "archive_structured_exports");
            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_structured_export_values",
                table: "archive_structured_exports",
                sql: "version >= 1 AND schema = 'records-export.v1' AND length(trim(payload_json)) > 0 AND content_hash ~ '^[0-9a-f]{64}$' AND byte_count > 0");
        }
    }
}
