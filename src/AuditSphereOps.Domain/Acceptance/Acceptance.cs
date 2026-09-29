// Acceptance + engagements: questionnaires (CE/RV seeds), decisions, engagement shells (§§13–14, 27.2).
namespace AuditSphereOps.Domain.Acceptance;

public static class AcceptancePaths
{
  /// <summary>First acceptance: the full CE onboarding bank (KYC, AML, independence, ...).</summary>
  public const string NewClient = "NEW_CLIENT";
  /// <summary>Recurring client: the RV delta bank against the prior accepted decision.</summary>
  public const string Continuance = "CONTINUANCE";
}

public sealed class EvaluationResponse
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public string Bank { get; set; } = "CE";  // CE | RV
  public string QuestionId { get; set; } = string.Empty;
  public string Answer { get; set; } = string.Empty;
  /// <summary>Reference to the KYC/AML/independence evidence supporting the answer (a document or record identity).</summary>
  public string? EvidenceReference { get; set; }
  /// <summary>Client input generation the answer belongs to; a new generation requires fresh answers.</summary>
  public long Generation { get; set; } = 1;
  public long Revision { get; set; } = 1;
  public Guid AnsweredByUserId { get; set; }
  public DateTimeOffset AnsweredAt { get; set; }
}

public sealed class AcceptanceDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public Guid? EngagementId { get; set; }
  public string Decision { get; set; } = "Pending"; // Pending|Accepted|AcceptedWithConditions|Declined|Deferred
  public string ServiceRoute { get; set; } = string.Empty;
  public long Generation { get; set; } = 1;         // §22: approvals bind to generation
  public string Rationale { get; set; } = string.Empty;
  public string? Conditions { get; set; }
  /// <summary>NEW_CLIENT or CONTINUANCE: which checklist the decision was made against.</summary>
  public string Path { get; set; } = AcceptancePaths.NewClient;
  /// <summary>For a continuance decision, the prior accepted decision it reviews the delta against.</summary>
  public Guid? PriorDecisionId { get; set; }
  public string EvaluationTemplateVersion { get; set; } = string.Empty;
  public string EvaluationSnapshotDigest { get; set; } = string.Empty;
  public Guid? DecidedByUserId { get; set; }
  public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class SpecialistClearance
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public Guid? EngagementId { get; set; }
  public string Area { get; set; } = string.Empty; // Independence|AML|Valuation|Tax|IT
  public Guid? SpecialistUserId { get; set; }
  public string SpecialistName { get; set; } = string.Empty;
  public string Status { get; set; } = "PENDING"; // PENDING|CLEARED|HOLD|CONDITIONS
  public string? EvidenceReference { get; set; }
  public string? Conditions { get; set; }
  public DateTimeOffset? ClearedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class QuestionnaireTemplate
{
  public Guid Id { get; set; }
  public string Bank { get; set; } = "CE"; // CE|RV
  public string Version { get; set; } = "1.0";
  public string Name { get; set; } = string.Empty;
  public bool IsActive { get; set; } = true;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class QuestionDefinition
{
  public Guid Id { get; set; }
  public Guid TemplateId { get; set; }
  public string QuestionCode { get; set; } = string.Empty; // CE-01..CE-62, RV-01..RV-30
  public string Section { get; set; } = string.Empty;      // Section letter / title
  public string PromptText { get; set; } = string.Empty;
  public string Category { get; set; } = string.Empty;
  public string AnswerType { get; set; } = "BOOLEAN";      // BOOLEAN|TEXT|CHOICE
  /// <summary>YES or NO when that answer is adverse and needs a cleared specialist review; null when informational.</summary>
  public string? AdverseAnswer { get; set; }
  /// <summary>A key continuance delta item (management, borrowings, fraud, litigation): an adverse answer escalates.</summary>
  public bool EscalatesOnChange { get; set; }
  public bool RequiresEvidence { get; set; }
  public int SortOrder { get; set; }
}
