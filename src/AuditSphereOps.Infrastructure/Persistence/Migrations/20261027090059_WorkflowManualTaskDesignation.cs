using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowManualTaskDesignation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "required_task",
                table: "workflow_task_bindings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_task_binding_manual_requirement",
                table: "workflow_task_bindings",
                sql: "required_task IS NULL OR template_step_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE workflow_task_bindings IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM workflow_task_bindings WHERE required_task IS NOT NULL) THEN
                    RAISE EXCEPTION 'Manual task designations prevent rollback without changing engagement progress.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_task_binding_manual_requirement",
                table: "workflow_task_bindings");

            migrationBuilder.DropColumn(
                name: "required_task",
                table: "workflow_task_bindings");
        }
    }
}
