using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workflow_task_bindings_firm_id_client_id_engagement_id",
                table: "workflow_task_bindings");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_workflow_template_dependencies_revision_id",
                table: "workflow_template_dependencies",
                columns: new[] { "firm_id", "template_revision_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_workflow_task_bindings_scope_id",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "workflow_task_dependencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_dependency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predecessor_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    successor_binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dependency_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_task_dependencies", x => x.id);
                    table.UniqueConstraint("AK_workflow_task_dependencies_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_task_dependency_values", "predecessor_binding_id <> successor_binding_id AND dependency_type = 'TASK_COMPLETED'");
                    table.ForeignKey(
                        name: "FK_workflow_task_dependencies_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_dependencies_workflow_task_bindings_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.predecessor_binding_id },
                        principalTable: "workflow_task_bindings",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_dependencies_workflow_task_bindings_firm_id_~1",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.successor_binding_id },
                        principalTable: "workflow_task_bindings",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_task_dependencies_workflow_template_dependencies_f~",
                        columns: x => new { x.firm_id, x.template_revision_id, x.template_dependency_id },
                        principalTable: "workflow_template_dependencies",
                        principalColumns: new[] { "firm_id", "template_revision_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependencies_firm_id_client_id_engagement_id_~",
                table: "workflow_task_dependencies",
                columns: new[] { "firm_id", "client_id", "engagement_id", "predecessor_binding_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependencies_firm_id_client_id_engagement_id~1",
                table: "workflow_task_dependencies",
                columns: new[] { "firm_id", "client_id", "engagement_id", "successor_binding_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependencies_firm_id_created_by_user_id",
                table: "workflow_task_dependencies",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependencies_firm_id_engagement_id_successor_~",
                table: "workflow_task_dependencies",
                columns: new[] { "firm_id", "engagement_id", "successor_binding_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_dependencies_firm_id_template_revision_id_tem~",
                table: "workflow_task_dependencies",
                columns: new[] { "firm_id", "template_revision_id", "template_dependency_id" });

            migrationBuilder.CreateIndex(
                name: "UX_workflow_task_dependencies_edge",
                table: "workflow_task_dependencies",
                columns: new[] { "firm_id", "engagement_id", "predecessor_binding_id", "successor_binding_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_workflow_task_dependency_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP <> 'INSERT' THEN
                    RAISE EXCEPTION 'workflow task dependencies are append-only';
                  END IF;
                  IF NOT EXISTS (
                    SELECT 1
                    FROM workflow_task_bindings predecessor
                    JOIN workflow_task_bindings successor
                      ON successor.firm_id = NEW.firm_id
                     AND successor.client_id = NEW.client_id
                     AND successor.engagement_id = NEW.engagement_id
                     AND successor.id = NEW.successor_binding_id
                    JOIN workflow_template_dependencies source
                      ON source.firm_id = NEW.firm_id
                     AND source.template_revision_id = NEW.template_revision_id
                     AND source.id = NEW.template_dependency_id
                    WHERE predecessor.firm_id = NEW.firm_id
                      AND predecessor.client_id = NEW.client_id
                      AND predecessor.engagement_id = NEW.engagement_id
                      AND predecessor.id = NEW.predecessor_binding_id
                      AND predecessor.template_revision_id = NEW.template_revision_id
                      AND successor.template_revision_id = NEW.template_revision_id
                      AND predecessor.template_step_id = source.predecessor_step_id
                      AND successor.template_step_id = source.successor_step_id
                      AND NEW.dependency_type = source.dependency_type
                  ) THEN
                    RAISE EXCEPTION 'workflow task dependency does not match its pinned template steps';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_workflow_task_dependency_append_only
                  BEFORE INSERT OR UPDATE OR DELETE ON workflow_task_dependencies
                  FOR EACH ROW EXECUTE FUNCTION prevent_workflow_task_dependency_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_workflow_task_dependency_append_only ON workflow_task_dependencies; DROP FUNCTION IF EXISTS prevent_workflow_task_dependency_mutation();");
            migrationBuilder.DropTable(
                name: "workflow_task_dependencies");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_workflow_template_dependencies_revision_id",
                table: "workflow_template_dependencies");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_workflow_task_bindings_scope_id",
                table: "workflow_task_bindings");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_bindings_firm_id_client_id_engagement_id",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "client_id", "engagement_id" });
        }
    }
}
