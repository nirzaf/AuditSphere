using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public static class QuestionnaireSeed
{
  public static readonly Guid CeTemplateId = Guid.Parse("00000000-0000-0000-0000-0000000000ce");
  public static readonly Guid RvTemplateId = Guid.Parse("00000000-0000-0000-0000-0000000000ba");

  public static readonly QuestionnaireTemplate CeTemplate = new()
  {
    Id = CeTemplateId,
    Bank = "CE",
    Version = "1.0",
    Name = "Client Acquisition & Evaluation Bank (CE-62)",
    IsActive = true,
    CreatedAt = DateTimeOffset.UnixEpoch
  };

  public static readonly QuestionnaireTemplate RvTemplate = new()
  {
    Id = RvTemplateId,
    Bank = "RV",
    Version = "1.0",
    Name = "Continuance & Review Question Bank (RV-30)",
    IsActive = true,
    CreatedAt = DateTimeOffset.UnixEpoch
  };

  public static readonly QuestionDefinition[] CeQuestions =
  [
    // A.1 Identity & Legal Existence
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-001", Section = "A.1", PromptText = "Has the client's legal existence been verified?", Category = "Identity", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 1 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-002", Section = "A.1", PromptText = "Is the registered address verified?", Category = "Identity", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 2 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-003", Section = "A.1", PromptText = "Are directors/key officers identified?", Category = "Identity", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 3 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-004", Section = "A.1", PromptText = "Are authorized signatories identified?", Category = "Identity", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 4 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-005", Section = "A.1", PromptText = "Is the client's business activity clearly understood?", Category = "Identity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 5 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-006", Section = "A.1", PromptText = "Are all material jurisdictions of operation known?", Category = "Identity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 6 },

    // A.2 Ownership & Beneficial Ownership
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-010", Section = "A.2", PromptText = "Is the full ownership structure documented?", Category = "Ownership", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 10 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-011", Section = "A.2", PromptText = "Are ultimate beneficial owners identified where required?", Category = "Ownership", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 11 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-012", Section = "A.2", PromptText = "Can beneficial ownership be independently verified to a reasonable level?", Category = "Ownership", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 12 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-013", Section = "A.2", PromptText = "Are nominees, trusts or layered entities involved?", Category = "Ownership", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 13 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-014", Section = "A.2", PromptText = "Has ownership changed materially in the last 12 months?", Category = "Ownership", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 14 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-015", Section = "A.2", PromptText = "Is the ownership structure unusually complex relative to business purpose?", Category = "Ownership", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 15 },

    // A.3 Management Integrity & Reputation
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-020", Section = "A.3", PromptText = "Are there known integrity concerns involving owners/directors/senior management?", Category = "Integrity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 20 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-021", Section = "A.3", PromptText = "Has management previously provided misleading or inconsistent information?", Category = "Integrity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 21 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-022", Section = "A.3", PromptText = "Is management willing to correct identified accounting errors?", Category = "Integrity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 22 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-023", Section = "A.3", PromptText = "Does management accept responsibility for the financial statements?", Category = "Integrity", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 23 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-024", Section = "A.3", PromptText = "Is management cooperative with information requests?", Category = "Integrity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 24 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-025", Section = "A.3", PromptText = "Are there unexplained adverse media or serious reputation concerns?", Category = "Integrity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 25 },

    // A.4 AML/CFT, Sanctions and PEP Risk
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-030", Section = "A.4", PromptText = "Has required KYC/CDD been completed?", Category = "AML", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 30 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-031", Section = "A.4", PromptText = "Are any owners/controllers/directors PEPs or close associates?", Category = "AML", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 31 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-032", Section = "A.4", PromptText = "Are there sanctions matches requiring legal/compliance action?", Category = "AML", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 32 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-033", Section = "A.4", PromptText = "Does the client operate in a high-risk sector/jurisdiction?", Category = "AML", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 33 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-034", Section = "A.4", PromptText = "Is expected transaction behavior consistent with the stated business?", Category = "AML", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 34 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-035", Section = "A.4", PromptText = "Is source of funds/source of wealth satisfactorily understood?", Category = "AML", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 35 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-036", Section = "A.4", PromptText = "Are there unexplained cash-intensive or complex transactions?", Category = "AML", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 36 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-037", Section = "A.4", PromptText = "Have material beneficial-ownership changes been screened?", Category = "AML", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 37 },

    // A.5 Previous Auditor & History
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-040", Section = "A.5", PromptText = "Has professional clearance from the predecessor auditor been sought and received?", Category = "AuditorHistory", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 40 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-041", Section = "A.5", PromptText = "Did the predecessor report any reasons not to accept the engagement?", Category = "AuditorHistory", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 41 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-042", Section = "A.5", PromptText = "Were there accounting or audit disagreements with the prior auditor?", Category = "AuditorHistory", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 42 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-043", Section = "A.5", PromptText = "Were prior-year audit reports modified, qualified or disclaimed?", Category = "AuditorHistory", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 43 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-044", Section = "A.5", PromptText = "Has the client frequently changed auditors?", Category = "AuditorHistory", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 44 },

    // A.6 Financial & Going-Concern Profile
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-050", Section = "A.6", PromptText = "Are historical financial statements available and reviewed?", Category = "Financial", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 50 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-051", Section = "A.6", PromptText = "Are there material going-concern indicators?", Category = "Financial", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 51 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-052", Section = "A.6", PromptText = "Is the client experiencing severe liquidity or working capital strain?", Category = "Financial", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 52 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-053", Section = "A.6", PromptText = "Are there significant debt defaults or covenant breaches?", Category = "Financial", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 53 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-054", Section = "A.6", PromptText = "Is the revenue model transparent and sustainable?", Category = "Financial", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 54 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-055", Section = "A.6", PromptText = "Are there major contingent liabilities or unrecorded exposures?", Category = "Financial", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 55 },

    // A.7 Engagement Complexity & Resources
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-060", Section = "A.7", PromptText = "Does the firm possess required industry expertise and competencies?", Category = "Complexity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 60 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-061", Section = "A.7", PromptText = "Are specialist skills required (valuation, IT, tax, actuarial)?", Category = "Complexity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 61 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-062", Section = "A.7", PromptText = "Are adequate staffing resources available for the required timetable?", Category = "Complexity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 62 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-063", Section = "A.7", PromptText = "Is an Engagement Quality Reviewer (EQR) required by policy/regulation?", Category = "Complexity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 63 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-064", Section = "A.7", PromptText = "Are group audit requirements or component auditor communications involved?", Category = "Complexity", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 64 },

    // A.8 Independence, Ethics and Conflicts
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-070", Section = "A.8", PromptText = "Have all partner and team personal financial independence confirmations been received?", Category = "Independence", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 70 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-071", Section = "A.8", PromptText = "Are there prohibited non-audit services provided or proposed?", Category = "Independence", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 71 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-072", Section = "A.8", PromptText = "Are there close business, family, or employment relationships with client key personnel?", Category = "Independence", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 72 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-073", Section = "A.8", PromptText = "Are fee dependency limits respected under firm independence thresholds?", Category = "Independence", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 73 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-074", Section = "A.8", PromptText = "Are there existing disputes, litigation, or unpaid overdue fees from past services?", Category = "Independence", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 74 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-075", Section = "A.8", PromptText = "Are contingent fee arrangements strictly absent for assurance services?", Category = "Independence", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 75 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-076", Section = "A.8", PromptText = "Has partner rotation policy been reviewed and confirmed compliant?", Category = "Independence", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 76 },

    // A.9 Commercial & Engagement Viability
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-080", Section = "A.9", PromptText = "Is the proposed engagement fee commercially viable and reflective of scope?", Category = "Commercial", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 80 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-081", Section = "A.9", PromptText = "Are payment terms agreed and credit risk acceptable?", Category = "Commercial", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 81 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-082", Section = "A.9", PromptText = "Is the reporting deadline realistic without compromising quality?", Category = "Commercial", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 82 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-083", Section = "A.9", PromptText = "Are client accounting records expected to be in an auditable condition?", Category = "Commercial", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 83 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-084", Section = "A.9", PromptText = "Is engagement letter terms acceptance formal and documented?", Category = "Commercial", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 84 },

    // A.10 Data, Cyber and Confidentiality
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-090", Section = "A.10", PromptText = "Are data privacy and cross-border data transfer restrictions identified?", Category = "Cyber", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 90 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-091", Section = "A.10", PromptText = "Can client data be securely ingested and retained in the firm's repository boundary?", Category = "Cyber", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 91 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-092", Section = "A.10", PromptText = "Are there special client security or confidentiality requirements?", Category = "Cyber", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 92 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-093", Section = "A.10", PromptText = "Is client IT environment sufficiently stable for remote/electronic PBC requests?", Category = "Cyber", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 93 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-094", Section = "A.10", PromptText = "Are there known recent material cybersecurity breaches at the client?", Category = "Cyber", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 94 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-095", Section = "A.10", PromptText = "Are third-party service organizations or cloud systems SOC reports required?", Category = "Cyber", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 95 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-096", Section = "A.10", PromptText = "Does the client permit secure staging outside local premises?", Category = "Cyber", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 96 },
    new() { Id = Guid.NewGuid(), TemplateId = CeTemplateId, QuestionCode = "CE-097", Section = "A.10", PromptText = "Have appropriate confidentiality non-disclosure terms been agreed?", Category = "Cyber", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 97 }
  ];

  public static readonly QuestionDefinition[] RvQuestions =
  [
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-001", Section = "B.1", PromptText = "Any change in legal name or registration?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 1 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-002", Section = "B.1", PromptText = "Any ownership/UBO change?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 2 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-003", Section = "B.1", PromptText = "Any new director/key manager?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 3 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-004", Section = "B.1", PromptText = "Any new country of operation?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 4 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-005", Section = "B.1", PromptText = "Any major change in business model?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 5 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-006", Section = "B.1", PromptText = "Any acquisition/disposal/restructuring?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 6 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-007", Section = "B.1", PromptText = "Any new financing or debt covenant concern?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 7 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-008", Section = "B.1", PromptText = "Any new significant related parties?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 8 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-009", Section = "B.1", PromptText = "Any major litigation/regulatory investigation?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 9 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-010", Section = "B.1", PromptText = "Any change in reporting framework?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 10 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-011", Section = "B.1", PromptText = "Any change in accounting software/data environment?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 11 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-012", Section = "B.1", PromptText = "Were last year's records delivered late/incomplete?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 12 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-013", Section = "B.1", PromptText = "Were there repeated unsupported balances?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 13 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-014", Section = "B.1", PromptText = "Were there significant proposed adjustments?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 14 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-015", Section = "B.1", PromptText = "Did management refuse material adjustments?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 15 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-016", Section = "B.1", PromptText = "Were significant deficiencies reported?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 16 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-017", Section = "B.1", PromptText = "Was last year's report modified?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 17 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-018", Section = "B.1", PromptText = "Was there a scope limitation?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 18 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-019", Section = "B.1", PromptText = "Were representations difficult to obtain?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 19 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-020", Section = "B.1", PromptText = "Any suspected/confirmed fraud or illegal act concerns?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 20 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-021", Section = "B.1", PromptText = "Any complaints/allegations involving the engagement?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 21 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-022", Section = "B.1", PromptText = "Are fees significantly overdue?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 22 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-023", Section = "B.1", PromptText = "Can the firm remain independent?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 23 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-024", Section = "B.1", PromptText = "Does the firm still have competent resources?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 24 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-025", Section = "B.1", PromptText = "Are deadlines achievable?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 25 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-026", Section = "B.1", PromptText = "Have prior acceptance conditions been satisfied?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 26 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-027", Section = "B.1", PromptText = "Has client risk rating increased?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 27 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-028", Section = "B.1", PromptText = "Should engagement scope/fee change?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 28 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-029", Section = "B.1", PromptText = "Is a new engagement letter required by policy/change?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = false, SortOrder = 29 },
    new() { Id = Guid.NewGuid(), TemplateId = RvTemplateId, QuestionCode = "RV-030", Section = "B.1", PromptText = "Should the relationship continue?", Category = "Continuance", AnswerType = "BOOLEAN", RequiresEvidence = true, SortOrder = 30 }
  ];

  public static async Task SeedTemplatesAndDefinitionsAsync(IAuditSphereDbContext db, CancellationToken ct = default)
  {
    if (!await db.QuestionnaireTemplates.AnyAsync(x => x.Id == CeTemplateId, ct))
    {
      db.QuestionnaireTemplates.Add(CeTemplate);
      foreach (var q in CeQuestions)
      {
        db.QuestionDefinitions.Add(q);
      }
    }

    if (!await db.QuestionnaireTemplates.AnyAsync(x => x.Id == RvTemplateId, ct))
    {
      db.QuestionnaireTemplates.Add(RvTemplate);
      foreach (var q in RvQuestions)
      {
        db.QuestionDefinitions.Add(q);
      }
    }
  }
}
