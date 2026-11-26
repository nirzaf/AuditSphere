namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable result of a reviewed statement-layout save or publication command.</summary>
public sealed class StatementLayoutCommandReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string Action { get; set; } = string.Empty;
  public string RequestHash { get; set; } = string.Empty;
  public Guid LayoutVersionId { get; set; }
  public string? ExpectedLayoutHash { get; set; }
  public string ResultLayoutHash { get; set; } = string.Empty;
  public string ResultStatus { get; set; } = string.Empty;
  public int ResultVersionNumber { get; set; }
  public string InputJson { get; set; } = string.Empty;
  public string ResultJson { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
