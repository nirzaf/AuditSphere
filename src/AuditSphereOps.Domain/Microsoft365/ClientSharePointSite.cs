namespace AuditSphereOps.Domain.Microsoft365;

/// <summary>One immutable requested location per client; remote and membership health are independent projections.</summary>
public sealed class ClientSharePointSite
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ConnectionRevisionId { get; set; }
  public Guid RequestedByUserId { get; set; }
  public string TenantId { get; set; } = string.Empty;
  public string RequestedUrl { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string OwnershipMarker { get; set; } = string.Empty;
  public string State { get; set; } = "REQUESTED";
  public string? SiteId { get; set; }
  public string? DriveId { get; set; }
  public string? RootItemId { get; set; }
  public string? StaffGroupId { get; set; }
  public string MembershipState { get; set; } = "PENDING";
  public string MemberObjectIdsJson { get; set; } = "[]";
  public string DesiredDigest { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public DateTimeOffset? CreationDispatchedAt { get; set; }
  public Guid? CreationOperationId { get; set; }
  public Guid? LastOperationId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? VerifiedAt { get; set; }
  public DateTimeOffset? LastMembershipSyncAt { get; set; }
}
