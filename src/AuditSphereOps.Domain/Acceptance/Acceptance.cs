// Acceptance + engagements: questionnaires (CE/RV seeds), decisions, engagement shells (§§13–14, 27.2).
namespace AuditSphereOps.Domain.Acceptance;

public sealed class EvaluationResponse
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public string Bank { get; set; } = "CE";  // CE | RV
  public string QuestionId { get; set; } = string.Empty;
  public string Answer { get; set; } = string.Empty;
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
  public string Decision { get; set; } = "Pending"; // Pending|Accepted|AcceptedWithConditions|Declined
  public string ServiceRoute { get; set; } = string.Empty;
  public long Generation { get; set; } = 1;         // §22: approvals bind to generation
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
  public bool RequiresEvidence { get; set; }
  public int SortOrder { get; set; }
}

