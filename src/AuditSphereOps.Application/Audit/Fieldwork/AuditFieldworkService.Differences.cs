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
  public static async Task<CommandResult<DifferenceValue>> RecordDifferenceAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordDifferenceRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.AccountArea) || string.IsNullOrWhiteSpace(request.DifferenceType) ||
        string.IsNullOrWhiteSpace(request.Description) || request.Amount == 0 || !IsCurrency(request.Currency) ||
        request.MaterialityReference.Trim().Length > 1000 || request.QualitativeConcerns.Trim().Length > 4000)
      return Invalid<DifferenceValue>("An audit difference requires a signed non-zero amount, area, type, description and currency.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<DifferenceValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.ProcedureId is not null && !await IsApplicableProcedureAsync(db, actor.FirmId, auth.ClientId, request.EngagementId, request.ProcedureId.Value, ct))
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked, "The linked procedure is not applicable.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var difference = new AuditDifference
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ProcedureId = request.ProcedureId, AccountArea = request.AccountArea.Trim(), DifferenceType = request.DifferenceType.Trim(),
      Description = request.Description.Trim(), MaterialityReference = TrimOrNull(request.MaterialityReference),
      QualitativeConcerns = TrimOrNull(request.QualitativeConcerns), Amount = request.Amount, Currency = request.Currency.ToUpperInvariant(),
      InputGeneration = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditDifferences.Add(difference);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<DifferenceValue>.Ok(new(difference.Id, difference.Status));
  }

  public static async Task<CommandResult<DifferenceValue>> EvaluateDifferenceAsync(
    IAuditSphereDbContext db, ActorContext actor, EvaluateDifferenceRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Evaluation))
      return Invalid<DifferenceValue>("Difference evaluation requires a conclusion.");
    var existing = await db.AuditDifferences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.AuditDifferenceId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<DifferenceValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (existing!.InputGeneration != await CurrentGenerationAsync(db, existing.ClientId, existing.FirmId, ct))
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.GenerationStale, "The difference is based on stale inputs.");
    if (existing.CreatedByUserId == actor.UserId)
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot evaluate the same difference.");
    if (request.Corrected)
    {
      if (existing.ProposedJournalId is null || existing.ProposedJournalRevision is null ||
          existing.SourceReflectionReconciliationId is null || existing.VerifiedAdjustedSnapshotId is null)
        return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked,
          "A difference cannot be marked corrected without typed journal, source-reflection and adjusted-snapshot evidence.");
      if (existing.CorrectionState == AuditDifferenceCorrectionStates.Rejected)
        return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked, "A rejected correction cannot become verified reflected without a new reviewed difference.");
      if (!HasCurrentJournalImpact(existing))
        return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked, "The exact journal impact evidence is missing, stale or unsupported.");
      var journal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == existing.ProposedJournalId &&
        x.FirmId == existing.FirmId && x.ClientId == existing.ClientId && x.EngagementId == existing.EngagementId &&
        x.Revision == existing.ProposedJournalRevision && x.Status == "Posted", ct);
      var reflection = await db.JournalSourceReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == existing.SourceReflectionReconciliationId &&
        x.FirmId == existing.FirmId && x.ClientId == existing.ClientId && x.EngagementId == existing.EngagementId &&
        x.JournalRevision == existing.ProposedJournalRevision && x.State == ReflectionStates.Reflected, ct);
      var snapshot = await db.AdjustedTrialBalanceSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == existing.VerifiedAdjustedSnapshotId &&
        x.FirmId == existing.FirmId && x.ClientId == existing.ClientId && x.EngagementId == existing.EngagementId, ct);
      if (journal is null || reflection is null || snapshot is null || reflection.BaseDatasetId != journal.BaseDatasetId ||
          snapshot.BaseDatasetId != journal.BaseDatasetId || !string.Equals(reflection.LogicalJournalNumber, journal.JournalNumber, StringComparison.Ordinal))
        return CommandResult<DifferenceValue>.Fail(ErrorCodes.GateBlocked,
          "The correction evidence is stale or does not match the exact posted journal revision.");
    }
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditDifferences.SingleAsync(x => x.Id == existing.Id && x.FirmId == actor.FirmId, ct);
    live.Corrected = request.Corrected;
    live.ManagementResponse = TrimOrNull(request.ManagementResponse);
    live.CorrectionReference = TrimOrNull(request.CorrectionReference);
    live.Evaluation = request.Evaluation.Trim();
    live.Status = request.Corrected ? AuditDifferenceStatuses.VerifiedReflected : AuditDifferenceStatuses.Evaluated;
    if (request.Corrected)
      live.CorrectionState = AuditDifferenceCorrectionStates.VerifiedReflected;
    live.EvaluatedByUserId = actor.UserId;
    live.EvaluatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<DifferenceValue>.Ok(new(live.Id, live.Status));
  }

  public static async Task<CommandResult<IReadOnlyList<AuditDifferenceSummary>>> GetDifferenceSummariesAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizeEngagementAsync(db, actor, engagementId, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<AuditDifferenceSummary>>.Fail(auth.ErrorCode!, auth.Message!);
    var differences = await db.AuditDifferences.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId).ToListAsync(ct);
    var summaries = differences.GroupBy(x => x.Currency, StringComparer.OrdinalIgnoreCase)
      .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
      .Select(group =>
      {
        var unadjusted = group.Where(x => !x.Corrected);
        var corrected = group.Where(x => x.Corrected);
        return new AuditDifferenceSummary(
          group.Key.ToUpperInvariant(), group.Count(),
          MoneyPolicy.Normalize(group.Sum(x => Math.Abs(x.Amount))),
          MoneyPolicy.Normalize(group.Sum(x => x.Amount)),
          MoneyPolicy.Normalize(unadjusted.Sum(x => Math.Abs(x.Amount))),
          MoneyPolicy.Normalize(unadjusted.Sum(x => x.Amount)),
          MoneyPolicy.Normalize(corrected.Sum(x => Math.Abs(x.Amount))),
          MoneyPolicy.Normalize(corrected.Sum(x => x.Amount)));
      }).ToArray();
    return CommandResult<IReadOnlyList<AuditDifferenceSummary>>.Ok(summaries);
  }

  private static async Task<string?> CurrentDifferenceSnapshotAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, CancellationToken ct)
  {
    var materiality = await db.MaterialityAssessments.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (materiality is null) return null;
    var approval = await db.MaterialityApprovals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      x.MaterialityAssessmentId == materiality.Id, ct);
    if (approval is null) return null;
    var differences = await db.AuditDifferences.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && x.EngagementId == engagementId)
      .OrderBy(x => x.Id)
      .Select(x => new
      {
        x.Id, x.AccountArea, x.DifferenceType, x.Description, x.Amount, x.Currency, x.Corrected,
        x.ManagementResponse, x.CorrectionReference, x.MaterialityReference, x.QualitativeConcerns,
        x.CorrectionState, x.JournalImpactHash, x.Evaluation, x.Status, x.InputGeneration
      }).ToListAsync(ct);
    var snapshot = new
    {
      Version = "audit-difference-aggregate.v1",
      Materiality = new
      {
        materiality.Id, materiality.OverallMateriality, materiality.PerformanceMateriality,
        materiality.ClearlyTrivialThreshold, ApprovalId = approval.Id, approval.ApprovedByUserId, approval.ApprovedAt
      },
      Differences = differences
    };
    var json = JsonSerializer.Serialize(snapshot);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))) + ":" + json;
  }

  public static async Task<CommandResult<DifferenceValue>> SetDifferenceCorrectionStateAsync(
    IAuditSphereDbContext db, ActorContext actor, SetDifferenceCorrectionStateRequest request,
    CancellationToken ct = default)
  {
    var state = request.CorrectionState.Trim().ToUpperInvariant();
    if (state is not (AuditDifferenceCorrectionStates.Proposed or AuditDifferenceCorrectionStates.Agreed or
        AuditDifferenceCorrectionStates.Rejected or AuditDifferenceCorrectionStates.AppliedInReporting or
        AuditDifferenceCorrectionStates.ReportedPostedExternally))
      return Invalid<DifferenceValue>("The correction state is unsupported.");
    if (state == AuditDifferenceCorrectionStates.Rejected && string.IsNullOrWhiteSpace(request.Reason))
      return Invalid<DifferenceValue>("Rejecting a correction requires a professional reason.");
    var existing = await db.AuditDifferences.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.AuditDifferenceId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<DifferenceValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (existing is null)
      return Denied<DifferenceValue>();
    if (existing.CreatedByUserId == actor.UserId)
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot set the correction state.");
    if (existing.InputGeneration != await CurrentGenerationAsync(db, existing.ClientId, existing.FirmId, ct))
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.GenerationStale, "The difference is based on stale inputs.");
    if (existing.Corrected || existing.CorrectionState == AuditDifferenceCorrectionStates.VerifiedReflected ||
        !CanTransitionCorrectionState(existing.CorrectionState, state))
      return CommandResult<DifferenceValue>.Fail(ErrorCodes.ProtectedState, "The correction state cannot move from its current reviewed state.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditDifferences.SingleAsync(x => x.Id == existing.Id && x.FirmId == actor.FirmId, ct);
    live.CorrectionState = state;
    if (!string.IsNullOrWhiteSpace(request.Reason))
      live.Evaluation = request.Reason.Trim();
    if (state == AuditDifferenceCorrectionStates.Rejected)
    {
      live.Corrected = false;
      live.Status = AuditDifferenceStatuses.Evaluated;
    }
    live.EvaluatedByUserId = actor.UserId;
    live.EvaluatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<DifferenceValue>.Ok(new(live.Id, live.Status));
  }

  public static async Task<CommandResult> LinkDifferenceToJournalAsync(
    IAuditSphereDbContext db, ActorContext actor, LinkDifferenceToJournalRequest request,
    CancellationToken ct = default)
  {
    var state = request.CorrectionState.Trim().ToUpperInvariant();
    if (request.JournalRevision < 1 || request.SourceReflectionReconciliationId == Guid.Empty ||
        request.VerifiedAdjustedSnapshotId == Guid.Empty || state is not (
          AuditDifferenceCorrectionStates.Proposed or AuditDifferenceCorrectionStates.Agreed or
          AuditDifferenceCorrectionStates.AppliedInReporting or AuditDifferenceCorrectionStates.ReportedPostedExternally))
      return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "A correction link needs an exact revision, source reflection, snapshot and supported state.");
    var difference = await db.AuditDifferences.SingleOrDefaultAsync(x => x.Id == request.AuditDifferenceId &&
      x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, difference, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (difference is null)
      return Denied();
    if (!CanTransitionCorrectionState(difference.CorrectionState, state))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The correction state cannot move from its current reviewed state.");
    var journal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.JournalId &&
      x.FirmId == actor.FirmId && x.ClientId == difference!.ClientId && x.EngagementId == difference.EngagementId &&
      x.Revision == request.JournalRevision && x.Status != "Void", ct);
    var reflection = await db.JournalSourceReconciliations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.SourceReflectionReconciliationId && x.FirmId == actor.FirmId &&
      x.ClientId == difference!.ClientId && x.EngagementId == difference.EngagementId &&
      x.JournalRevision == request.JournalRevision, ct);
    var snapshot = await db.AdjustedTrialBalanceSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.VerifiedAdjustedSnapshotId && x.FirmId == actor.FirmId &&
      x.ClientId == difference!.ClientId && x.EngagementId == difference.EngagementId, ct);
    if (journal is null || reflection is null || snapshot is null || reflection.BaseDatasetId != journal.BaseDatasetId ||
        snapshot.BaseDatasetId != journal.BaseDatasetId ||
        !string.Equals(reflection.LogicalJournalNumber, journal.JournalNumber, StringComparison.Ordinal))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The journal, source reflection and adjusted snapshot are not the same exact source lineage.");
    var rawLines = await db.AdjustmentLines.AsNoTracking().Where(x => x.JournalId == journal.Id)
      .OrderBy(x => x.AccountCode).ThenBy(x => x.Id)
      .Select(x => new { x.AccountCode, x.Debit, x.Credit })
      .ToListAsync(ct);
    var lines = rawLines.Select(x => new JournalImpactLine(x.AccountCode, x.Debit, x.Credit,
      MoneyPolicy.Normalize(x.Debit - x.Credit), [])).ToList();
    if (lines.Count == 0)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The linked journal has no immutable lines.");

    MappingVersion? mapping = null;
    var mappingAllocations = new List<MappingAllocation>();
    var disclosureByDestination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (db is IClientAccountingDbContext accountingDb)
    {
      mapping = await accountingDb.MappingVersions.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ClientId == difference.ClientId && x.EngagementId == difference.EngagementId &&
        x.DatasetId == journal.BaseDatasetId && x.Status == AccountingPackageStates.MappingApproved)
        .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
      if (mapping is not null)
      {
        mappingAllocations = await accountingDb.MappingAllocations.AsNoTracking().Where(x =>
          x.FirmId == actor.FirmId && x.ClientId == difference.ClientId && x.EngagementId == difference.EngagementId &&
          x.MappingVersionId == mapping.Id).OrderBy(x => x.SourceAccountCode).ThenBy(x => x.DestinationCode).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(mapping.TaxonomyVersion))
        {
          var taxonomyId = await accountingDb.ReportingTaxonomyVersions.AsNoTracking().Where(x =>
            x.FirmId == actor.FirmId && x.Code == mapping.TaxonomyVersion && x.Status == AccountingWorkflowStates.Approved)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
          if (taxonomyId is not null)
            disclosureByDestination = await accountingDb.ReportingTaxonomyNodes.AsNoTracking().Where(x =>
              x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomyId.Value)
              .ToDictionaryAsync(x => x.Code, x => x.DisclosureArea, StringComparer.OrdinalIgnoreCase);
        }
      }
    }

    var classifiedLines = lines.Select(line =>
    {
      var signedAmount = line.SignedAmount;
      var allocations = mappingAllocations.Where(x => string.Equals(x.SourceAccountCode.Trim(), line.AccountCode.Trim(), StringComparison.OrdinalIgnoreCase))
        .Select(x =>
        {
          var section = x.StatementSection.Trim().ToUpperInvariant();
          var disclosure = disclosureByDestination.GetValueOrDefault(x.DestinationCode.Trim())?.Trim().ToUpperInvariant();
          var allocatedAmount = MoneyPolicy.Normalize(signedAmount * x.Fraction);
          return new JournalImpactAllocation(x.DestinationCode.Trim(), section, (x.AuditArea ?? string.Empty).Trim(),
            string.IsNullOrWhiteSpace(disclosure) ? "UNSPECIFIED" : disclosure, MoneyPolicy.Normalize(x.Fraction), allocatedAmount,
            IsProfitSection(section) ? allocatedAmount : 0m, IsEquitySection(section) ? allocatedAmount : 0m, true);
        }).ToArray();
      return allocations.Length == 0
        ? line with { Allocations = [new JournalImpactAllocation("UNMAPPED", "UNMAPPED", string.Empty, "UNMAPPED", 1m, signedAmount, 0m, 0m, false)] }
        : line with { Allocations = allocations };
    }).ToArray();
    var effects = classifiedLines.SelectMany(x => x.Allocations).ToArray();
    var classificationStatus = classifiedLines.All(x => x.Allocations.All(a => a.Mapped)) ? "MAPPED" : "PARTIAL_OR_UNMAPPED";
    var impactJson = JsonSerializer.Serialize(new
    {
      Schema = "journal-impact.v2", journal.Id, journal.JournalNumber, journal.Revision, journal.Purpose, journal.Currency,
      Mapping = mapping is null ? null : new { mapping.Id, mapping.Version, mapping.Generation, mapping.TaxonomyVersion },
      Classification = new
      {
        Status = classificationStatus,
        AccountEffects = classifiedLines.Select(x => new { x.AccountCode, Amount = x.SignedAmount }),
        StatementEffects = effects.GroupBy(x => x.StatementSection, StringComparer.OrdinalIgnoreCase)
          .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
          .Select(x => new { StatementSection = x.Key, Amount = MoneyPolicy.Normalize(x.Sum(y => y.SignedAmount)) }),
        ProfitEffect = MoneyPolicy.Normalize(effects.Sum(x => x.ProfitEffect)),
        EquityEffect = MoneyPolicy.Normalize(effects.Sum(x => x.EquityEffect)),
        DisclosureEffects = effects.GroupBy(x => x.DisclosureArea, StringComparer.OrdinalIgnoreCase)
          .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
          .Select(x => new { DisclosureArea = x.Key, Amount = MoneyPolicy.Normalize(x.Sum(y => y.SignedAmount)) })
      },
      Lines = classifiedLines
    });
    difference.ProposedJournalId = journal.Id;
    difference.ProposedJournalRevision = journal.Revision;
    difference.SourceReflectionReconciliationId = reflection.Id;
    difference.VerifiedAdjustedSnapshotId = snapshot.Id;
    difference.CorrectionState = state;
    difference.JournalImpactJson = impactJson;
    difference.JournalImpactHash = Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(impactJson));
    difference.CorrectionReference = $"journal:{journal.Id:D}:revision:{journal.Revision}";
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static bool CanTransitionCorrectionState(string? current, string requested)
  {
    var prior = current?.Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(prior))
      return requested is not AuditDifferenceCorrectionStates.VerifiedReflected;
    if (prior == requested)
      return true;
    return prior switch
    {
      AuditDifferenceCorrectionStates.Proposed => requested is AuditDifferenceCorrectionStates.Agreed or AuditDifferenceCorrectionStates.Rejected,
      AuditDifferenceCorrectionStates.Agreed => requested is AuditDifferenceCorrectionStates.AppliedInReporting or AuditDifferenceCorrectionStates.Rejected,
      AuditDifferenceCorrectionStates.AppliedInReporting => requested is AuditDifferenceCorrectionStates.ReportedPostedExternally or AuditDifferenceCorrectionStates.Rejected,
      _ => false
    };
  }

  private static bool HasCurrentJournalImpact(AuditDifference difference)
  {
    if (string.IsNullOrWhiteSpace(difference.JournalImpactJson) || string.IsNullOrWhiteSpace(difference.JournalImpactHash))
      return false;
    try
    {
      using var document = JsonDocument.Parse(difference.JournalImpactJson);
      return document.RootElement.TryGetProperty("Schema", out var schema) &&
        schema.GetString() == "journal-impact.v2" &&
        string.Equals(Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(difference.JournalImpactJson)),
          difference.JournalImpactHash, StringComparison.OrdinalIgnoreCase);
    }
    catch (JsonException)
    {
      return false;
    }
  }

  private sealed record JournalImpactLine(string AccountCode, decimal Debit, decimal Credit, decimal SignedAmount,
    IReadOnlyList<JournalImpactAllocation> Allocations);

  private sealed record JournalImpactAllocation(string DestinationCode, string StatementSection, string AuditArea,
    string DisclosureArea, decimal Fraction, decimal SignedAmount, decimal ProfitEffect, decimal EquityEffect, bool Mapped);
}
