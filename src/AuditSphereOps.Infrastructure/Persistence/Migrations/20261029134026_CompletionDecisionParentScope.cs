using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletionDecisionParentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_partner_clearance_scope_id",
                table: "partner_completion_clearances",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_audit_opinion_clearance_scope",
                table: "audit_opinion_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "partner_clearance_id" },
                principalTable: "partner_completion_clearances",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_partner_clearance_srm_scope",
                table: "partner_completion_clearances",
                columns: new[] { "firm_id", "client_id", "engagement_id", "summary_review_memorandum_id" },
                principalTable: "audit_deliverables",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_audit_opinion_clearance_scope",
                table: "audit_opinion_decisions");

            migrationBuilder.DropForeignKey(
                name: "fk_partner_clearance_srm_scope",
                table: "partner_completion_clearances");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_partner_clearance_scope_id",
                table: "partner_completion_clearances");

        }
    }
}
