using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermanentFileFreeze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_engagement_file_freeze_values",
                table: "engagement_file_freezes");

            // Older application versions could reopen frozen files into AMENDMENT_OPEN and clear the work block.
            // Normalize those rows before constraining the state set, and restore the terminal work block.
            migrationBuilder.Sql("""
                UPDATE engagements e
                SET professional_work_blocked = TRUE,
                    generation = CASE WHEN generation < 9223372036854775807 THEN generation + 1 ELSE generation END
                WHERE EXISTS (
                  SELECT 1 FROM engagement_file_freezes f
                  WHERE f.firm_id = e.firm_id AND f.engagement_id = e.id AND f.state = 'AMENDMENT_OPEN');

                UPDATE engagement_file_freezes
                SET state = 'FROZEN',
                    frozen_at = COALESCE(frozen_at, updated_at, due_at)
                WHERE state = 'AMENDMENT_OPEN';
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_file_freeze_amendment_values",
                table: "file_freeze_amendments");

            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (
                    SELECT 1 FROM file_freeze_amendments
                    WHERE (closed_at IS NULL) <> (closed_by_user_id IS NULL)
                  ) THEN
                    RAISE EXCEPTION 'file-freeze amendment closure evidence is inconsistent; reconcile actor and timestamp before applying PermanentFileFreeze';
                  END IF;
                END $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_file_freeze_amendment_values",
                table: "file_freeze_amendments",
                sql: "length(reason) > 0 AND (approved_by_user_id IS NULL OR approved_by_user_id <> requested_by_user_id) AND ((opened_at IS NULL) = (approved_by_user_id IS NULL)) AND (closed_at IS NULL OR opened_at IS NOT NULL) AND ((closed_at IS NULL) = (closed_by_user_id IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_engagement_file_freeze_values",
                table: "engagement_file_freezes",
                sql: "state IN ('SCHEDULED','FROZEN') AND external_read_only IN ('NOT_REQUESTED','REQUESTED','OBSERVED','BLOCKED_EXTERNAL') AND due_at = report_signed_at + interval '60 days' AND revision >= 1 AND ((state = 'SCHEDULED') = (frozen_at IS NULL))");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_frozen_file_unfreeze() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'freeze records are retained';
                  END IF;
                  IF OLD.state = 'FROZEN' AND (
                    NEW.id IS DISTINCT FROM OLD.id OR
                    NEW.firm_id IS DISTINCT FROM OLD.firm_id OR
                    NEW.client_id IS DISTINCT FROM OLD.client_id OR
                    NEW.engagement_id IS DISTINCT FROM OLD.engagement_id OR
                    NEW.report_deliverable_id IS DISTINCT FROM OLD.report_deliverable_id OR
                    NEW.report_signed_at IS DISTINCT FROM OLD.report_signed_at OR
                    NEW.due_at IS DISTINCT FROM OLD.due_at OR
                    NEW.revision IS DISTINCT FROM OLD.revision OR
                    NEW.state IS DISTINCT FROM 'FROZEN' OR
                    NEW.frozen_at IS DISTINCT FROM OLD.frozen_at
                  ) THEN
                    RAISE EXCEPTION 'a frozen archive is terminal; its identity and freeze evidence cannot change';
                  END IF;
                  RETURN NEW;
                END;
                $$;

                CREATE FUNCTION prevent_file_freeze_amendment_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'supplementary-record requests are retained';
                  END IF;
                  IF NEW.id IS DISTINCT FROM OLD.id OR
                     NEW.firm_id IS DISTINCT FROM OLD.firm_id OR
                     NEW.freeze_id IS DISTINCT FROM OLD.freeze_id OR
                     NEW.engagement_id IS DISTINCT FROM OLD.engagement_id OR
                     NEW.reason IS DISTINCT FROM OLD.reason OR
                     NEW.requested_by_user_id IS DISTINCT FROM OLD.requested_by_user_id OR
                     NEW.requested_at IS DISTINCT FROM OLD.requested_at THEN
                    RAISE EXCEPTION 'supplementary-record request evidence is immutable';
                  END IF;
                  IF OLD.approved_by_user_id IS NOT NULL AND (
                    NEW.approved_by_user_id IS DISTINCT FROM OLD.approved_by_user_id OR
                    NEW.opened_at IS DISTINCT FROM OLD.opened_at
                  ) THEN
                    RAISE EXCEPTION 'supplementary-record approval is immutable';
                  END IF;
                  IF OLD.approved_by_user_id IS NULL AND NEW.approved_by_user_id IS NOT NULL AND
                     (NEW.opened_at IS NULL OR NEW.approved_by_user_id = NEW.requested_by_user_id OR
                      NEW.closed_by_user_id IS NOT NULL OR NEW.closed_at IS NOT NULL) THEN
                    RAISE EXCEPTION 'supplementary-record approval must be independent and precede closure';
                  END IF;
                  IF OLD.closed_by_user_id IS NOT NULL AND (
                    NEW.closed_by_user_id IS DISTINCT FROM OLD.closed_by_user_id OR
                    NEW.closed_at IS DISTINCT FROM OLD.closed_at
                  ) THEN
                    RAISE EXCEPTION 'supplementary-record closure is immutable';
                  END IF;
                  IF OLD.closed_by_user_id IS NULL AND NEW.closed_by_user_id IS NOT NULL AND
                     (OLD.approved_by_user_id IS NULL OR NEW.closed_at IS NULL) THEN
                    RAISE EXCEPTION 'only an approved request can be closed';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_file_freeze_amendments_guard
                  BEFORE UPDATE OR DELETE ON file_freeze_amendments
                  FOR EACH ROW EXECUTE FUNCTION prevent_file_freeze_amendment_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  RAISE EXCEPTION 'PermanentFileFreeze is a compliance safety boundary and cannot be downgraded automatically.';
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_file_freeze_amendment_values",
                table: "file_freeze_amendments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_engagement_file_freeze_values",
                table: "engagement_file_freezes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_file_freeze_amendment_values",
                table: "file_freeze_amendments",
                sql: "length(reason) > 0 AND (approved_by_user_id IS NULL OR approved_by_user_id <> requested_by_user_id) AND ((opened_at IS NULL) = (approved_by_user_id IS NULL)) AND (closed_at IS NULL OR opened_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_engagement_file_freeze_values",
                table: "engagement_file_freezes",
                sql: "state IN ('SCHEDULED','FROZEN','AMENDMENT_OPEN') AND external_read_only IN ('NOT_REQUESTED','REQUESTED','OBSERVED','BLOCKED_EXTERNAL') AND due_at = report_signed_at + interval '60 days' AND revision >= 1 AND ((state = 'SCHEDULED') = (frozen_at IS NULL))");
        }
    }
}
