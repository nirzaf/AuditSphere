using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTemplatePbcDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_template_step_values",
                table: "workflow_template_steps");

            migrationBuilder.AddColumn<string>(
                name: "pbc_acceptance_criteria",
                table: "workflow_template_steps",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pbc_area",
                table: "workflow_template_steps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pbc_confidentiality",
                table: "workflow_template_steps",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pbc_control_totals",
                table: "workflow_template_steps",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pbc_entity_scope",
                table: "workflow_template_steps",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pbc_objective",
                table: "workflow_template_steps",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pbc_requested_format",
                table: "workflow_template_steps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_template_step_values",
                table: "workflow_template_steps",
                sql: "stable_key ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$' AND phase IN ('CLIENT_ONBOARDING','AUDIT_PLANNING','AUDIT_FIELDWORK','MANAGER_REVIEW','PARTNER_SIGNOFF','REPORT_DELIVERY','ARCHIVE_CLOSE') AND length(trim(title)) BETWEEN 1 AND 200 AND length(instructions) <= 4000 AND (suggested_staffing_level IS NULL OR suggested_staffing_level IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND (estimated_effort_minutes IS NULL OR estimated_effort_minutes BETWEEN 1 AND 1000000) AND (due_date_offset_days IS NULL OR due_date_offset_days BETWEEN -3650 AND 3650) AND (pbc_request_reference IS NULL OR length(trim(pbc_request_reference)) BETWEEN 1 AND 120) AND ((pbc_objective IS NULL AND pbc_entity_scope IS NULL AND pbc_area IS NULL AND pbc_requested_format IS NULL AND pbc_control_totals IS NULL AND pbc_confidentiality IS NULL AND pbc_acceptance_criteria IS NULL) OR (length(trim(pbc_objective)) BETWEEN 1 AND 2000 AND length(trim(pbc_entity_scope)) BETWEEN 1 AND 500 AND length(trim(pbc_area)) BETWEEN 1 AND 200 AND length(trim(pbc_requested_format)) BETWEEN 1 AND 200 AND (pbc_control_totals IS NULL OR length(trim(pbc_control_totals)) BETWEEN 1 AND 2000) AND length(trim(pbc_confidentiality)) BETWEEN 1 AND 100 AND length(trim(pbc_acceptance_criteria)) BETWEEN 1 AND 4000)) AND sort_order BETWEEN 1 AND 100000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_template_step_values",
                table: "workflow_template_steps");

            migrationBuilder.DropColumn(
                name: "pbc_acceptance_criteria",
                table: "workflow_template_steps");

            migrationBuilder.DropColumn(
                name: "pbc_area",
                table: "workflow_template_steps");

            migrationBuilder.DropColumn(
                name: "pbc_confidentiality",
                table: "workflow_template_steps");

            migrationBuilder.DropColumn(
                name: "pbc_control_totals",
                table: "workflow_template_steps");

            migrationBuilder.DropColumn(
                name: "pbc_entity_scope",
                table: "workflow_template_steps");

            migrationBuilder.DropColumn(
                name: "pbc_objective",
                table: "workflow_template_steps");

            migrationBuilder.DropColumn(
                name: "pbc_requested_format",
                table: "workflow_template_steps");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_template_step_values",
                table: "workflow_template_steps",
                sql: "stable_key ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$' AND phase IN ('CLIENT_ONBOARDING','AUDIT_PLANNING','AUDIT_FIELDWORK','MANAGER_REVIEW','PARTNER_SIGNOFF','REPORT_DELIVERY','ARCHIVE_CLOSE') AND length(trim(title)) BETWEEN 1 AND 200 AND length(instructions) <= 4000 AND (suggested_staffing_level IS NULL OR suggested_staffing_level IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')) AND (estimated_effort_minutes IS NULL OR estimated_effort_minutes BETWEEN 1 AND 1000000) AND (due_date_offset_days IS NULL OR due_date_offset_days BETWEEN -3650 AND 3650) AND (pbc_request_reference IS NULL OR length(trim(pbc_request_reference)) BETWEEN 1 AND 120) AND sort_order BETWEEN 1 AND 100000");
        }
    }
}
