using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcedureEvidenceLinkScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_procedure_evidence_links_audit_procedures_procedure_id",
                table: "procedure_evidence_links");

            migrationBuilder.DropForeignKey(
                name: "FK_procedure_evidence_links_pbc_upload_intents_pbc_upload_inte~",
                table: "procedure_evidence_links");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_pbc_upload_intents_firm_id_client_id_engagement_id_pbc_requ~",
                table: "pbc_upload_intents",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_procedure_evidence_links_firm_id_client_id_engagement_id_pb~",
                table: "procedure_evidence_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id", "pbc_upload_intent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_procedure_evidence_links_firm_id_client_id_engagement_id_pr~",
                table: "procedure_evidence_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_procedure_evidence_link_procedure_scope",
                table: "procedure_evidence_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" },
                principalTable: "audit_procedures",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_procedure_evidence_link_upload_scope",
                table: "procedure_evidence_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id", "pbc_upload_intent_id" },
                principalTable: "pbc_upload_intents",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_procedure_evidence_link_procedure_scope",
                table: "procedure_evidence_links");

            migrationBuilder.DropForeignKey(
                name: "fk_procedure_evidence_link_upload_scope",
                table: "procedure_evidence_links");

            migrationBuilder.DropIndex(
                name: "IX_procedure_evidence_links_firm_id_client_id_engagement_id_pb~",
                table: "procedure_evidence_links");

            migrationBuilder.DropIndex(
                name: "IX_procedure_evidence_links_firm_id_client_id_engagement_id_pr~",
                table: "procedure_evidence_links");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_pbc_upload_intents_firm_id_client_id_engagement_id_pbc_requ~",
                table: "pbc_upload_intents");

            migrationBuilder.AddForeignKey(
                name: "FK_procedure_evidence_links_audit_procedures_procedure_id",
                table: "procedure_evidence_links",
                column: "procedure_id",
                principalTable: "audit_procedures",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_procedure_evidence_links_pbc_upload_intents_pbc_upload_inte~",
                table: "procedure_evidence_links",
                column: "pbc_upload_intent_id",
                principalTable: "pbc_upload_intents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
