using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmJournalSupportingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "supporting_evidence_content",
                table: "firm_journals",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supporting_evidence_content_type",
                table: "firm_journals",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supporting_evidence_file_name",
                table: "firm_journals",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supporting_evidence_sha256",
                table: "firm_journals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "supporting_evidence_uploaded_at",
                table: "firm_journals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supporting_evidence_uploaded_by_user_id",
                table: "firm_journals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_firm_journals_firm_id_supporting_evidence_uploaded_by_user_~",
                table: "firm_journals",
                columns: new[] { "firm_id", "supporting_evidence_uploaded_by_user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_journal_supporting_evidence",
                table: "firm_journals",
                sql: "(supporting_evidence_content IS NULL AND supporting_evidence_file_name IS NULL AND supporting_evidence_content_type IS NULL AND supporting_evidence_sha256 IS NULL AND supporting_evidence_uploaded_by_user_id IS NULL AND supporting_evidence_uploaded_at IS NULL) OR (supporting_evidence_content IS NOT NULL AND octet_length(supporting_evidence_content) BETWEEN 1 AND 5242880 AND length(supporting_evidence_file_name) BETWEEN 1 AND 255 AND length(supporting_evidence_content_type) BETWEEN 1 AND 255 AND supporting_evidence_sha256 ~ '^[0-9a-f]{64}$' AND supporting_evidence_uploaded_by_user_id IS NOT NULL AND supporting_evidence_uploaded_at IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_firm_journals_users_firm_id_supporting_evidence_uploaded_by~",
                table: "firm_journals",
                columns: new[] { "firm_id", "supporting_evidence_uploaded_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_firm_journal_evidence_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    IF OLD.supporting_evidence_content IS NOT NULL THEN
                      RAISE EXCEPTION 'Firm journal supporting evidence is immutable.' USING ERRCODE = '55000';
                    END IF;
                    RETURN OLD;
                  END IF;
                  IF ROW(OLD.supporting_evidence_file_name, OLD.supporting_evidence_content_type,
                         OLD.supporting_evidence_content, OLD.supporting_evidence_sha256,
                         OLD.supporting_evidence_uploaded_by_user_id, OLD.supporting_evidence_uploaded_at)
                     IS DISTINCT FROM
                     ROW(NEW.supporting_evidence_file_name, NEW.supporting_evidence_content_type,
                         NEW.supporting_evidence_content, NEW.supporting_evidence_sha256,
                         NEW.supporting_evidence_uploaded_by_user_id, NEW.supporting_evidence_uploaded_at) THEN
                    RAISE EXCEPTION 'Firm journal supporting evidence is immutable.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_firm_journal_evidence_immutable
                  BEFORE UPDATE OF supporting_evidence_file_name, supporting_evidence_content_type,
                    supporting_evidence_content, supporting_evidence_sha256,
                    supporting_evidence_uploaded_by_user_id, supporting_evidence_uploaded_at
                    OR DELETE ON firm_journals
                  FOR EACH ROW EXECUTE FUNCTION prevent_firm_journal_evidence_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_firm_journal_evidence_immutable ON firm_journals;
                DROP FUNCTION IF EXISTS prevent_firm_journal_evidence_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_firm_journals_users_firm_id_supporting_evidence_uploaded_by~",
                table: "firm_journals");

            migrationBuilder.DropIndex(
                name: "IX_firm_journals_firm_id_supporting_evidence_uploaded_by_user_~",
                table: "firm_journals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_journal_supporting_evidence",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "supporting_evidence_content",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "supporting_evidence_content_type",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "supporting_evidence_file_name",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "supporting_evidence_sha256",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "supporting_evidence_uploaded_at",
                table: "firm_journals");

            migrationBuilder.DropColumn(
                name: "supporting_evidence_uploaded_by_user_id",
                table: "firm_journals");
        }
    }
}
