using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LifecycleAuditRemediationIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_group_deliverable_values",
                table: "group_deliverables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_client_offboarding_record_values",
                table: "client_offboarding_records");

            migrationBuilder.AddColumn<string>(
                name: "archive_request_key",
                table: "group_deliverables",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issue_request_key",
                table: "group_deliverables",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_key",
                table: "group_deliverables",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "group_deliverables",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "request_key",
                table: "client_offboarding_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_deliverables_scope_request_key",
                table: "group_deliverables",
                columns: new[] { "firm_id", "scope_version_id", "request_key" },
                unique: true,
                filter: "request_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_group_deliverables_scope_issue_request_key",
                table: "group_deliverables",
                columns: new[] { "firm_id", "scope_version_id", "issue_request_key" },
                unique: true,
                filter: "issue_request_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_group_deliverables_scope_archive_request_key",
                table: "group_deliverables",
                columns: new[] { "firm_id", "scope_version_id", "archive_request_key" },
                unique: true,
                filter: "archive_request_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_group_deliverable_values",
                table: "group_deliverables",
                sql: "format_version <> '' AND run_hash ~ '^[0-9a-f]{64}$' AND input_manifest_digest ~ '^[0-9a-f]{64}$' AND deliverable_sha256 ~ '^[0-9a-f]{64}$' AND status IN ('BUILT','ISSUED','ARCHIVED') AND revision >= 1 AND ((status = 'BUILT' AND issued_by_user_id IS NULL AND issued_at IS NULL AND archived_by_user_id IS NULL AND archived_at IS NULL) OR (status = 'ISSUED' AND issued_by_user_id IS NOT NULL AND issued_at IS NOT NULL AND archived_by_user_id IS NULL AND archived_at IS NULL) OR (status = 'ARCHIVED' AND issued_by_user_id IS NOT NULL AND issued_at IS NOT NULL AND archived_by_user_id IS NOT NULL AND archived_at IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_client_offboarding_records_request_key",
                table: "client_offboarding_records",
                columns: new[] { "firm_id", "client_id", "request_key" },
                unique: true,
                filter: "request_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_offboarding_record_values",
                table: "client_offboarding_records",
                sql: "length(trim(status)) > 0 AND length(trim(disposition_notes)) > 0 AND readiness_digest ~ '^[0-9a-f]{64}$' AND readiness_overall_state IN ('ACTION_REQUIRED','UNKNOWN','NO_FINDINGS')");

            migrationBuilder.Sql("""
                CREATE FUNCTION guard_group_deliverable_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'INSERT' THEN
                    IF NEW.status <> 'BUILT' OR NEW.revision <> 1 OR NEW.request_key IS NULL OR
                       NEW.request_key !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' THEN
                      RAISE EXCEPTION 'New group deliverables require a keyed BUILT revision.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                  ELSIF TG_OP = 'UPDATE' THEN
                    IF (NEW.id, NEW.firm_id, NEW.group_id, NEW.scope_version_id, NEW.run_id, NEW.run_hash,
                        NEW.input_manifest_digest, NEW.deliverable_sha256, NEW.deliverable_json, NEW.format_version,
                        NEW.created_by_user_id, NEW.created_at, NEW.request_key)
                       IS DISTINCT FROM
                       (OLD.id, OLD.firm_id, OLD.group_id, OLD.scope_version_id, OLD.run_id, OLD.run_hash,
                        OLD.input_manifest_digest, OLD.deliverable_sha256, OLD.deliverable_json, OLD.format_version,
                        OLD.created_by_user_id, OLD.created_at, OLD.request_key) THEN
                      RAISE EXCEPTION 'Group deliverable bytes and source identity are immutable.' USING ERRCODE = '55000';
                    END IF;

                    IF OLD.status = 'BUILT' AND NEW.status = 'ISSUED' THEN
                      IF NEW.revision <> OLD.revision + 1 OR NEW.issued_by_user_id IS NULL OR NEW.issued_at IS NULL OR
                         NEW.authorized_release_key IS NULL OR length(trim(NEW.authorized_release_key)) = 0 OR
                         NEW.issue_request_key IS NULL OR
                         NEW.issue_request_key !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' OR
                         NEW.archived_by_user_id IS NOT NULL OR NEW.archived_at IS NOT NULL OR NEW.archive_reason IS NOT NULL OR
                         NEW.archive_request_key IS NOT NULL THEN
                        RAISE EXCEPTION 'Group deliverable issue transition is invalid.' USING ERRCODE = '23514';
                      END IF;
                      RETURN NEW;
                    ELSIF OLD.status = 'ISSUED' AND NEW.status = 'ARCHIVED' THEN
                      IF NEW.revision <> OLD.revision + 1 OR NEW.issued_by_user_id IS DISTINCT FROM OLD.issued_by_user_id OR
                         NEW.issued_at IS DISTINCT FROM OLD.issued_at OR NEW.authorized_release_key IS DISTINCT FROM OLD.authorized_release_key OR
                         NEW.issue_request_key IS DISTINCT FROM OLD.issue_request_key OR NEW.archived_by_user_id IS NULL OR
                         NEW.archived_at IS NULL OR NEW.archive_reason IS NULL OR length(trim(NEW.archive_reason)) = 0 OR
                         NEW.archive_request_key IS NULL OR
                         NEW.archive_request_key !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' THEN
                        RAISE EXCEPTION 'Group deliverable archive transition is invalid.' USING ERRCODE = '23514';
                      END IF;
                      RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'Group deliverable history permits only BUILT to ISSUED to ARCHIVED.' USING ERRCODE = '55000';
                  END IF;

                  RAISE EXCEPTION 'Group deliverable history is append-only.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_group_deliverables_guard
                  BEFORE INSERT OR UPDATE OR DELETE ON group_deliverables
                  FOR EACH ROW EXECUTE FUNCTION guard_group_deliverable_mutation();
                CREATE FUNCTION reject_group_deliverable_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Group deliverable history cannot be truncated.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_group_deliverables_no_truncate
                  BEFORE TRUNCATE ON group_deliverables
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_group_deliverable_truncate();

                CREATE FUNCTION guard_client_offboarding_record_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'INSERT' THEN
                    IF NEW.status <> 'BLOCKED_DECISION' OR NEW.request_key IS NULL OR
                       NEW.request_key !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' THEN
                      RAISE EXCEPTION 'Only keyed BLOCKED_DECISION review snapshots may be recorded.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                  END IF;
                  RAISE EXCEPTION 'Offboarding review history is append-only.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_client_offboarding_records_append_only
                  BEFORE INSERT OR UPDATE OR DELETE ON client_offboarding_records
                  FOR EACH ROW EXECUTE FUNCTION guard_client_offboarding_record_mutation();
                CREATE FUNCTION reject_client_offboarding_record_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Offboarding review history cannot be truncated.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_client_offboarding_records_no_truncate
                  BEFORE TRUNCATE ON client_offboarding_records
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_client_offboarding_record_truncate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE group_deliverables, client_offboarding_records IN ACCESS EXCLUSIVE MODE; DO $$ BEGIN IF EXISTS (SELECT 1 FROM group_deliverables) OR EXISTS (SELECT 1 FROM client_offboarding_records) THEN RAISE EXCEPTION 'Cannot remove retained group deliverable or offboarding review history.' USING ERRCODE = '55000'; END IF; END $$;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_group_deliverables_no_truncate ON group_deliverables; DROP TRIGGER IF EXISTS trg_group_deliverables_guard ON group_deliverables; DROP FUNCTION IF EXISTS reject_group_deliverable_truncate(); DROP FUNCTION IF EXISTS guard_group_deliverable_mutation(); DROP TRIGGER IF EXISTS trg_client_offboarding_records_no_truncate ON client_offboarding_records; DROP TRIGGER IF EXISTS trg_client_offboarding_records_append_only ON client_offboarding_records; DROP FUNCTION IF EXISTS reject_client_offboarding_record_truncate(); DROP FUNCTION IF EXISTS guard_client_offboarding_record_mutation();");

            migrationBuilder.DropIndex(
                name: "ix_group_deliverables_scope_request_key",
                table: "group_deliverables");
            migrationBuilder.DropIndex(
                name: "ix_group_deliverables_scope_issue_request_key",
                table: "group_deliverables");
            migrationBuilder.DropIndex(
                name: "ix_group_deliverables_scope_archive_request_key",
                table: "group_deliverables");
            migrationBuilder.DropCheckConstraint(
                name: "ck_group_deliverable_values",
                table: "group_deliverables");
            migrationBuilder.DropIndex(
                name: "ix_client_offboarding_records_request_key",
                table: "client_offboarding_records");
            migrationBuilder.DropCheckConstraint(
                name: "ck_client_offboarding_record_values",
                table: "client_offboarding_records");

            migrationBuilder.DropColumn(name: "archive_request_key", table: "group_deliverables");
            migrationBuilder.DropColumn(name: "issue_request_key", table: "group_deliverables");
            migrationBuilder.DropColumn(name: "request_key", table: "group_deliverables");
            migrationBuilder.DropColumn(name: "revision", table: "group_deliverables");
            migrationBuilder.DropColumn(name: "request_key", table: "client_offboarding_records");

            migrationBuilder.AddCheckConstraint(
                name: "ck_group_deliverable_values",
                table: "group_deliverables",
                sql: "format_version <> '' AND run_hash ~ '^[0-9a-f]{64}$' AND input_manifest_digest ~ '^[0-9a-f]{64}$' AND deliverable_sha256 ~ '^[0-9a-f]{64}$' AND status IN ('BUILT','ISSUED','ARCHIVED')");
            migrationBuilder.AddCheckConstraint(
                name: "ck_client_offboarding_record_values",
                table: "client_offboarding_records",
                sql: "length(trim(status)) > 0 AND length(trim(disposition_notes)) > 0 AND readiness_digest ~ '^[0-9a-f]{64}$'");
        }
    }
}
