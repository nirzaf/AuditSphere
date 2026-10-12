using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SteV22OwnerDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_risk_band_assessment_rule",
                table: "risk_band_assessments");

            migrationBuilder.AddColumn<bool>(
                name: "critical_estimate",
                table: "risk_band_assessments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "response_channel",
                table: "proposals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_risk_band_assessment_rule",
                table: "risk_band_assessments",
                sql: "likelihood_score BETWEEN 1 AND 3 AND magnitude_score BETWEEN 1 AND 3 AND length(rationale) > 0 AND band = CASE WHEN significant OR fraud_risk OR critical_estimate THEN 'RED' WHEN likelihood_score * magnitude_score >= 6 THEN 'RED' WHEN likelihood_score * magnitude_score >= 3 THEN 'AMBER' ELSE 'GREEN' END");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proposal_response_channel",
                table: "proposals",
                sql: "response_channel IS NULL OR response_channel IN ('EMAIL','SIGNED_LETTER','IN_PERSON','PHONE')");

            // STE v2.2 section 4.2.1 Track B (ADR-0017). The continuance bank gains a legal-notice question and a recorded
            // prior-year fee-settlement confirmation, both with evidence. Every statement is conditional on the continuance
            // template existing and is safe to run twice. Decisions already recorded keep the template version they pinned.
            migrationBuilder.Sql("""
                UPDATE questionnaire_templates SET version = '1.1'
                WHERE id = '00000000-0000-0000-0000-0000000000ba' AND version = '1.0';

                UPDATE question_definitions SET sort_order = 40
                WHERE template_id = '00000000-0000-0000-0000-0000000000ba' AND question_code = 'RV-030' AND sort_order = 30;

                INSERT INTO question_definitions
                  (id, template_id, question_code, section, prompt_text, category, answer_type, requires_evidence, sort_order, adverse_answer, escalates_on_change)
                SELECT gen_random_uuid(), t.id, 'RV-031', 'B.1', 'Any legal notice, claim or demand received since the last acceptance?',
                       'Continuance', 'BOOLEAN', true, 31, 'YES', true
                FROM questionnaire_templates t
                WHERE t.id = '00000000-0000-0000-0000-0000000000ba'
                  AND NOT EXISTS (SELECT 1 FROM question_definitions q WHERE q.template_id = t.id AND q.question_code = 'RV-031');

                INSERT INTO question_definitions
                  (id, template_id, question_code, section, prompt_text, category, answer_type, requires_evidence, sort_order, adverse_answer, escalates_on_change)
                SELECT gen_random_uuid(), t.id, 'RV-032', 'B.1', 'Are all prior-year professional fees settled?',
                       'Continuance', 'BOOLEAN', true, 32, 'NO', false
                FROM questionnaire_templates t
                WHERE t.id = '00000000-0000-0000-0000-0000000000ba'
                  AND NOT EXISTS (SELECT 1 FROM question_definitions q WHERE q.template_id = t.id AND q.question_code = 'RV-032');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A question that already has a recorded answer is kept, so rolling back never orphans evidence.
            migrationBuilder.Sql("""
                DELETE FROM question_definitions q
                WHERE q.template_id = '00000000-0000-0000-0000-0000000000ba' AND q.question_code IN ('RV-031', 'RV-032')
                  AND NOT EXISTS (SELECT 1 FROM evaluation_responses r WHERE r.bank = 'RV' AND r.question_id = q.question_code);

                UPDATE question_definitions SET sort_order = 30
                WHERE template_id = '00000000-0000-0000-0000-0000000000ba' AND question_code = 'RV-030' AND sort_order = 40;

                UPDATE questionnaire_templates SET version = '1.0'
                WHERE id = '00000000-0000-0000-0000-0000000000ba' AND version = '1.1'
                  AND NOT EXISTS (SELECT 1 FROM question_definitions q
                                  WHERE q.template_id = '00000000-0000-0000-0000-0000000000ba' AND q.question_code IN ('RV-031', 'RV-032'));
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_risk_band_assessment_rule",
                table: "risk_band_assessments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_proposal_response_channel",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "critical_estimate",
                table: "risk_band_assessments");

            migrationBuilder.DropColumn(
                name: "response_channel",
                table: "proposals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_risk_band_assessment_rule",
                table: "risk_band_assessments",
                sql: "likelihood_score BETWEEN 1 AND 3 AND magnitude_score BETWEEN 1 AND 3 AND length(rationale) > 0 AND band = CASE WHEN significant OR fraud_risk THEN 'RED' WHEN likelihood_score * magnitude_score >= 6 THEN 'RED' WHEN likelihood_score * magnitude_score >= 3 THEN 'AMBER' ELSE 'GREEN' END");
        }
    }
}
