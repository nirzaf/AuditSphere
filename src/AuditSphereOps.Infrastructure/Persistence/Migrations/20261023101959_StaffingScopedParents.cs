using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffingScopedParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_engagement_staff_assignments_engagements_engagement_id",
                table: "engagement_staff_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_engagement_staff_assignments_role_grants_role_grant_id",
                table: "engagement_staff_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_engagement_staff_assignments_users_user_id",
                table: "engagement_staff_assignments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_role_grants_firm_id_user_id_id",
                table: "role_grants",
                columns: new[] { "firm_id", "user_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_staff_assignments_firm_id_client_id_engagement_id",
                table: "engagement_staff_assignments",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_staff_assignments_firm_id_user_id_role_grant_id",
                table: "engagement_staff_assignments",
                columns: new[] { "firm_id", "user_id", "role_grant_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_staff_assignment_engagement_scope",
                table: "engagement_staff_assignments",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staff_assignment_grant_scope",
                table: "engagement_staff_assignments",
                columns: new[] { "firm_id", "user_id", "role_grant_id" },
                principalTable: "role_grants",
                principalColumns: new[] { "firm_id", "user_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staff_assignment_user_scope",
                table: "engagement_staff_assignments",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_staff_assignment_engagement_scope",
                table: "engagement_staff_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_staff_assignment_grant_scope",
                table: "engagement_staff_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_staff_assignment_user_scope",
                table: "engagement_staff_assignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_role_grants_firm_id_user_id_id",
                table: "role_grants");

            migrationBuilder.DropIndex(
                name: "IX_engagement_staff_assignments_firm_id_client_id_engagement_id",
                table: "engagement_staff_assignments");

            migrationBuilder.DropIndex(
                name: "IX_engagement_staff_assignments_firm_id_user_id_role_grant_id",
                table: "engagement_staff_assignments");

            migrationBuilder.AddForeignKey(
                name: "FK_engagement_staff_assignments_engagements_engagement_id",
                table: "engagement_staff_assignments",
                column: "engagement_id",
                principalTable: "engagements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_engagement_staff_assignments_role_grants_role_grant_id",
                table: "engagement_staff_assignments",
                column: "role_grant_id",
                principalTable: "role_grants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_engagement_staff_assignments_users_user_id",
                table: "engagement_staff_assignments",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
