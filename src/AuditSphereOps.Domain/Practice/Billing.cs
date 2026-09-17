// Billing: invoice/credit/receipt/allocation artifacts; owner finance profile authorizes release (§41.4).
namespace AuditSphereOps.Domain.Practice;

public sealed class BillingAccount
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Invoice
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid BillingAccountId { get; set; }
  public string InvoiceNumber { get; set; } = string.Empty;
  public decimal Subtotal { get; set; }
  public decimal Tax { get; set; }
  public decimal Total { get; set; }
  public string Status { get; set; } = "Draft"; // Draft|Issued|Paid|Credited
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class InvoiceLine
{
  public Guid Id { get; set; }
  public Guid InvoiceId { get; set; }
  public string Description { get; set; } = string.Empty;
  public decimal Quantity { get; set; }
  public decimal UnitPrice { get; set; }
  public decimal LineTotal { get; set; }
}

public sealed class Receipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid BillingAccountId { get; set; }
  public decimal Amount { get; set; }
  public string Reference { get; set; } = string.Empty;
  public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class Allocation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ReceiptId { get; set; }
  public Guid InvoiceId { get; set; }
  public decimal Amount { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
