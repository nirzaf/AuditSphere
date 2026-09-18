using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthorizationIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_users_tenant_subject",
                table: "users",
                columns: new[] { "tenant_id", "subject" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_kind",
                table: "users",
                sql: "user_kind IN ('Staff','Client')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_session",
                table: "users",
                sql: "session_epoch >= 1 AND length(subject) > 0 AND length(tenant_id) > 0 AND length(email) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_role_grants_firm_id_client_id",
                table: "role_grants",
                columns: new[] { "firm_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_role_grants_firm_id_engagement_id",
                table: "role_grants",
                columns: new[] { "firm_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_role_grants_user_id",
                table: "role_grants",
                column: "user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_role_grant_scope",
                table: "role_grants",
                sql: "length(role) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_engagement_generation",
                table: "engagements",
                sql: "generation >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_holds_firm_id_engagement_id",
                table: "engagement_holds",
                columns: new[] { "firm_id", "engagement_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_hold_release",
                table: "engagement_holds",
                sql: "length(hold_kind) > 0 AND (NOT released OR released_at IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_engagement_holds_engagements_firm_id_engagement_id",
                table: "engagement_holds",
                columns: new[] { "firm_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_engagements_practice_clients_firm_id_practice_client_id",
                table: "engagements",
                columns: new[] { "firm_id", "practice_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_role_grants_engagements_firm_id_engagement_id",
                table: "role_grants",
                columns: new[] { "firm_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_role_grants_practice_clients_firm_id_client_id",
                table: "role_grants",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_role_grants_users_user_id",
                table: "role_grants",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_engagement_holds_engagements_firm_id_engagement_id",
                table: "engagement_holds");

            migrationBuilder.DropForeignKey(
                name: "FK_engagements_practice_clients_firm_id_practice_client_id",
                table: "engagements");

            migrationBuilder.DropForeignKey(
                name: "FK_role_grants_engagements_firm_id_engagement_id",
                table: "role_grants");

            migrationBuilder.DropForeignKey(
                name: "FK_role_grants_practice_clients_firm_id_client_id",
                table: "role_grants");

            migrationBuilder.DropForeignKey(
                name: "FK_role_grants_users_user_id",
                table: "role_grants");

            migrationBuilder.DropIndex(
                name: "ux_users_tenant_subject",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_kind",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_session",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_role_grants_firm_id_client_id",
                table: "role_grants");

            migrationBuilder.DropIndex(
                name: "IX_role_grants_firm_id_engagement_id",
                table: "role_grants");

            migrationBuilder.DropIndex(
                name: "IX_role_grants_user_id",
                table: "role_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_role_grant_scope",
                table: "role_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_engagement_generation",
                table: "engagements");

            migrationBuilder.DropIndex(
                name: "IX_engagement_holds_firm_id_engagement_id",
                table: "engagement_holds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hold_release",
                table: "engagement_holds");
        }
    }
}
