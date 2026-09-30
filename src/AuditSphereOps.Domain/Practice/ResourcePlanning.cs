// Resource planning and engagement staffing (STE 2.2-01..03): four staffing levels mapped explicitly to
// authorization roles, staff profiles with skills, certifications, capacity and target utilization, availability,
// and a week-based allocation plan. Plans are editable; staffing assignments are revoked, never rewritten.
namespace AuditSphereOps.Domain.Practice;

public static class StaffingLevels
{
  public const string EngagementPartner = "ENGAGEMENT_PARTNER";
  public const string AuditManager = "AUDIT_MANAGER";
  public const string SeniorAuditor = "SENIOR_AUDITOR";
  public const string StaffAssociate = "STAFF_ASSOCIATE";

  public static readonly string[] All = [EngagementPartner, AuditManager, SeniorAuditor, StaffAssociate];

  /// <summary>Review hierarchy rank: a reviewer must rank above the preparer.</summary>
  public static int Rank(string level) => level switch
  {
    EngagementPartner => 4,
    AuditManager => 3,
    SeniorAuditor => 2,
    StaffAssociate => 1,
    _ => 0
  };

  /// <summary>The authorization role granted for the engagement at each staffing level.</summary>
  public static string AuthorizationRole(string level) => level switch
  {
    EngagementPartner => "Partner",
    AuditManager => "Manager",
    SeniorAuditor => "Senior",
    StaffAssociate => "Staff",
    _ => throw new ArgumentOutOfRangeException(nameof(level))
  };

  public static string Label(string level) => level switch
  {
    EngagementPartner => "Engagement Partner",
    AuditManager => "Audit Manager",
    SeniorAuditor => "Senior Auditor",
    StaffAssociate => "Staff Associate",
    _ => level
  };
}

public sealed class EngagementStaffAssignment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid UserId { get; set; }
  public string StaffingLevel { get; set; } = StaffingLevels.StaffAssociate;
  /// <summary>The engagement-scoped role grant this assignment created; revoked together with it.</summary>
  public Guid RoleGrantId { get; set; }
  public Guid AssignedByUserId { get; set; }
  public DateTimeOffset AssignedAt { get; set; }
  public DateTimeOffset? RevokedAt { get; set; }
  public Guid? RevokedByUserId { get; set; }
}

public sealed class StaffProfile
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid UserId { get; set; }
  public string Department { get; set; } = string.Empty;
  /// <summary>Comma-separated skill tags, normalized upper case.</summary>
  public string Skills { get; set; } = string.Empty;
  public int WeeklyCapacityMinutes { get; set; }
  public decimal TargetUtilizationPercent { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public Guid UpdatedByUserId { get; set; }
}

public sealed class StaffCertification
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid UserId { get; set; }
  public string Name { get; set; } = string.Empty;
  public string? Issuer { get; set; }
  public DateOnly? ExpiresOn { get; set; }
  public DateTimeOffset RecordedAt { get; set; }
  public Guid RecordedByUserId { get; set; }
}

public static class StaffAvailabilityKinds
{
  public const string Leave = "LEAVE";
  public const string Training = "TRAINING";
  public const string PublicHoliday = "PUBLIC_HOLIDAY";
}

public sealed class StaffAvailability
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid UserId { get; set; }
  public DateOnly StartDate { get; set; }
  public DateOnly EndDate { get; set; }
  public string Kind { get; set; } = StaffAvailabilityKinds.Leave;
  public int MinutesPerDay { get; set; }
  public DateTimeOffset RecordedAt { get; set; }
  public Guid RecordedByUserId { get; set; }
}

/// <summary>Planned minutes for one person on one engagement in one week (Monday start).</summary>
public sealed class StaffAllocation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid UserId { get; set; }
  public DateOnly WeekStart { get; set; }
  public int PlannedMinutes { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public Guid UpdatedByUserId { get; set; }
}

public static class BudgetPhases
{
  public const string Planning = "PLANNING";
  public const string Fieldwork = "FIELDWORK";
  public const string Completion = "COMPLETION";
  public const string Reporting = "REPORTING";
  /// <summary>Legacy lines and time recorded before phases existed; kept visible so totals still reconcile.</summary>
  public const string Unassigned = "UNASSIGNED";

  public static readonly string[] Assignable = [Planning, Fieldwork, Completion, Reporting];
}
