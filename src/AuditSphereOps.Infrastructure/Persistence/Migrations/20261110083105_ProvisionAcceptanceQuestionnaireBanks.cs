using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    // Data-only migration: the EF model snapshot is unchanged, so migration metadata lives here
    // instead of duplicating the full 40k-line target model designer.
    [DbContext(typeof(AuditSphereDbContext))]
    [Migration("20261110083105_ProvisionAcceptanceQuestionnaireBanks")]
    public partial class ProvisionAcceptanceQuestionnaireBanks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
SELECT pg_advisory_xact_lock(hashtext('AuditSphere:questionnaire-bank-provisioning:v1'));
LOCK TABLE questionnaire_templates, question_definitions IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE pms_expected_questionnaire_templates (
  id uuid PRIMARY KEY, bank text NOT NULL, version text NOT NULL, name text NOT NULL, is_active boolean NOT NULL
) ON COMMIT DROP;
INSERT INTO pms_expected_questionnaire_templates (id, bank, version, name, is_active) VALUES
  ('00000000-0000-0000-0000-0000000000ce', 'CE', '1.0', 'Client Acquisition & Evaluation Bank (CE-62)', TRUE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV', '1.1', 'Continuance & Review Question Bank (RV-30)', TRUE);

CREATE TEMP TABLE pms_expected_question_definitions (
  template_id uuid NOT NULL, question_code text NOT NULL, section text NOT NULL, prompt_text text NOT NULL,
  category text NOT NULL, answer_type text NOT NULL, requires_evidence boolean NOT NULL, sort_order integer NOT NULL,
  adverse_answer text NULL, escalates_on_change boolean NOT NULL,
  PRIMARY KEY (template_id, question_code)
) ON COMMIT DROP;
INSERT INTO pms_expected_question_definitions
  (template_id, question_code, section, prompt_text, category, answer_type, requires_evidence, sort_order, adverse_answer, escalates_on_change)
VALUES
  ('00000000-0000-0000-0000-0000000000ce', 'CE-001', 'A.1', 'Has the client''s legal existence been verified?', 'Identity', 'BOOLEAN', true, 1, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-002', 'A.1', 'Is the registered address verified?', 'Identity', 'BOOLEAN', true, 2, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-003', 'A.1', 'Are directors/key officers identified?', 'Identity', 'BOOLEAN', true, 3, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-004', 'A.1', 'Are authorized signatories identified?', 'Identity', 'BOOLEAN', true, 4, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-005', 'A.1', 'Is the client''s business activity clearly understood?', 'Identity', 'BOOLEAN', false, 5, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-006', 'A.1', 'Are all material jurisdictions of operation known?', 'Identity', 'BOOLEAN', false, 6, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-010', 'A.2', 'Is the full ownership structure documented?', 'Ownership', 'BOOLEAN', true, 10, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-011', 'A.2', 'Are ultimate beneficial owners identified where required?', 'Ownership', 'BOOLEAN', true, 11, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-012', 'A.2', 'Can beneficial ownership be independently verified to a reasonable level?', 'Ownership', 'BOOLEAN', true, 12, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-013', 'A.2', 'Are nominees, trusts or layered entities involved?', 'Ownership', 'BOOLEAN', false, 13, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-014', 'A.2', 'Has ownership changed materially in the last 12 months?', 'Ownership', 'BOOLEAN', false, 14, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-015', 'A.2', 'Is the ownership structure unusually complex relative to business purpose?', 'Ownership', 'BOOLEAN', false, 15, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-020', 'A.3', 'Are there known integrity concerns involving owners/directors/senior management?', 'Integrity', 'BOOLEAN', false, 20, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-021', 'A.3', 'Has management previously provided misleading or inconsistent information?', 'Integrity', 'BOOLEAN', false, 21, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-022', 'A.3', 'Is management willing to correct identified accounting errors?', 'Integrity', 'BOOLEAN', false, 22, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-023', 'A.3', 'Does management accept responsibility for the financial statements?', 'Integrity', 'BOOLEAN', true, 23, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-024', 'A.3', 'Is management cooperative with information requests?', 'Integrity', 'BOOLEAN', false, 24, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-025', 'A.3', 'Are there unexplained adverse media or serious reputation concerns?', 'Integrity', 'BOOLEAN', false, 25, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-030', 'A.4', 'Has required KYC/CDD been completed?', 'AML', 'BOOLEAN', true, 30, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-031', 'A.4', 'Are any owners/controllers/directors PEPs or close associates?', 'AML', 'BOOLEAN', true, 31, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-032', 'A.4', 'Are there sanctions matches requiring legal/compliance action?', 'AML', 'BOOLEAN', true, 32, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-033', 'A.4', 'Does the client operate in a high-risk sector/jurisdiction?', 'AML', 'BOOLEAN', false, 33, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-034', 'A.4', 'Is expected transaction behavior consistent with the stated business?', 'AML', 'BOOLEAN', false, 34, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-035', 'A.4', 'Is source of funds/source of wealth satisfactorily understood?', 'AML', 'BOOLEAN', true, 35, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-036', 'A.4', 'Are there unexplained cash-intensive or complex transactions?', 'AML', 'BOOLEAN', false, 36, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-037', 'A.4', 'Have material beneficial-ownership changes been screened?', 'AML', 'BOOLEAN', true, 37, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-040', 'A.5', 'Has professional clearance from the predecessor auditor been sought and received?', 'AuditorHistory', 'BOOLEAN', true, 40, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-041', 'A.5', 'Did the predecessor report any reasons not to accept the engagement?', 'AuditorHistory', 'BOOLEAN', true, 41, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-042', 'A.5', 'Were there accounting or audit disagreements with the prior auditor?', 'AuditorHistory', 'BOOLEAN', false, 42, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-043', 'A.5', 'Were prior-year audit reports modified, qualified or disclaimed?', 'AuditorHistory', 'BOOLEAN', true, 43, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-044', 'A.5', 'Has the client frequently changed auditors?', 'AuditorHistory', 'BOOLEAN', false, 44, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-050', 'A.6', 'Are historical financial statements available and reviewed?', 'Financial', 'BOOLEAN', true, 50, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-051', 'A.6', 'Are there material going-concern indicators?', 'Financial', 'BOOLEAN', false, 51, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-052', 'A.6', 'Is the client experiencing severe liquidity or working capital strain?', 'Financial', 'BOOLEAN', false, 52, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-053', 'A.6', 'Are there significant debt defaults or covenant breaches?', 'Financial', 'BOOLEAN', false, 53, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-054', 'A.6', 'Is the revenue model transparent and sustainable?', 'Financial', 'BOOLEAN', false, 54, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-055', 'A.6', 'Are there major contingent liabilities or unrecorded exposures?', 'Financial', 'BOOLEAN', false, 55, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-060', 'A.7', 'Does the firm possess required industry expertise and competencies?', 'Complexity', 'BOOLEAN', false, 60, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-061', 'A.7', 'Are specialist skills required (valuation, IT, tax, actuarial)?', 'Complexity', 'BOOLEAN', false, 61, NULL, FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-062', 'A.7', 'Are adequate staffing resources available for the required timetable?', 'Complexity', 'BOOLEAN', false, 62, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-063', 'A.7', 'Is an Engagement Quality Reviewer (EQR) required by policy/regulation?', 'Complexity', 'BOOLEAN', false, 63, NULL, FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-064', 'A.7', 'Are group audit requirements or component auditor communications involved?', 'Complexity', 'BOOLEAN', false, 64, NULL, FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-070', 'A.8', 'Have all partner and team personal financial independence confirmations been received?', 'Independence', 'BOOLEAN', true, 70, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-071', 'A.8', 'Are there prohibited non-audit services provided or proposed?', 'Independence', 'BOOLEAN', true, 71, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-072', 'A.8', 'Are there close business, family, or employment relationships with client key personnel?', 'Independence', 'BOOLEAN', false, 72, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-073', 'A.8', 'Are fee dependency limits respected under firm independence thresholds?', 'Independence', 'BOOLEAN', false, 73, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-074', 'A.8', 'Are there existing disputes, litigation, or unpaid overdue fees from past services?', 'Independence', 'BOOLEAN', false, 74, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-075', 'A.8', 'Are contingent fee arrangements strictly absent for assurance services?', 'Independence', 'BOOLEAN', true, 75, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-076', 'A.8', 'Has partner rotation policy been reviewed and confirmed compliant?', 'Independence', 'BOOLEAN', true, 76, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-080', 'A.9', 'Is the proposed engagement fee commercially viable and reflective of scope?', 'Commercial', 'BOOLEAN', false, 80, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-081', 'A.9', 'Are payment terms agreed and credit risk acceptable?', 'Commercial', 'BOOLEAN', false, 81, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-082', 'A.9', 'Is the reporting deadline realistic without compromising quality?', 'Commercial', 'BOOLEAN', false, 82, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-083', 'A.9', 'Are client accounting records expected to be in an auditable condition?', 'Commercial', 'BOOLEAN', false, 83, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-084', 'A.9', 'Is engagement letter terms acceptance formal and documented?', 'Commercial', 'BOOLEAN', true, 84, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-090', 'A.10', 'Are data privacy and cross-border data transfer restrictions identified?', 'Cyber', 'BOOLEAN', false, 90, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-091', 'A.10', 'Can client data be securely ingested and retained in the firm''s repository boundary?', 'Cyber', 'BOOLEAN', false, 91, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-092', 'A.10', 'Are there special client security or confidentiality requirements?', 'Cyber', 'BOOLEAN', false, 92, NULL, FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-093', 'A.10', 'Is client IT environment sufficiently stable for remote/electronic PBC requests?', 'Cyber', 'BOOLEAN', false, 93, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-094', 'A.10', 'Are there known recent material cybersecurity breaches at the client?', 'Cyber', 'BOOLEAN', false, 94, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-095', 'A.10', 'Are third-party service organizations or cloud systems SOC reports required?', 'Cyber', 'BOOLEAN', false, 95, NULL, FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-096', 'A.10', 'Does the client permit secure staging outside local premises?', 'Cyber', 'BOOLEAN', false, 96, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ce', 'CE-097', 'A.10', 'Have appropriate confidentiality non-disclosure terms been agreed?', 'Cyber', 'BOOLEAN', true, 97, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-001', 'B.1', 'Any change in legal name or registration?', 'Continuance', 'BOOLEAN', true, 1, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-002', 'B.1', 'Any ownership/UBO change?', 'Continuance', 'BOOLEAN', true, 2, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-003', 'B.1', 'Any new director/key manager?', 'Continuance', 'BOOLEAN', false, 3, 'YES', TRUE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-004', 'B.1', 'Any new country of operation?', 'Continuance', 'BOOLEAN', false, 4, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-005', 'B.1', 'Any major change in business model?', 'Continuance', 'BOOLEAN', false, 5, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-006', 'B.1', 'Any acquisition/disposal/restructuring?', 'Continuance', 'BOOLEAN', false, 6, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-007', 'B.1', 'Any new financing or debt covenant concern?', 'Continuance', 'BOOLEAN', false, 7, 'YES', TRUE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-008', 'B.1', 'Any new significant related parties?', 'Continuance', 'BOOLEAN', false, 8, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-009', 'B.1', 'Any major litigation/regulatory investigation?', 'Continuance', 'BOOLEAN', true, 9, 'YES', TRUE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-010', 'B.1', 'Any change in reporting framework?', 'Continuance', 'BOOLEAN', false, 10, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-011', 'B.1', 'Any change in accounting software/data environment?', 'Continuance', 'BOOLEAN', false, 11, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-012', 'B.1', 'Were last year''s records delivered late/incomplete?', 'Continuance', 'BOOLEAN', false, 12, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-013', 'B.1', 'Were there repeated unsupported balances?', 'Continuance', 'BOOLEAN', false, 13, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-014', 'B.1', 'Were there significant proposed adjustments?', 'Continuance', 'BOOLEAN', false, 14, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-015', 'B.1', 'Did management refuse material adjustments?', 'Continuance', 'BOOLEAN', true, 15, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-016', 'B.1', 'Were significant deficiencies reported?', 'Continuance', 'BOOLEAN', false, 16, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-017', 'B.1', 'Was last year''s report modified?', 'Continuance', 'BOOLEAN', true, 17, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-018', 'B.1', 'Was there a scope limitation?', 'Continuance', 'BOOLEAN', false, 18, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-019', 'B.1', 'Were representations difficult to obtain?', 'Continuance', 'BOOLEAN', false, 19, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-020', 'B.1', 'Any suspected/confirmed fraud or illegal act concerns?', 'Continuance', 'BOOLEAN', true, 20, 'YES', TRUE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-021', 'B.1', 'Any complaints/allegations involving the engagement?', 'Continuance', 'BOOLEAN', false, 21, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-022', 'B.1', 'Are fees significantly overdue?', 'Continuance', 'BOOLEAN', false, 22, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-023', 'B.1', 'Can the firm remain independent?', 'Continuance', 'BOOLEAN', true, 23, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-024', 'B.1', 'Does the firm still have competent resources?', 'Continuance', 'BOOLEAN', false, 24, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-025', 'B.1', 'Are deadlines achievable?', 'Continuance', 'BOOLEAN', false, 25, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-026', 'B.1', 'Have prior acceptance conditions been satisfied?', 'Continuance', 'BOOLEAN', true, 26, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-027', 'B.1', 'Has client risk rating increased?', 'Continuance', 'BOOLEAN', false, 27, 'YES', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-028', 'B.1', 'Should engagement scope/fee change?', 'Continuance', 'BOOLEAN', false, 28, NULL, FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-029', 'B.1', 'Is a new engagement letter required by policy/change?', 'Continuance', 'BOOLEAN', false, 29, NULL, FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-030', 'B.1', 'Should the relationship continue?', 'Continuance', 'BOOLEAN', true, 40, 'NO', FALSE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-031', 'B.1', 'Any legal notice, claim or demand received since the last acceptance?', 'Continuance', 'BOOLEAN', true, 31, 'YES', TRUE),
  ('00000000-0000-0000-0000-0000000000ba', 'RV-032', 'B.1', 'Are all prior-year professional fees settled?', 'Continuance', 'BOOLEAN', true, 32, 'NO', FALSE);
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM pms_expected_questionnaire_templates e
    JOIN questionnaire_templates t ON t.id = e.id
    WHERE ROW(t.bank, t.version, t.name, t.is_active) IS DISTINCT FROM ROW(e.bank, e.version, e.name, e.is_active)
  ) OR EXISTS (
    SELECT 1 FROM questionnaire_templates t
    JOIN pms_expected_questionnaire_templates e ON e.bank = t.bank AND e.version = t.version
    WHERE t.id <> e.id
  ) OR EXISTS (
    SELECT 1 FROM questionnaire_templates t
    JOIN pms_expected_questionnaire_templates e ON e.bank = t.bank
    WHERE t.is_active AND t.id <> e.id
  ) THEN
    RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'questionnaire provisioning conflict: retained template differs from the coded active bank';
  END IF;
