using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record ReviewStaffCertificationRequest(Guid CertificationId, Guid RequestId, string SourceDigest,
  long ExpectedRevision, string Decision, string EvidenceReference, string Reason);

/// <summary>Retained review identity and source applicability, separate from qualification expiry.</summary>
public sealed record StaffCertificationReviewView(Guid Id, long Revision, string Decision, Guid ReviewerUserId,
  DateTimeOffset ReviewedAt, string EvidenceReference, string Reason, string SourceDigest);
/// <summary>Exact current source and latest administrative decision for a bounded review workspace.</summary>
public sealed record StaffCertificationReviewWorkspace(Guid CertificationId, Guid SubjectUserId, string Name,
  string? Issuer, DateOnly? ExpiresOn, Guid RecordedByUserId, DateTimeOffset RecordedAt, string SourceDigest,
  long Revision, StaffCertificationReviewView? LatestReview, bool VerifiedForCurrentSource, bool Expired, bool CanReview);

public static partial class ResourcePlanningService
{
  public static async Task<CommandResult<StaffCertificationReviewWorkspace>> GetCertificationReviewAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid certificationId, CancellationToken ct = default)
  {
    var readAuthority = new AuthorizationRequest(actor.FirmId, RequiredRoles: PlanningRoles, InternalOnly: true, RequireFirmWide: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, readAuthority, ct);
    if (!auth.Succeeded) return CommandResult<StaffCertificationReviewWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
    var source = await db.StaffCertifications.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == certificationId, ct);
    if (source is null) return CommandResult<StaffCertificationReviewWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var latest = await db.StaffCertificationReviews.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.CertificationId == source.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var admin = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true, RequireFirmWide: true), ct);
    var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, readAuthority, ct);
    if (!auth.Succeeded) return CommandResult<StaffCertificationReviewWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<StaffCertificationReviewWorkspace>.Ok(new(source.Id, source.UserId, source.Name, source.Issuer,
      source.ExpiresOn, source.RecordedByUserId, source.RecordedAt, StaffCertificationEvidence.SourceDigest(source),
      latest?.Revision ?? 0, latest is null ? null : new(latest.Id, latest.Revision, latest.Decision, latest.ReviewedByUserId,
        latest.ReviewedAt, latest.EvidenceReference, latest.Reason, latest.SourceDigest),
      StaffCertificationEvidence.IsApplicable(source, latest?.Evidence(), today), source.ExpiresOn < today,
      admin.Succeeded && actor.UserId != source.UserId && actor.UserId != source.RecordedByUserId));
  }
  /// <summary>Publishes an independent administrative decision; never assigns a role or rewrites past staffing.</summary>
  public static async Task<CommandResult<Guid>> ReviewCertificationAsync(IAuditSphereDbContext db, ActorContext actor,
    ReviewStaffCertificationRequest request, CancellationToken ct = default)
  {
    var authority = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true, RequireFirmWide: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, authority, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var decision = (request.Decision ?? string.Empty).Trim().ToUpperInvariant();
    var reference = (request.EvidenceReference ?? string.Empty).Trim();
    var reason = (request.Reason ?? string.Empty).Trim();
    if (request.RequestId == Guid.Empty || request.CertificationId == Guid.Empty || request.ExpectedRevision < 0 ||
        request.SourceDigest is not { Length: 64 } || decision is not ("VERIFIED" or "REJECTED" or "REVOKED") ||
        reason.Length is < 1 or > 4000 || reference.Length > 1000 || decision == "VERIFIED" && reference.Length == 0)
      return CommandResult<Guid>.Fail("resource.review-invalid", "An exact source, decision, reason and verification evidence are required.");
    var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
      Version = 1, actor.FirmId, actor.UserId, request.CertificationId, request.SourceDigest,
      request.ExpectedRevision, Decision = decision, EvidenceReference = reference, Reason = reason
    }))).ToLowerInvariant();
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    // Serialize review heads and retries with staffing/role publication before reading the exact source.
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, authority, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var prior = await db.StaffCertificationReviews.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.RequestId == request.RequestId, ct);
    if (prior is not null)
      return prior.RequestDigest == digest ? CommandResult<Guid>.Ok(prior.Id)
        : CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This request identity belongs to a different decision.");
    var source = await db.StaffCertifications.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == request.CertificationId, ct);
    if (source is null || source.UserId == actor.UserId || source.RecordedByUserId == actor.UserId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "An independent authorized reviewer is required.");
    var subject = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={source.UserId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (subject is null || subject.UserKind != "Staff" || decision == "VERIFIED" && subject.Disabled)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Current staff identity is required.");
    var revision = await db.StaffCertificationReviews.Where(x => x.FirmId == actor.FirmId && x.CertificationId == source.Id)
      .Select(x => (long?)x.Revision).MaxAsync(ct) ?? 0;
    if (revision != request.ExpectedRevision || StaffCertificationEvidence.SourceDigest(source) != request.SourceDigest ||
        decision == "REVOKED" && revision == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Reload the certification source and current review before deciding.");
    var now = DateTimeOffset.UtcNow;
    if (source.RecordedAt > now || source.RecordedAt == default || decision == "VERIFIED" && source.ExpiresOn < DateOnly.FromDateTime(now.UtcDateTime))
      return CommandResult<Guid>.Fail("resource.review-invalid", "Future, missing or expired source evidence cannot be verified.");
    var review = new StaffCertificationReview
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, CertificationId = source.Id, Revision = revision + 1,
      RequestId = request.RequestId, RequestDigest = digest, SubjectUserId = source.UserId,
      SourceName = source.Name, SourceIssuer = source.Issuer, SourceExpiresOn = source.ExpiresOn,
      SourceRecordedAt = source.RecordedAt, SourceRecordedByUserId = source.RecordedByUserId, SourceDigest = request.SourceDigest,
      Decision = decision, ReviewedByUserId = actor.UserId, ReviewedAt = now, EvidenceReference = reference, Reason = reason
    };
    db.StaffCertificationReviews.Add(review);
    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(review.Id);
  }
}
