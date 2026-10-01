using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialTenderChapters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_commercial_documents_firm_id_quotation_version_id_kind",
                table: "commercial_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_document_values",
                table: "commercial_documents");

            migrationBuilder.AddColumn<string>(
                name: "audit_methodology",
                table: "firm_commercial_profiles",
                type: "character varying(16000)",
                maxLength: 16000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "firm_history_and_registrations",
                table: "firm_commercial_profiles",
                type: "character varying(16000)",
                maxLength: 16000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "industry_credentials",
                table: "firm_commercial_profiles",
                type: "character varying(16000)",
                maxLength: 16000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_commercial_documents_firm_id_quotation_version_id_kind",
                table: "commercial_documents",
                columns: new[] { "firm_id", "quotation_version_id", "kind" },
                unique: true,
                filter: "kind IN ('QUOTATION','ENGAGEMENT_LETTER','COMPREHENSIVE_PROPOSAL')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_document_values",
                table: "commercial_documents",
                sql: "kind IN ('QUOTATION','ENGAGEMENT_LETTER','PAYMENT_RECEIPT','COMPREHENSIVE_PROPOSAL') AND length(template_version) > 0 AND length(sha256_hex) = 64 AND octet_length(bytes) > 0 AND length(file_name) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_commercial_documents_firm_id_quotation_version_id_kind",
                table: "commercial_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_document_values",
                table: "commercial_documents");

            migrationBuilder.DropColumn(
                name: "audit_methodology",
                table: "firm_commercial_profiles");

            migrationBuilder.DropColumn(
                name: "firm_history_and_registrations",
                table: "firm_commercial_profiles");

            migrationBuilder.DropColumn(
                name: "industry_credentials",
                table: "firm_commercial_profiles");

            migrationBuilder.CreateIndex(
                name: "IX_commercial_documents_firm_id_quotation_version_id_kind",
                table: "commercial_documents",
                columns: new[] { "firm_id", "quotation_version_id", "kind" },
                unique: true,
                filter: "kind IN ('QUOTATION','ENGAGEMENT_LETTER')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_document_values",
                table: "commercial_documents",
                sql: "kind IN ('QUOTATION','ENGAGEMENT_LETTER','PAYMENT_RECEIPT') AND length(template_version) > 0 AND length(sha256_hex) = 64 AND octet_length(bytes) > 0 AND length(file_name) > 0");
        }
    }
}
