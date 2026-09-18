using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Reviews;

public sealed record CreateApprovalRequest(
  string TargetKind,
  Guid TargetId,
  long ExpectedRevision,
  long ExpectedInputGeneration,
  long ExpectedPolicyGeneration,
  string ManifestDigest,
  string Decision = ApprovalStates.Approved);

public sealed record ApprovalApplicabilityResult(
  Guid ApprovalId,
  string Status,
  string Reason,
  long CurrentTargetRevision,
  long CurrentInputGeneration,
  long CurrentPolicyGeneration,
  string? FailureCode);

/// <summary>
/// Approval decisions are immutable historical facts. Applicability is a separately
/// updated projection evaluated against current target and guard generations.
/// </summary>
public static class ApprovalService
{
  private static readonly string[] ApprovalRoles = ["Reviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    CreateApprovalRequest request,
    CancellationToken ct = default)
  {
    var validation = Validate(request);
    if (validation is not null)
      return CommandResult<Guid>.Fail("approvals.invalid", validation);

    var target = await LoadWorkpaperAsync(db, actor.FirmId, request.TargetId, forUpdate: false, ct);
    if (target is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, target, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var firm = await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (firm is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var client = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id = {actor.FirmId} AND id = {target.ClientId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (client is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var current = await LoadWorkpaperAsync(db, actor.FirmId, request.TargetId, forUpdate: true, ct);
    if (current is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizeAsync(db, actor, current, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    if (current.Revision != request.ExpectedRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The approved target changed; reload the current revision.");
    if (client.InputGeneration != request.ExpectedInputGeneration ||
        firm.PolicyGeneration != request.ExpectedPolicyGeneration)
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Approval inputs or policy changed; reload the current candidate.");

    var approval = new Approval
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = current.ClientId,
      EngagementId = current.EngagementId, TargetKind = "WORKPAPER", TargetId = current.Id,
      TargetRevision = current.Revision, InputGeneration = client.InputGeneration,
      PolicyGeneration = firm.PolicyGeneration, ManifestDigest = request.ManifestDigest,
      Decision = request.Decision.Trim().ToUpperInvariant(), DecidedByUserId = actor.UserId,
      DecidedAt = DateTimeOffset.UtcNow
    };
    db.Approvals.Add(approval);
    db.ApprovalApplicabilities.Add(new ApprovalApplicability
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ApprovalId = approval.Id,
      Status = approval.Decision == ApprovalStates.Approved ? ApprovalStates.Current : ApprovalStates.Rejected,
      Reason = approval.Decision == ApprovalStates.Approved ? "Approval matches the captured target and generations." : "Approval decision was rejected.",
      CurrentTargetRevision = current.Revision, CurrentInputGeneration = client.InputGeneration,
      CurrentPolicyGeneration = firm.PolicyGeneration, EvaluatedAt = DateTimeOffset.UtcNow
    });
    try
    {
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail("approvals.conflict", "The approval identity changed; retry from the current candidate.");
    }
    return CommandResult<Guid>.Ok(approval.Id);
  }

  public static async Task<CommandResult<ApprovalApplicabilityResult>> EvaluateAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid approvalId,
    CancellationToken ct = default)
  {
    if (approvalId == Guid.Empty)
      return CommandResult<ApprovalApplicabilityResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == approvalId && x.FirmId == actor.FirmId, ct);
    if (approval is null)
      return CommandResult<ApprovalApplicabilityResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationAsync(db, actor, approval.ClientId, approval.EngagementId, ct);
    if (!auth.Succeeded)
      return CommandResult<ApprovalApplicabilityResult>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var firm = await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    var client = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id = {actor.FirmId} AND id = {approval.ClientId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    var current = await LoadWorkpaperAsync(db, actor.FirmId, approval.TargetId, forUpdate: true, ct);
    if (firm is null || client is null || current is null)
      return CommandResult<ApprovalApplicabilityResult>.Fail(ErrorCodes.GateBlocked, "Approval scope is unavailable.");
    auth = await AuthorizationAsync(db, actor, current.ClientId, current.EngagementId, ct);
    if (!auth.Succeeded)
      return CommandResult<ApprovalApplicabilityResult>.Fail(auth.ErrorCode!, auth.Message!);

    var (status, reason, failureCode) = approval.Decision != ApprovalStates.Approved
      ? (ApprovalStates.Rejected, "The historical approval decision was rejected.", ErrorCodes.GateBlocked)
      : current.Revision != approval.TargetRevision
        ? (ApprovalStates.Stale, "The approved target revision changed.", ErrorCodes.StaleRevision)
        : client.InputGeneration != approval.InputGeneration || firm.PolicyGeneration != approval.PolicyGeneration
          ? (ApprovalStates.Stale, "The client input or firm policy generation changed.", ErrorCodes.GenerationStale)
          : (ApprovalStates.Current, "Approval matches the current target and generations.", (string?)null);

    var projection = await db.ApprovalApplicabilities.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ApprovalId == approval.Id, ct);
    if (projection is null)
    {
      projection = new ApprovalApplicability { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ApprovalId = approval.Id };
      db.ApprovalApplicabilities.Add(projection);
    }
    projection.Status = status;
    projection.Reason = reason;
    projection.CurrentTargetRevision = current.Revision;
    projection.CurrentInputGeneration = client.InputGeneration;
    projection.CurrentPolicyGeneration = firm.PolicyGeneration;
    projection.EvaluatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ApprovalApplicabilityResult>.Ok(new ApprovalApplicabilityResult(
      approval.Id, status, reason, current.Revision, client.InputGeneration,
      firm.PolicyGeneration, failureCode));
  }

  public static async Task<CommandResult> RequireCurrentAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid approvalId,
    CancellationToken ct = default)
  {
    var result = await EvaluateAsync(db, actor, approvalId, ct);
    if (!result.Succeeded)
      return CommandResult.Fail(result.ErrorCode!, result.Message!);
    return result.Value!.Status == ApprovalStates.Current
      ? CommandResult.Ok()
      : CommandResult.Fail(result.Value.FailureCode ?? ErrorCodes.GateBlocked, result.Value.Reason);
  }

  private static async Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, Workpaper target, CancellationToken ct) =>
    await AuthorizationAsync(db, actor, target.ClientId, target.EngagementId, ct);

  private static Task<CommandResult> AuthorizationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, ApprovalRoles, InternalOnly: true), ct);

  private static Task<Workpaper?> LoadWorkpaperAsync(
    IAuditSphereDbContext db, Guid firmId, Guid id, bool forUpdate, CancellationToken ct) =>
    forUpdate
      ? db.Workpapers.FromSqlInterpolated($"SELECT * FROM workpapers WHERE id = {id} AND firm_id = {firmId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct)
      : db.Workpapers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == firmId, ct);

  private static string? Validate(CreateApprovalRequest request)
  {
    if (request.TargetId == Guid.Empty || !string.Equals(request.TargetKind.Trim(), "WORKPAPER", StringComparison.OrdinalIgnoreCase))
      return "Only a stored workpaper target is supported by this slice.";
    if (request.ExpectedRevision < 1 || request.ExpectedInputGeneration < 1 || request.ExpectedPolicyGeneration < 1)
      return "Approval revision and generations must be positive.";
    if (request.ManifestDigest.Length != 64 || request.ManifestDigest.Any(c => !Uri.IsHexDigit(c)) ||
        request.ManifestDigest != request.ManifestDigest.ToLowerInvariant())
      return "The approval manifest digest must be lowercase SHA-256 hex.";
    var decision = request.Decision.Trim().ToUpperInvariant();
    return decision is ApprovalStates.Approved or ApprovalStates.Rejected
      ? null : "Approval decision is invalid.";
  }
}
