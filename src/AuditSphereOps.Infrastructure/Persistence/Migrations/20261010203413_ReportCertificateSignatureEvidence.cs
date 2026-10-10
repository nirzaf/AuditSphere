using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportCertificateSignatureEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "certificate_issuer",
                table: "signature_applications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "certificate_not_after",
                table: "signature_applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "certificate_serial_number",
                table: "signature_applications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "certificate_subject",
                table: "signature_applications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "certificate_thumbprint_sha256",
                table: "signature_applications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_kind",
                table: "signature_applications",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "VISUAL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_signature_application_kind",
                table: "signature_applications",
                sql: "signature_kind IN ('VISUAL','CERTIFICATE') AND ((signature_kind = 'CERTIFICATE') = (certificate_thumbprint_sha256 IS NOT NULL)) AND ((certificate_thumbprint_sha256 IS NULL AND certificate_subject IS NULL AND certificate_issuer IS NULL AND certificate_serial_number IS NULL AND certificate_not_after IS NULL) OR (certificate_thumbprint_sha256 ~ '^[0-9a-f]{64}$' AND length(certificate_subject) > 0 AND length(certificate_issuer) > 0 AND length(certificate_serial_number) > 0 AND certificate_not_after IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_signature_application_kind",
                table: "signature_applications");

            migrationBuilder.DropColumn(
                name: "certificate_issuer",
                table: "signature_applications");

            migrationBuilder.DropColumn(
                name: "certificate_not_after",
                table: "signature_applications");

            migrationBuilder.DropColumn(
                name: "certificate_serial_number",
                table: "signature_applications");

            migrationBuilder.DropColumn(
                name: "certificate_subject",
                table: "signature_applications");

            migrationBuilder.DropColumn(
                name: "certificate_thumbprint_sha256",
                table: "signature_applications");

            migrationBuilder.DropColumn(
                name: "signature_kind",
                table: "signature_applications");
        }
    }
}
