using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowReminderRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings");

            migrationBuilder.AddColumn<Guid>(
                name: "reminder_operation_id",
                table: "workflow_task_bindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reminder_rule_revision_id",
                table: "workflow_task_bindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workflow_reminder_rule_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    content_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_reminder_rule_decisions", x => x.id);
                    table.UniqueConstraint("AK_workflow_reminder_rule_decisions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_reminder_rule_decision_values", "kind IN ('DRAFT_CREATED','DRAFT_EDITED','REVIEWED','PUBLISHED','SUPERSEDED','SUSPENDED','RESUMED') AND content_digest ~ '^[0-9a-f]{64}$' AND length(trim(rationale)) BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "FK_workflow_reminder_rule_decisions_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_reminder_rule_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    creation_request_key = table.Column<Guid>(type: "uuid", nullable: false),
                    creation_request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    source_kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    recipient_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    offset_days = table.Column<int>(type: "integer", nullable: false),
                    repeat_every_days = table.Column<int>(type: "integer", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quiet_hours_start = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    quiet_hours_end = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    effective_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    content_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_edited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_session_epoch = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_reminder_rule_revisions", x => x.id);
                    table.UniqueConstraint("AK_workflow_reminder_rule_revisions_firm_creation_request", x => new { x.firm_id, x.creation_request_key });
                    table.UniqueConstraint("AK_workflow_reminder_rule_revisions_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_workflow_reminder_rule_revisions_scope_id", x => new { x.firm_id, x.rule_id, x.id });
                    table.CheckConstraint("ck_workflow_reminder_rule_revision_values", "version >= 1 AND revision >= 1 AND creation_request_key <> '00000000-0000-0000-0000-000000000000'::uuid AND creation_request_digest ~ '^[0-9a-f]{64}$' AND status IN ('DRAFT','REVIEWED','PUBLISHED','SUPERSEDED') AND length(trim(name)) BETWEEN 2 AND 160 AND source_kind IN ('TASK_DUE','PBC_DUE') AND recipient_kind IN ('RESPONSIBLE_STAFF','ENGAGEMENT_REVIEWER') AND offset_days BETWEEN -30 AND 365 AND repeat_every_days BETWEEN 0 AND 90 AND length(trim(time_zone_id)) BETWEEN 1 AND 100 AND quiet_hours_start ~ '^(?:[01][0-9]|2[0-3]):[0-5][0-9]$' AND quiet_hours_end ~ '^(?:[01][0-9]|2[0-3]):[0-5][0-9]$' AND content_digest ~ '^[0-9a-f]{64}$' AND ((status = 'DRAFT' AND reviewed_by_user_id IS NULL AND reviewed_at IS NULL AND published_by_user_id IS NULL AND published_at IS NULL AND published_session_epoch IS NULL) OR (status = 'REVIEWED' AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND reviewed_by_user_id <> created_by_user_id AND reviewed_by_user_id <> last_edited_by_user_id AND published_by_user_id IS NULL AND published_at IS NULL AND published_session_epoch IS NULL) OR (status IN ('PUBLISHED','SUPERSEDED') AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL AND reviewed_by_user_id <> created_by_user_id AND reviewed_by_user_id <> last_edited_by_user_id AND published_by_user_id IS NOT NULL AND published_at IS NOT NULL AND published_session_epoch >= 1))");
                    table.ForeignKey(
                        name: "FK_workflow_reminder_rule_revisions_users_firm_id_created_by_u~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_reminder_rule_revisions_users_firm_id_last_edited_~",
                        columns: x => new { x.firm_id, x.last_edited_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_reminder_rule_revisions_users_firm_id_published_by~",
                        columns: x => new { x.firm_id, x.published_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_reminder_rule_revisions_users_firm_id_reviewed_by_~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_reminder_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_published_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspended_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suspension_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_evaluation_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scan_after_due_date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    scan_after_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scan_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_reminder_rules", x => x.id);
                    table.UniqueConstraint("AK_workflow_reminder_rules_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_reminder_rule_values", "revision >= 1 AND ((suspended_at IS NULL AND suspended_by_user_id IS NULL AND suspension_reason IS NULL) OR (suspended_at IS NOT NULL AND suspended_by_user_id IS NOT NULL AND suspension_reason IS NOT NULL AND length(trim(suspension_reason)) BETWEEN 1 AND 1000)) AND ((scan_after_due_date IS NULL) = (scan_after_source_id IS NULL)) AND (scan_after_due_date IS NULL OR (scan_after_due_date ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND scan_revision_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_workflow_reminder_rules_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_reminder_rules_users_firm_id_suspended_by_user_id",
                        columns: x => new { x.firm_id, x.suspended_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_reminder_rules_workflow_reminder_rule_revisions_fi~",
                        columns: x => new { x.firm_id, x.id, x.current_published_revision_id },
                        principalTable: "workflow_reminder_rule_revisions",
                        principalColumns: new[] { "firm_id", "rule_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_reminder_rules_workflow_reminder_rule_revisions_f~1",
                        columns: x => new { x.firm_id, x.id, x.scan_revision_id },
                        principalTable: "workflow_reminder_rule_revisions",
                        principalColumns: new[] { "firm_id", "rule_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_bindings_firm_id_reminder_rule_revision_id",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "reminder_rule_revision_id" });

            migrationBuilder.CreateIndex(
                name: "UX_workflow_task_bindings_reminder_operation",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "reminder_operation_id" },
                unique: true,
                filter: "reminder_operation_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings",
                sql: "ordinal BETWEEN 1 AND 100000 AND generation >= 1 AND revision >= 1 AND (planned_effort_minutes IS NULL OR planned_effort_minutes BETWEEN 1 AND 1000000) AND due_date_policy IN ('MANUAL','FIXED','TEMPLATE_OFFSET') AND assignment_source IN ('MANUAL','TEMPLATE','MANUAL_REASSIGNMENT','AUTOMATION') AND (assigned_role IS NULL OR assigned_role IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND length(source_key) BETWEEN 1 AND 160 AND creation_request_digest ~ '^[0-9a-f]{64}$' AND ((template_revision_id IS NULL AND template_step_id IS NULL) OR (template_revision_id IS NOT NULL AND template_step_id IS NOT NULL)) AND (review_procedure_result_id IS NULL OR source_key LIKE 'REVIEW-REWORK:%') AND ((reminder_rule_revision_id IS NULL) = (reminder_operation_id IS NULL)) AND (cancellation_reason IS NULL OR length(trim(cancellation_reason)) > 0) AND (superseded_at IS NULL OR (supersession_reason IS NOT NULL AND length(trim(supersession_reason)) > 0))");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rule_decisions_firm_id_actor_user_id",
                table: "workflow_reminder_rule_decisions",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rule_decisions_firm_id_rule_id_occurred_a~",
                table: "workflow_reminder_rule_decisions",
                columns: new[] { "firm_id", "rule_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rule_decisions_firm_id_rule_id_rule_revis~",
                table: "workflow_reminder_rule_decisions",
                columns: new[] { "firm_id", "rule_id", "rule_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rule_revisions_firm_id_created_by_user_id",
                table: "workflow_reminder_rule_revisions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rule_revisions_firm_id_last_edited_by_use~",
                table: "workflow_reminder_rule_revisions",
                columns: new[] { "firm_id", "last_edited_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rule_revisions_firm_id_published_by_user_~",
                table: "workflow_reminder_rule_revisions",
                columns: new[] { "firm_id", "published_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rule_revisions_firm_id_reviewed_by_user_id",
                table: "workflow_reminder_rule_revisions",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rule_revisions_firm_id_rule_id_status",
                table: "workflow_reminder_rule_revisions",
                columns: new[] { "firm_id", "rule_id", "status" });

            migrationBuilder.CreateIndex(
                name: "UX_workflow_reminder_one_open_revision",
                table: "workflow_reminder_rule_revisions",
                columns: new[] { "firm_id", "rule_id" },
                unique: true,
                filter: "status IN ('DRAFT','REVIEWED')");

            migrationBuilder.CreateIndex(
                name: "UX_workflow_reminder_one_published_revision",
                table: "workflow_reminder_rule_revisions",
                columns: new[] { "firm_id", "rule_id" },
                unique: true,
                filter: "status = 'PUBLISHED'");

            migrationBuilder.CreateIndex(
                name: "UX_workflow_reminder_rule_revisions_version",
                table: "workflow_reminder_rule_revisions",
                columns: new[] { "firm_id", "rule_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rules_firm_id_created_at_id",
                table: "workflow_reminder_rules",
                columns: new[] { "firm_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rules_firm_id_created_by_user_id",
                table: "workflow_reminder_rules",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rules_firm_id_id_current_published_revisi~",
                table: "workflow_reminder_rules",
                columns: new[] { "firm_id", "id", "current_published_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rules_firm_id_id_scan_revision_id",
                table: "workflow_reminder_rules",
                columns: new[] { "firm_id", "id", "scan_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rules_firm_id_last_evaluated_at_id",
                table: "workflow_reminder_rules",
                columns: new[] { "firm_id", "last_evaluated_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_reminder_rules_firm_id_suspended_by_user_id",
                table: "workflow_reminder_rules",
                columns: new[] { "firm_id", "suspended_by_user_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_task_bindings_durable_operations_firm_id_reminder_~",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "reminder_operation_id" },
                principalTable: "durable_operations",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_task_bindings_workflow_reminder_rule_revisions_fir~",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "reminder_rule_revision_id" },
                principalTable: "workflow_reminder_rule_revisions",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_reminder_rule_decisions_workflow_reminder_rule_rev~",
                table: "workflow_reminder_rule_decisions",
                columns: new[] { "firm_id", "rule_id", "rule_revision_id" },
                principalTable: "workflow_reminder_rule_revisions",
                principalColumns: new[] { "firm_id", "rule_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_reminder_rule_decisions_workflow_reminder_rules_fi~",
                table: "workflow_reminder_rule_decisions",
                columns: new[] { "firm_id", "rule_id" },
                principalTable: "workflow_reminder_rules",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_reminder_rule_revisions_workflow_reminder_rules_fi~",
                table: "workflow_reminder_rule_revisions",
                columns: new[] { "firm_id", "rule_id" },
                principalTable: "workflow_reminder_rules",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_workflow_reminder_decision_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'workflow reminder decisions are append-only';
                END;
                $$;

                CREATE TRIGGER trg_workflow_reminder_rule_decisions_append_only
                  BEFORE UPDATE OR DELETE ON workflow_reminder_rule_decisions
                  FOR EACH ROW EXECUTE FUNCTION prevent_workflow_reminder_decision_mutation();

                CREATE OR REPLACE FUNCTION guard_workflow_reminder_revision_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'workflow reminder revisions cannot be deleted';
                  END IF;

                  IF ROW(NEW.firm_id, NEW.rule_id, NEW.version, NEW.creation_request_key,
                         NEW.creation_request_digest, NEW.created_by_user_id, NEW.created_at)
                     IS DISTINCT FROM
                     ROW(OLD.firm_id, OLD.rule_id, OLD.version, OLD.creation_request_key,
                         OLD.creation_request_digest, OLD.created_by_user_id, OLD.created_at) THEN
                    RAISE EXCEPTION 'workflow reminder revision identity is immutable';
                  END IF;

                  IF OLD.status = 'DRAFT' AND NEW.status IN ('DRAFT','REVIEWED') THEN
                    IF NEW.status = 'REVIEWED' AND ROW(
                         NEW.name, NEW.source_kind, NEW.recipient_kind, NEW.offset_days, NEW.repeat_every_days,
                         NEW.time_zone_id, NEW.quiet_hours_start, NEW.quiet_hours_end, NEW.effective_from_utc,
                         NEW.content_digest, NEW.last_edited_by_user_id)
                       IS DISTINCT FROM ROW(
                         OLD.name, OLD.source_kind, OLD.recipient_kind, OLD.offset_days, OLD.repeat_every_days,
                         OLD.time_zone_id, OLD.quiet_hours_start, OLD.quiet_hours_end, OLD.effective_from_utc,
                         OLD.content_digest, OLD.last_edited_by_user_id) THEN
                      RAISE EXCEPTION 'review must bind the exact saved reminder definition';
                    END IF;
                    RETURN NEW;
                  END IF;

                  IF OLD.status = 'REVIEWED' AND NEW.status = 'PUBLISHED' THEN
                    IF ROW(NEW.name, NEW.source_kind, NEW.recipient_kind, NEW.offset_days, NEW.repeat_every_days,
                           NEW.time_zone_id, NEW.quiet_hours_start, NEW.quiet_hours_end, NEW.effective_from_utc,
                           NEW.content_digest, NEW.last_edited_by_user_id, NEW.reviewed_by_user_id, NEW.reviewed_at)
                       IS DISTINCT FROM ROW(OLD.name, OLD.source_kind, OLD.recipient_kind, OLD.offset_days,
                           OLD.repeat_every_days, OLD.time_zone_id, OLD.quiet_hours_start, OLD.quiet_hours_end,
                           OLD.effective_from_utc, OLD.content_digest, OLD.last_edited_by_user_id,
                           OLD.reviewed_by_user_id, OLD.reviewed_at) THEN
                      RAISE EXCEPTION 'publication must retain the independently reviewed reminder definition';
                    END IF;
                    RETURN NEW;
                  END IF;

                  IF OLD.status = 'PUBLISHED' AND NEW.status = 'SUPERSEDED' THEN
                    IF ROW(NEW.name, NEW.source_kind, NEW.recipient_kind, NEW.offset_days, NEW.repeat_every_days,
                           NEW.time_zone_id, NEW.quiet_hours_start, NEW.quiet_hours_end, NEW.effective_from_utc,
                           NEW.content_digest, NEW.last_edited_by_user_id, NEW.reviewed_by_user_id, NEW.reviewed_at,
                           NEW.published_by_user_id, NEW.published_session_epoch, NEW.published_at)
                       IS DISTINCT FROM ROW(OLD.name, OLD.source_kind, OLD.recipient_kind, OLD.offset_days,
                           OLD.repeat_every_days, OLD.time_zone_id, OLD.quiet_hours_start, OLD.quiet_hours_end,
                           OLD.effective_from_utc, OLD.content_digest, OLD.last_edited_by_user_id,
                           OLD.reviewed_by_user_id, OLD.reviewed_at, OLD.published_by_user_id,
                           OLD.published_session_epoch, OLD.published_at) THEN
                      RAISE EXCEPTION 'supersession must retain the exact published reminder definition';
                    END IF;
                    RETURN NEW;
                  END IF;

                  RAISE EXCEPTION 'invalid workflow reminder revision state transition';
                END;
                $$;

                CREATE TRIGGER trg_workflow_reminder_rule_revisions_guard
                  BEFORE UPDATE OR DELETE ON workflow_reminder_rule_revisions
                  FOR EACH ROW EXECUTE FUNCTION guard_workflow_reminder_revision_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM workflow_reminder_rules)
                     OR EXISTS (SELECT 1 FROM workflow_reminder_rule_revisions)
                     OR EXISTS (SELECT 1 FROM workflow_reminder_rule_decisions)
                     OR EXISTS (SELECT 1 FROM durable_operations WHERE operation_kind = 'CreateWorkflowReminderTask.v1') THEN
                    RAISE EXCEPTION 'Workflow reminder downgrade would discard rule, decision, or queued-work evidence.';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_workflow_reminder_rule_decisions_append_only ON workflow_reminder_rule_decisions;
                DROP FUNCTION IF EXISTS prevent_workflow_reminder_decision_mutation();
                DROP TRIGGER IF EXISTS trg_workflow_reminder_rule_revisions_guard ON workflow_reminder_rule_revisions;
                DROP FUNCTION IF EXISTS guard_workflow_reminder_revision_mutation();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_task_bindings_durable_operations_firm_id_reminder_~",
                table: "workflow_task_bindings");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_task_bindings_workflow_reminder_rule_revisions_fir~",
                table: "workflow_task_bindings");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_reminder_rules_workflow_reminder_rule_revisions_fi~",
                table: "workflow_reminder_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_reminder_rules_workflow_reminder_rule_revisions_f~1",
                table: "workflow_reminder_rules");

            migrationBuilder.DropTable(
                name: "workflow_reminder_rule_decisions");

            migrationBuilder.DropTable(
                name: "workflow_reminder_rule_revisions");

            migrationBuilder.DropTable(
                name: "workflow_reminder_rules");

            migrationBuilder.DropIndex(
                name: "IX_workflow_task_bindings_firm_id_reminder_rule_revision_id",
                table: "workflow_task_bindings");

            migrationBuilder.DropIndex(
                name: "UX_workflow_task_bindings_reminder_operation",
                table: "workflow_task_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings");

            migrationBuilder.DropColumn(
                name: "reminder_operation_id",
                table: "workflow_task_bindings");

            migrationBuilder.DropColumn(
                name: "reminder_rule_revision_id",
                table: "workflow_task_bindings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_binding_values",
                table: "workflow_task_bindings",
                sql: "ordinal BETWEEN 1 AND 100000 AND generation >= 1 AND revision >= 1 AND (planned_effort_minutes IS NULL OR planned_effort_minutes BETWEEN 1 AND 1000000) AND due_date_policy IN ('MANUAL','FIXED','TEMPLATE_OFFSET') AND assignment_source IN ('MANUAL','TEMPLATE','MANUAL_REASSIGNMENT','AUTOMATION') AND (assigned_role IS NULL OR assigned_role IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND length(source_key) BETWEEN 1 AND 160 AND creation_request_digest ~ '^[0-9a-f]{64}$' AND ((template_revision_id IS NULL AND template_step_id IS NULL) OR (template_revision_id IS NOT NULL AND template_step_id IS NOT NULL)) AND (review_procedure_result_id IS NULL OR source_key LIKE 'REVIEW-REWORK:%') AND (cancellation_reason IS NULL OR length(trim(cancellation_reason)) > 0) AND (superseded_at IS NULL OR (supersession_reason IS NOT NULL AND length(trim(supersession_reason)) > 0))");
        }
    }
}
