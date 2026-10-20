using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTemplateEvidenceRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "workflow_template_evidence_requirement_id",
                table: "workflow_task_evidence_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workflow_template_evidence_requirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_template_evidence_requirements", x => x.id);
                    table.UniqueConstraint("AK_workflow_template_evidence_requirements_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workflow_template_evidence_requirement_values", "requirement_key ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$' AND kind IN ('CURRENT_MATERIALITY_APPROVED','PBC_ACCEPTED_OR_CLOSED','CURRENT_PROCEDURE_REVIEWED')");
                    table.ForeignKey(
                        name: "FK_workflow_template_evidence_requirements_workflow_template_r~",
                        columns: x => new { x.firm_id, x.template_revision_id },
                        principalTable: "workflow_template_revisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_template_evidence_requirements_workflow_template_s~",
                        columns: x => new { x.firm_id, x.template_revision_id, x.template_step_id },
                        principalTable: "workflow_template_steps",
                        principalColumns: new[] { "firm_id", "template_revision_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_evidence_rules_firm_id_workflow_template_evid~",
                table: "workflow_task_evidence_rules",
                columns: new[] { "firm_id", "workflow_template_evidence_requirement_id" });

            migrationBuilder.CreateIndex(
                name: "UX_workflow_template_evidence_requirements_step_key",
                table: "workflow_template_evidence_requirements",
                columns: new[] { "firm_id", "template_revision_id", "template_step_id", "requirement_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_task_evidence_rules_workflow_template_evidence_req~",
                table: "workflow_task_evidence_rules",
                columns: new[] { "firm_id", "workflow_template_evidence_requirement_id" },
                principalTable: "workflow_template_evidence_requirements",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(WorkflowTemplateEvidenceGuardSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM workflow_template_evidence_requirements)
                     OR EXISTS (SELECT 1 FROM workflow_task_evidence_rules
                                WHERE workflow_template_evidence_requirement_id IS NOT NULL) THEN
                    RAISE EXCEPTION 'Template evidence declarations and lineage must be retained';
                  END IF;
                END; $$;
                """);
            migrationBuilder.Sql(WorkflowTemplateEvidenceGuardSql.Down);
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_task_evidence_rules_workflow_template_evidence_req~",
                table: "workflow_task_evidence_rules");

            migrationBuilder.DropTable(
                name: "workflow_template_evidence_requirements");

            migrationBuilder.DropIndex(
                name: "IX_workflow_task_evidence_rules_firm_id_workflow_template_evid~",
                table: "workflow_task_evidence_rules");

            migrationBuilder.DropColumn(
                name: "workflow_template_evidence_requirement_id",
                table: "workflow_task_evidence_rules");
        }
    }
}
