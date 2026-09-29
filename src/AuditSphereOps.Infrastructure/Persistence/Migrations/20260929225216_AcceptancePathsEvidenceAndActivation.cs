using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcceptancePathsEvidenceAndActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_evaluation_response_values",
                table: "evaluation_responses");

            migrationBuilder.AddColumn<string>(
                name: "adverse_answer",
                table: "question_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "escalates_on_change",
                table: "question_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "evidence_reference",
                table: "evaluation_responses",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "generation",
                table: "evaluation_responses",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "path",
                table: "acceptance_decisions",
                type: "text",
                nullable: false,
                defaultValue: "NEW_CLIENT");

            migrationBuilder.AddColumn<Guid>(
                name: "prior_decision_id",
                table: "acceptance_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "engagement_activations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practice_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acceptance_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_generation = table.Column<long>(type: "bigint", nullable: false),
                    acceptance_path = table.Column<string>(type: "text", nullable: false),
                    activated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_activations", x => x.id);
                    table.CheckConstraint("ck_engagement_activation_values", "client_generation >= 1 AND acceptance_path IN ('NEW_CLIENT','CONTINUANCE')");
                    table.ForeignKey(
                        name: "FK_engagement_activations_acceptance_decisions_firm_id_accepta~",
                        columns: x => new { x.firm_id, x.acceptance_decision_id },
                        principalTable: "acceptance_decisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_activations_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_question_adverse_answer",
                table: "question_definitions",
                sql: "adverse_answer IS NULL OR adverse_answer IN ('YES','NO')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evaluation_response_values",
                table: "evaluation_responses",
                sql: "bank IN ('CE','RV') AND revision >= 1 AND generation >= 1 AND length(trim(question_id)) > 0 AND length(trim(answer)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_acceptance_decision_path",
                table: "acceptance_decisions",
                sql: "path IN ('NEW_CLIENT','CONTINUANCE') AND (path = 'CONTINUANCE' OR prior_decision_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_activations_engagement_id",
                table: "engagement_activations",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_activations_firm_id_acceptance_decision_id",
                table: "engagement_activations",
                columns: new[] { "firm_id", "acceptance_decision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_activations_firm_id_engagement_id",
                table: "engagement_activations",
                columns: new[] { "firm_id", "engagement_id" },
                unique: true);

            migrationBuilder.Sql("""
                UPDATE question_definitions SET adverse_answer = 'NO' WHERE question_code IN ('CE-001','CE-002','CE-003','CE-004','CE-005','CE-006','CE-010','CE-011','CE-012','CE-022','CE-023','CE-024','CE-030','CE-034','CE-035','CE-037','CE-040','CE-050','CE-054','CE-060','CE-062','CE-070','CE-073','CE-075','CE-076','CE-080','CE-081','CE-082','CE-083','CE-084','CE-090','CE-091','CE-093','CE-096','CE-097','RV-023','RV-024','RV-025','RV-026','RV-030');
                UPDATE question_definitions SET adverse_answer = 'YES' WHERE question_code IN ('CE-013','CE-014','CE-015','CE-020','CE-021','CE-025','CE-031','CE-032','CE-033','CE-036','CE-041','CE-042','CE-043','CE-044','CE-051','CE-052','CE-053','CE-055','CE-071','CE-072','CE-074','CE-094','RV-001','RV-002','RV-003','RV-004','RV-005','RV-006','RV-007','RV-008','RV-009','RV-010','RV-011','RV-012','RV-013','RV-014','RV-015','RV-016','RV-017','RV-018','RV-019','RV-020','RV-021','RV-022','RV-027');
                UPDATE question_definitions SET escalates_on_change = true WHERE question_code IN ('RV-003','RV-007','RV-009','RV-020');
                CREATE FUNCTION prevent_engagement_activation_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'an engagement activation is an immutable Partner sign-off';
                END;
                $$;
                CREATE TRIGGER trg_engagement_activations_append_only BEFORE UPDATE OR DELETE ON engagement_activations
                  FOR EACH ROW EXECUTE FUNCTION prevent_engagement_activation_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_engagement_activations_append_only ON engagement_activations;
                DROP FUNCTION IF EXISTS prevent_engagement_activation_mutation();
                """);

            migrationBuilder.DropTable(
                name: "engagement_activations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_question_adverse_answer",
                table: "question_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_evaluation_response_values",
                table: "evaluation_responses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_acceptance_decision_path",
                table: "acceptance_decisions");

            migrationBuilder.DropColumn(
                name: "adverse_answer",
                table: "question_definitions");

            migrationBuilder.DropColumn(
                name: "escalates_on_change",
                table: "question_definitions");

            migrationBuilder.DropColumn(
                name: "evidence_reference",
                table: "evaluation_responses");

            migrationBuilder.DropColumn(
                name: "generation",
                table: "evaluation_responses");

            migrationBuilder.DropColumn(
                name: "path",
                table: "acceptance_decisions");

            migrationBuilder.DropColumn(
                name: "prior_decision_id",
                table: "acceptance_decisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evaluation_response_values",
                table: "evaluation_responses",
                sql: "bank IN ('CE','RV') AND revision >= 1 AND length(trim(question_id)) > 0 AND length(trim(answer)) > 0");
        }
    }
}
