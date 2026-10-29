using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcedurePhysicalLinkScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_procedure_physical_links_audit_procedures_procedure_id",
                table: "procedure_physical_links");

            migrationBuilder.DropForeignKey(
                name: "FK_procedure_physical_links_physical_evidence_items_physical_e~",
                table: "procedure_physical_links");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_physical_evidence_items_firm_id_client_id_engagement_id_id",
                table: "physical_evidence_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_procedure_physical_links_firm_id_client_id_engagement_id_ph~",
                table: "procedure_physical_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "physical_evidence_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_procedure_physical_links_firm_id_client_id_engagement_id_pr~",
                table: "procedure_physical_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_procedure_physical_link_item_scope",
                table: "procedure_physical_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "physical_evidence_item_id" },
                principalTable: "physical_evidence_items",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_procedure_physical_link_procedure_scope",
                table: "procedure_physical_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" },
                principalTable: "audit_procedures",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_procedure_physical_link_item_scope",
                table: "procedure_physical_links");

            migrationBuilder.DropForeignKey(
                name: "fk_procedure_physical_link_procedure_scope",
                table: "procedure_physical_links");

            migrationBuilder.DropIndex(
                name: "IX_procedure_physical_links_firm_id_client_id_engagement_id_ph~",
                table: "procedure_physical_links");

            migrationBuilder.DropIndex(
                name: "IX_procedure_physical_links_firm_id_client_id_engagement_id_pr~",
                table: "procedure_physical_links");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_physical_evidence_items_firm_id_client_id_engagement_id_id",
                table: "physical_evidence_items");

            migrationBuilder.AddForeignKey(
                name: "FK_procedure_physical_links_audit_procedures_procedure_id",
                table: "procedure_physical_links",
                column: "procedure_id",
                principalTable: "audit_procedures",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_procedure_physical_links_physical_evidence_items_physical_e~",
                table: "procedure_physical_links",
                column: "physical_evidence_item_id",
                principalTable: "physical_evidence_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
