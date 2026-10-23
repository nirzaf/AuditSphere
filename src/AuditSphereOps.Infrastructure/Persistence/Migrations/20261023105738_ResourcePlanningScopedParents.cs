using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResourcePlanningScopedParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_staff_allocations_engagements_engagement_id",
                table: "staff_allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_staff_allocations_users_user_id",
                table: "staff_allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_staff_availabilities_users_user_id",
                table: "staff_availabilities");

            migrationBuilder.DropForeignKey(
                name: "FK_staff_certifications_users_user_id",
                table: "staff_certifications");

            migrationBuilder.DropForeignKey(
                name: "FK_staff_profiles_users_user_id",
                table: "staff_profiles");

            migrationBuilder.CreateIndex(
                name: "IX_staff_allocations_firm_id_client_id_engagement_id",
                table: "staff_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_allocations_firm_id_user_id",
                table: "staff_allocations",
                columns: new[] { "firm_id", "user_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_staff_allocation_engagement_scope",
                table: "staff_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staff_allocation_user_scope",
                table: "staff_allocations",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staff_availability_user_scope",
                table: "staff_availabilities",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staff_certification_user_scope",
                table: "staff_certifications",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_staff_profile_user_scope",
                table: "staff_profiles",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_staff_allocation_engagement_scope",
                table: "staff_allocations");

            migrationBuilder.DropForeignKey(
                name: "fk_staff_allocation_user_scope",
                table: "staff_allocations");

            migrationBuilder.DropForeignKey(
                name: "fk_staff_availability_user_scope",
                table: "staff_availabilities");

            migrationBuilder.DropForeignKey(
                name: "fk_staff_certification_user_scope",
                table: "staff_certifications");

            migrationBuilder.DropForeignKey(
                name: "fk_staff_profile_user_scope",
                table: "staff_profiles");

            migrationBuilder.DropIndex(
                name: "IX_staff_allocations_firm_id_client_id_engagement_id",
                table: "staff_allocations");

            migrationBuilder.DropIndex(
                name: "IX_staff_allocations_firm_id_user_id",
                table: "staff_allocations");

            migrationBuilder.AddForeignKey(
                name: "FK_staff_allocations_engagements_engagement_id",
                table: "staff_allocations",
                column: "engagement_id",
                principalTable: "engagements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_staff_allocations_users_user_id",
                table: "staff_allocations",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_staff_availabilities_users_user_id",
                table: "staff_availabilities",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_staff_certifications_users_user_id",
                table: "staff_certifications",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_staff_profiles_users_user_id",
                table: "staff_profiles",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