END $$;

INSERT INTO questionnaire_templates (id, bank, version, name, is_active, created_at)
SELECT e.id, e.bank, e.version, e.name, e.is_active, TIMESTAMPTZ '1970-01-01 00:00:00+00'
FROM pms_expected_questionnaire_templates e
WHERE NOT EXISTS (SELECT 1 FROM questionnaire_templates t WHERE t.id = e.id);

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM question_definitions q
    JOIN pms_expected_question_definitions e ON e.template_id = q.template_id AND e.question_code = q.question_code
    WHERE ROW(q.section, q.prompt_text, q.category, q.answer_type, q.requires_evidence, q.sort_order,
              q.adverse_answer, q.escalates_on_change)
      IS DISTINCT FROM
          ROW(e.section, e.prompt_text, e.category, e.answer_type, e.requires_evidence, e.sort_order,
              e.adverse_answer, e.escalates_on_change)
  ) OR EXISTS (
    SELECT 1 FROM question_definitions q
    JOIN pms_expected_questionnaire_templates t ON t.id = q.template_id
    WHERE NOT EXISTS (
      SELECT 1 FROM pms_expected_question_definitions e
      WHERE e.template_id = q.template_id AND e.question_code = q.question_code
    )
  ) THEN
    RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'questionnaire provisioning conflict: retained question content differs from the coded bank';
  END IF;
END $$;

-- Insert only absent codes. Existing question IDs and all answer rows remain untouched.
INSERT INTO question_definitions
  (id, template_id, question_code, section, prompt_text, category, answer_type, requires_evidence, sort_order, adverse_answer, escalates_on_change)
SELECT gen_random_uuid(), e.template_id, e.question_code, e.section, e.prompt_text, e.category, e.answer_type,
       e.requires_evidence, e.sort_order, e.adverse_answer, e.escalates_on_change
FROM pms_expected_question_definitions e
WHERE NOT EXISTS (
  SELECT 1 FROM question_definitions q
  WHERE q.template_id = e.template_id AND q.question_code = e.question_code
)
ORDER BY e.template_id, e.question_code;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Provisioned question banks may already have answers. Keep catalogue data on rollback;
            // reapplying this migration validates and converges without replacing persisted identities.
        }
    }
}
