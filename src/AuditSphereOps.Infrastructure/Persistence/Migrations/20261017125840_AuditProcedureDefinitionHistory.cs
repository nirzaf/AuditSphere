using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditProcedureDefinitionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures");

            migrationBuilder.DropIndex(
                name: "IX_audit_procedure_results_firm_id_client_id_engagement_id_aud~",
                table: "audit_procedure_results");

            migrationBuilder.AddColumn<int>(
                name: "current_definition_revision",
                table: "audit_procedures",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "definition_revision_id",
                table: "audit_procedure_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_procedure_definition_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    program_procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_title = table.Column<string>(type: "text", nullable: false),
                    effective_wording = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    previous_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_legacy_snapshot = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_procedure_definition_revisions", x => x.id);
                    table.UniqueConstraint("AK_audit_procedure_definition_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id, x.id });
                    table.CheckConstraint("ck_audit_procedure_definition_values", "revision > 0 AND length(trim(effective_title)) > 0 AND length(trim(effective_wording)) BETWEEN 1 AND 2000 AND length(request_digest) = 64 AND ((is_legacy_snapshot AND actor_user_id IS NULL AND authored_at IS NULL) OR (NOT is_legacy_snapshot AND actor_user_id IS NOT NULL AND authored_at IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_audit_procedure_definition_revisions_audit_procedure_defini~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id, x.previous_revision_id },
                        principalTable: "audit_procedure_definition_revisions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_definition_revisions_audit_procedures_firm_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_definition_revisions_audit_program_procedur~",
                        columns: x => new { x.firm_id, x.program_procedure_id },
                        principalTable: "audit_program_procedures",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_definition_revisions_engagements_firm_id_cl~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_definition_revisions_users_firm_id_actor_us~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Preserve any prior tailoring as an unattributed effective snapshot. The immutable
            // ProgramProcedureId continues to identify the original Word-derived source wording.
            migrationBuilder.Sql("""
                INSERT INTO audit_procedure_definition_revisions
                  (id, firm_id, client_id, engagement_id, audit_procedure_id, revision, program_procedure_id,
                   effective_title, effective_wording, rationale, command_id, request_digest, previous_revision_id,
                   actor_user_id, authored_at, is_legacy_snapshot, recorded_at)
                SELECT gen_random_uuid(), firm_id, client_id, engagement_id, id, 1, program_procedure_id,
                       title, coalesce(source_wording, title), applicability_rationale, gen_random_uuid(), repeat('0', 64),
                       NULL, NULL, NULL, TRUE, statement_timestamp()
                FROM audit_procedures
                WHERE program_procedure_id IS NOT NULL;

                UPDATE audit_procedures SET current_definition_revision = 1
                WHERE program_procedure_id IS NOT NULL;

                CREATE OR REPLACE FUNCTION prevent_audit_procedure_definition_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Audit procedure definition history is append-only.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_audit_procedure_definition_append_only
                  BEFORE UPDATE OR DELETE ON audit_procedure_definition_revisions
                  FOR EACH ROW EXECUTE FUNCTION prevent_audit_procedure_definition_mutation();

                CREATE OR REPLACE FUNCTION prevent_controlled_procedure_source_rewrite() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.program_procedure_id IS NOT NULL AND
                     NEW.source_wording IS DISTINCT FROM OLD.source_wording THEN
                    RAISE EXCEPTION 'Controlled source wording is immutable; append a definition revision.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_controlled_procedure_source_immutable
                  BEFORE UPDATE OF source_wording ON audit_procedures
                  FOR EACH ROW EXECUTE FUNCTION prevent_controlled_procedure_source_rewrite();
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures",
                sql: "length(trim(title)) > 0 AND status IN ('PLANNED','IN_PROGRESS','SUBMITTED','IN_REVIEW','CHANGES_REQUIRED','REVIEWED') AND applicability_status IN ('PENDING','APPLICABLE','NA_PENDING_REVIEW','NA_APPROVED') AND current_definition_revision >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_audit_procedure_result_definition",
                table: "audit_procedure_results",
                columns: new[] { "firm_id", "audit_procedure_id", "definition_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_results_firm_id_client_id_engagement_id_aud~",
                table: "audit_procedure_results",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "definition_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_definition_revisions_firm_id_actor_user_id",
                table: "audit_procedure_definition_revisions",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_definition_revisions_firm_id_client_id_enga~",
                table: "audit_procedure_definition_revisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "previous_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_definition_revisions_firm_id_program_proced~",
                table: "audit_procedure_definition_revisions",
                columns: new[] { "firm_id", "program_procedure_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_procedure_definition_command",
                table: "audit_procedure_definition_revisions",
                columns: new[] { "firm_id", "audit_procedure_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_audit_procedure_definition_revision",
                table: "audit_procedure_definition_revisions",
                columns: new[] { "firm_id", "audit_procedure_id", "revision" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_procedure_results_audit_procedure_definition_revision~",
                table: "audit_procedure_results",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "definition_revision_id" },
                principalTable: "audit_procedure_definition_revisions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM audit_procedure_definition_revisions)
                     OR EXISTS (SELECT 1 FROM audit_procedure_results WHERE definition_revision_id IS NOT NULL) THEN
                    RAISE EXCEPTION 'Refusing to remove retained audit procedure definition history or result bindings.';
                  END IF;
                END;
                $$;
                DROP TRIGGER IF EXISTS trg_audit_procedure_definition_append_only ON audit_procedure_definition_revisions;
                DROP FUNCTION IF EXISTS prevent_audit_procedure_definition_mutation();
                DROP TRIGGER IF EXISTS trg_controlled_procedure_source_immutable ON audit_procedures;
                DROP FUNCTION IF EXISTS prevent_controlled_procedure_source_rewrite();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_audit_procedure_results_audit_procedure_definition_revision~",
                table: "audit_procedure_results");

            migrationBuilder.DropTable(
                name: "audit_procedure_definition_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures");

            migrationBuilder.DropIndex(
                name: "ix_audit_procedure_result_definition",
                table: "audit_procedure_results");

            migrationBuilder.DropIndex(
                name: "IX_audit_procedure_results_firm_id_client_id_engagement_id_aud~",
                table: "audit_procedure_results");

            migrationBuilder.DropColumn(
                name: "current_definition_revision",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "definition_revision_id",
                table: "audit_procedure_results");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures",
                sql: "length(trim(title)) > 0 AND status IN ('PLANNED','IN_PROGRESS','SUBMITTED','IN_REVIEW','CHANGES_REQUIRED','REVIEWED') AND applicability_status IN ('PENDING','APPLICABLE','NA_PENDING_REVIEW','NA_APPROVED')");

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_results_firm_id_client_id_engagement_id_aud~",
                table: "audit_procedure_results",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id" });
        }
    }
}
