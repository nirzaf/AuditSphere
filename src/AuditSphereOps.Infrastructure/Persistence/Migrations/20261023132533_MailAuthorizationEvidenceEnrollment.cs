using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MailAuthorizationEvidenceEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mail_attestation_sha256",
                table: "m365_tenant_capability_verifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mail_authorization_model",
                table: "m365_tenant_capability_verifications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mail_binding_sha256",
                table: "m365_tenant_capability_verifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "mail_canonical_payload",
                table: "m365_tenant_capability_verifications",
                type: "bytea",
                maxLength: 1048576,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "mail_detached_cms",
                table: "m365_tenant_capability_verifications",
                type: "bytea",
                maxLength: 65536,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mail_evidence_expires_at_utc",
                table: "m365_tenant_capability_verifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mail_provider_snapshot_sha256",
                table: "m365_tenant_capability_verifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_m365_mail_attestation_binding",
                table: "m365_tenant_capability_verifications",
                columns: new[] { "firm_id", "mail_attestation_sha256", "mail_binding_sha256" },
                unique: true,
                filter: "mail_attestation_sha256 IS NOT NULL AND mail_binding_sha256 IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_m365_mail_authorization_evidence",
                table: "m365_tenant_capability_verifications",
                sql: "(mail_authorization_model IS NULL AND mail_attestation_sha256 IS NULL AND mail_binding_sha256 IS NULL AND mail_provider_snapshot_sha256 IS NULL AND mail_evidence_expires_at_utc IS NULL AND mail_canonical_payload IS NULL AND mail_detached_cms IS NULL) OR (mail_authorization_model IS NOT NULL AND mail_authorization_model = 'LEGACY_APPLICATION_ACCESS_POLICY' AND capability = 'OUTBOUND_MAIL' AND permission = 'Mail.Send' AND state = 'VERIFIED' AND connection_revision_id IS NOT NULL AND consent_attempt_id IS NOT NULL AND mail_attestation_sha256 IS NOT NULL AND mail_attestation_sha256 ~ '^[0-9a-f]{64}$' AND mail_binding_sha256 IS NOT NULL AND mail_binding_sha256 ~ '^[0-9a-f]{64}$' AND mail_provider_snapshot_sha256 IS NOT NULL AND mail_provider_snapshot_sha256 ~ '^[0-9a-f]{64}$' AND mail_evidence_expires_at_utc IS NOT NULL AND mail_evidence_expires_at_utc > observed_at AND mail_canonical_payload IS NOT NULL AND octet_length(mail_canonical_payload) BETWEEN 2 AND 1048576 AND mail_detached_cms IS NOT NULL AND octet_length(mail_detached_cms) BETWEEN 1 AND 65536)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE m365_tenant_capability_verifications IN ACCESS EXCLUSIVE MODE;
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM m365_tenant_capability_verifications
                    WHERE mail_authorization_model IS NOT NULL OR mail_attestation_sha256 IS NOT NULL
                      OR mail_binding_sha256 IS NOT NULL OR mail_provider_snapshot_sha256 IS NOT NULL
                      OR mail_evidence_expires_at_utc IS NOT NULL OR mail_canonical_payload IS NOT NULL
                      OR mail_detached_cms IS NOT NULL) THEN
                    RAISE EXCEPTION 'Retained signed mail authorization evidence prevents downgrade.' USING ERRCODE = '55000';
                  END IF;
                END $guard$;
                """);

            migrationBuilder.DropIndex(
                name: "ux_m365_mail_attestation_binding",
                table: "m365_tenant_capability_verifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_m365_mail_authorization_evidence",
                table: "m365_tenant_capability_verifications");

            migrationBuilder.DropColumn(
                name: "mail_attestation_sha256",
                table: "m365_tenant_capability_verifications");

            migrationBuilder.DropColumn(
                name: "mail_authorization_model",
                table: "m365_tenant_capability_verifications");

            migrationBuilder.DropColumn(
                name: "mail_binding_sha256",
                table: "m365_tenant_capability_verifications");

            migrationBuilder.DropColumn(
                name: "mail_canonical_payload",
                table: "m365_tenant_capability_verifications");

            migrationBuilder.DropColumn(
                name: "mail_detached_cms",
                table: "m365_tenant_capability_verifications");

            migrationBuilder.DropColumn(
                name: "mail_evidence_expires_at_utc",
                table: "m365_tenant_capability_verifications");

            migrationBuilder.DropColumn(
                name: "mail_provider_snapshot_sha256",
                table: "m365_tenant_capability_verifications");
        }
    }
}
