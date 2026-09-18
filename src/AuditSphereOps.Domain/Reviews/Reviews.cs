// Reviews + completion: points, approvals bind exact revisions/generations; release fenced (§§22, 24, 29).
namespace AuditSphereOps.Domain.Reviews;

public sealed class ReviewPoint
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid TargetId { get; set; }
  public string TargetKind { get; set; } = string.Empty;
  public long TargetRevision { get; set; }
  public string Comment { get; set; } = string.Empty;
  public bool Significant { get; set; }
  public bool Cleared { get; set; }
  public Guid RaisedByUserId { get; set; }
  public DateTimeOffset RaisedAt { get; set; }
}

public sealed class Approval
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string TargetKind { get; set; } = string.Empty;
  public Guid TargetId { get; set; }
  public long TargetRevision { get; set; }
  public long InputGeneration { get; set; }
  public long PolicyGeneration { get; set; }
  public string ManifestDigest { get; set; } = string.Empty;
  public string Decision { get; set; } = "APPROVED";
  public Guid DecidedByUserId { get; set; }
  public DateTimeOffset DecidedAt { get; set; }
}

public static class ApprovalStates
{
  public const string Approved = "APPROVED";
  public const string Rejected = "REJECTED";
  public const string Current = "CURRENT";
  public const string Stale = "STALE";
}

/// <summary>Mutable applicability projection for an immutable historical approval.</summary>
public sealed class ApprovalApplicability
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ApprovalId { get; set; }
  public string Status { get; set; } = ApprovalStates.Current;
  public string Reason { get; set; } = string.Empty;
  public long CurrentTargetRevision { get; set; }
  public long CurrentInputGeneration { get; set; }
  public long CurrentPolicyGeneration { get; set; }
  public DateTimeOffset EvaluatedAt { get; set; }
}
