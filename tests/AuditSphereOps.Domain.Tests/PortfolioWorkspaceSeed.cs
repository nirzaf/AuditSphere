using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Read-side synthetic records only; establishes no professional approval or live release acceptance.</summary>
internal static class PortfolioWorkspaceSeed
{
  internal static async Task PopulateAsync(AuditSphereDbContext db, PbcSeed.Fixture f, int count = 1)
  {
    var now = DateTimeOffset.UtcNow;
    db.EngagementHolds.Add(new EngagementHold { Id = Guid.NewGuid(), FirmId = f.FirmId,
      EngagementId = f.EngagementId, HoldKind = "Acceptance", Reason = "Synthetic portfolio read fixture", CreatedAt = now });
    for (var i = 0; i < count; i++)
    {
      var target = Guid.NewGuid(); var approval = Guid.NewGuid(); var candidate = Guid.NewGuid();
      var digest = new string('a', 64); var key = "portfolio-synthetic-" + candidate.ToString("N");
      db.Approvals.Add(new Approval { Id = approval, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, TargetKind = "WORKPAPER", TargetId = target,
        TargetRevision = 9007199254740993, InputGeneration = 1, PolicyGeneration = 1,
        ManifestDigest = digest, DecidedByUserId = f.Reviewer.Id, DecidedAt = now });
      db.ReleaseCandidates.Add(new ReleaseCandidate { Id = candidate, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, TargetId = target, TargetRevision = 9007199254740993, ApprovalId = approval,
        ManifestDigest = digest, Status = i == 0 ? "ISSUED" : "READY", CreatedAt = now.AddSeconds(i) });
      if (i == 0)
      {
        var checkpoint = Guid.NewGuid();
        db.ReleaseCheckpoints.Add(new ReleaseCheckpoint { Id = checkpoint, FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = f.EngagementId, ReleaseCandidateId = candidate, CandidateRevision = 1,
          ManifestDigest = digest, ReadBackDigest = digest, StoredReference = "synthetic-read-only",
          AuthorizedReleaseKey = key, VerifiedStatus = "VERIFIED", CreatedAt = now });
        db.Releases.Add(new Release { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = f.EngagementId, ReleaseCandidateId = candidate, PackageId = target, PackageRevision = 9007199254740993,
          CheckpointId = checkpoint, ManifestDigest = digest, AuthorizedReleaseKey = key,
          ReleasedByUserId = f.Reviewer.Id, ReleasedAt = now });
      }
    }
    foreach (var engagement in new Guid?[] { f.EngagementId, null })
      db.DurableOperations.Add(new DurableOperation { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = engagement, OperationKind = "SYNTHETIC_PORTFOLIO_READ", RequestDigest = new string('b', 64),
        IdempotencyKey = Guid.NewGuid().ToString("N"), RequestBytes = [1], ExpectedRevision = 1, CreatedAt = now, NextAttemptAt = now });
    await db.SaveChangesAsync();
  }
}
