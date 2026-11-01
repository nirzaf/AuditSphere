using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

public partial class ClientPortalIntentContactScope : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    // Keep the original global EF relationships and indexes. This additional PostgreSQL
    // constraint binds the stored contact and client to the intent's firm and client.
    migrationBuilder.AddForeignKey(
      name: "fk_client_portal_intents_contact_scope",
      table: "client_portal_intents",
      columns: new[] { "firm_id", "practice_client_id", "client_contact_id" },
      principalTable: "client_contacts",
      principalColumns: new[] { "firm_id", "practice_client_id", "id" },
      onDelete: ReferentialAction.Restrict);
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.DropForeignKey(
      name: "fk_client_portal_intents_contact_scope",
      table: "client_portal_intents");
  }
}
