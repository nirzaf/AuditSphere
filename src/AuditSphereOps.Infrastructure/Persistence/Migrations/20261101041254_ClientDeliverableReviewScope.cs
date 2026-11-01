using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientDeliverableReviewScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_client_deliverable_review_firm_id_id",
                table: "client_deliverable_reviews",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_deliverable_reviews_firm_id_client_id_engagement_id_~",
                table: "client_deliverable_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "deliverable_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_client_deliverable_comment_review_scope",
                table: "client_deliverable_comments",
                columns: new[] { "firm_id", "review_id" },
                principalTable: "client_deliverable_reviews",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_client_deliverable_review_deliverable_scope",
                table: "client_deliverable_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "deliverable_id" },
                principalTable: "audit_deliverables",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_client_deliverable_comment_review_scope",
                table: "client_deliverable_comments");

            migrationBuilder.DropForeignKey(
                name: "fk_client_deliverable_review_deliverable_scope",
                table: "client_deliverable_reviews");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_client_deliverable_review_firm_id_id",
                table: "client_deliverable_reviews");

            migrationBuilder.DropIndex(
                name: "IX_client_deliverable_reviews_firm_id_client_id_engagement_id_~",
                table: "client_deliverable_reviews");
        }
    }
}
