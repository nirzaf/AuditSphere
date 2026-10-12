using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTemplateCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_templates", x => x.id);
                    table.UniqueConstraint("AK_workflow_templates_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_template_archive", "(archived_at IS NULL AND archived_by_user_id IS NULL) OR (archived_at IS NOT NULL AND archived_by_user_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_workflow_templates_users_firm_id_archived_by_user_id",
                        columns: x => new { x.firm_id, x.archived_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_templates_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_template_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    service_route = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    industry_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    content_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_edited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_template_revisions", x => x.id);
                    table.UniqueConstraint("AK_workflow_template_revisions_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_workflow_template_revisions_scope_id", x => new { x.firm_id, x.template_id, x.id });
                    table.CheckConstraint("ck_workflow_template_revision_values", "version >= 1 AND revision >= 1 AND status IN ('DRAFT','REVIEWED','PUBLISHED','SUPERSEDED') AND length(trim(name)) BETWEEN 2 AND 160 AND length(trim(service_route)) BETWEEN 1 AND 50 AND content_digest ~ '^[0-9a-f]{64}$' AND ((reviewed_by_user_id IS NULL AND reviewed_at IS NULL) OR (reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL)) AND ((published_by_user_id IS NULL AND published_at IS NULL) OR (published_by_user_id IS NOT NULL AND published_at IS NOT NULL)) AND (status NOT IN ('REVIEWED','PUBLISHED','SUPERSEDED') OR reviewed_at IS NOT NULL) AND (status NOT IN ('PUBLISHED','SUPERSEDED') OR published_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_workflow_template_revisions_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_template_revisions_users_firm_id_last_edited_by_us~",
                        columns: x => new { x.firm_id, x.last_edited_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_template_revisions_users_firm_id_published_by_user~",
                        columns: x => new { x.firm_id, x.published_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_template_revisions_users_firm_id_reviewed_by_user_~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_template_revisions_workflow_templates_firm_id_temp~",
                        columns: x => new { x.firm_id, x.template_id },
                        principalTable: "workflow_templates",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_template_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    content_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_template_decisions", x => x.id);
                    table.UniqueConstraint("AK_workflow_template_decisions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_template_decision_values", "kind IN ('DRAFT_CREATED','DRAFT_EDITED','REVIEWED','PUBLISHED','SUPERSEDED','ARCHIVED') AND content_digest ~ '^[0-9a-f]{64}$' AND length(trim(rationale)) BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "FK_workflow_template_decisions_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_template_decisions_workflow_template_revisions_fir~",
                        columns: x => new { x.firm_id, x.template_id, x.template_revision_id },
                        principalTable: "workflow_template_revisions",
                        principalColumns: new[] { "firm_id", "template_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_template_decisions_workflow_templates_firm_id_temp~",
                        columns: x => new { x.firm_id, x.template_id },
                        principalTable: "workflow_templates",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_template_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stable_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    phase = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    suggested_staffing_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    estimated_effort_minutes = table.Column<int>(type: "integer", nullable: true),
                    due_date_offset_days = table.Column<int>(type: "integer", nullable: true),
                    pbc_request_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    required_task = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_template_steps", x => x.id);
                    table.UniqueConstraint("AK_workflow_template_steps_revision_id", x => new { x.firm_id, x.template_revision_id, x.id });
                    table.CheckConstraint("ck_workflow_template_step_values", "stable_key ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$' AND phase IN ('CLIENT_ONBOARDING','AUDIT_PLANNING','AUDIT_FIELDWORK','MANAGER_REVIEW','PARTNER_SIGNOFF','REPORT_DELIVERY','ARCHIVE_CLOSE') AND length(trim(title)) BETWEEN 1 AND 200 AND length(instructions) <= 4000 AND (suggested_staffing_level IS NULL OR suggested_staffing_level IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND (estimated_effort_minutes IS NULL OR estimated_effort_minutes BETWEEN 1 AND 1000000) AND (due_date_offset_days IS NULL OR due_date_offset_days BETWEEN -3650 AND 3650) AND (pbc_request_reference IS NULL OR length(trim(pbc_request_reference)) BETWEEN 1 AND 120) AND sort_order BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "FK_workflow_template_steps_workflow_template_revisions_firm_id~",
                        columns: x => new { x.firm_id, x.template_revision_id },
                        principalTable: "workflow_template_revisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_template_dependencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predecessor_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    successor_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dependency_type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_template_dependencies", x => x.id);
                    table.CheckConstraint("ck_workflow_template_dependency_values", "predecessor_step_id <> successor_step_id AND dependency_type = 'TASK_COMPLETED'");
                    table.ForeignKey(
                        name: "FK_workflow_template_dependencies_workflow_template_revisions_~",
                        columns: x => new { x.firm_id, x.template_revision_id },
                        principalTable: "workflow_template_revisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_template_dependencies_workflow_template_steps_firm~",
                        columns: x => new { x.firm_id, x.template_revision_id, x.predecessor_step_id },
                        principalTable: "workflow_template_steps",
                        principalColumns: new[] { "firm_id", "template_revision_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_template_dependencies_workflow_template_steps_fir~1",
                        columns: x => new { x.firm_id, x.template_revision_id, x.successor_step_id },
                        principalTable: "workflow_template_steps",
                        principalColumns: new[] { "firm_id", "template_revision_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_bindings_firm_id_template_revision_id_templat~",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "template_revision_id", "template_step_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_decisions_firm_id_actor_user_id",
                table: "workflow_template_decisions",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_decisions_firm_id_template_id_occurred_at",
                table: "workflow_template_decisions",
                columns: new[] { "firm_id", "template_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_decisions_firm_id_template_id_template_re~",
                table: "workflow_template_decisions",
                columns: new[] { "firm_id", "template_id", "template_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_dependencies_firm_id_template_revision_i~1",
                table: "workflow_template_dependencies",
                columns: new[] { "firm_id", "template_revision_id", "predecessor_step_id", "successor_step_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_dependencies_firm_id_template_revision_id~",
                table: "workflow_template_dependencies",
                columns: new[] { "firm_id", "template_revision_id", "successor_step_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_revisions_firm_id_created_by_user_id",
                table: "workflow_template_revisions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_revisions_firm_id_last_edited_by_user_id",
                table: "workflow_template_revisions",
                columns: new[] { "firm_id", "last_edited_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_revisions_firm_id_published_by_user_id",
                table: "workflow_template_revisions",
                columns: new[] { "firm_id", "published_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_revisions_firm_id_reviewed_by_user_id",
                table: "workflow_template_revisions",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_revisions_firm_id_template_id_status",
                table: "workflow_template_revisions",
                columns: new[] { "firm_id", "template_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_revisions_firm_id_template_id_version",
                table: "workflow_template_revisions",
                columns: new[] { "firm_id", "template_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_workflow_template_one_published_revision",
                table: "workflow_template_revisions",
                columns: new[] { "firm_id", "template_id" },
                unique: true,
                filter: "status = 'PUBLISHED'");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_steps_firm_id_template_revision_id_sort_o~",
                table: "workflow_template_steps",
                columns: new[] { "firm_id", "template_revision_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_template_steps_firm_id_template_revision_id_stable~",
                table: "workflow_template_steps",
                columns: new[] { "firm_id", "template_revision_id", "stable_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_templates_firm_id_archived_by_user_id",
                table: "workflow_templates",
                columns: new[] { "firm_id", "archived_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_templates_firm_id_created_at",
                table: "workflow_templates",
                columns: new[] { "firm_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_templates_firm_id_created_by_user_id",
                table: "workflow_templates",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_task_bindings_workflow_template_steps_firm_id_temp~",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "template_revision_id", "template_step_id" },
                principalTable: "workflow_template_steps",
                principalColumns: new[] { "firm_id", "template_revision_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION guard_workflow_template_revision() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'workflow template revisions are retained';
                  END IF;
                  IF ROW(NEW.id, NEW.firm_id, NEW.template_id, NEW.version, NEW.created_by_user_id, NEW.created_at)
                     IS DISTINCT FROM ROW(OLD.id, OLD.firm_id, OLD.template_id, OLD.version, OLD.created_by_user_id, OLD.created_at) THEN
                    RAISE EXCEPTION 'workflow template revision identity is immutable';
                  END IF;
                  IF OLD.status = 'DRAFT' AND NEW.status = 'DRAFT' THEN
                    IF NEW.revision <> OLD.revision + 1 OR NEW.reviewed_by_user_id IS DISTINCT FROM OLD.reviewed_by_user_id OR
                       NEW.reviewed_at IS DISTINCT FROM OLD.reviewed_at OR NEW.published_by_user_id IS DISTINCT FROM OLD.published_by_user_id OR
                       NEW.published_at IS DISTINCT FROM OLD.published_at THEN
                      RAISE EXCEPTION 'draft edit must advance revision without review or publication metadata';
                    END IF;
                    RETURN NEW;
                  ELSIF OLD.status = 'DRAFT' AND NEW.status = 'REVIEWED' THEN
                    IF ROW(NEW.name, NEW.service_route, NEW.industry_key, NEW.content_digest, NEW.last_edited_by_user_id, NEW.updated_at)
                       IS DISTINCT FROM ROW(OLD.name, OLD.service_route, OLD.industry_key, OLD.content_digest, OLD.last_edited_by_user_id, OLD.updated_at) OR
                       NEW.revision <> OLD.revision + 1 OR NEW.reviewed_by_user_id IS NULL OR NEW.reviewed_at IS NULL OR
                       NEW.published_by_user_id IS DISTINCT FROM OLD.published_by_user_id OR NEW.published_at IS DISTINCT FROM OLD.published_at THEN
                      RAISE EXCEPTION 'review must bind the exact unchanged draft revision';
                    END IF;
                    RETURN NEW;
                  ELSIF OLD.status = 'REVIEWED' AND NEW.status = 'PUBLISHED' THEN
                    IF ROW(NEW.name, NEW.service_route, NEW.industry_key, NEW.content_digest, NEW.created_by_user_id,
                           NEW.created_at, NEW.last_edited_by_user_id, NEW.updated_at, NEW.reviewed_by_user_id, NEW.reviewed_at)
                       IS DISTINCT FROM ROW(OLD.name, OLD.service_route, OLD.industry_key, OLD.content_digest, OLD.created_by_user_id,
                           OLD.created_at, OLD.last_edited_by_user_id, OLD.updated_at, OLD.reviewed_by_user_id, OLD.reviewed_at) OR
                       NEW.revision <> OLD.revision + 1 OR NEW.published_by_user_id IS NULL OR NEW.published_at IS NULL THEN
                      RAISE EXCEPTION 'publication must preserve the independently reviewed revision';
                    END IF;
                    RETURN NEW;
                  ELSIF OLD.status = 'PUBLISHED' AND NEW.status = 'SUPERSEDED' THEN
                    IF (to_jsonb(NEW) - 'status') IS DISTINCT FROM (to_jsonb(OLD) - 'status') THEN
                      RAISE EXCEPTION 'superseding cannot mutate published template content';
                    END IF;
                    RETURN NEW;
                  END IF;
                  RAISE EXCEPTION 'invalid workflow template revision transition: % to %', OLD.status, NEW.status;
                END;
                $$;

                CREATE TRIGGER trg_workflow_template_revision_guard
                  BEFORE UPDATE OR DELETE ON workflow_template_revisions
                  FOR EACH ROW EXECUTE FUNCTION guard_workflow_template_revision();

                CREATE OR REPLACE FUNCTION guard_workflow_template_step_write() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                  revision_status text;
                  scoped_revision uuid;
                  scoped_firm uuid;
                BEGIN
                  scoped_revision := CASE WHEN TG_OP = 'DELETE' THEN OLD.template_revision_id ELSE NEW.template_revision_id END;
                  scoped_firm := CASE WHEN TG_OP = 'DELETE' THEN OLD.firm_id ELSE NEW.firm_id END;
                  SELECT status INTO revision_status FROM workflow_template_revisions
                    WHERE firm_id = scoped_firm AND id = scoped_revision;
                  IF revision_status IS DISTINCT FROM 'DRAFT' THEN
                    RAISE EXCEPTION 'steps may only change while their template revision is a draft';
                  END IF;
                  IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
                END;
                $$;

                CREATE TRIGGER trg_workflow_template_step_guard
                  BEFORE INSERT OR UPDATE OR DELETE ON workflow_template_steps
                  FOR EACH ROW EXECUTE FUNCTION guard_workflow_template_step_write();
                CREATE TRIGGER trg_workflow_template_dependency_guard
                  BEFORE INSERT OR UPDATE OR DELETE ON workflow_template_dependencies
                  FOR EACH ROW EXECUTE FUNCTION guard_workflow_template_step_write();

                CREATE OR REPLACE FUNCTION guard_workflow_template_identity() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'workflow template identities are retained'; END IF;
                  IF ROW(NEW.id, NEW.firm_id, NEW.created_by_user_id, NEW.created_at)
                     IS DISTINCT FROM ROW(OLD.id, OLD.firm_id, OLD.created_by_user_id, OLD.created_at) OR
                     OLD.archived_at IS NOT NULL OR NEW.archived_at IS NULL OR NEW.archived_by_user_id IS NULL THEN
                    RAISE EXCEPTION 'workflow template identity only permits one-way archival';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_workflow_template_identity_guard
                  BEFORE UPDATE OR DELETE ON workflow_templates
                  FOR EACH ROW EXECUTE FUNCTION guard_workflow_template_identity();

                CREATE OR REPLACE FUNCTION prevent_workflow_template_decision_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'workflow template decisions are append-only'; END;
                $$;
                CREATE TRIGGER trg_workflow_template_decision_append_only
                  BEFORE UPDATE OR DELETE ON workflow_template_decisions
                  FOR EACH ROW EXECUTE FUNCTION prevent_workflow_template_decision_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_task_bindings_workflow_template_steps_firm_id_temp~",
                table: "workflow_task_bindings");

            migrationBuilder.DropTable(
                name: "workflow_template_decisions");

            migrationBuilder.DropTable(
                name: "workflow_template_dependencies");

            migrationBuilder.DropTable(
                name: "workflow_template_steps");

            migrationBuilder.DropTable(
                name: "workflow_template_revisions");

            migrationBuilder.DropTable(
                name: "workflow_templates");

            migrationBuilder.DropIndex(
                name: "IX_workflow_task_bindings_firm_id_template_revision_id_templat~",
                table: "workflow_task_bindings");

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS prevent_workflow_template_decision_mutation();
                DROP FUNCTION IF EXISTS guard_workflow_template_identity();
                DROP FUNCTION IF EXISTS guard_workflow_template_step_write();
                DROP FUNCTION IF EXISTS guard_workflow_template_revision();
                """);
        }
    }
}
