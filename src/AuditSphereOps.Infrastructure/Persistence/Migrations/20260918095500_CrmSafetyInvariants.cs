using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmSafetyInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proposal_content",
                table: "proposals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_client_contact",
                table: "client_contacts");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_users_firm_id_id",
                table: "users",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_proposals_firm_id_approved_by_user_id",
                table: "proposals",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_proposal_content",
                table: "proposals",
                sql: "length(service_profile_id) > 0 AND length(scope) > 0 AND length(deliverables) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND fee >= 0 AND currency ~ '^[A-Z]{3}$'");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_firm_id_owner_user_id",
                table: "opportunities",
                columns: new[] { "firm_id", "owner_user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities",
                sql: "stage IN ('DISCOVERY','PROPOSAL','NEGOTIATION','WON','LOST') AND length(service_route) > 0 AND length(entity_scope) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND expected_fee >= 0 AND (probability IS NULL OR probability BETWEEN 0 AND 100) AND currency ~ '^[A-Z]{3}$'");

            migrationBuilder.CreateIndex(
                name: "IX_leads_firm_id_owner_user_id",
                table: "leads",
                columns: new[] { "firm_id", "owner_user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_contact",
                table: "client_contacts",
                sql: "length(full_name) > 0 AND length(email) > 0 AND length(role) > 0 AND (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from)");

            migrationBuilder.AddForeignKey(
                name: "FK_leads_users_firm_id_owner_user_id",
                table: "leads",
                columns: new[] { "firm_id", "owner_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_users_firm_id_owner_user_id",
                table: "opportunities",
                columns: new[] { "firm_id", "owner_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_proposals_users_firm_id_approved_by_user_id",
                table: "proposals",
                columns: new[] { "firm_id", "approved_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_leads_users_firm_id_owner_user_id",
                table: "leads");

            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_users_firm_id_owner_user_id",
                table: "opportunities");

            migrationBuilder.DropForeignKey(
                name: "FK_proposals_users_firm_id_approved_by_user_id",
                table: "proposals");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_users_firm_id_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_proposals_firm_id_approved_by_user_id",
                table: "proposals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_proposal_content",
                table: "proposals");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_firm_id_owner_user_id",
                table: "opportunities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_leads_firm_id_owner_user_id",
                table: "leads");

            migrationBuilder.DropCheckConstraint(
                name: "ck_client_contact",
                table: "client_contacts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proposal_content",
                table: "proposals",
                sql: "length(service_profile_id) > 0 AND length(scope) > 0 AND length(deliverables) > 0 AND length(currency) = 3");

            migrationBuilder.AddCheckConstraint(
                name: "ck_opportunity_state",
                table: "opportunities",
                sql: "stage IN ('DISCOVERY','PROPOSAL','NEGOTIATION','WON','LOST') AND length(service_route) > 0 AND length(entity_scope) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_contact",
                table: "client_contacts",
                sql: "length(full_name) > 0 AND length(email) > 0 AND length(role) > 0");
        }
    }
}
