using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdHocProcedureHistoryScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ad_hoc_procedure_insertions_audit_procedures_procedure_id",
                table: "ad_hoc_procedure_insertions");

            migrationBuilder.DropForeignKey(
                name: "FK_ad_hoc_procedure_revisions_audit_procedures_procedure_id",
                table: "ad_hoc_procedure_revisions");

            migrationBuilder.CreateIndex(
                name: "IX_ad_hoc_procedure_insertions_firm_id_client_id_engagement_id~",
                table: "ad_hoc_procedure_insertions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_ad_hoc_insertion_procedure_scope",
                table: "ad_hoc_procedure_insertions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" },
                principalTable: "audit_procedures",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ad_hoc_revision_procedure_scope",
                table: "ad_hoc_procedure_revisions",
                columns: new[] { "firm_id", "procedure_id" },
                principalTable: "audit_procedures",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ad_hoc_insertion_procedure_scope",
                table: "ad_hoc_procedure_insertions");

            migrationBuilder.DropForeignKey(
                name: "fk_ad_hoc_revision_procedure_scope",
                table: "ad_hoc_procedure_revisions");

            migrationBuilder.DropIndex(
                name: "IX_ad_hoc_procedure_insertions_firm_id_client_id_engagement_id~",
                table: "ad_hoc_procedure_insertions");

            migrationBuilder.AddForeignKey(
                name: "FK_ad_hoc_procedure_insertions_audit_procedures_procedure_id",
                table: "ad_hoc_procedure_insertions",
                column: "procedure_id",
                principalTable: "audit_procedures",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ad_hoc_procedure_revisions_audit_procedures_procedure_id",
                table: "ad_hoc_procedure_revisions",
                column: "procedure_id",
                principalTable: "audit_procedures",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
