using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FindingReviewedCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "findings",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "finding_amendments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    before_revision = table.Column<long>(type: "bigint", nullable: false),
                    after_revision = table.Column<long>(type: "bigint", nullable: false),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    after_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finding_amendments", x => x.id);
                    table.UniqueConstraint("ak_finding_amendment_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.finding_id, x.id });
                    table.CheckConstraint("ck_finding_amendment_values", "actor_epoch >= 1 AND before_revision >= 1 AND after_revision = before_revision + 1 AND action IN ('RECORD_RESPONSE','DESIGNATE_MANAGEMENT_LETTER','UNDESIGNATE_MANAGEMENT_LETTER') AND length(before_json) BETWEEN 1 AND 1000000 AND length(after_json) BETWEEN 1 AND 1000000 AND jsonb_typeof(before_json::jsonb) = 'object' AND jsonb_typeof(after_json::jsonb) = 'object'");
                    table.ForeignKey(
                        name: "FK_finding_amendments_findings_firm_id_client_id_engagement_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.finding_id },
                        principalTable: "findings",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finding_amendments_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finding_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    input_json = table.Column<string>(type: "character varying(150000)", maxLength: 150000, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finding_command_receipts", x => x.id);
                    table.CheckConstraint("ck_finding_command_receipt_values", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND action IN ('RECORD_RESPONSE','DESIGNATE_MANAGEMENT_LETTER','UNDESIGNATE_MANAGEMENT_LETTER') AND length(input_json) BETWEEN 1 AND 150000 AND length(preview_json) BETWEEN 1 AND 1000000 AND jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object'");
                    table.ForeignKey(
                        name: "FK_finding_command_receipts_finding_amendments_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.finding_id, x.amendment_id },
                        principalTable: "finding_amendments",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "finding_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finding_command_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_finding_revision",
                table: "findings",
                sql: "revision >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_finding_amendment_actor",
                table: "finding_amendments",
                columns: new[] { "firm_id", "actor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_finding_amendment_finding",
                table: "finding_amendments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "finding_id" });

            migrationBuilder.CreateIndex(
                name: "ux_finding_amendment_revision",
                table: "finding_amendments",
                columns: new[] { "firm_id", "finding_id", "after_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_finding_receipt_event_scope",
                table: "finding_command_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "finding_id", "amendment_id" });

            migrationBuilder.CreateIndex(
                name: "ux_finding_receipt_actor_request",
                table: "finding_command_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_finding_receipt_amendment",
                table: "finding_command_receipts",
                columns: new[] { "firm_id", "amendment_id" },
                unique: true);
            migrationBuilder.Sql("""
CREATE FUNCTION finding_command_snapshot(f findings)
RETURNS jsonb LANGUAGE sql STABLE AS $$
  SELECT jsonb_build_object(
    'FindingId', f.id, 'ClientId', f.client_id, 'EngagementId', f.engagement_id, 'Revision', f.revision::text,
    'CreatedByUserId', f.actor_id, 'FindingType', f.finding_type, 'ImpactDescription', f.impact_description,
    'MonetaryAmount', f.monetary_amount::text, 'Corrected', f.corrected, 'ManagementResponse', f.management_response,
    'Status', f.status, 'LetterDesignatedAt', f.letter_designated_at, 'LetterDesignatedByUserId', f.letter_designated_by_user_id,
    'LetterRecommendation', f.letter_recommendation, 'CreatedAt', f.created_at);
$$;
CREATE FUNCTION enforce_finding_command_transition()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
  actor uuid := nullif(current_setting('audit.finding_actor_id',true),'')::uuid;
  epoch bigint := nullif(current_setting('audit.finding_actor_epoch',true),'')::bigint;
  action text := current_setting('audit.finding_action',true);
  event_id uuid := nullif(current_setting('audit.finding_event_id',true),'')::uuid;
BEGIN
  IF actor IS NULL OR epoch IS NULL OR event_id IS NULL OR OLD.revision = 9223372036854775807 OR NEW.revision <> OLD.revision + 1
    OR NOT EXISTS (SELECT 1 FROM users u WHERE u.id=actor AND u.firm_id=NEW.firm_id AND u.session_epoch=epoch AND NOT u.disabled AND u.user_kind <> 'Client')
    OR NOT EXISTS (SELECT 1 FROM engagements e WHERE e.firm_id=NEW.firm_id AND e.id=NEW.engagement_id AND e.practice_client_id=NEW.client_id AND NOT e.professional_work_blocked)
    OR EXISTS (SELECT 1 FROM engagement_holds h WHERE h.firm_id=NEW.firm_id AND h.engagement_id=NEW.engagement_id AND NOT h.released)
    OR EXISTS (SELECT 1 FROM engagement_file_freezes z WHERE z.firm_id=NEW.firm_id AND z.engagement_id=NEW.engagement_id AND z.state='FROZEN')
    OR NOT EXISTS (SELECT 1 FROM role_grants g WHERE g.firm_id=NEW.firm_id AND g.user_id=actor AND g.revoked_at IS NULL
      AND (g.expires_at IS NULL OR g.expires_at > clock_timestamp())
      AND ((g.client_id IS NULL AND g.engagement_id IS NULL) OR (g.client_id=NEW.client_id AND g.engagement_id IS NULL) OR (g.client_id=NEW.client_id AND g.engagement_id=NEW.engagement_id))
      AND ((action='RECORD_RESPONSE' AND g.role IN ('Partner','Manager','SeniorManager','Senior','Staff','Auditor','EngagementLeader','Administrator'))
        OR (action IN ('DESIGNATE_MANAGEMENT_LETTER','UNDESIGNATE_MANAGEMENT_LETTER') AND g.role IN ('Manager','Partner','Administrator'))))
  THEN RAISE EXCEPTION 'A finding transition requires current scoped actor authority and a fresh revision.' USING ERRCODE='55000'; END IF;
  IF action='RECORD_RESPONSE' THEN
    IF (to_jsonb(NEW)-ARRAY['revision','management_response','corrected','status']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['revision','management_response','corrected','status'])
      OR NEW.management_response IS NULL OR length(trim(NEW.management_response))=0
      OR NEW.status IS DISTINCT FROM (CASE WHEN NEW.corrected THEN 'CORRECTED' ELSE 'EVALUATED' END)
    THEN RAISE EXCEPTION 'A response transition may change only the recorded response and correction outcome.' USING ERRCODE='55000'; END IF;
  ELSIF action IN ('DESIGNATE_MANAGEMENT_LETTER','UNDESIGNATE_MANAGEMENT_LETTER') THEN
    IF (to_jsonb(NEW)-ARRAY['revision','letter_designated_at','letter_designated_by_user_id','letter_recommendation']) IS DISTINCT FROM
       (to_jsonb(OLD)-ARRAY['revision','letter_designated_at','letter_designated_by_user_id','letter_recommendation'])
      OR (action='DESIGNATE_MANAGEMENT_LETTER' AND (NEW.letter_designated_at IS NULL OR NEW.letter_designated_by_user_id IS DISTINCT FROM actor OR NEW.letter_recommendation IS NULL OR length(trim(NEW.letter_recommendation)) NOT BETWEEN 1 AND 2000))
      OR (action='UNDESIGNATE_MANAGEMENT_LETTER' AND (NEW.letter_designated_at IS NOT NULL OR NEW.letter_designated_by_user_id IS NOT NULL OR NEW.letter_recommendation IS NOT NULL))
    THEN RAISE EXCEPTION 'A designation transition must retain the exact authorized letter decision.' USING ERRCODE='55000'; END IF;
  ELSE RAISE EXCEPTION 'Unsupported finding transition.' USING ERRCODE='55000'; END IF;
  RETURN NEW;
END;
$$;
CREATE TRIGGER finding_command_transition_guard BEFORE UPDATE ON findings
FOR EACH ROW EXECUTE FUNCTION enforce_finding_command_transition();

CREATE FUNCTION retain_finding_command_transition()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  INSERT INTO finding_amendments(id,firm_id,client_id,engagement_id,finding_id,actor_id,actor_epoch,action,before_revision,after_revision,before_json,after_json,created_at)
  VALUES (current_setting('audit.finding_event_id')::uuid,NEW.firm_id,NEW.client_id,NEW.engagement_id,NEW.id,
    current_setting('audit.finding_actor_id')::uuid,current_setting('audit.finding_actor_epoch')::bigint,current_setting('audit.finding_action'),
    OLD.revision,NEW.revision,finding_command_snapshot(OLD)::text,finding_command_snapshot(NEW)::text,clock_timestamp());
  RETURN NEW;
END;
$$;
CREATE TRIGGER finding_command_transition_evidence AFTER UPDATE ON findings
FOR EACH ROW EXECUTE FUNCTION retain_finding_command_transition();

CREATE FUNCTION guard_finding_amendment()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Finding amendments are immutable.' USING ERRCODE='55000'; END IF;
  IF pg_trigger_depth() <> 2 OR NEW.id IS DISTINCT FROM nullif(current_setting('audit.finding_event_id',true),'')::uuid
    OR NEW.actor_id IS DISTINCT FROM nullif(current_setting('audit.finding_actor_id',true),'')::uuid
    OR NEW.actor_epoch IS DISTINCT FROM nullif(current_setting('audit.finding_actor_epoch',true),'')::bigint
    OR NEW.action IS DISTINCT FROM current_setting('audit.finding_action',true)
    OR NOT EXISTS (SELECT 1 FROM findings f WHERE f.id=NEW.finding_id AND f.firm_id=NEW.firm_id AND f.client_id=NEW.client_id AND f.engagement_id=NEW.engagement_id
      AND f.revision=NEW.after_revision AND finding_command_snapshot(f)=NEW.after_json::jsonb)
  THEN RAISE EXCEPTION 'Finding amendment evidence must originate from the real scoped transition.' USING ERRCODE='55000'; END IF;
  RETURN NEW;
END;
$$;
CREATE TRIGGER finding_amendment_guard BEFORE INSERT OR UPDATE OR DELETE ON finding_amendments
FOR EACH ROW EXECUTE FUNCTION guard_finding_amendment();

CREATE FUNCTION guard_finding_command_receipt()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
  e finding_amendments%ROWTYPE;
  p jsonb;
  i jsonb;
  before_state jsonb;
  after_state jsonb;
BEGIN
  IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Finding command receipts are immutable.' USING ERRCODE='55000'; END IF;
  SELECT * INTO e FROM finding_amendments WHERE id=NEW.amendment_id AND firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id AND finding_id=NEW.finding_id;
  p:=NEW.preview_json::jsonb; i:=NEW.input_json::jsonb;
  before_state:=e.before_json::jsonb; after_state:=e.after_json::jsonb;
  IF e.id IS NULL OR NEW.amendment_id IS DISTINCT FROM nullif(current_setting('audit.finding_event_id',true),'')::uuid
    OR NEW.actor_id IS DISTINCT FROM nullif(current_setting('audit.finding_actor_id',true),'')::uuid
    OR NEW.actor_epoch IS DISTINCT FROM nullif(current_setting('audit.finding_actor_epoch',true),'')::bigint
    OR NEW.action IS DISTINCT FROM current_setting('audit.finding_action',true) OR e.actor_id IS DISTINCT FROM NEW.actor_id OR e.actor_epoch IS DISTINCT FROM NEW.actor_epoch OR e.action IS DISTINCT FROM NEW.action
    OR NOT EXISTS (SELECT 1 FROM users u WHERE u.firm_id=NEW.firm_id AND u.id=NEW.actor_id AND u.session_epoch=NEW.actor_epoch AND NOT u.disabled)
    OR NOT EXISTS (SELECT 1 FROM findings f WHERE f.id=NEW.finding_id AND f.firm_id=NEW.firm_id AND f.client_id=NEW.client_id AND f.engagement_id=NEW.engagement_id AND f.revision=e.after_revision)
    OR NOT EXISTS (SELECT 1 FROM role_grants g WHERE g.firm_id=NEW.firm_id AND g.user_id=NEW.actor_id AND g.revoked_at IS NULL
      AND (g.expires_at IS NULL OR g.expires_at > clock_timestamp())
      AND ((g.client_id IS NULL AND g.engagement_id IS NULL) OR (g.client_id=NEW.client_id AND g.engagement_id IS NULL) OR (g.client_id=NEW.client_id AND g.engagement_id=NEW.engagement_id))
      AND ((NEW.action='RECORD_RESPONSE' AND g.role IN ('Partner','Manager','SeniorManager','Senior','Staff','Auditor','EngagementLeader','Administrator'))
        OR (NEW.action IN ('DESIGNATE_MANAGEMENT_LETTER','UNDESIGNATE_MANAGEMENT_LETTER') AND g.role IN ('Manager','Partner','Administrator'))))
    OR (p->>'RequestId')::uuid IS DISTINCT FROM NEW.request_id OR p->>'RequestHash' IS DISTINCT FROM NEW.request_hash
    OR p->>'ReviewBasis' IS DISTINCT FROM NEW.review_basis OR p->'CanonicalInput' IS DISTINCT FROM i OR i->>'Action' IS DISTINCT FROM NEW.action
    OR p->>'CanProceed' IS DISTINCT FROM 'true' OR (p->>'ExpectedRevision')::bigint IS DISTINCT FROM e.before_revision OR (p->>'NextRevision')::bigint IS DISTINCT FROM e.after_revision
    OR ((p->'Before')-ARRAY['CreatedAt','LetterDesignatedAt']) IS DISTINCT FROM (before_state-ARRAY['CreatedAt','LetterDesignatedAt'])
    OR (p->'Before'->>'CreatedAt')::timestamptz IS DISTINCT FROM (before_state->>'CreatedAt')::timestamptz
    OR (p->'Before'->>'LetterDesignatedAt')::timestamptz IS DISTINCT FROM (before_state->>'LetterDesignatedAt')::timestamptz
  THEN RAISE EXCEPTION 'Finding receipt does not bind its exact scoped actor transition and before-state.' USING ERRCODE='23514'; END IF;
  IF NEW.action='RECORD_RESPONSE' THEN
    IF after_state->>'ManagementResponse' IS DISTINCT FROM i->'Response'->>'ManagementResponse'
      OR (after_state->>'Corrected')::boolean IS DISTINCT FROM (i->'Response'->>'Corrected')::boolean
      OR i->'Designation' IS DISTINCT FROM 'null'::jsonb
    THEN RAISE EXCEPTION 'Finding receipt response differs from the real amendment.' USING ERRCODE='23514'; END IF;
  ELSIF NEW.action='DESIGNATE_MANAGEMENT_LETTER' THEN
    IF after_state->>'LetterRecommendation' IS DISTINCT FROM i->'Designation'->>'Recommendation'
      OR (after_state->>'LetterDesignatedByUserId')::uuid IS DISTINCT FROM NEW.actor_id OR after_state->>'LetterDesignatedAt' IS NULL
      OR i->'Response' IS DISTINCT FROM 'null'::jsonb
    THEN RAISE EXCEPTION 'Finding receipt recommendation differs from the real amendment.' USING ERRCODE='23514'; END IF;
  ELSIF NEW.action='UNDESIGNATE_MANAGEMENT_LETTER' THEN
    IF after_state->>'LetterDesignatedAt' IS NOT NULL OR after_state->>'LetterRecommendation' IS NOT NULL OR after_state->>'LetterDesignatedByUserId' IS NOT NULL
      OR i->'Response' IS DISTINCT FROM 'null'::jsonb OR i->'Designation' IS DISTINCT FROM 'null'::jsonb
    THEN RAISE EXCEPTION 'Finding receipt does not describe an undesignation.' USING ERRCODE='23514'; END IF;
  ELSE RAISE EXCEPTION 'Unsupported finding receipt action.' USING ERRCODE='23514'; END IF;
  RETURN NEW;
EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range OR datetime_field_overflow THEN
  RAISE EXCEPTION 'Malformed finding receipt evidence.' USING ERRCODE='23514';
END;
$$;
CREATE TRIGGER finding_command_receipt_guard BEFORE INSERT OR UPDATE OR DELETE ON finding_command_receipts
FOR EACH ROW EXECUTE FUNCTION guard_finding_command_receipt();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Serialize against publication and refuse destruction of retained evidence, including legacy amendments.
            migrationBuilder.Sql("""
LOCK TABLE findings, finding_amendments, finding_command_receipts IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM finding_amendments) OR EXISTS (SELECT 1 FROM finding_command_receipts) THEN
    RAISE EXCEPTION 'Finding amendment evidence must be retained; downgrade refused.' USING ERRCODE='55000';
  END IF;
END $$;
DROP TRIGGER finding_command_receipt_guard ON finding_command_receipts;
DROP TRIGGER finding_amendment_guard ON finding_amendments;
DROP TRIGGER finding_command_transition_evidence ON findings;
DROP TRIGGER finding_command_transition_guard ON findings;
DROP FUNCTION guard_finding_command_receipt();
DROP FUNCTION guard_finding_amendment();
DROP FUNCTION retain_finding_command_transition();
DROP FUNCTION enforce_finding_command_transition();
DROP FUNCTION finding_command_snapshot(findings);
""");

            migrationBuilder.DropTable(
                name: "finding_command_receipts");

            migrationBuilder.DropTable(
                name: "finding_amendments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_finding_revision",
                table: "findings");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "findings");
        }
    }
}
