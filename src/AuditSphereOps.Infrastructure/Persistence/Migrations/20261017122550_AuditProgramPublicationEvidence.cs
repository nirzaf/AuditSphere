using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditProgramPublicationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_program_version_values",
                table: "audit_program_versions");

            migrationBuilder.AddColumn<string>(
                name: "content_digest",
                table: "audit_program_versions",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "publication_review_reference",
                table: "audit_program_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_program_version_values",
                table: "audit_program_versions",
                sql: "length(trim(program_code)) > 0 AND length(trim(version)) > 0 AND length(source_hash) = 64 AND (content_digest IS NULL OR length(content_digest) = 64) AND (publication_review_reference IS NULL OR length(trim(publication_review_reference)) BETWEEN 1 AND 200) AND status IN ('DRAFT','PUBLISHED','RETIRED') AND ((status = 'PUBLISHED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL) OR status <> 'PUBLISHED') AND (status <> 'PUBLISHED' OR content_digest IS NULL OR publication_review_reference IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM audit_program_versions WHERE content_digest IS NOT NULL OR publication_review_reference IS NOT NULL) THEN RAISE EXCEPTION 'Cannot roll back audit programme publication evidence while reviewed records exist.'; END IF; END $$;");
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_program_version_values",
                table: "audit_program_versions");

            migrationBuilder.DropColumn(
                name: "content_digest",
                table: "audit_program_versions");

            migrationBuilder.DropColumn(
                name: "publication_review_reference",
                table: "audit_program_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_program_version_values",
                table: "audit_program_versions",
                sql: "length(trim(program_code)) > 0 AND length(trim(version)) > 0 AND length(source_hash) = 64 AND status IN ('DRAFT','PUBLISHED','RETIRED') AND ((status = 'PUBLISHED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL) OR status <> 'PUBLISHED')");
        }
    }
}
