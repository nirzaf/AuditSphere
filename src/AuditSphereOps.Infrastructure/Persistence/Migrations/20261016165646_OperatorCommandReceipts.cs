using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperatorCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_cancellation",
                table: "durable_operations");

            migrationBuilder.CreateTable(
                name: "operator_command_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recovery_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    after_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operator_command_events", x => x.id);
                    table.UniqueConstraint("ak_operator_command_event_owner", x => new { x.firm_id, x.actor_id, x.id });
                    table.CheckConstraint("ck_operator_command_event", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND action IN ('RETRY','CANCEL','LIFT_QUARANTINE','APPROVE_RECOVERY_RESTART') AND ((action IN ('RETRY','CANCEL') AND operation_id IS NOT NULL AND operation_event_id IS NOT NULL AND recovery_session_id IS NULL) OR (action = 'LIFT_QUARANTINE' AND operation_id IS NULL AND operation_event_id IS NULL) OR (action = 'APPROVE_RECOVERY_RESTART' AND operation_id IS NULL AND operation_event_id IS NULL AND recovery_session_id IS NOT NULL)) AND length(before_json) BETWEEN 1 AND 150000 AND length(after_json) BETWEEN 1 AND 150000 AND jsonb_typeof(before_json::jsonb)='object' AND jsonb_typeof(after_json::jsonb)='object'");
                    table.ForeignKey(
                        name: "fk_operator_command_event_actor",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operator_command_event_operation",
                        column: x => x.operation_id,
                        principalTable: "durable_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operator_command_event_session",
                        columns: x => new { x.firm_id, x.recovery_session_id },
                        principalTable: "recovery_sessions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operator_command_event_source",
                        column: x => x.operation_event_id,
                        principalTable: "operation_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operator_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operator_command_receipts", x => x.id);
                    table.CheckConstraint("ck_operator_command_receipt", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND action IN ('RETRY','CANCEL','LIFT_QUARANTINE','APPROVE_RECOVERY_RESTART') AND length(input_json) BETWEEN 1 AND 65000 AND length(preview_json) BETWEEN 1 AND 250000 AND jsonb_typeof(input_json::jsonb)='object' AND jsonb_typeof(preview_json::jsonb)='object'");
                    table.ForeignKey(
                        name: "fk_operator_command_receipt_event",
                        columns: x => new { x.firm_id, x.actor_id, x.event_id },
                        principalTable: "operator_command_events",
                        principalColumns: new[] { "firm_id", "actor_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operator_command_events_firm_id_recovery_session_id",
                table: "operator_command_events",
                columns: new[] { "firm_id", "recovery_session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_operator_command_events_operation_event_id",
                table: "operator_command_events",
                column: "operation_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_operator_command_events_operation_id",
                table: "operator_command_events",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ux_operator_command_event_request",
                table: "operator_command_events",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operator_command_receipts_firm_id_actor_id_event_id",
                table: "operator_command_receipts",
                columns: new[] { "firm_id", "actor_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ux_operator_command_receipt_event",
                table: "operator_command_receipts",
                columns: new[] { "firm_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_operator_command_receipt_request",
                table: "operator_command_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);
            migrationBuilder.Sql("""
            -- Bind operator evidence to the exact transition; later mutable queue heads do not rewrite receipts.
            CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;
            DO $$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_extension e JOIN pg_namespace n ON n.oid=e.extnamespace WHERE e.extname='pgcrypto' AND n.nspname='public') THEN
                RAISE EXCEPTION 'pgcrypto must be available in the public schema for operator receipt integrity.';
              END IF;
            END $$;
            CREATE FUNCTION operator_command_hash(v jsonb) RETURNS text LANGUAGE sql IMMUTABLE STRICT AS $$
             SELECT encode(public.digest(convert_to(v::text,'UTF8'),'sha256'),'hex');
            $$;
            CREATE FUNCTION operator_command_hash_snapshot(v jsonb) RETURNS jsonb LANGUAGE plpgsql IMMUTABLE STRICT AS $$
            DECLARE r jsonb:=v;path text[];epoch_us bigint;
            BEGIN
             FOREACH path SLICE 1 IN ARRAY ARRAY[['Operation','NextAttemptAt'],['Operation','CompletedAt'],['RecoverySession','CreatedAt'],['RecoverySession','ApprovedRestartAt']] LOOP
              IF r#>>path IS NOT NULL THEN
               epoch_us:=((extract(epoch FROM (r#>>path)::timestamptz)*1000000)::numeric)::bigint;
               r:=jsonb_set(r,path,to_jsonb(epoch_us::text));
              END IF;
             END LOOP;
             RETURN r;
            END;
            $$;
            CREATE FUNCTION operator_command_basis(f uuid,a uuid,e bigint,b jsonb) RETURNS text LANGUAGE sql IMMUTABLE STRICT AS $$
             SELECT operator_command_hash(jsonb_build_array(1,f::text,a::text,e,operator_command_hash_snapshot(b)));
            $$;
            CREATE FUNCTION operator_command_request_hash(f uuid,a uuid,e bigint,r uuid,i jsonb,b jsonb) RETURNS text LANGUAGE sql IMMUTABLE STRICT AS $$
             SELECT operator_command_hash(jsonb_build_array(1,f::text,a::text,e,r::text,i,operator_command_basis(f,a,e,b)));
            $$;
            ALTER TABLE operator_command_events ALTER CONSTRAINT fk_operator_command_event_source DEFERRABLE INITIALLY DEFERRED;
            ALTER TABLE durable_operations ADD CONSTRAINT ck_operation_cancellation
             CHECK(status <> 'CANCELLED_WITH_DISPOSITION' OR (cancellation_disposition IS NOT NULL AND length(trim(cancellation_disposition)) BETWEEN 1 AND 2000)) NOT VALID;

            CREATE FUNCTION operator_command_snapshot(f firm_safety_states,o durable_operations,s recovery_sessions) RETURNS jsonb LANGUAGE sql STABLE AS $$
            SELECT jsonb_build_object('OperatingMode',(f).operating_mode,'DeploymentEpoch',(f).deployment_epoch::text,'RecoveryEpoch',(f).recovery_epoch::text,'PolicyGeneration',(f).policy_generation::text,
             'Operation',(CASE WHEN (o).id IS NULL THEN NULL ELSE jsonb_build_object('Id',(o).id,'ClientId',(o).client_id,'EngagementId',(o).engagement_id,'TargetId',(o).target_id,
             'CorrelationId',(o).correlation_id,'Kind',(o).operation_kind,'RequestDigest',(o).request_digest,'ExpectedRevision',(o).expected_revision::text,'AttemptToken',(o).attempt_token::text,
             'AttemptCount',(o).attempt_count::text,'Status',(o).status,'IsReconciliation',(o).is_reconciliation,'HasLease',((o).lease_owner IS NOT NULL OR (o).lease_expires_at IS NOT NULL),
             'ErrorCode',(o).error_code,'CancellationDisposition',(o).cancellation_disposition,'NextAttemptAt',(o).next_attempt_at,'CompletedAt',(o).completed_at,
             'HasResultEvidence',((o).result_identity IS NOT NULL AND (o).result_digest IS NOT NULL)) END),
             'RecoverySession',(CASE WHEN (s).id IS NULL THEN NULL ELSE jsonb_build_object('Id',(s).id,'ExternalEpoch',(s).external_epoch::text,'RestorePoint',(s).restore_point,
             'ReconciliationScope',(s).reconciliation_scope,'Findings',(s).findings,'CreatedAt',(s).created_at,'ApprovedRestartAt',(s).approved_restart_at,'ApprovedByUserId',(s).approved_by_user_id) END));
            $$;
            CREATE FUNCTION operator_command_normalize(v jsonb) RETURNS jsonb LANGUAGE plpgsql STABLE AS $$
            DECLARE r jsonb:=v;path text[];
            BEGIN
             FOREACH path SLICE 1 IN ARRAY ARRAY[['Operation','NextAttemptAt'],['Operation','CompletedAt'],['RecoverySession','CreatedAt'],['RecoverySession','ApprovedRestartAt']] LOOP
              IF r#>>path IS NOT NULL THEN r:=jsonb_set(r,path,to_jsonb((r#>>path)::timestamptz));END IF;
             END LOOP;
             RETURN r;
            END;
            $$;
            CREATE FUNCTION operator_command_authorized(f uuid) RETURNS boolean LANGUAGE sql VOLATILE AS $$
             SELECT EXISTS(SELECT 1 FROM users u WHERE u.firm_id=f AND u.id=nullif(current_setting('audit.operator_actor_id',true),'')::uuid AND NOT u.disabled
              AND u.session_epoch=nullif(current_setting('audit.operator_actor_epoch',true),'')::bigint)
             AND EXISTS(SELECT 1 FROM role_grants g WHERE g.firm_id=f AND g.user_id=nullif(current_setting('audit.operator_actor_id',true),'')::uuid AND lower(g.role)='administrator'
              AND g.client_id IS NULL AND g.engagement_id IS NULL AND g.revoked_at IS NULL AND (g.expires_at IS NULL OR g.expires_at>clock_timestamp()));
            $$;
            CREATE FUNCTION retain_operator_command_transition() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE action_value text:=current_setting('audit.operator_action',true);eid uuid:=nullif(current_setting('audit.operator_event_id',true),'')::uuid;
             actor uuid:=nullif(current_setting('audit.operator_actor_id',true),'')::uuid;epoch bigint:=nullif(current_setting('audit.operator_actor_epoch',true),'')::bigint;
             f firm_safety_states%ROWTYPE;nf firm_safety_states%ROWTYPE;s recovery_sessions%ROWTYPE;b jsonb;a jsonb;i jsonb;fid uuid;oid uuid;sid uuid;source uuid;requestid uuid;expected_hash text;
            BEGIN
             IF eid IS NULL OR action_value IS NULL OR action_value='' THEN
              IF TG_TABLE_NAME='durable_operations' THEN
               IF (NEW.status='CANCELLED_WITH_DISPOSITION' AND OLD.status IS DISTINCT FROM NEW.status)
                 OR (NEW.status='RETRY_WAIT' AND OLD.status IN ('PROVIDER_BLOCKED','DEAD_LETTER','AUTHORIZATION_BLOCKED','RESULT_UNCERTAIN'))
               THEN RAISE EXCEPTION 'Operator retry and cancellation require preview-bound receipt evidence.' USING ERRCODE='55000';END IF;
              ELSIF TG_TABLE_NAME='recovery_sessions' THEN
               IF OLD.approved_restart_at IS NULL AND NEW.approved_restart_at IS NOT NULL
               THEN RAISE EXCEPTION 'Recovery restart approval requires preview-bound receipt evidence.' USING ERRCODE='55000';END IF;
              ELSIF TG_TABLE_NAME='firm_safety_states' THEN
               IF OLD.operating_mode='RECOVERY_QUARANTINE' AND NEW.operating_mode='LOCAL_ONLY' AND NEW.deployment_epoch=OLD.deployment_epoch+1
               THEN RAISE EXCEPTION 'Quarantine lift requires preview-bound receipt evidence.' USING ERRCODE='55000';END IF;
              END IF;
              RETURN NEW;
             END IF;
             i:=current_setting('audit.operator_input')::jsonb;
             IF TG_TABLE_NAME='firm_safety_states' THEN fid:=NEW.id;ELSE fid:=NEW.firm_id;END IF;
             IF NOT operator_command_authorized(fid) OR (i->>'Action') IS DISTINCT FROM action_value THEN RAISE EXCEPTION 'Current exact operator publication authority required.' USING ERRCODE='55000';END IF;
             SELECT * INTO f FROM firm_safety_states WHERE id=fid;
             SELECT * INTO s FROM recovery_sessions WHERE firm_id=fid ORDER BY external_epoch DESC,created_at DESC,id DESC LIMIT 1;
             IF TG_TABLE_NAME='durable_operations' THEN
              oid:=NEW.id;source:=nullif(current_setting('audit.operator_source_id',true),'')::uuid;
              IF action_value NOT IN ('RETRY','CANCEL') OR oid IS DISTINCT FROM nullif(current_setting('audit.operator_operation_id',true),'')::uuid
               OR oid IS DISTINCT FROM (i->>'OperationId')::uuid OR source IS NULL OR OLD.firm_id IS DISTINCT FROM NEW.firm_id
               OR OLD.lease_owner IS NOT NULL OR OLD.lease_expires_at IS NOT NULL OR NEW.lease_owner IS NOT NULL OR NEW.lease_expires_at IS NOT NULL
              THEN RAISE EXCEPTION 'Operator transition must bind its exact unleased operation.' USING ERRCODE='55000';END IF;
              IF action_value='RETRY' THEN
               IF f.operating_mode<>'LOCAL_ONLY' OR OLD.status NOT IN ('PROVIDER_BLOCKED','DEAD_LETTER','AUTHORIZATION_BLOCKED','RESULT_UNCERTAIN') OR NEW.status<>'RETRY_WAIT'
                OR NEW.attempt_count<>0 OR NEW.error_code IS NOT NULL OR NEW.is_reconciliation IS DISTINCT FROM (OLD.is_reconciliation OR OLD.status='RESULT_UNCERTAIN')
                OR (to_jsonb(NEW)-ARRAY['status','error_code','next_attempt_at','attempt_count','lease_owner','lease_expires_at','is_reconciliation']) IS DISTINCT FROM
                   (to_jsonb(OLD)-ARRAY['status','error_code','next_attempt_at','attempt_count','lease_owner','lease_expires_at','is_reconciliation'])
               THEN RAISE EXCEPTION 'Re-arm must preserve uncertainty and the original operation attempt.' USING ERRCODE='55000';END IF;
              ELSE
               IF OLD.status NOT IN ('PENDING','RETRY_WAIT') OR OLD.is_reconciliation OR NEW.status<>'CANCELLED_WITH_DISPOSITION' OR NEW.error_code IS DISTINCT FROM 'operator-cancelled'
                OR NEW.cancellation_disposition IS NULL OR NEW.cancellation_disposition IS DISTINCT FROM (i->>'Disposition') OR length(trim(NEW.cancellation_disposition)) NOT BETWEEN 1 AND 2000
                OR (to_jsonb(NEW)-ARRAY['status','error_code','cancellation_disposition','next_attempt_at']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','error_code','cancellation_disposition','next_attempt_at'])
               THEN RAISE EXCEPTION 'Cancellation must retain its exact queued source and disposition.' USING ERRCODE='55000';END IF;
              END IF;
              b:=operator_command_snapshot(f,OLD,s);a:=operator_command_snapshot(f,NEW,s);
             ELSIF TG_TABLE_NAME='recovery_sessions' THEN
              sid:=NEW.id;
              IF action_value<>'APPROVE_RECOVERY_RESTART' OR sid IS DISTINCT FROM s.id OR sid IS DISTINCT FROM nullif(current_setting('audit.operator_session_id',true),'')::uuid
               OR sid IS DISTINCT FROM (i->>'RecoverySessionId')::uuid OR f.operating_mode<>'RECOVERY_QUARANTINE' OR f.recovery_epoch<>OLD.external_epoch OR f.deployment_epoch=9223372036854775807
               OR OLD.approved_restart_at IS NOT NULL OR NEW.approved_restart_at IS NULL OR NEW.approved_by_user_id IS DISTINCT FROM actor OR NEW.findings IS DISTINCT FROM (i->>'Findings')
               OR length(trim(NEW.findings)) NOT BETWEEN 1 AND 10000 OR (to_jsonb(NEW)-ARRAY['findings','approved_restart_at','approved_by_user_id']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['findings','approved_restart_at','approved_by_user_id'])
              THEN RAISE EXCEPTION 'Restart approval must record a new decision for the exact current recovery epoch.' USING ERRCODE='55000';END IF;
              nf:=f;nf.operating_mode:='LOCAL_ONLY';nf.deployment_epoch:=f.deployment_epoch+1;
              b:=operator_command_snapshot(f,NULL::durable_operations,OLD);a:=operator_command_snapshot(nf,NULL::durable_operations,NEW);
             ELSIF TG_TABLE_NAME='firm_safety_states' THEN
              IF action_value='APPROVE_RECOVERY_RESTART' THEN
               -- Session update already generated the real event, projecting this required firm transition.
               IF NOT EXISTS(SELECT 1 FROM operator_command_events e WHERE e.id=eid AND e.firm_id=fid AND e.action=action_value
                 AND (e.before_json::jsonb->>'DeploymentEpoch')::bigint=OLD.deployment_epoch AND (e.after_json::jsonb->>'DeploymentEpoch')::bigint=NEW.deployment_epoch
                 AND (e.after_json::jsonb->>'RecoveryEpoch')::bigint=NEW.recovery_epoch AND NEW.operating_mode='LOCAL_ONLY')
                OR (to_jsonb(NEW)-ARRAY['operating_mode','deployment_epoch']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['operating_mode','deployment_epoch'])
               THEN RAISE EXCEPTION 'Firm restart must complete its real captured approval.' USING ERRCODE='55000';END IF;
               RETURN NEW;
              END IF;
              sid:=s.id;
              IF action_value<>'LIFT_QUARANTINE' OR OLD.operating_mode<>'RECOVERY_QUARANTINE' OR NEW.operating_mode<>'LOCAL_ONLY' OR OLD.deployment_epoch=9223372036854775807
               OR NEW.deployment_epoch<>OLD.deployment_epoch+1 OR (to_jsonb(NEW)-ARRAY['operating_mode','deployment_epoch']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['operating_mode','deployment_epoch'])
               OR sid IS DISTINCT FROM nullif(current_setting('audit.operator_session_id',true),'')::uuid OR sid IS DISTINCT FROM (i->>'RecoverySessionId')::uuid
               OR (sid IS NULL AND OLD.recovery_epoch<>0) OR (sid IS NOT NULL AND (s.external_epoch<>OLD.recovery_epoch OR s.approved_restart_at IS NULL OR s.approved_by_user_id IS NULL OR length(trim(s.findings))=0))
              THEN RAISE EXCEPTION 'Lift must retain the exact approved quarantine and increment its epoch once.' USING ERRCODE='55000';END IF;
              b:=operator_command_snapshot(OLD,NULL::durable_operations,s);a:=operator_command_snapshot(NEW,NULL::durable_operations,s);
             ELSE RAISE EXCEPTION 'Unsupported operator source.' USING ERRCODE='55000';END IF;
             IF b=a THEN RAISE EXCEPTION 'A no-op cannot create operator effect evidence.' USING ERRCODE='55000';END IF;
             requestid:=nullif(current_setting('audit.operator_request_id',true),'')::uuid;
             expected_hash:=operator_command_request_hash(fid,actor,epoch,requestid,i,b);
             IF current_setting('audit.operator_hash',true) IS DISTINCT FROM expected_hash THEN
              RAISE EXCEPTION 'Operator request hash must bind the exact actor, request, intent and reviewed source snapshot.' USING ERRCODE='55000';
             END IF;
             INSERT INTO operator_command_events(id,firm_id,actor_id,actor_epoch,request_id,request_hash,action,operation_id,recovery_session_id,operation_event_id,before_json,after_json,created_at)
             VALUES(eid,fid,actor,epoch,requestid,expected_hash,action_value,oid,sid,source,b::text,a::text,clock_timestamp());
             RETURN NEW;
            END;
            $$;
            CREATE TRIGGER operator_operation_transition AFTER UPDATE ON durable_operations FOR EACH ROW EXECUTE FUNCTION retain_operator_command_transition();
            CREATE TRIGGER operator_recovery_transition AFTER UPDATE ON recovery_sessions FOR EACH ROW EXECUTE FUNCTION retain_operator_command_transition();
            CREATE TRIGGER operator_firm_transition AFTER UPDATE ON firm_safety_states FOR EACH ROW EXECUTE FUNCTION retain_operator_command_transition();

            CREATE FUNCTION guard_operator_command_event() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
             IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Operator events are immutable.' USING ERRCODE='55000';END IF;
             IF pg_trigger_depth()<>2 OR NEW.id IS DISTINCT FROM nullif(current_setting('audit.operator_event_id',true),'')::uuid
              OR NEW.actor_id IS DISTINCT FROM nullif(current_setting('audit.operator_actor_id',true),'')::uuid OR NEW.actor_epoch IS DISTINCT FROM nullif(current_setting('audit.operator_actor_epoch',true),'')::bigint
              OR NEW.request_id IS DISTINCT FROM nullif(current_setting('audit.operator_request_id',true),'')::uuid OR NEW.request_hash IS DISTINCT FROM current_setting('audit.operator_hash',true)
              OR NEW.request_hash IS DISTINCT FROM operator_command_request_hash(NEW.firm_id,NEW.actor_id,NEW.actor_epoch,NEW.request_id,current_setting('audit.operator_input',true)::jsonb,NEW.before_json::jsonb)
              OR NEW.action IS DISTINCT FROM current_setting('audit.operator_action',true) OR NOT operator_command_authorized(NEW.firm_id)
              OR NEW.before_json::jsonb=NEW.after_json::jsonb
             THEN RAISE EXCEPTION 'Only a real exact scoped source transition can create operator evidence.' USING ERRCODE='55000';END IF;
             RETURN NEW;
            END;
            $$;
            CREATE TRIGGER operator_command_event_guard BEFORE INSERT OR UPDATE OR DELETE ON operator_command_events FOR EACH ROW EXECUTE FUNCTION guard_operator_command_event();
            CREATE TRIGGER operator_command_event_no_truncate BEFORE TRUNCATE ON operator_command_events FOR EACH STATEMENT EXECUTE FUNCTION guard_operator_command_event();

            CREATE FUNCTION guard_operator_command_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE e operator_command_events%ROWTYPE;f firm_safety_states%ROWTYPE;o durable_operations%ROWTYPE;s recovery_sessions%ROWTYPE;i jsonb;p jsonb;
            BEGIN
             IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Operator receipts are immutable.' USING ERRCODE='55000';END IF;
             SELECT * INTO e FROM operator_command_events WHERE id=NEW.event_id AND firm_id=NEW.firm_id AND actor_id=NEW.actor_id;
             SELECT * INTO f FROM firm_safety_states WHERE id=NEW.firm_id;
             IF e.operation_id IS NOT NULL THEN SELECT * INTO o FROM durable_operations WHERE id=e.operation_id AND firm_id=NEW.firm_id;END IF;
             SELECT * INTO s FROM recovery_sessions WHERE firm_id=NEW.firm_id ORDER BY external_epoch DESC,created_at DESC,id DESC LIMIT 1;
             i:=NEW.input_json::jsonb;p:=NEW.preview_json::jsonb;
             IF e.id IS NULL OR e.actor_epoch IS DISTINCT FROM NEW.actor_epoch OR e.request_id IS DISTINCT FROM NEW.request_id OR e.request_hash IS DISTINCT FROM NEW.request_hash OR e.action IS DISTINCT FROM NEW.action
              OR e.id IS DISTINCT FROM nullif(current_setting('audit.operator_event_id',true),'')::uuid OR NOT operator_command_authorized(NEW.firm_id)
              OR i IS DISTINCT FROM current_setting('audit.operator_input',true)::jsonb OR (i->>'Action') IS DISTINCT FROM NEW.action
              OR (i->>'OperationId')::uuid IS DISTINCT FROM e.operation_id OR (i->>'RecoverySessionId')::uuid IS DISTINCT FROM e.recovery_session_id
              OR (p->>'RequestId')::uuid IS DISTINCT FROM NEW.request_id OR (p->>'RequestHash') IS DISTINCT FROM NEW.request_hash OR (p->>'CanProceed') IS DISTINCT FROM 'true'
              OR (p->>'ReviewBasis') IS DISTINCT FROM operator_command_basis(NEW.firm_id,NEW.actor_id,NEW.actor_epoch,e.before_json::jsonb)
              OR NEW.request_hash IS DISTINCT FROM operator_command_request_hash(NEW.firm_id,NEW.actor_id,NEW.actor_epoch,NEW.request_id,i,e.before_json::jsonb)
              OR (p->'CanonicalInput') IS DISTINCT FROM i OR (p->'Blockers') IS DISTINCT FROM '[]'::jsonb
              OR operator_command_normalize(p->'Before') IS DISTINCT FROM operator_command_normalize(e.before_json::jsonb)
              OR operator_command_normalize(operator_command_snapshot(f,o,s)) IS DISTINCT FROM operator_command_normalize(e.after_json::jsonb)
             THEN RAISE EXCEPTION 'Receipt must bind its exact original event, preview, canonical intent and actual publication.' USING ERRCODE='23514';END IF;
             IF (NEW.action='RETRY' AND ((i->'Disposition') IS DISTINCT FROM 'null'::jsonb OR (i->'Findings') IS DISTINCT FROM 'null'::jsonb))
              OR (NEW.action='CANCEL' AND ((i->'Findings') IS DISTINCT FROM 'null'::jsonb OR (i->>'Disposition') IS NULL OR length(trim(i->>'Disposition')) NOT BETWEEN 1 AND 2000 OR (i->>'Disposition') IS DISTINCT FROM (e.after_json::jsonb->'Operation'->>'CancellationDisposition')))
              OR (NEW.action='LIFT_QUARANTINE' AND ((i->'Disposition') IS DISTINCT FROM 'null'::jsonb OR (i->'Findings') IS DISTINCT FROM 'null'::jsonb))
              OR (NEW.action='APPROVE_RECOVERY_RESTART' AND ((i->'Disposition') IS DISTINCT FROM 'null'::jsonb OR (i->>'Findings') IS DISTINCT FROM (e.after_json::jsonb->'RecoverySession'->>'Findings')))
             THEN RAISE EXCEPTION 'Receipt action fields must equal its exact operator effect.' USING ERRCODE='23514';END IF;
             IF e.operation_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM operation_events source WHERE source.id=e.operation_event_id AND source.operation_id=e.operation_id
              AND source.executor=NEW.actor_id::text AND source.token=(e.before_json::jsonb->'Operation'->>'AttemptToken')::bigint
              AND source.kind=(CASE WHEN NEW.action='RETRY' THEN 'operation.recovery-retry.v1' ELSE 'operation.cancelled.v1' END))
             THEN RAISE EXCEPTION 'Receipt requires its exact same-operation actor/action/attempt source event.' USING ERRCODE='23514';END IF;
             RETURN NEW;
            EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range OR datetime_field_overflow THEN RAISE EXCEPTION 'Malformed operator receipt evidence.' USING ERRCODE='23514';
            END;
            $$;
            CREATE TRIGGER operator_command_receipt_guard BEFORE INSERT OR UPDATE OR DELETE ON operator_command_receipts FOR EACH ROW EXECUTE FUNCTION guard_operator_command_receipt();
            CREATE TRIGGER operator_command_receipt_no_truncate BEFORE TRUNCATE ON operator_command_receipts FOR EACH STATEMENT EXECUTE FUNCTION guard_operator_command_receipt();
            CREATE FUNCTION require_operator_command_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
             IF NOT EXISTS(SELECT 1 FROM operator_command_receipts r WHERE r.firm_id=NEW.firm_id AND r.actor_id=NEW.actor_id AND r.event_id=NEW.id AND r.actor_epoch=NEW.actor_epoch
              AND r.request_id=NEW.request_id AND r.request_hash=NEW.request_hash AND r.action=NEW.action)
             THEN RAISE EXCEPTION 'Operator transition and original acknowledgement must commit atomically.' USING ERRCODE='23514';END IF;
             RETURN NEW;
            END;
            $$;
            CREATE CONSTRAINT TRIGGER operator_command_receipt_required AFTER INSERT ON operator_command_events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_operator_command_receipt();
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            -- Retained operator evidence is append-only and cannot be discarded by a downgrade.
            LOCK TABLE firm_safety_states,recovery_sessions,durable_operations,operation_events,operator_command_events,operator_command_receipts IN ACCESS EXCLUSIVE MODE;
            DO $$ BEGIN
             IF EXISTS(SELECT 1 FROM operator_command_events) OR EXISTS(SELECT 1 FROM operator_command_receipts) THEN
              RAISE EXCEPTION 'Operator evidence must be retained; downgrade refused.' USING ERRCODE='55000';
             END IF;
            END $$;
            DROP TRIGGER operator_command_receipt_required ON operator_command_events;
            DROP TRIGGER operator_command_receipt_no_truncate ON operator_command_receipts;
            DROP TRIGGER operator_command_receipt_guard ON operator_command_receipts;
            DROP TRIGGER operator_command_event_no_truncate ON operator_command_events;
            DROP TRIGGER operator_command_event_guard ON operator_command_events;
            DROP TRIGGER operator_operation_transition ON durable_operations;
            DROP TRIGGER operator_recovery_transition ON recovery_sessions;
            DROP TRIGGER operator_firm_transition ON firm_safety_states;
            DROP FUNCTION require_operator_command_receipt();
            DROP FUNCTION guard_operator_command_receipt();
            DROP FUNCTION guard_operator_command_event();
            DROP FUNCTION retain_operator_command_transition();
            DROP FUNCTION operator_command_authorized(uuid);
            DROP FUNCTION operator_command_normalize(jsonb);
            DROP FUNCTION operator_command_snapshot(firm_safety_states,durable_operations,recovery_sessions);
            DROP FUNCTION operator_command_request_hash(uuid,uuid,bigint,uuid,jsonb,jsonb);
            DROP FUNCTION operator_command_basis(uuid,uuid,bigint,jsonb);
            DROP FUNCTION operator_command_hash_snapshot(jsonb);
            DROP FUNCTION operator_command_hash(jsonb);
            """);
            migrationBuilder.DropTable(
                name: "operator_command_receipts");

            migrationBuilder.DropTable(
                name: "operator_command_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_cancellation",
                table: "durable_operations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_cancellation",
                table: "durable_operations",
                sql: "status <> 'CANCELLED_WITH_DISPOSITION' OR length(trim(cancellation_disposition)) > 0");
        }
    }
}
