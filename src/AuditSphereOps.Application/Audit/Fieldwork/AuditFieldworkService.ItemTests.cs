using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditFieldworkService
{
  public static async Task<CommandResult<ItemTestValue>> RecordItemTestAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordItemTestRequest request, CancellationToken ct = default)
  {
    var item = await db.AuditSelectionItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SelectionItemId && x.FirmId == actor.FirmId, ct);
    if (item is null)
      return Denied<ItemTestValue>();
    var auth = await AuthorizeEntityAsync(db, actor, item, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ItemTestValue>.Fail(auth.ErrorCode!, auth.Message!);
    var result = request.Result.Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(request.WorkPerformed) || request.EvidenceReferences is null ||
        result is not (AuditItemTestResults.Pending or AuditItemTestResults.Pass or AuditItemTestResults.Exception or AuditItemTestResults.Limitation) ||
        result is AuditItemTestResults.Pass or AuditItemTestResults.Exception && request.EvidenceReferences.Count == 0 ||
        result is AuditItemTestResults.Exception or AuditItemTestResults.Limitation && string.IsNullOrWhiteSpace(request.FollowUp))
      return Invalid<ItemTestValue>("Item tests require work and a bounded result; pass/exception outcomes need evidence, and exceptions or limitations need follow-up.");
    var selection = await db.AuditSelections.AsNoTracking().SingleAsync(x => x.Id == item.SelectionId && x.FirmId == actor.FirmId, ct);
    if (selection.Status != AuditSelectionStatuses.Reviewed)
      return CommandResult<ItemTestValue>.Fail(ErrorCodes.GateBlocked, "The selection must be reviewed before item testing.");
    if (selection.InputGeneration != await CurrentGenerationAsync(db, selection.ClientId, selection.FirmId, ct))
      return CommandResult<ItemTestValue>.Fail(ErrorCodes.GenerationStale, "The selected population changed; item testing is stale.");
    var generation = await CurrentGenerationAsync(db, selection.ClientId, selection.FirmId, ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Professional mutations of a frozen audit baseline are refused under the same transaction boundary
    // as the write (STE-REM-09).
    var writable = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(db, actor, item.EngagementId, "record item test", ct);
    if (!writable.Succeeded)
    {
      await tx.CommitAsync(ct); // persist the recorded refused attempt before refusing
      return CommandResult<ItemTestValue>.Fail(writable.ErrorCode!, writable.Message!);
    }
    // Sampled-item execution is a substantive test route: it requires the same current approved
    // planning basis as procedure results (STE-REM-04), fenced inside the execution transaction.
    var readiness = await ProcedureRiskBandEvaluator.ReadinessBlockerAsync(db, item.FirmId, item.EngagementId, ct);
    if (readiness is not null)
      return CommandResult<ItemTestValue>.Fail(ErrorCodes.GateBlocked, readiness);
    var revision = (await db.AuditItemTests.AsNoTracking().Where(x => x.SelectionItemId == item.Id).MaxAsync(x => (long?)x.Revision, ct) ?? 0) + 1;
    var test = new AuditItemTest
    {
      Id = Guid.CreateVersion7(), FirmId = item.FirmId, ClientId = item.ClientId, EngagementId = item.EngagementId,
      SelectionId = item.SelectionId, SelectionItemId = item.Id, ProcedureId = selection.ProcedureId, Revision = revision,
      WorkPerformed = request.WorkPerformed.Trim(), EvidenceReferencesJson = JsonSerializer.Serialize(request.EvidenceReferences),
      Result = result, ExceptionAmount = request.ExceptionAmount, ContradictoryEvidence = TrimOrNull(request.ContradictoryEvidence),
      FollowUp = TrimOrNull(request.FollowUp), InputGeneration = generation, TestedByUserId = actor.UserId, TestedAt = DateTimeOffset.UtcNow
    };
    db.AuditItemTests.Add(test);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ItemTestValue>.Ok(new(test.Id, test.Revision, test.Result));
  }

  public static async Task<CommandResult> ReviewItemTestAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewItemTestRequest request, CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditItemTestReviewDecisions.Reviewed or AuditItemTestReviewDecisions.ChangesRequired) ||
        decision == AuditItemTestReviewDecisions.ChangesRequired && string.IsNullOrWhiteSpace(request.Comment))
      return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "Item-test review requires a valid decision and comment for changes.");
    var test = await db.AuditItemTests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.AuditItemTestId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, test, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (test!.TestedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same item test.");
    var selection = await db.AuditSelections.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == test.SelectionId && x.FirmId == actor.FirmId, ct);
    if (selection is null || selection.Status != AuditSelectionStatuses.Reviewed)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The selection must remain reviewed before item-test review.");
    var latestRevision = await db.AuditItemTests.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.SelectionItemId == test.SelectionItemId)
      .MaxAsync(x => (long?)x.Revision, ct);
    if (latestRevision != test.Revision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Only the current item-test revision can be reviewed.");
    if (test.InputGeneration != await CurrentGenerationAsync(db, test.ClientId, test.FirmId, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The item-test inputs changed; record a current revision.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var writable = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(db, actor, test.EngagementId, "review item test", ct);
    if (!writable.Succeeded)
    {
      await tx.CommitAsync(ct); // persist the recorded refused attempt before refusing
      return CommandResult.Fail(writable.ErrorCode!, writable.Message!);
    }
    // Item-test review is substantive too: review against the current approved planning basis only (STE-REM-04).
    var readiness = await ProcedureRiskBandEvaluator.ReadinessBlockerAsync(db, test.FirmId, test.EngagementId, ct);
    if (readiness is not null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, readiness);
    db.AuditItemTestReviews.Add(new AuditItemTestReview
    {
      Id = Guid.CreateVersion7(), FirmId = test.FirmId, ClientId = test.ClientId, EngagementId = test.EngagementId,
      SelectionItemId = test.SelectionItemId, AuditItemTestId = test.Id, TestRevision = test.Revision,
      Decision = decision, Comment = TrimOrNull(request.Comment), ReviewerUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
