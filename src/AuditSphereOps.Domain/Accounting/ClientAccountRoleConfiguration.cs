namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable role proposal. Independent approval activates this exact account and interval.</summary>
public sealed class ClientAccountRoleConfiguration
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ChartVersionId { get; set; }
  public Guid AccountId { get; set; }
  public string Role { get; set; } = "";
  public DateOnly EffectiveFrom { get; set; }
  public DateOnly? EffectiveTo { get; set; }
  public string Reason { get; set; } = "";
  public Guid ProposedByUserId { get; set; }
  public DateTimeOffset ProposedAt { get; set; }
}

public sealed class ClientAccountRoleDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ConfigurationId { get; set; }
  public string Decision { get; set; } = "";
  public string Reason { get; set; } = "";
  public Guid ReviewedByUserId { get; set; }
  public DateTimeOffset ReviewedAt { get; set; }
}
