// Fieldwork connections (STE 3.2-02..05): the persisted sampling calculation log, links from a procedure to the exact
// received client upload, the physical file index with movement history, and ad hoc steps inserted into an adopted
// programme. Evidence rows are append-only; physical locations change only through recorded movements.
namespace AuditSphereOps.Domain.Audit;

/// <summary>Everything needed to re-perform one engine selection: parameters, seed and the exact source digest.</summary>
public sealed class AuditSamplingRun
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid SelectionId { get; set; }
  public Guid ProcedureId { get; set; }
  public Guid ScheduleId { get; set; }
  public string Method { get; set; } = string.Empty;
  public decimal? Interval { get; set; }
  public decimal? KeyItemThreshold { get; set; }
  public int? SampleSize { get; set; }
  public int? Seed { get; set; }
  public int PopulationCount { get; set; }
  public decimal PopulationAbsoluteTotal { get; set; }
  public int SelectedCount { get; set; }
  public decimal SelectedAbsoluteTotal { get; set; }
  public decimal CoveragePercent { get; set; }
  public string SourceDigest { get; set; } = string.Empty;
  public string SelectionDigest { get; set; } = string.Empty;
  public string EngineVersion { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A procedure step's link to one exact received client upload (by its receipt digest).</summary>
public sealed class ProcedureEvidenceLink
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ProcedureId { get; set; }
  public Guid PbcUploadIntentId { get; set; }
  public Guid PbcRequestId { get; set; }
  public string FileName { get; set; } = string.Empty;
  public string ContentSha256 { get; set; } = string.Empty;
  public string? Note { get; set; }
  public Guid LinkedByUserId { get; set; }
  public DateTimeOffset LinkedAt { get; set; }
}

/// <summary>A physical paper file in a hybrid engagement, e.g. file index X-1 held in Box 3.</summary>
public sealed class PhysicalEvidenceItem
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string FileIndex { get; set; } = string.Empty;
  public string BoxReference { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  /// <summary>Current location, changed only together with an appended movement.</summary>
  public string CurrentLocation { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PhysicalEvidenceMovement
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PhysicalEvidenceItemId { get; set; }
  public string? FromLocation { get; set; }
  public string ToLocation { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public Guid MovedByUserId { get; set; }
  public DateTimeOffset MovedAt { get; set; }
}

public sealed class ProcedurePhysicalLink
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ProcedureId { get; set; }
  public Guid PhysicalEvidenceItemId { get; set; }
  public Guid LinkedByUserId { get; set; }
  public DateTimeOffset LinkedAt { get; set; }
}

/// <summary>Records that a procedure was inserted ad hoc into an adopted programme, against the programme's baseline.</summary>
public sealed class AdHocProcedureInsertion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ProcedureId { get; set; }
  public Guid EngagementProgramId { get; set; }
  public Guid BaselineProgramVersionId { get; set; }
  public Guid? RiskId { get; set; }
  public string Reason { get; set; } = string.Empty;
  public Guid InsertedByUserId { get; set; }
  public DateTimeOffset InsertedAt { get; set; }
}

/// <summary>Each wording of an ad hoc step, so edits never overwrite what was inserted or reviewed.</summary>
public sealed class AdHocProcedureRevision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ProcedureId { get; set; }
  public int Revision { get; set; }
  public string Wording { get; set; } = string.Empty;
  public Guid EditedByUserId { get; set; }
  public DateTimeOffset EditedAt { get; set; }
}

/// <summary>Whether a confirmation is critical to the final report; append-only, the newest entry applies.</summary>
public sealed class ConfirmationCriticality
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ConfirmationCaseId { get; set; }
  public bool Critical { get; set; }
  public string Rationale { get; set; } = string.Empty;
  public Guid SetByUserId { get; set; }
  public DateTimeOffset SetAt { get; set; }
}
