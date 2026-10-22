using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmScopedRoleGrantAndPortalSignInUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_client_portal_first_sign_ins_users_user_id",
                table: "client_portal_first_sign_ins");

            migrationBuilder.DropForeignKey(
                name: "FK_role_grants_users_user_id",
                table: "role_grants");

            migrationBuilder.AddForeignKey(
                name: "FK_client_portal_first_sign_ins_users_firm_id_user_id",
                table: "client_portal_first_sign_ins",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_role_grants_users_firm_id_user_id",
                table: "role_grants",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_client_portal_first_sign_ins_users_firm_id_user_id",
                table: "client_portal_first_sign_ins");

            migrationBuilder.DropForeignKey(
                name: "FK_role_grants_users_firm_id_user_id",
                table: "role_grants");

            migrationBuilder.AddForeignKey(
                name: "FK_client_portal_first_sign_ins_users_user_id",
                table: "client_portal_first_sign_ins",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_role_grants_users_user_id",
                table: "role_grants",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
