using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TechnicalLibraryVersionFirmScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_technical_library_versions_technical_library_documents_docu~",
                table: "technical_library_versions");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_technical_library_documents_firm_id_id",
                table: "technical_library_documents",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_technical_library_versions_firm_id_document_id",
                table: "technical_library_versions",
                columns: new[] { "firm_id", "document_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_technical_library_version_document_scope",
                table: "technical_library_versions",
                columns: new[] { "firm_id", "document_id" },
                principalTable: "technical_library_documents",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_technical_library_version_document_scope",
                table: "technical_library_versions");

            migrationBuilder.DropIndex(
                name: "IX_technical_library_versions_firm_id_document_id",
                table: "technical_library_versions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_technical_library_documents_firm_id_id",
                table: "technical_library_documents");

            migrationBuilder.AddForeignKey(
                name: "FK_technical_library_versions_technical_library_documents_docu~",
                table: "technical_library_versions",
                column: "document_id",
                principalTable: "technical_library_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
