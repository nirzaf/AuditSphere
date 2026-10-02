using System.Text.Json;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Security;

/// <summary>Deterministic local access review fence; excludes the preview's transient proposed timestamp.</summary>
public static class RoleAssignmentReviewDigest
{
  public static string Compute(RoleAssignmentPreview review, RoleAssignmentRequest request) => Hashing.Sha256Hex(JsonSerializer.Serialize(new
  {
    review.UserId, review.UserKind,
    Reason = request.Reason?.Trim(), request.ReplacesGrantId, request.EffectiveFrom,
    Current = review.CurrentAccess.OrderBy(x => x.GrantId).ToArray(),
    Proposed = new { review.Proposed.Role, review.Proposed.ScopeKind, review.Proposed.ClientId, review.Proposed.EngagementId,
      review.Proposed.GroupId, review.Proposed.ExpiresAt, review.Proposed.GroupGrant },
    Added = review.AddedCapabilities.Order().ToArray(), Removed = review.RemovedCapabilities.Order().ToArray(),
    review.ScopeExpansion, review.ScopeReduction,
    Independence = review.IndependenceImpact.Order().ToArray(), Warnings = review.Warnings.Order().ToArray(), Blocks = review.BlockingReasons.Order().ToArray()
  }));
}
