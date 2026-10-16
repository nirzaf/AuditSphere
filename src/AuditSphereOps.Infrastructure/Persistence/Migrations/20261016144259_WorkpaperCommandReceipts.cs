using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkpaperCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workpaper_command_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workpaper_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    after_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workpaper_command_events", x => x.id);
                    table.UniqueConstraint("ak_workpaper_command_event_scope", x => new { x.firm_id, x.client_id, x.engagement_id, x.workpaper_id, x.id });
                    table.CheckConstraint("ck_workpaper_command_event", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND action IN ('SAVE_DRAFT','DISCARD_DRAFT','SUBMIT') AND length(before_json) BETWEEN 1 AND 3000000 AND length(after_json) BETWEEN 1 AND 3000000 AND jsonb_typeof(before_json::jsonb)='object' AND jsonb_typeof(after_json::jsonb)='object'");
                    table.ForeignKey(
                        name: "fk_workpaper_command_event_actor",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workpaper_command_event_target",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workpaper_id },
                        principalTable: "workpapers",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workpaper_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workpaper_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    input_json = table.Column<string>(type: "character varying(800000)", maxLength: 800000, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workpaper_command_receipts", x => x.id);
                    table.CheckConstraint("ck_workpaper_command_receipt", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND action IN ('SAVE_DRAFT','DISCARD_DRAFT','SUBMIT') AND length(input_json) BETWEEN 1 AND 800000 AND (preview_json IS NULL OR length(preview_json) BETWEEN 1 AND 4000000) AND jsonb_typeof(input_json::jsonb)='object' AND (preview_json IS NULL OR jsonb_typeof(preview_json::jsonb)='object')");
                    table.ForeignKey(
                        name: "fk_workpaper_command_receipt_actor",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workpaper_command_receipt_event",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workpaper_id, x.event_id },
                        principalTable: "workpaper_command_events",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "workpaper_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workpaper_command_event_scope",
                table: "workpaper_command_events",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workpaper_id" });

            migrationBuilder.CreateIndex(
                name: "ux_workpaper_command_event_request",
                table: "workpaper_command_events",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workpaper_command_receipt_scope",
                table: "workpaper_command_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workpaper_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ux_workpaper_command_receipt_event",
                table: "workpaper_command_receipts",
                columns: new[] { "firm_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_workpaper_command_receipt_request",
                table: "workpaper_command_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);
            migrationBuilder.Sql("""
CREATE FUNCTION workpaper_command_snapshot(w workpapers,d workpaper_drafts) RETURNS jsonb LANGUAGE sql STABLE AS $$
SELECT jsonb_build_object('WorkpaperId',(w).id,'ClientId',(w).client_id,'EngagementId',(w).engagement_id,'Index',(w).wp_index,'Title',(w).title,'ProcedureId',(w).procedure_id,
 'Revision',(w).revision::text,'Status',(w).status,'InputGeneration',(SELECT input_generation::text FROM client_safety_states WHERE firm_id=(w).firm_id AND id=(w).client_id),
 'PolicyGeneration',(SELECT policy_generation::text FROM firm_safety_states WHERE id=(w).firm_id),'WorkPerformed',(w).work_performed,'Conclusion',(w).conclusion,'SubmittedAt',(w).submitted_at,
 'Draft',(CASE WHEN (d).id IS NULL THEN NULL ELSE jsonb_build_object('Id',(d).id,'BaseWorkpaperRevision',(d).base_workpaper_revision::text,
 'BaseInputGeneration',(d).base_input_generation::text,'BasePolicyGeneration',(d).base_policy_generation::text,'DraftRevision',(d).draft_revision::text,'LastSaveId',(d).last_save_id,
 'WorkPerformed',(d).work_performed,'Conclusion',(d).conclusion,'Lifecycle',(d).lifecycle,'LastSavedAt',(d).last_saved_at) END));
$$;
CREATE FUNCTION workpaper_normalize_snapshot(s jsonb) RETURNS jsonb LANGUAGE plpgsql STABLE AS $$
DECLARE r jsonb:=s;
BEGIN
 r:=jsonb_set(r,'{SubmittedAt}',coalesce(to_jsonb((s->>'SubmittedAt')::timestamptz),'null'::jsonb));
 IF s->'Draft' IS DISTINCT FROM 'null'::jsonb THEN
  r:=jsonb_set(r,'{Draft,LastSavedAt}',coalesce(to_jsonb((s->'Draft'->>'LastSavedAt')::timestamptz),'null'::jsonb));
 END IF;
 RETURN r;
END;
$$;
CREATE FUNCTION workpaper_command_authorized(f uuid,c uuid,e uuid) RETURNS boolean LANGUAGE sql VOLATILE AS $$
 SELECT EXISTS(SELECT 1 FROM users u WHERE u.firm_id=f AND u.id=nullif(current_setting('audit.workpaper_actor_id',true),'')::uuid
  AND u.session_epoch=nullif(current_setting('audit.workpaper_actor_epoch',true),'')::bigint AND NOT u.disabled AND lower(u.user_kind)<>'client')
 AND EXISTS(SELECT 1 FROM role_grants g WHERE g.firm_id=f AND g.user_id=nullif(current_setting('audit.workpaper_actor_id',true),'')::uuid AND g.revoked_at IS NULL
  AND (g.expires_at IS NULL OR g.expires_at>clock_timestamp()) AND ((g.client_id IS NULL AND g.engagement_id IS NULL) OR (g.client_id=c AND g.engagement_id IS NULL) OR (g.client_id=c AND g.engagement_id=e))
  AND lower(g.role) IN ('partner','manager','seniormanager','senior','staff','auditor','engagementleader','administrator'))
 AND EXISTS(SELECT 1 FROM firm_safety_states WHERE id=f AND policy_generation>=1)
 AND EXISTS(SELECT 1 FROM client_safety_states WHERE firm_id=f AND id=c AND input_generation>=1)
 AND EXISTS(SELECT 1 FROM engagements WHERE firm_id=f AND id=e AND practice_client_id=c AND NOT professional_work_blocked)
 AND NOT EXISTS(SELECT 1 FROM engagement_holds WHERE firm_id=f AND engagement_id=e AND NOT released)
 AND NOT EXISTS(SELECT 1 FROM engagement_file_freezes WHERE firm_id=f AND engagement_id=e AND state='FROZEN');
$$;
CREATE FUNCTION guard_initial_submitted_workpaper() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.status='SUBMITTED_SNAPSHOT' AND (current_setting('audit.workpaper_action',true) IS DISTINCT FROM 'PROCEDURE_SUBMIT'
  OR NEW.id IS DISTINCT FROM nullif(current_setting('audit.workpaper_procedure_paper',true),'')::uuid
  OR NEW.actor_id IS DISTINCT FROM nullif(current_setting('audit.workpaper_actor_id',true),'')::uuid
  OR NOT workpaper_command_authorized(NEW.firm_id,NEW.client_id,NEW.engagement_id))
 THEN RAISE EXCEPTION 'An initially submitted workpaper requires its exact scoped procedure publication.' USING ERRCODE='55000';END IF;
 RETURN NEW;
END;
$$;
CREATE TRIGGER workpaper_initial_submission_guard BEFORE INSERT ON workpapers FOR EACH ROW EXECUTE FUNCTION guard_initial_submitted_workpaper();
CREATE FUNCTION require_initial_workpaper_submission() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.status='SUBMITTED_SNAPSHOT' AND NOT EXISTS(
  SELECT 1 FROM workpaper_submissions s JOIN audit_procedure_results r ON r.firm_id=s.firm_id AND r.client_id=s.client_id AND r.engagement_id=s.engagement_id AND r.workpaper_id=s.workpaper_id
  WHERE s.firm_id=NEW.firm_id AND s.client_id=NEW.client_id AND s.engagement_id=NEW.engagement_id AND s.workpaper_id=NEW.id AND s.revision=NEW.revision
   AND r.audit_procedure_id=NEW.procedure_id AND r.prepared_by_user_id=s.actor_id
   AND s.work_performed IS NOT DISTINCT FROM NEW.work_performed AND s.conclusion IS NOT DISTINCT FROM NEW.conclusion AND s.submitted_at=NEW.submitted_at
   AND r.work_performed IS NOT DISTINCT FROM NEW.work_performed AND r.conclusion IS NOT DISTINCT FROM NEW.conclusion)
 THEN RAISE EXCEPTION 'An initially submitted workpaper requires its matching immutable submission and procedure result.' USING ERRCODE='23514';END IF;
 RETURN NEW;
END;
$$;
CREATE CONSTRAINT TRIGGER workpaper_initial_submission_required AFTER INSERT ON workpapers DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_initial_workpaper_submission();
CREATE FUNCTION guard_workpaper_command_draft() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE action text:=current_setting('audit.workpaper_action',true);w workpapers%ROWTYPE;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Durable draft lifecycle evidence is retained.' USING ERRCODE='55000'; END IF;
 SELECT * INTO w FROM workpapers WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id AND id=NEW.workpaper_id;
 IF w.id IS NULL OR NEW.owner_user_id IS DISTINCT FROM nullif(current_setting('audit.workpaper_actor_id',true),'')::uuid
  OR nullif(current_setting('audit.workpaper_event_id',true),'') IS NULL OR NOT workpaper_command_authorized(NEW.firm_id,NEW.client_id,NEW.engagement_id)
 THEN RAISE EXCEPTION 'Draft publication requires current exact scoped actor authority and publication context.' USING ERRCODE='55000'; END IF;
 IF TG_OP='UPDATE' AND (to_jsonb(NEW)-ARRAY['base_workpaper_revision','base_input_generation','base_policy_generation','draft_revision','work_performed','conclusion','last_save_id','last_saved_at','lifecycle'])
  IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['base_workpaper_revision','base_input_generation','base_policy_generation','draft_revision','work_performed','conclusion','last_save_id','last_saved_at','lifecycle'])
 THEN RAISE EXCEPTION 'Draft identity and ownership are immutable.' USING ERRCODE='55000'; END IF;
 IF action='SAVE_DRAFT' THEN
  IF w.status<>'WORKING' OR NEW.lifecycle<>'ACTIVE' OR NEW.last_save_id IS DISTINCT FROM nullif(current_setting('audit.workpaper_request_id',true),'')::uuid
   OR NEW.base_workpaper_revision<>w.revision OR NEW.base_input_generation IS DISTINCT FROM (SELECT input_generation FROM client_safety_states WHERE firm_id=NEW.firm_id AND id=NEW.client_id)
   OR NEW.base_policy_generation IS DISTINCT FROM (SELECT policy_generation FROM firm_safety_states WHERE id=NEW.firm_id)
   OR (TG_OP='INSERT' AND NEW.draft_revision<>1)
   OR (TG_OP='UPDATE' AND (OLD.lifecycle='CONSUMED' OR OLD.draft_revision=9223372036854775807 OR NEW.draft_revision<>OLD.draft_revision+1))
  THEN RAISE EXCEPTION 'Save must publish a fresh draft on the current safety basis.' USING ERRCODE='55000'; END IF;
 ELSIF action IN ('DISCARD_DRAFT','SUBMIT') THEN
  IF TG_OP<>'UPDATE' OR OLD.lifecycle<>'ACTIVE' OR NEW.lifecycle IS DISTINCT FROM (CASE WHEN action='SUBMIT' THEN 'CONSUMED' ELSE 'DISCARDED' END)
   OR (to_jsonb(NEW)-'lifecycle') IS DISTINCT FROM (to_jsonb(OLD)-'lifecycle')
   OR (action='SUBMIT' AND (w.status<>'SUBMITTED_SNAPSHOT' OR NOT EXISTS(SELECT 1 FROM workpaper_command_events e WHERE e.id=nullif(current_setting('audit.workpaper_event_id',true),'')::uuid AND e.workpaper_id=w.id AND e.action='SUBMIT')))
  THEN RAISE EXCEPTION 'A final draft transition retains its exact original content and identity.' USING ERRCODE='55000'; END IF;
 ELSE RAISE EXCEPTION 'Unsupported draft publication.' USING ERRCODE='55000'; END IF;
 RETURN NEW;
END;
$$;
CREATE TRIGGER workpaper_command_draft_guard BEFORE INSERT OR UPDATE OR DELETE ON workpaper_drafts FOR EACH ROW EXECUTE FUNCTION guard_workpaper_command_draft();
CREATE FUNCTION retain_workpaper_draft_command() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE w workpapers%ROWTYPE;b jsonb;action text:=current_setting('audit.workpaper_action',true);
BEGIN
 IF action='SUBMIT' THEN RETURN NEW; END IF;
 SELECT * INTO w FROM workpapers WHERE firm_id=NEW.firm_id AND id=NEW.workpaper_id;
 IF TG_OP='INSERT' THEN b:=workpaper_command_snapshot(w,NULL::workpaper_drafts);ELSE b:=workpaper_command_snapshot(w,OLD);END IF;
 INSERT INTO workpaper_command_events(id,firm_id,client_id,engagement_id,workpaper_id,actor_id,actor_epoch,request_id,request_hash,action,before_json,after_json,created_at)
 VALUES(current_setting('audit.workpaper_event_id')::uuid,NEW.firm_id,NEW.client_id,NEW.engagement_id,NEW.workpaper_id,NEW.owner_user_id,current_setting('audit.workpaper_actor_epoch')::bigint,
 current_setting('audit.workpaper_request_id')::uuid,current_setting('audit.workpaper_request_hash'),action,b::text,workpaper_command_snapshot(w,NEW)::text,clock_timestamp());
 RETURN NEW;
END;
$$;
CREATE TRIGGER workpaper_draft_command_evidence AFTER INSERT OR UPDATE ON workpaper_drafts FOR EACH ROW EXECUTE FUNCTION retain_workpaper_draft_command();
CREATE FUNCTION guard_workpaper_command_submit() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE d workpaper_drafts%ROWTYPE;
BEGIN
 IF NEW.status<>'SUBMITTED_SNAPSHOT' THEN RETURN NEW;END IF;
 IF OLD.status<>'WORKING' OR OLD.revision=9223372036854775807 OR NEW.revision<>OLD.revision+1 OR NEW.submitted_at IS NULL
  OR current_setting('audit.workpaper_action',true) IS DISTINCT FROM 'SUBMIT' OR nullif(current_setting('audit.workpaper_event_id',true),'') IS NULL
  OR NOT workpaper_command_authorized(NEW.firm_id,NEW.client_id,NEW.engagement_id)
  OR (to_jsonb(NEW)-ARRAY['status','revision','work_performed','conclusion','submitted_at']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','revision','work_performed','conclusion','submitted_at'])
 THEN RAISE EXCEPTION 'Submission must freeze one real scoped working revision.' USING ERRCODE='55000';END IF;
 SELECT * INTO d FROM workpaper_drafts WHERE firm_id=NEW.firm_id AND workpaper_id=NEW.id AND owner_user_id=current_setting('audit.workpaper_actor_id')::uuid;
 IF current_setting('audit.workpaper_legacy',true) IS DISTINCT FROM 'true' AND (d.id IS NULL OR d.lifecycle<>'ACTIVE' OR d.work_performed IS DISTINCT FROM NEW.work_performed OR d.conclusion IS DISTINCT FROM NEW.conclusion
  OR d.base_workpaper_revision<>OLD.revision OR d.base_input_generation IS DISTINCT FROM (SELECT input_generation FROM client_safety_states WHERE firm_id=NEW.firm_id AND id=NEW.client_id)
  OR d.base_policy_generation IS DISTINCT FROM (SELECT policy_generation FROM firm_safety_states WHERE id=NEW.firm_id))
 THEN RAISE EXCEPTION 'Reviewed submission requires the exact acknowledged current draft.' USING ERRCODE='55000';END IF;
 RETURN NEW;
END;
$$;
CREATE TRIGGER workpaper_command_submit_guard BEFORE UPDATE ON workpapers FOR EACH ROW EXECUTE FUNCTION guard_workpaper_command_submit();
CREATE FUNCTION retain_workpaper_submit_command() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE d workpaper_drafts%ROWTYPE;a jsonb;
BEGIN
 IF NEW.status<>'SUBMITTED_SNAPSHOT' THEN RETURN NEW;END IF;
 SELECT * INTO d FROM workpaper_drafts WHERE firm_id=NEW.firm_id AND workpaper_id=NEW.id AND owner_user_id=current_setting('audit.workpaper_actor_id')::uuid;
 a:=workpaper_command_snapshot(NEW,d);
 IF d.id IS NOT NULL AND d.lifecycle='ACTIVE' THEN a:=jsonb_set(a,'{Draft,Lifecycle}','"CONSUMED"'::jsonb);END IF;
 INSERT INTO workpaper_command_events(id,firm_id,client_id,engagement_id,workpaper_id,actor_id,actor_epoch,request_id,request_hash,action,before_json,after_json,created_at)
 VALUES(current_setting('audit.workpaper_event_id')::uuid,NEW.firm_id,NEW.client_id,NEW.engagement_id,NEW.id,current_setting('audit.workpaper_actor_id')::uuid,current_setting('audit.workpaper_actor_epoch')::bigint,
 current_setting('audit.workpaper_request_id')::uuid,current_setting('audit.workpaper_request_hash'),'SUBMIT',workpaper_command_snapshot(OLD,d)::text,a::text,clock_timestamp());
 RETURN NEW;
END;
$$;
CREATE TRIGGER workpaper_submit_command_evidence AFTER UPDATE ON workpapers FOR EACH ROW EXECUTE FUNCTION retain_workpaper_submit_command();
CREATE FUNCTION guard_workpaper_submission_publication() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF current_setting('audit.workpaper_action',true)='PROCEDURE_SUBMIT' THEN
  IF NEW.workpaper_id IS DISTINCT FROM nullif(current_setting('audit.workpaper_procedure_paper',true),'')::uuid
   OR NEW.actor_id IS DISTINCT FROM nullif(current_setting('audit.workpaper_actor_id',true),'')::uuid
   OR NOT workpaper_command_authorized(NEW.firm_id,NEW.client_id,NEW.engagement_id)
  THEN RAISE EXCEPTION 'Procedure workpaper publication requires its current scoped actor.' USING ERRCODE='55000';END IF;
  RETURN NEW;
 END IF;
 IF current_setting('audit.workpaper_action',true) IS DISTINCT FROM 'SUBMIT' OR NEW.actor_id IS DISTINCT FROM nullif(current_setting('audit.workpaper_actor_id',true),'')::uuid
  OR NOT EXISTS(SELECT 1 FROM workpaper_command_events e WHERE e.id=nullif(current_setting('audit.workpaper_event_id',true),'')::uuid AND e.firm_id=NEW.firm_id AND e.client_id=NEW.client_id AND e.engagement_id=NEW.engagement_id AND e.workpaper_id=NEW.workpaper_id AND e.action='SUBMIT'
   AND (e.after_json::jsonb->>'Revision')::bigint=NEW.revision AND (e.after_json::jsonb->>'WorkPerformed') IS NOT DISTINCT FROM NEW.work_performed
   AND (e.after_json::jsonb->>'Conclusion') IS NOT DISTINCT FROM NEW.conclusion AND (e.after_json::jsonb->>'SubmittedAt')::timestamptz=NEW.submitted_at)
 THEN RAISE EXCEPTION 'A submission must originate from its real command transition.' USING ERRCODE='55000';END IF;
 RETURN NEW;
END;
$$;
CREATE TRIGGER workpaper_submission_publication_guard BEFORE INSERT ON workpaper_submissions FOR EACH ROW EXECUTE FUNCTION guard_workpaper_submission_publication();
CREATE FUNCTION guard_workpaper_command_event() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Workpaper command events are immutable.' USING ERRCODE='55000';END IF;
 IF pg_trigger_depth()<>2 OR NEW.id IS DISTINCT FROM nullif(current_setting('audit.workpaper_event_id',true),'')::uuid
  OR NEW.actor_id IS DISTINCT FROM nullif(current_setting('audit.workpaper_actor_id',true),'')::uuid OR NEW.actor_epoch IS DISTINCT FROM nullif(current_setting('audit.workpaper_actor_epoch',true),'')::bigint
  OR NEW.request_id IS DISTINCT FROM nullif(current_setting('audit.workpaper_request_id',true),'')::uuid OR NEW.request_hash IS DISTINCT FROM current_setting('audit.workpaper_request_hash',true)
  OR NEW.action IS DISTINCT FROM current_setting('audit.workpaper_action',true)
  OR NOT EXISTS(SELECT 1 FROM workpapers w WHERE w.firm_id=NEW.firm_id AND w.client_id=NEW.client_id AND w.engagement_id=NEW.engagement_id AND w.id=NEW.workpaper_id AND w.revision=(NEW.after_json::jsonb->>'Revision')::bigint)
 THEN RAISE EXCEPTION 'Workpaper evidence must be created by the real scoped source transition.' USING ERRCODE='55000';END IF;
 RETURN NEW;
END;
$$;
CREATE TRIGGER workpaper_command_event_guard BEFORE INSERT OR UPDATE OR DELETE ON workpaper_command_events FOR EACH ROW EXECUTE FUNCTION guard_workpaper_command_event();
CREATE FUNCTION guard_workpaper_command_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE e workpaper_command_events%ROWTYPE;w workpapers%ROWTYPE;d workpaper_drafts%ROWTYPE;i jsonb;p jsonb;b jsonb;a jsonb;v jsonb;
BEGIN
 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Workpaper receipts are immutable.' USING ERRCODE='55000';END IF;
 SELECT * INTO e FROM workpaper_command_events WHERE id=NEW.event_id AND firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id AND workpaper_id=NEW.workpaper_id;
 SELECT * INTO w FROM workpapers WHERE id=NEW.workpaper_id AND firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id;
 SELECT * INTO d FROM workpaper_drafts WHERE firm_id=NEW.firm_id AND workpaper_id=NEW.workpaper_id AND owner_user_id=NEW.actor_id;
 i:=NEW.input_json::jsonb;p:=NEW.preview_json::jsonb;b:=e.before_json::jsonb;a:=e.after_json::jsonb;
 IF e.id IS NULL OR e.actor_id IS DISTINCT FROM NEW.actor_id OR e.actor_epoch IS DISTINCT FROM NEW.actor_epoch OR e.request_id IS DISTINCT FROM NEW.request_id OR e.request_hash IS DISTINCT FROM NEW.request_hash OR e.action IS DISTINCT FROM NEW.action
  OR NEW.event_id IS DISTINCT FROM nullif(current_setting('audit.workpaper_event_id',true),'')::uuid OR NOT workpaper_command_authorized(NEW.firm_id,NEW.client_id,NEW.engagement_id)
  OR (NEW.action IN ('DISCARD_DRAFT','SUBMIT') AND current_setting('audit.workpaper_legacy',true) IS DISTINCT FROM 'true' AND NEW.preview_json IS NULL)
  OR (i->>'Action') IS DISTINCT FROM NEW.action OR workpaper_normalize_snapshot(workpaper_command_snapshot(w,d)) IS DISTINCT FROM workpaper_normalize_snapshot(a)
 THEN RAISE EXCEPTION 'Receipt must bind its real original actor-scoped publication.' USING ERRCODE='23514';END IF;
 IF NEW.preview_json IS NOT NULL AND ((p->>'RequestId')::uuid IS DISTINCT FROM NEW.request_id OR (p->>'RequestHash') IS DISTINCT FROM NEW.request_hash OR (p->'CanonicalInput') IS DISTINCT FROM i OR (p->>'CanProceed') IS DISTINCT FROM 'true'
  OR workpaper_normalize_snapshot(p->'Before') IS DISTINCT FROM workpaper_normalize_snapshot(b))
 THEN RAISE EXCEPTION 'Receipt preview must retain the exact actual before-state and intent.' USING ERRCODE='23514';END IF;
 IF NEW.action='SAVE_DRAFT' THEN
  v:=i->'Save';
  IF (a->'Draft'->>'LastSaveId')::uuid IS DISTINCT FROM NEW.request_id OR (v->>'WorkPerformed') IS DISTINCT FROM (a->'Draft'->>'WorkPerformed') OR (v->>'Conclusion') IS DISTINCT FROM (a->'Draft'->>'Conclusion')
   OR (v->>'BaseWorkpaperRevision') IS DISTINCT FROM (a->'Draft'->>'BaseWorkpaperRevision') OR (v->>'BaseInputGeneration') IS DISTINCT FROM (a->'Draft'->>'BaseInputGeneration') OR (v->>'BasePolicyGeneration') IS DISTINCT FROM (a->'Draft'->>'BasePolicyGeneration')
   OR ((b->'Draft' IS NULL OR b->'Draft'='null'::jsonb) AND (v->>'ExpectedDraftRevision') NOT IN ('0','1'))
   OR ((b->'Draft'->>'Lifecycle')='DISCARDED' AND (v->>'ExpectedDraftRevision') IS DISTINCT FROM '0')
   OR ((b->'Draft'->>'Lifecycle')='ACTIVE' AND (v->>'ExpectedDraftRevision') IS DISTINCT FROM (b->'Draft'->>'DraftRevision'))
   OR (i->'Submit') IS DISTINCT FROM 'null'::jsonb OR (i->'Discard') IS DISTINCT FROM 'null'::jsonb
  THEN RAISE EXCEPTION 'Save acknowledgement differs from the actual original draft transition.' USING ERRCODE='23514';END IF;
 ELSIF NEW.action='DISCARD_DRAFT' THEN
  IF (i->'Discard'->>'ExpectedDraftRevision') IS DISTINCT FROM (b->'Draft'->>'DraftRevision') OR (a->'Draft'->>'Lifecycle') IS DISTINCT FROM 'DISCARDED'
   OR (i->'Save') IS DISTINCT FROM 'null'::jsonb OR (i->'Submit') IS DISTINCT FROM 'null'::jsonb
  THEN RAISE EXCEPTION 'Discard acknowledgement differs from the exact draft.' USING ERRCODE='23514';END IF;
 ELSIF NEW.action='SUBMIT' THEN
  v:=i->'Submit';
  IF (v->>'ExpectedRevision') IS DISTINCT FROM (b->>'Revision') OR (v->>'WorkPerformed') IS DISTINCT FROM (a->>'WorkPerformed') OR (v->>'Conclusion') IS DISTINCT FROM (a->>'Conclusion')
   OR ((v->>'ExpectedDraftRevision') IS NOT NULL AND (v->>'ExpectedDraftRevision') IS DISTINCT FROM (b->'Draft'->>'DraftRevision'))
   OR ((v->>'DraftSaveId') IS NOT NULL AND (v->>'DraftSaveId') IS DISTINCT FROM (b->'Draft'->>'LastSaveId'))
   OR NOT EXISTS(SELECT 1 FROM workpaper_submissions s WHERE s.firm_id=NEW.firm_id AND s.client_id=NEW.client_id AND s.engagement_id=NEW.engagement_id AND s.workpaper_id=NEW.workpaper_id AND s.actor_id=NEW.actor_id AND s.revision=(a->>'Revision')::bigint
    AND s.work_performed IS NOT DISTINCT FROM (a->>'WorkPerformed') AND s.conclusion IS NOT DISTINCT FROM (a->>'Conclusion') AND s.submitted_at=(a->>'SubmittedAt')::timestamptz)
   OR (i->'Save') IS DISTINCT FROM 'null'::jsonb OR (i->'Discard') IS DISTINCT FROM 'null'::jsonb
  THEN RAISE EXCEPTION 'Submission acknowledgement must retain the real immutable submission and consumed draft.' USING ERRCODE='23514';END IF;
 ELSE RAISE EXCEPTION 'Unsupported workpaper receipt.' USING ERRCODE='23514';END IF;
 RETURN NEW;
EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range OR datetime_field_overflow THEN RAISE EXCEPTION 'Malformed workpaper receipt evidence.' USING ERRCODE='23514';
END;
$$;
CREATE TRIGGER workpaper_command_receipt_guard BEFORE INSERT OR UPDATE OR DELETE ON workpaper_command_receipts FOR EACH ROW EXECUTE FUNCTION guard_workpaper_command_receipt();
CREATE FUNCTION require_workpaper_command_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM workpaper_command_receipts r WHERE r.event_id=NEW.id AND r.firm_id=NEW.firm_id AND r.actor_id=NEW.actor_id AND r.request_id=NEW.request_id)
 THEN RAISE EXCEPTION 'A workpaper source transition and its original acknowledgement must commit atomically.' USING ERRCODE='23514';END IF;
 RETURN NEW;
END;
$$;
CREATE CONSTRAINT TRIGGER workpaper_command_receipt_required AFTER INSERT ON workpaper_command_events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_workpaper_command_receipt();

CREATE FUNCTION verify_procedure_workpaper_submission() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM workpaper_command_events e WHERE e.firm_id=NEW.firm_id AND e.workpaper_id=NEW.workpaper_id AND e.action='SUBMIT'
  AND (e.after_json::jsonb->>'Revision')::bigint=NEW.revision) AND NOT EXISTS(
  SELECT 1 FROM audit_procedure_results r JOIN workpapers w ON w.id=r.workpaper_id AND w.firm_id=r.firm_id AND w.client_id=r.client_id AND w.engagement_id=r.engagement_id
  WHERE r.firm_id=NEW.firm_id AND r.client_id=NEW.client_id AND r.engagement_id=NEW.engagement_id AND r.workpaper_id=NEW.workpaper_id
   AND r.prepared_by_user_id=NEW.actor_id AND w.procedure_id=r.audit_procedure_id AND w.status='SUBMITTED_SNAPSHOT' AND w.revision=NEW.revision
   AND r.work_performed IS NOT DISTINCT FROM NEW.work_performed AND r.conclusion IS NOT DISTINCT FROM NEW.conclusion
   AND w.work_performed IS NOT DISTINCT FROM NEW.work_performed AND w.conclusion IS NOT DISTINCT FROM NEW.conclusion AND w.submitted_at=NEW.submitted_at)
 THEN RAISE EXCEPTION 'A procedure workpaper must retain its real same-scope procedure result.' USING ERRCODE='23514';END IF;
 RETURN NEW;
END;
$$;
CREATE CONSTRAINT TRIGGER workpaper_procedure_submission_required AFTER INSERT ON workpaper_submissions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_procedure_workpaper_submission();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Serialize with source publication and refuse destruction of retained acknowledgements/history.
            migrationBuilder.Sql("""
LOCK TABLE workpapers, workpaper_drafts, workpaper_submissions, workpaper_command_events, workpaper_command_receipts IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM workpaper_command_events) OR EXISTS(SELECT 1 FROM workpaper_command_receipts) THEN
  RAISE EXCEPTION 'Workpaper command evidence must be retained; downgrade refused.' USING ERRCODE='55000';
 END IF;
END $$;
DROP TRIGGER workpaper_initial_submission_required ON workpapers;
DROP TRIGGER workpaper_initial_submission_guard ON workpapers;
DROP FUNCTION require_initial_workpaper_submission();
DROP FUNCTION guard_initial_submitted_workpaper();
DROP TRIGGER workpaper_procedure_submission_required ON workpaper_submissions;
DROP TRIGGER workpaper_submission_publication_guard ON workpaper_submissions;
DROP TRIGGER workpaper_command_receipt_required ON workpaper_command_events;
DROP TRIGGER workpaper_command_receipt_guard ON workpaper_command_receipts;
DROP TRIGGER workpaper_command_event_guard ON workpaper_command_events;
DROP TRIGGER workpaper_submit_command_evidence ON workpapers;
DROP TRIGGER workpaper_command_submit_guard ON workpapers;
DROP TRIGGER workpaper_draft_command_evidence ON workpaper_drafts;
DROP TRIGGER workpaper_command_draft_guard ON workpaper_drafts;
DROP FUNCTION verify_procedure_workpaper_submission();
DROP FUNCTION require_workpaper_command_receipt();
DROP FUNCTION guard_workpaper_command_receipt();
DROP FUNCTION guard_workpaper_command_event();
DROP FUNCTION guard_workpaper_submission_publication();
DROP FUNCTION retain_workpaper_submit_command();
DROP FUNCTION guard_workpaper_command_submit();
DROP FUNCTION retain_workpaper_draft_command();
DROP FUNCTION guard_workpaper_command_draft();
DROP FUNCTION workpaper_command_authorized(uuid,uuid,uuid);
DROP FUNCTION workpaper_normalize_snapshot(jsonb);
DROP FUNCTION workpaper_command_snapshot(workpapers,workpaper_drafts);
""");

            migrationBuilder.DropTable(
                name: "workpaper_command_receipts");

            migrationBuilder.DropTable(
                name: "workpaper_command_events");
        }
    }
}
