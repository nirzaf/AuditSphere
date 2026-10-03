using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeAdjustmentPlanEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "adjustment_plan_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    after_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adjustment_plan_actions", x => x.id);
                    table.CheckConstraint("ck_adjustment_plan_action", "action IN ('CREATE','FINALIZE') AND actor_epoch >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000 AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000");
                    table.ForeignKey(
                        name: "FK_adjustment_plan_actions_adjustment_plans_firm_id_plan_id",
                        columns: x => new { x.firm_id, x.plan_id },
                        principalTable: "adjustment_plans",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_adjustment_plan_actions_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_plan_actions_firm_id_actor_id_request_id",
                table: "adjustment_plan_actions",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_adjustment_plan_actions_firm_id_plan_id_created_at",
                table: "adjustment_plan_actions",
                columns: new[] { "firm_id", "plan_id", "created_at" });
            migrationBuilder.Sql("""
              CREATE FUNCTION guard_native_plan_action() RETURNS trigger LANGUAGE plpgsql AS $$
              DECLARE p adjustment_plans; d trial_balance_datasets; r jsonb;
              BEGIN
                IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Plan command evidence is append-only'; END IF;
                SELECT * INTO p FROM adjustment_plans WHERE firm_id=NEW.firm_id AND id=NEW.plan_id FOR KEY SHARE;
                SELECT * INTO d FROM trial_balance_datasets WHERE id=p.base_dataset_id;
                IF p.id IS NULL OR p.client_id<>NEW.client_id OR p.engagement_id<>NEW.engagement_id
                  OR p.base_dataset_id<>NEW.dataset_id OR d.firm_id<>p.firm_id OR d.client_id<>p.client_id OR d.engagement_id<>p.engagement_id
                  OR (NEW.action='CREATE' AND (p.status<>'Draft' OR p.created_by_user_id<>NEW.actor_id))
                  OR (NEW.action='FINALIZE' AND p.status<>'Finalized') THEN
                  RAISE EXCEPTION 'Plan evidence requires exact scoped context and result';
                END IF;
                r:=NEW.after_json::jsonb;
                IF r->>'Id' IS DISTINCT FROM NEW.id::text OR r->>'PlanId' IS DISTINCT FROM NEW.plan_id::text
                  OR r->>'DatasetId' IS DISTINCT FROM NEW.dataset_id::text OR r->>'ActorId' IS DISTINCT FROM NEW.actor_id::text
                  OR r->>'RequestId' IS DISTINCT FROM NEW.request_id::text OR r->>'RequestHash' IS DISTINCT FROM NEW.request_hash
                  OR r->>'Action' IS DISTINCT FROM NEW.action OR r->>'Reason' IS DISTINCT FROM NEW.reason
                  OR r->>'EvidenceReference' IS DISTINCT FROM NEW.evidence_reference OR r->>'Status' IS DISTINCT FROM p.status
                  OR (NEW.action='FINALIZE' AND (r->>'ResultHash' IS DISTINCT FROM p.result_hash
                    OR (r->>'Debits')::numeric IS DISTINCT FROM p.applied_debits
                    OR (r->>'Credits')::numeric IS DISTINCT FROM p.applied_credits_abs
                    OR (r->>'AppliedCount')::integer IS DISTINCT FROM p.applied_journal_count)) THEN
                  RAISE EXCEPTION 'Plan receipt must match retained identity and result';
                END IF;
                PERFORM NEW.before_json::jsonb;
                RETURN NEW;
              END $$;
              CREATE TRIGGER native_plan_action_guard BEFORE INSERT OR UPDATE OR DELETE ON adjustment_plan_actions
                FOR EACH ROW EXECUTE FUNCTION guard_native_plan_action();

              CREATE FUNCTION guard_native_plan_header() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF OLD.status='Finalized' OR EXISTS(SELECT 1 FROM adjustment_plan_actions WHERE plan_id=OLD.id) THEN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Retained plans cannot be deleted'; END IF;
                  IF ROW(NEW.id,NEW.firm_id,NEW.client_id,NEW.engagement_id,NEW.base_dataset_id,NEW.created_by_user_id,NEW.created_at)
                    IS DISTINCT FROM ROW(OLD.id,OLD.firm_id,OLD.client_id,OLD.engagement_id,OLD.base_dataset_id,OLD.created_by_user_id,OLD.created_at)
                    OR (OLD.status='Finalized' AND NEW IS DISTINCT FROM OLD)
                    OR (OLD.status='Draft' AND NEW.status<>'Finalized' AND NEW IS DISTINCT FROM OLD) THEN
                    RAISE EXCEPTION 'Retained plan context and calculations are immutable';
                  END IF;
                END IF;
                IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER native_plan_header_guard BEFORE UPDATE OR DELETE ON adjustment_plans
                FOR EACH ROW EXECUTE FUNCTION guard_native_plan_header();

              CREATE FUNCTION guard_native_plan_membership() RETURNS trigger LANGUAGE plpgsql AS $$
              DECLARE target uuid; p adjustment_plans;
              BEGIN
                IF TG_OP='INSERT' THEN target:=NEW.plan_id; ELSE target:=OLD.plan_id; END IF;
                SELECT * INTO p FROM adjustment_plans WHERE id=target FOR UPDATE;
                IF p.status='Finalized' OR EXISTS(SELECT 1 FROM adjustment_plan_actions WHERE plan_id=target) THEN
                  RAISE EXCEPTION 'Retained plan membership is immutable; create a replacement';
                END IF;
                IF TG_OP='UPDATE' AND NEW.plan_id IS DISTINCT FROM OLD.plan_id THEN
                  RAISE EXCEPTION 'Plan membership cannot move between plans';
                END IF;
                IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER native_plan_membership_guard BEFORE INSERT OR UPDATE OR DELETE ON adjustment_plan_lines
                FOR EACH ROW EXECUTE FUNCTION guard_native_plan_membership();

              CREATE FUNCTION require_native_plan_finalization_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF EXISTS(SELECT 1 FROM adjustment_plan_actions WHERE plan_id=NEW.id AND action='CREATE')
                  AND NOT EXISTS(SELECT 1 FROM adjustment_plan_actions WHERE plan_id=NEW.id AND action='FINALIZE'
                    AND after_json::jsonb->>'ResultHash'=NEW.result_hash) THEN
                  RAISE EXCEPTION 'Native plan finalization requires atomic retained evidence';
                END IF;
                RETURN NULL;
              END $$;
              CREATE CONSTRAINT TRIGGER native_plan_finalization_evidence AFTER UPDATE ON adjustment_plans
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.status='Finalized')
                EXECUTE FUNCTION require_native_plan_finalization_evidence();
              """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
              DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM adjustment_plan_actions) THEN
                  RAISE EXCEPTION 'Retained native plan evidence prevents migration rollback';
                END IF;
              END $$;
              DROP TRIGGER native_plan_finalization_evidence ON adjustment_plans;
              DROP FUNCTION require_native_plan_finalization_evidence();
              DROP TRIGGER native_plan_membership_guard ON adjustment_plan_lines;
              DROP FUNCTION guard_native_plan_membership();
              DROP TRIGGER native_plan_header_guard ON adjustment_plans;
              DROP FUNCTION guard_native_plan_header();
              DROP TRIGGER native_plan_action_guard ON adjustment_plan_actions;
              DROP FUNCTION guard_native_plan_action();
              """);
            migrationBuilder.DropTable(
                name: "adjustment_plan_actions");
        }
    }
}
