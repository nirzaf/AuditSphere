using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewPointReviewedCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_review_points_firm_id_client_id_engagement_id",
                table: "review_points");

            migrationBuilder.AddColumn<long>(
                name: "disposition_revision",
                table: "review_points",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_review_point_scope_id",
                table: "review_points",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "review_point_disposition_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_point_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_review_point_disposition_events", x => x.id);
                    table.UniqueConstraint("ak_review_point_event_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.review_point_id, x.id });
                    table.CheckConstraint("ck_review_point_event_values", "actor_epoch >= 1 AND before_revision >= 1 AND after_revision = before_revision + 1 AND action IN ('CLEAR','REOPEN') AND length(before_json) BETWEEN 1 AND 1000000 AND length(after_json) BETWEEN 1 AND 1000000 AND jsonb_typeof(before_json::jsonb) = 'object' AND jsonb_typeof(after_json::jsonb) = 'object'");
                    table.ForeignKey(
                        name: "FK_review_point_disposition_events_review_points_firm_id_clien~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.review_point_id },
                        principalTable: "review_points",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_point_disposition_events_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "review_point_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disposition_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    input_json = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_point_command_receipts", x => x.id);
                    table.CheckConstraint("ck_review_point_command_receipt_values", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND action IN ('CLEAR','REOPEN') AND length(input_json) BETWEEN 1 AND 1000 AND length(preview_json) BETWEEN 1 AND 1000000 AND jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object'");
                    table.ForeignKey(
                        name: "FK_review_point_command_receipts_review_point_disposition_even~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.review_point_id, x.disposition_event_id },
                        principalTable: "review_point_disposition_events",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "review_point_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_review_point_command_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_review_point_disposition_revision",
                table: "review_points",
                sql: "disposition_revision >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_review_point_receipt_event_scope",
                table: "review_point_command_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "review_point_id", "disposition_event_id" });

            migrationBuilder.CreateIndex(
                name: "ux_review_point_receipt_actor_request",
                table: "review_point_command_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_review_point_receipt_event",
                table: "review_point_command_receipts",
                columns: new[] { "firm_id", "disposition_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_point_event_actor",
                table: "review_point_disposition_events",
                columns: new[] { "firm_id", "actor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_review_point_event_point",
                table: "review_point_disposition_events",
                columns: new[] { "firm_id", "client_id", "engagement_id", "review_point_id" });

            migrationBuilder.CreateIndex(
                name: "ux_review_point_event_revision",
                table: "review_point_disposition_events",
                columns: new[] { "firm_id", "review_point_id", "after_revision" },
                unique: true);
            migrationBuilder.Sql("""
CREATE FUNCTION review_point_command_snapshot(f review_points)
RETURNS jsonb LANGUAGE sql STABLE AS $$
  SELECT jsonb_build_object('ReviewPointId',f.id,'ClientId',f.client_id,'EngagementId',f.engagement_id,
    'DispositionRevision',f.disposition_revision::text,'TargetKind',f.target_kind,'TargetId',f.target_id,
    'TargetRevision',f.target_revision::text,'RaisedByUserId',f.raised_by_user_id,'RaisedAt',f.raised_at,
    'Significant',f.significant,'Cleared',f.cleared,'Comment',f.comment,
    'Status',(CASE WHEN f.cleared THEN 'CLEARED' WHEN f.significant THEN 'BLOCKING' ELSE 'OPEN' END));
$$;
CREATE FUNCTION enforce_review_point_disposition_transition()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
  actor uuid := nullif(current_setting('audit.review_point_actor_id',true),'')::uuid;
  epoch bigint := nullif(current_setting('audit.review_point_actor_epoch',true),'')::bigint;
  action text := current_setting('audit.review_point_action',true);
  event_id uuid := nullif(current_setting('audit.review_point_event_id',true),'')::uuid;
BEGIN
  IF actor IS NULL OR epoch IS NULL OR event_id IS NULL OR OLD.disposition_revision=9223372036854775807
    OR NEW.disposition_revision <> OLD.disposition_revision+1
    OR NEW.cleared IS DISTINCT FROM (CASE WHEN action='CLEAR' THEN true WHEN action='REOPEN' THEN false ELSE NULL END)
    OR (to_jsonb(NEW)-ARRAY['cleared','disposition_revision']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['cleared','disposition_revision'])
    OR NOT EXISTS (SELECT 1 FROM users u WHERE u.firm_id=NEW.firm_id AND u.id=actor AND u.session_epoch=epoch AND NOT u.disabled AND lower(u.user_kind)<>'client')
    OR NOT EXISTS (SELECT 1 FROM role_grants g WHERE g.firm_id=NEW.firm_id AND g.user_id=actor AND g.revoked_at IS NULL
      AND (g.expires_at IS NULL OR g.expires_at>clock_timestamp())
      AND ((g.client_id IS NULL AND g.engagement_id IS NULL) OR (g.client_id=NEW.client_id AND g.engagement_id IS NULL) OR (g.client_id=NEW.client_id AND g.engagement_id=NEW.engagement_id))
      AND lower(g.role) IN ('partner','manager','seniormanager','senior','staff','auditor','engagementleader','administrator'))
    OR EXISTS (SELECT 1 FROM engagement_file_freezes z WHERE z.firm_id=NEW.firm_id AND z.engagement_id=NEW.engagement_id AND z.state='FROZEN')
  THEN RAISE EXCEPTION 'Review point disposition requires a fresh revision and current exact scoped actor authority; frozen originals remain read-only.' USING ERRCODE='55000'; END IF;
  RETURN NEW;
END;
$$;
CREATE TRIGGER review_point_disposition_transition_guard BEFORE UPDATE ON review_points
FOR EACH ROW EXECUTE FUNCTION enforce_review_point_disposition_transition();
CREATE FUNCTION retain_review_point_disposition_transition()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  INSERT INTO review_point_disposition_events(id,firm_id,client_id,engagement_id,review_point_id,actor_id,actor_epoch,action,before_revision,after_revision,before_json,after_json,created_at)
  VALUES(current_setting('audit.review_point_event_id')::uuid,NEW.firm_id,NEW.client_id,NEW.engagement_id,NEW.id,
    current_setting('audit.review_point_actor_id')::uuid,current_setting('audit.review_point_actor_epoch')::bigint,current_setting('audit.review_point_action'),
    OLD.disposition_revision,NEW.disposition_revision,review_point_command_snapshot(OLD)::text,review_point_command_snapshot(NEW)::text,clock_timestamp());
  RETURN NEW;
END;
$$;
CREATE TRIGGER review_point_disposition_transition_evidence AFTER UPDATE ON review_points
FOR EACH ROW EXECUTE FUNCTION retain_review_point_disposition_transition();
CREATE FUNCTION guard_review_point_disposition_event()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Review point disposition events are immutable.' USING ERRCODE='55000'; END IF;
  IF pg_trigger_depth()<>2 OR NEW.id IS DISTINCT FROM nullif(current_setting('audit.review_point_event_id',true),'')::uuid
    OR NEW.actor_id IS DISTINCT FROM nullif(current_setting('audit.review_point_actor_id',true),'')::uuid
    OR NEW.actor_epoch IS DISTINCT FROM nullif(current_setting('audit.review_point_actor_epoch',true),'')::bigint
    OR NEW.action IS DISTINCT FROM current_setting('audit.review_point_action',true)
    OR NOT EXISTS(SELECT 1 FROM review_points f WHERE f.id=NEW.review_point_id AND f.firm_id=NEW.firm_id AND f.client_id=NEW.client_id AND f.engagement_id=NEW.engagement_id
      AND f.disposition_revision=NEW.after_revision AND review_point_command_snapshot(f)=NEW.after_json::jsonb)
  THEN RAISE EXCEPTION 'Review point disposition evidence must originate from its real scoped transition.' USING ERRCODE='55000'; END IF;
  RETURN NEW;
END;
$$;
CREATE TRIGGER review_point_disposition_event_guard BEFORE INSERT OR UPDATE OR DELETE ON review_point_disposition_events
FOR EACH ROW EXECUTE FUNCTION guard_review_point_disposition_event();
CREATE FUNCTION guard_review_point_command_receipt()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
  e review_point_disposition_events%ROWTYPE;
  p jsonb;
  i jsonb;
  before_state jsonb;
  after_state jsonb;
BEGIN
  IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Review point command receipts are immutable.' USING ERRCODE='55000'; END IF;
  SELECT * INTO e FROM review_point_disposition_events WHERE id=NEW.disposition_event_id AND firm_id=NEW.firm_id
    AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id AND review_point_id=NEW.review_point_id;
  p:=NEW.preview_json::jsonb; i:=NEW.input_json::jsonb; before_state:=e.before_json::jsonb; after_state:=e.after_json::jsonb;
  IF e.id IS NULL OR e.actor_id IS DISTINCT FROM NEW.actor_id OR e.actor_epoch IS DISTINCT FROM NEW.actor_epoch OR e.action IS DISTINCT FROM NEW.action
    OR NEW.disposition_event_id IS DISTINCT FROM nullif(current_setting('audit.review_point_event_id',true),'')::uuid
    OR NEW.actor_id IS DISTINCT FROM nullif(current_setting('audit.review_point_actor_id',true),'')::uuid
    OR NEW.actor_epoch IS DISTINCT FROM nullif(current_setting('audit.review_point_actor_epoch',true),'')::bigint
    OR NEW.action IS DISTINCT FROM current_setting('audit.review_point_action',true)
    OR NOT EXISTS(SELECT 1 FROM users u WHERE u.firm_id=NEW.firm_id AND u.id=NEW.actor_id AND u.session_epoch=NEW.actor_epoch AND NOT u.disabled AND lower(u.user_kind)<>'client')
    OR NOT EXISTS(SELECT 1 FROM role_grants g WHERE g.firm_id=NEW.firm_id AND g.user_id=NEW.actor_id AND g.revoked_at IS NULL
      AND (g.expires_at IS NULL OR g.expires_at>clock_timestamp())
      AND ((g.client_id IS NULL AND g.engagement_id IS NULL) OR (g.client_id=NEW.client_id AND g.engagement_id IS NULL) OR (g.client_id=NEW.client_id AND g.engagement_id=NEW.engagement_id))
      AND lower(g.role) IN ('partner','manager','seniormanager','senior','staff','auditor','engagementleader','administrator'))
    OR NOT EXISTS(SELECT 1 FROM review_points f WHERE f.firm_id=NEW.firm_id AND f.client_id=NEW.client_id AND f.engagement_id=NEW.engagement_id
      AND f.id=NEW.review_point_id AND f.disposition_revision=e.after_revision AND review_point_command_snapshot(f)=after_state)
    OR (p->>'RequestId')::uuid IS DISTINCT FROM NEW.request_id OR (p->>'RequestHash') IS DISTINCT FROM NEW.request_hash
    OR (p->>'ReviewBasis') IS DISTINCT FROM NEW.review_basis OR (p->'CanonicalInput') IS DISTINCT FROM i OR (i->>'Action') IS DISTINCT FROM NEW.action
    OR (p->>'CanProceed') IS DISTINCT FROM 'true'
    OR (p->>'ExpectedDispositionRevision')::bigint IS DISTINCT FROM e.before_revision OR (p->>'NextDispositionRevision')::bigint IS DISTINCT FROM e.after_revision
    OR ((p->'Before')-ARRAY['RaisedAt']) IS DISTINCT FROM (before_state-ARRAY['RaisedAt'])
    OR (p->'Before'->>'RaisedAt')::timestamptz IS DISTINCT FROM (before_state->>'RaisedAt')::timestamptz
    OR (p->>'Cleared')::boolean IS DISTINCT FROM (after_state->>'Cleared')::boolean
    OR (after_state->>'Cleared')::boolean IS DISTINCT FROM (CASE WHEN NEW.action='CLEAR' THEN true WHEN NEW.action='REOPEN' THEN false ELSE NULL END)
  THEN RAISE EXCEPTION 'Review point receipt does not bind its exact scoped actor, before-state and disposition transition.' USING ERRCODE='23514'; END IF;
  RETURN NEW;
EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range OR datetime_field_overflow THEN
  RAISE EXCEPTION 'Malformed review point receipt evidence.' USING ERRCODE='23514';
END;
$$;
CREATE TRIGGER review_point_command_receipt_guard BEFORE INSERT OR UPDATE OR DELETE ON review_point_command_receipts
FOR EACH ROW EXECUTE FUNCTION guard_review_point_command_receipt();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Block publication and preserve both compatibility events and reviewed receipts.
            migrationBuilder.Sql("""
LOCK TABLE review_points, review_point_disposition_events, review_point_command_receipts IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
  IF EXISTS(SELECT 1 FROM review_point_disposition_events) OR EXISTS(SELECT 1 FROM review_point_command_receipts) THEN
    RAISE EXCEPTION 'Review point disposition evidence must be retained; downgrade refused.' USING ERRCODE='55000';
  END IF;
END $$;
DROP TRIGGER review_point_command_receipt_guard ON review_point_command_receipts;
DROP TRIGGER review_point_disposition_event_guard ON review_point_disposition_events;
DROP TRIGGER review_point_disposition_transition_evidence ON review_points;
DROP TRIGGER review_point_disposition_transition_guard ON review_points;
DROP FUNCTION guard_review_point_command_receipt();
DROP FUNCTION guard_review_point_disposition_event();
DROP FUNCTION retain_review_point_disposition_transition();
DROP FUNCTION enforce_review_point_disposition_transition();
DROP FUNCTION review_point_command_snapshot(review_points);
""");

            migrationBuilder.DropTable(
                name: "review_point_command_receipts");

            migrationBuilder.DropTable(
                name: "review_point_disposition_events");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_review_point_scope_id",
                table: "review_points");

            migrationBuilder.DropCheckConstraint(
                name: "ck_review_point_disposition_revision",
                table: "review_points");

            migrationBuilder.DropColumn(
                name: "disposition_revision",
                table: "review_points");

            migrationBuilder.CreateIndex(
                name: "IX_review_points_firm_id_client_id_engagement_id",
                table: "review_points",
                columns: new[] { "firm_id", "client_id", "engagement_id" });
        }
    }
}
