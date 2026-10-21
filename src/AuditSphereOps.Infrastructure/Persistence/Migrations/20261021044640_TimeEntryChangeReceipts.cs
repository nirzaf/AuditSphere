using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TimeEntryChangeReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "time_entry_change_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    actor_name = table.Column<string>(type: "text", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    time_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_time_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    source_after_json = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_entry_change_receipts", x => x.id);
                    table.CheckConstraint("ck_time_entry_change_receipt", "actor_epoch >= 1 AND source_revision >= 1 AND result_revision > source_revision AND result_revision - source_revision = 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND kind IN ('EDIT_UNAPPROVED','CORRECT_APPROVED') AND octet_length(before_json) BETWEEN 1 AND 64000 AND octet_length(source_after_json) BETWEEN 1 AND 64000 AND octet_length(result_json) BETWEEN 1 AND 64000 AND ((kind = 'EDIT_UNAPPROVED' AND result_time_entry_id = time_entry_id) OR (kind = 'CORRECT_APPROVED' AND result_time_entry_id <> time_entry_id))");
                    table.ForeignKey(
                        name: "FK_time_entry_change_receipts_time_entries_firm_id_result_time~",
                        columns: x => new { x.firm_id, x.result_time_entry_id },
                        principalTable: "time_entries",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_time_entry_change_receipts_time_entries_firm_id_time_entry_~",
                        columns: x => new { x.firm_id, x.time_entry_id },
                        principalTable: "time_entries",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_time_entry_change_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_time_entry_change_receipts_firm_id_actor_id_request_id",
                table: "time_entry_change_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_time_entry_change_receipts_firm_id_result_time_entry_id_res~",
                table: "time_entry_change_receipts",
                columns: new[] { "firm_id", "result_time_entry_id", "result_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_time_entry_change_receipts_firm_id_time_entry_id_created_at~",
                table: "time_entry_change_receipts",
                columns: new[] { "firm_id", "time_entry_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entry_change_receipts_firm_id_time_entry_id_source_rev~",
                table: "time_entry_change_receipts",
                columns: new[] { "firm_id", "time_entry_id", "source_revision" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION time_change_snapshot_matches(payload jsonb, stored time_entries) RETURNS boolean AS $$
                DECLARE field record; column_name text; stored_json jsonb := to_jsonb(stored);
                BEGIN
                  IF jsonb_typeof(payload) IS DISTINCT FROM 'object' OR NOT (payload ?& ARRAY['Id','FirmId','ClientId','EngagementId','TaskId','UserId','WorkDate','StartMinute','DurationMinutes','Role','Activity','Phase','RiskArea','MappingVersionId','FsliCode','BillableClassification','Narrative','NarrativeVisibility','Status','Currency','Revision','SupersedesId','RateCardVersionId','RatePerHour','SubmittedAt','SubmissionRound','LastReturnReason','ApprovedByUserId','ApprovedAt','CorrectionReason','CreatedAt']) THEN RETURN false; END IF;
                  FOR field IN SELECT key, value FROM jsonb_each(payload) LOOP
                    column_name := lower(regexp_replace(field.key, '([a-z0-9])([A-Z])', '\1_\2', 'g'));
                    IF NOT (stored_json ? column_name) THEN RETURN false; END IF;
                    IF field.key IN ('CreatedAt','SubmittedAt','ApprovedAt') THEN
                      IF (field.value #>> '{}')::timestamptz IS DISTINCT FROM (stored_json ->> column_name)::timestamptz THEN RETURN false; END IF;
                    ELSIF field.value IS DISTINCT FROM stored_json -> column_name THEN RETURN false;
                    END IF;
                  END LOOP;
                  RETURN true;
                END;
                $$ LANGUAGE plpgsql;
                CREATE FUNCTION prevent_time_entry_change_receipt_mutation() RETURNS trigger AS $$
                BEGIN RAISE EXCEPTION 'Time-entry change evidence is immutable' USING ERRCODE='23514'; END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER time_entry_change_receipts_immutable BEFORE UPDATE OR DELETE ON time_entry_change_receipts
                FOR EACH ROW EXECUTE FUNCTION prevent_time_entry_change_receipt_mutation();
                CREATE TRIGGER time_entry_change_receipts_no_truncate BEFORE TRUNCATE ON time_entry_change_receipts
                FOR EACH STATEMENT EXECUTE FUNCTION prevent_time_entry_change_receipt_mutation();
                CREATE FUNCTION enforce_time_entry_change_receipt() RETURNS trigger AS $$
                DECLARE source_entry time_entries; result_entry time_entries;
                  before_value jsonb := NEW.before_json::jsonb;
                  source_value jsonb := NEW.source_after_json::jsonb;
                  result_value jsonb := NEW.result_json::jsonb;
                  field_name text;
                BEGIN
                  IF jsonb_typeof(before_value) IS DISTINCT FROM 'object'
                      OR NOT (before_value ?& ARRAY['Id','FirmId','ClientId','EngagementId','TaskId','UserId','WorkDate','StartMinute','DurationMinutes','Role','Activity','Phase','RiskArea','MappingVersionId','FsliCode','BillableClassification','Narrative','NarrativeVisibility','Status','Currency','Revision','SupersedesId','RateCardVersionId','RatePerHour','SubmittedAt','SubmissionRound','LastReturnReason','ApprovedByUserId','ApprovedAt','CorrectionReason','CreatedAt'])
                      OR before_value - ARRAY['Id','FirmId','ClientId','EngagementId','TaskId','UserId','WorkDate','StartMinute','DurationMinutes','Role','Activity','Phase','RiskArea','MappingVersionId','FsliCode','BillableClassification','Narrative','NarrativeVisibility','Status','Currency','Revision','SupersedesId','RateCardVersionId','RatePerHour','SubmittedAt','SubmissionRound','LastReturnReason','ApprovedByUserId','ApprovedAt','CorrectionReason','CreatedAt'] IS DISTINCT FROM '{}'::jsonb THEN
                    RAISE EXCEPTION 'Time-change evidence requires the complete original entry' USING ERRCODE='23514';
                  END IF;
                  SELECT * INTO source_entry FROM time_entries WHERE firm_id=NEW.firm_id AND id=NEW.time_entry_id;
                  IF NOT FOUND THEN RAISE EXCEPTION 'Time-change source is unavailable' USING ERRCODE='23514'; END IF;
                  SELECT * INTO result_entry FROM time_entries WHERE firm_id=NEW.firm_id AND id=NEW.result_time_entry_id;
                  IF NOT FOUND THEN RAISE EXCEPTION 'Time-change result is unavailable' USING ERRCODE='23514'; END IF;
                  IF NOT time_change_snapshot_matches(source_value, source_entry)
                      OR NOT time_change_snapshot_matches(result_value, result_entry)
                      OR (before_value->>'Id')::uuid IS DISTINCT FROM NEW.time_entry_id
                      OR (before_value->>'FirmId')::uuid IS DISTINCT FROM NEW.firm_id
                      OR (before_value->>'Revision')::bigint IS DISTINCT FROM NEW.source_revision
                      OR (result_value->>'Revision')::bigint IS DISTINCT FROM NEW.result_revision THEN
                    RAISE EXCEPTION 'Time-change receipt requires its exact committed snapshots' USING ERRCODE='23514';
                  END IF;
                  IF NEW.kind='EDIT_UNAPPROVED' THEN
                    IF before_value->>'Status' NOT IN ('DRAFT','RETURNED')
                        OR source_value IS DISTINCT FROM result_value
                        OR (before_value - ARRAY['Revision','WorkDate','StartMinute','DurationMinutes','Activity','Narrative'])
                           IS DISTINCT FROM (result_value - ARRAY['Revision','WorkDate','StartMinute','DurationMinutes','Activity','Narrative']) THEN
                      RAISE EXCEPTION 'An unapproved edit must preserve its source context and review state' USING ERRCODE='23514';
                    END IF;
                  ELSE
                    IF before_value->>'Status' IS DISTINCT FROM 'APPROVED'
                        OR source_value->>'Status' IS DISTINCT FROM 'SUPERSEDED'
                        OR (source_value-'Status') IS DISTINCT FROM (before_value-'Status')
                        OR result_value->>'Status' IS DISTINCT FROM 'DRAFT'
                        OR (result_value->>'SupersedesId')::uuid IS DISTINCT FROM NEW.time_entry_id
                        OR (result_value->>'SubmissionRound')::integer IS DISTINCT FROM 0
                        OR result_value->>'SubmittedAt' IS NOT NULL OR result_value->>'ApprovedByUserId' IS NOT NULL
                        OR result_value->>'ApprovedAt' IS NOT NULL OR result_value->>'LastReturnReason' IS NOT NULL
                        OR result_value->>'CorrectionReason' IS DISTINCT FROM NEW.reason THEN
                      RAISE EXCEPTION 'An approved correction must retain its source and publish a separate unapproved draft' USING ERRCODE='23514';
                    END IF;
                    FOREACH field_name IN ARRAY ARRAY['FirmId','ClientId','EngagementId','TaskId','UserId','Role','Phase','RiskArea',
                      'MappingVersionId','FsliCode','BillableClassification','Currency','RateCardVersionId','RatePerHour'] LOOP
                      IF before_value->field_name IS DISTINCT FROM result_value->field_name THEN
                        RAISE EXCEPTION 'A time correction cannot change its retained billing or accounting context' USING ERRCODE='23514';
                      END IF;
                    END LOOP;
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER time_entry_change_receipt_publication AFTER INSERT ON time_entry_change_receipts
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_time_entry_change_receipt();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM time_entry_change_receipts) THEN
                    RAISE EXCEPTION 'Retained time-change evidence prevents rollback' USING ERRCODE='55000';
                  END IF;
                END $$;
                DROP FUNCTION enforce_time_entry_change_receipt() CASCADE;
                DROP FUNCTION prevent_time_entry_change_receipt_mutation() CASCADE;
                DROP FUNCTION time_change_snapshot_matches(jsonb, time_entries);
                """);
            migrationBuilder.DropTable(
                name: "time_entry_change_receipts");
        }
    }
}
