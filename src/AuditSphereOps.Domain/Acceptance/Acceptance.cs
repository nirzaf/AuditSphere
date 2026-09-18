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
