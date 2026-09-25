using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class AccountingAnalysisService
{
  public static async Task<CommandResult<Guid>> LinkAccountingEvidenceToProcedureAsync(
    IClientAccountingDbContext db, ActorContext actor, LinkAccountingEvidenceRequest request,
    CancellationToken ct = default)
  {
    var kind = request.Kind.Trim().ToUpperInvariant();
    if (request.EvidenceId == Guid.Empty || request.AuditProcedureResultId == Guid.Empty ||
        kind is not (AccountingEvidenceKinds.Ecl or AccountingEvidenceKinds.Inventory or AccountingEvidenceKinds.Specialist or
          AccountingEvidenceKinds.Analytical or AccountingEvidenceKinds.JournalRisk))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The accounting evidence link target is invalid.");

    var result = await db.AuditProcedureResults.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.AuditProcedureResultId && x.FirmId == actor.FirmId, ct);
    if (result is null || result.WorkpaperId is null ||
        result.Status is not (AuditProcedureResultStatuses.Submitted or AuditProcedureResultStatuses.Reviewed))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Accounting evidence must link to a submitted audit procedure result with a workpaper.");

    Guid clientId;
    Guid engagementId;
    switch (kind)
    {
      case AccountingEvidenceKinds.Ecl:
        var ecl = await db.EclAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (ecl is null)
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        clientId = ecl.ClientId;
        engagementId = ecl.EngagementId;
        break;
      case AccountingEvidenceKinds.Inventory:
        var inventory = await db.InventoryValuationAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (inventory is null)
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        clientId = inventory.ClientId;
        engagementId = inventory.EngagementId;
        break;
      case AccountingEvidenceKinds.Specialist:
        var specialist = await db.SpecialistAccountingSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (specialist is null)
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        clientId = specialist.ClientId;
        engagementId = specialist.EngagementId;
        break;
      case AccountingEvidenceKinds.Analytical:
        var analytical = await db.AnalyticalReviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (analytical is null)
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        clientId = analytical.ClientId;
        engagementId = analytical.EngagementId;
        break;
      default:
        var risk = await db.JournalRiskFlags.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (risk is null)
          return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        clientId = risk.ClientId;
        engagementId = risk.EngagementId;
        break;
    }

    if (result.ClientId != clientId || result.EngagementId != engagementId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The audit result and accounting evidence are outside the same engagement scope.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var existing = await db.AccountingEvidenceAuditLinks.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      x.EvidenceKind == kind && x.EvidenceId == request.EvidenceId &&
      x.AuditProcedureResultId == request.AuditProcedureResultId, ct);
    if (existing is not null)
      return CommandResult<Guid>.Ok(existing.Id);

    var link = new AccountingEvidenceAuditLink
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, EngagementId = engagementId,
      EvidenceKind = kind, EvidenceId = request.EvidenceId, AuditProcedureResultId = request.AuditProcedureResultId,
      LinkedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AccountingEvidenceAuditLinks.Add(link);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(link.Id);
  }

  public static async Task<CommandResult> ReviewAccountingEvidenceAsync(
    IClientAccountingDbContext db, ActorContext actor, ReviewAccountingEvidenceRequest request,
    CancellationToken ct = default)
  {
    var kind = request.Kind.Trim().ToUpperInvariant();
    var decision = request.Decision.Trim().ToUpperInvariant();
    var reviewerDecision = decision is AccountingEvidenceReviewDecisions.Approved or
      AccountingEvidenceReviewDecisions.ChangesRequired or AccountingEvidenceReviewDecisions.Rejected;
    var riskDecision = decision is AccountingEvidenceReviewDecisions.Cleared or
      AccountingEvidenceReviewDecisions.Escalated or AccountingEvidenceReviewDecisions.NotAnIssue;
    if (request.EvidenceId == Guid.Empty || (request.Conclusion?.Trim().Length ?? 0) > 4000 ||
        (request.CorroborationReference?.Trim().Length ?? 0) > 2000 ||
        (kind is not (AccountingEvidenceKinds.Ecl or AccountingEvidenceKinds.Inventory or AccountingEvidenceKinds.Specialist or
          AccountingEvidenceKinds.Analytical or AccountingEvidenceKinds.JournalRisk)) ||
        (kind == AccountingEvidenceKinds.JournalRisk ? !riskDecision : !reviewerDecision))
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The accounting evidence review target or decision is invalid.");

    Guid clientId;
    Guid engagementId;
    Guid createdByUserId;
    Guid? reconciliationId = null;
    string? recordedSourceHash = null;
    long? recordedInputGeneration = null;
    Action apply;
    switch (kind)
    {
      case AccountingEvidenceKinds.Ecl:
        var ecl = await db.EclAssessments.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (ecl is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && ecl.MethodologyVersion.Length == 0)
          return CommandResult.Fail(ErrorCodes.GateBlocked, "An ECL review needs a methodology version.");
        clientId = ecl.ClientId; engagementId = ecl.EngagementId; createdByUserId = ecl.CreatedByUserId;
        reconciliationId = ecl.ReconciliationId; recordedSourceHash = ecl.ReconciliationSourceHash;
        recordedInputGeneration = ecl.InputGeneration;
        apply = () => { ecl.Status = decision; ecl.ReviewedByUserId = actor.UserId; ecl.ReviewedAt = DateTimeOffset.UtcNow; };
        break;
      case AccountingEvidenceKinds.Inventory:
        var inventory = await db.InventoryValuationAssessments.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (inventory is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && inventory.MethodologyVersion.Length == 0)
          return CommandResult.Fail(ErrorCodes.GateBlocked, "An inventory review needs a methodology version.");
        clientId = inventory.ClientId; engagementId = inventory.EngagementId; createdByUserId = inventory.CreatedByUserId;
        reconciliationId = inventory.ReconciliationId; recordedSourceHash = inventory.ReconciliationSourceHash;
        recordedInputGeneration = inventory.InputGeneration;
        apply = () => { inventory.Status = decision; inventory.ReviewedByUserId = actor.UserId; inventory.ReviewedAt = DateTimeOffset.UtcNow; };
        break;
      case AccountingEvidenceKinds.Specialist:
        var specialist = await db.SpecialistAccountingSchedules.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (specialist is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && specialist.EvidenceReference.Length == 0)
          return CommandResult.Fail(ErrorCodes.GateBlocked, "A specialist schedule review needs evidence.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && specialist.Area == "ASSETS" &&
            (string.IsNullOrWhiteSpace(specialist.DepreciationMethod) || specialist.UsefulLifeMonths is not > 0))
          return CommandResult.Fail(ErrorCodes.GateBlocked, "An asset schedule review needs a depreciation method and positive useful life.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && specialist.Area == "FORECAST" &&
            string.IsNullOrWhiteSpace(request.Conclusion))
          return CommandResult.Fail(ErrorCodes.GateBlocked, "A going-concern forecast review needs an auditor conclusion.");
        clientId = specialist.ClientId; engagementId = specialist.EngagementId; createdByUserId = specialist.CreatedByUserId;
        recordedInputGeneration = specialist.InputGeneration;
        apply = () =>
        {
          specialist.Status = decision;
          specialist.ReviewedByUserId = actor.UserId;
          specialist.ReviewedAt = DateTimeOffset.UtcNow;
          if (!string.IsNullOrWhiteSpace(request.Conclusion))
            specialist.ReviewConclusion = request.Conclusion.Trim();
        };
        break;
      case AccountingEvidenceKinds.Analytical:
        var analytical = await db.AnalyticalReviews.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (analytical is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (decision == AccountingEvidenceReviewDecisions.Approved && analytical.Ratio is null)
          return CommandResult.Fail(ErrorCodes.GateBlocked, "Insufficient analytical data cannot be approved.");
        clientId = analytical.ClientId; engagementId = analytical.EngagementId; createdByUserId = analytical.CreatedByUserId;
        recordedInputGeneration = analytical.InputGeneration;
        apply = () =>
        {
          analytical.Status = decision;
          analytical.ReviewedByUserId = actor.UserId;
          analytical.ReviewedAt = DateTimeOffset.UtcNow;
          if (!string.IsNullOrWhiteSpace(request.Conclusion))
            analytical.ReviewConclusion = request.Conclusion.Trim();
        };
        break;
      default:
        var risk = await db.JournalRiskFlags.SingleOrDefaultAsync(x => x.Id == request.EvidenceId && x.FirmId == actor.FirmId, ct);
        if (risk is null)
          return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (string.IsNullOrWhiteSpace(request.Disposition))
          return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "A journal-risk review needs a recorded disposition.");
        clientId = risk.ClientId; engagementId = risk.EngagementId; createdByUserId = risk.CreatedByUserId ?? Guid.Empty;
        apply = () =>
        {
          risk.Status = decision;
          risk.Disposition = request.Disposition.Trim();
          if (!string.IsNullOrWhiteSpace(request.Conclusion))
            risk.ManagementExplanation = request.Conclusion.Trim();
          if (!string.IsNullOrWhiteSpace(request.CorroborationReference))
            risk.CorroborationReference = request.CorroborationReference.Trim();
          risk.ReviewedByUserId = actor.UserId;
          risk.ReviewedAt = DateTimeOffset.UtcNow;
        };
        break;
    }

    if (reconciliationId is { } sourceReconciliationId)
    {
      var reconciliation = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == sourceReconciliationId && x.FirmId == actor.FirmId && x.ClientId == clientId &&
        x.EngagementId == engagementId, ct);
      var currentGeneration = await db.ClientSafetyStates.AsNoTracking().Where(x =>
        x.Id == clientId && x.FirmId == actor.FirmId).Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
      if (reconciliation is null || currentGeneration is null ||
          reconciliation.Status is not ("RECONCILED" or "APPROVED") ||
          !string.Equals(reconciliation.SourceHash, recordedSourceHash, StringComparison.OrdinalIgnoreCase) ||
          currentGeneration.Value != recordedInputGeneration)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The accounting evidence source or client input generation changed; prepare new evidence.");
    }
    else if (recordedInputGeneration is { } recordedGeneration)
    {
      var currentGeneration = await db.ClientSafetyStates.AsNoTracking().Where(x =>
        x.Id == clientId && x.FirmId == actor.FirmId).Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
      if (currentGeneration is null || currentGeneration.Value != recordedGeneration)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The client input generation changed; prepare new evidence.");
    }

    if (decision == AccountingEvidenceReviewDecisions.Approved && kind == AccountingEvidenceKinds.Analytical &&
        string.IsNullOrWhiteSpace(request.Conclusion))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "An analytical review approval needs a conclusion tied to its replay snapshot.");

    if (decision == AccountingEvidenceReviewDecisions.Approved && kind != AccountingEvidenceKinds.JournalRisk)
    {
      var linkedReviewedResult = await db.AccountingEvidenceAuditLinks.AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId &&
        x.EvidenceKind == kind && x.EvidenceId == request.EvidenceId &&
        db.AuditProcedureResults.Any(result => result.FirmId == actor.FirmId &&
          result.ClientId == clientId && result.EngagementId == engagementId &&
          result.Id == x.AuditProcedureResultId && result.Status == AuditProcedureResultStatuses.Reviewed), ct);
      if (!linkedReviewedResult)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Accounting evidence approval requires a reviewed audit procedure result link.");
    }

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, ReviewerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    if (createdByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The evidence preparer cannot review the same evidence.");
    apply();
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static Task<bool> HasProposedAdjustmentAsync(
    IClientAccountingDbContext db, AccountingReconciliation reconciliation, Guid? proposedJournalId, CancellationToken ct)
  {
    if (proposedJournalId is null)
      return Task.FromResult(true);
    return db.AdjustmentJournals.AsNoTracking().AnyAsync(x =>
      x.Id == proposedJournalId.Value && x.FirmId == reconciliation.FirmId &&
      x.ClientId == reconciliation.ClientId && x.EngagementId == reconciliation.EngagementId &&
      x.Status != "Void" && (reconciliation.TrialBalanceDatasetId == null || x.BaseDatasetId == reconciliation.TrialBalanceDatasetId), ct);
  }

  private static bool IsSha256(string value) => value.Trim().Length == 64 && value.Trim().All(c =>
    c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
