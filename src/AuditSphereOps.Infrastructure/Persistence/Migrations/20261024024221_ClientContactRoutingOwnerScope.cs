using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientContactRoutingOwnerScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_client_contact_routings_firm_id_practice_client_id_client_c~",
                table: "client_contact_routings",
                columns: new[] { "firm_id", "practice_client_id", "client_contact_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_client_contact_routing_contact_scope",
                table: "client_contact_routings",
                columns: new[] { "firm_id", "practice_client_id", "client_contact_id" },
                principalTable: "client_contacts",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_client_contact_routing_contact_scope",
                table: "client_contact_routings");

            migrationBuilder.DropIndex(
                name: "IX_client_contact_routings_firm_id_practice_client_id_client_c~",
                table: "client_contact_routings");
        }
    }
}
