using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTemplateOpenRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_template_revision_values",
                table: "workflow_template_revisions");

            migrationBuilder.CreateIndex(
                name: "UX_workflow_template_one_open_revision",
                table: "workflow_template_revisions",
                columns: new[] { "firm_id", "template_id" },
                unique: true,
                filter: "status IN ('DRAFT','REVIEWED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_template_revision_values",
                table: "workflow_template_revisions",
                sql: "version >= 1 AND revision >= 1 AND status IN ('DRAFT','REVIEWED','PUBLISHED','SUPERSEDED') AND length(trim(name)) BETWEEN 2 AND 160 AND length(trim(service_route)) BETWEEN 1 AND 50 AND content_digest ~ '^[0-9a-f]{64}$' AND ((reviewed_by_user_id IS NULL AND reviewed_at IS NULL) OR (reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL)) AND ((published_by_user_id IS NULL AND published_at IS NULL) OR (published_by_user_id IS NOT NULL AND published_at IS NOT NULL)) AND (status NOT IN ('REVIEWED','PUBLISHED','SUPERSEDED') OR (reviewed_at IS NOT NULL AND reviewed_by_user_id <> created_by_user_id AND reviewed_by_user_id <> last_edited_by_user_id)) AND (status NOT IN ('PUBLISHED','SUPERSEDED') OR published_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_workflow_template_one_open_revision",
                table: "workflow_template_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workflow_template_revision_values",
                table: "workflow_template_revisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workflow_template_revision_values",
                table: "workflow_template_revisions",
                sql: "version >= 1 AND revision >= 1 AND status IN ('DRAFT','REVIEWED','PUBLISHED','SUPERSEDED') AND length(trim(name)) BETWEEN 2 AND 160 AND length(trim(service_route)) BETWEEN 1 AND 50 AND content_digest ~ '^[0-9a-f]{64}$' AND ((reviewed_by_user_id IS NULL AND reviewed_at IS NULL) OR (reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL)) AND ((published_by_user_id IS NULL AND published_at IS NULL) OR (published_by_user_id IS NOT NULL AND published_at IS NOT NULL)) AND (status NOT IN ('REVIEWED','PUBLISHED','SUPERSEDED') OR reviewed_at IS NOT NULL) AND (status NOT IN ('PUBLISHED','SUPERSEDED') OR published_at IS NOT NULL)");
        }
    }
}
