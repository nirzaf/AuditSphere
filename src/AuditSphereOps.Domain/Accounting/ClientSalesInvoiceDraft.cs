namespace AuditSphereOps.Domain.Accounting;

/// <summary>Append-only preparation revision. It is not a posted entry, issued invoice or firm fee invoice.</summary>
public sealed class ClientSalesInvoiceDraft
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid InvoiceId { get; set; }
  public long Revision { get; set; }
  public Guid? PreviousRevisionId { get; set; }
  public long? PreviousRevision { get; set; }
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = "";
  public string DraftReference { get; set; } = "";
  public string SourceReference { get; set; } = "";
  public Guid PeriodId { get; set; }
  public Guid CustomerId { get; set; }
  public Guid ChartVersionId { get; set; }
  public string Currency { get; set; } = "";
  public decimal NetAmount { get; set; }
  public decimal GrossAmount { get; set; }
  public string SnapshotJson { get; set; } = "";
  public string SnapshotHash { get; set; } = "";
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
