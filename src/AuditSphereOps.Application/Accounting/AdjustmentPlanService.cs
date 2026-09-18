// Source bridge and adjustment plans (§17.4): the reviewer verifies, per base, whether
// each approved journal revision is already reflected in the replacement source.
// Plans snapshot those decisions; only NOT_REFLECTED journals are applied, REFLECTED
// contributes zero, and UNKNOWN/PARTIALLY_REFLECTED — or a decision changed after
// planning — blocks finalization instead of guessing a remainder.
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static class SourceReconciliationService
{
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Partner", "Manager"];

  public static async Task<CommandResult> ResolveAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid baseDatasetId,
    string logicalJournalNumber,
    long journalRevision,
    string state,
    string evidence,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(logicalJournalNumber) || logicalJournalNumber.Trim().Length > 32)
      return CommandResult.Fail("reconciliation.rejected", "Logical journal number is required (max 32).");
    logicalJournalNumber = logicalJournalNumber.Trim();
    if (state is not (ReflectionStates.NotReflected or ReflectionStates.Reflected
        or ReflectionStates.PartiallyReflected or ReflectionStates.NotApplicable))
      return CommandResult.Fail("reconciliation.rejected", "Unknown reflection state.");
    evidence = (evidence ?? string.Empty).Trim();
    if ((state is ReflectionStates.Reflected or ReflectionStates.PartiallyReflected) && evidence.Length == 0)
      return CommandResult.Fail("reconciliation.rejected",
        "Reflection needs source posting identifiers or a line-level bridge — never an equal total alone.");
    if (evidence.Length > 2000)
      return CommandResult.Fail("reconciliation.rejected", "Evidence is bounded to 2000 characters.");

    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == baseDatasetId, ct);
    if (dataset is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(dataset.FirmId, dataset.ClientId, dataset.EngagementId, ReviewerRoles), ct);
    if (!auth.Succeeded)
      return CommandResult.Fail(auth.ErrorCode!, auth.Message!);

    var journalOk = await db.AdjustmentJournals.AsNoTracking().AnyAsync(j =>
      j.FirmId == dataset.FirmId && j.EngagementId == dataset.EngagementId &&
      j.JournalNumber == logicalJournalNumber && j.Revision == journalRevision &&
      j.Status == "Posted", ct);
    if (!journalOk)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Only a posted journal revision can be reconciled.");

    var existing = await db.JournalSourceReconciliations.SingleOrDefaultAsync(r =>
      r.FirmId == dataset.FirmId && r.EngagementId == dataset.EngagementId &&
      r.BaseDatasetId == baseDatasetId && r.LogicalJournalNumber == logicalJournalNumber, ct);
    if (existing is null)
    {
      db.JournalSourceReconciliations.Add(new JournalSourceReconciliation
      {
        Id = Guid.CreateVersion7(), FirmId = dataset.FirmId, ClientId = dataset.ClientId,
        EngagementId = dataset.EngagementId, BaseDatasetId = baseDatasetId,
        LogicalJournalNumber = logicalJournalNumber, JournalRevision = journalRevision,
        State = state, Evidence = evidence, ReviewedByUserId = actor.UserId,
        ReviewedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
    }
    else
    {
      if (existing.JournalRevision != journalRevision)
        return CommandResult.Fail(ErrorCodes.StaleRevision,
          "This base already reconciles a different revision; plan a replacement instead of rewriting.");
      existing.State = state;
      existing.Evidence = evidence;
      existing.ReviewedByUserId = actor.UserId;
      existing.ReviewedAt = DateTimeOffset.UtcNow;
    }
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}

public sealed record PlanLineInput(string LogicalJournalNumber, long JournalRevision, string Layer = "REPORTING");

public sealed record FinalizedPlan(
  Guid PlanId, IReadOnlyDictionary<string, decimal> Balances,
  decimal TotalDebits, decimal TotalCreditsAbs, int AppliedJournalCount, string ResultHash);

public static class AdjustmentPlanService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "Staff", "Partner", "Manager"];

  public static async Task<CommandResult<Guid>> CreatePlanAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid baseDatasetId,
    IReadOnlyList<PlanLineInput> lines,
    CancellationToken ct = default)
  {
    if (lines.Count == 0)
      return CommandResult<Guid>.Fail("plan.rejected", "A plan needs at least one journal line.");
    var seen = new HashSet<(string Logical, string Layer)>();
    foreach (var line in lines)
    {
      if (string.IsNullOrWhiteSpace(line.LogicalJournalNumber) || line.LogicalJournalNumber.Trim().Length > 32)
        return CommandResult<Guid>.Fail("plan.rejected", "Logical journal number is required (max 32).");
      if (line.JournalRevision < 1)
        return CommandResult<Guid>.Fail("plan.rejected", "Journal revision must be at least 1.");
      if (string.IsNullOrWhiteSpace(line.Layer) || line.Layer.Trim().Length > 32)
        return CommandResult<Guid>.Fail("plan.rejected", "Layer is required (max 32).");
      if (!seen.Add((line.LogicalJournalNumber.Trim(), line.Layer.Trim().ToUpperInvariant())))
        return CommandResult<Guid>.Fail("plan.rejected",
          $"Logical journal {line.LogicalJournalNumber} appears twice: one operative revision per purpose/layer.");
    }

    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == baseDatasetId, ct);
    if (dataset is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (dataset.ValidationStatus != "Accepted")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Plans require a validated base dataset.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(dataset.FirmId, dataset.ClientId, dataset.EngagementId, PreparerRoles), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    foreach (var line in lines)
    {
      var posted = await db.AdjustmentJournals.AsNoTracking().AnyAsync(j =>
        j.FirmId == dataset.FirmId && j.EngagementId == dataset.EngagementId &&
        j.JournalNumber == line.LogicalJournalNumber.Trim() && j.Revision == line.JournalRevision &&
        j.Status == "Posted", ct);
      if (!posted)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          $"Journal {line.LogicalJournalNumber} revision {line.JournalRevision} is not posted.");
    }

    var reflections = await db.JournalSourceReconciliations.AsNoTracking()
      .Where(r => r.FirmId == dataset.FirmId && r.EngagementId == dataset.EngagementId &&
        r.BaseDatasetId == baseDatasetId).ToListAsync(ct);
    var plan = new AdjustmentPlan
    {
      Id = Guid.CreateVersion7(), FirmId = dataset.FirmId, ClientId = dataset.ClientId,
      EngagementId = dataset.EngagementId, BaseDatasetId = baseDatasetId,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AdjustmentPlans.Add(plan);
    foreach (var line in lines)
    {
      var current = reflections.SingleOrDefault(r =>
        r.LogicalJournalNumber == line.LogicalJournalNumber.Trim() &&
        r.JournalRevision == line.JournalRevision);
      db.AdjustmentPlanLines.Add(new AdjustmentPlanLine
      {
        Id = Guid.CreateVersion7(), PlanId = plan.Id,
        LogicalJournalNumber = line.LogicalJournalNumber.Trim(),
        JournalRevision = line.JournalRevision,
        Layer = line.Layer.Trim().ToUpperInvariant(),
        ReflectionState = current?.State ?? ReflectionStates.Unknown
      });
    }
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "Duplicate plan line identity.");
    }
    return CommandResult<Guid>.Ok(plan.Id);
  }

  public static async Task<CommandResult<FinalizedPlan>> FinalizeAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid planId,
    CancellationToken ct = default)
  {
    var plan = await db.AdjustmentPlans.SingleOrDefaultAsync(p => p.Id == planId, ct);
    if (plan is null)
      return CommandResult<FinalizedPlan>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(plan.FirmId, plan.ClientId, plan.EngagementId, PreparerRoles), ct);
    if (!auth.Succeeded)
      return CommandResult<FinalizedPlan>.Fail(auth.ErrorCode!, auth.Message!);
    if (plan.Status != "Draft")
      return CommandResult<FinalizedPlan>.Fail(ErrorCodes.ProtectedState, "Only a draft plan can be finalized.");

    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == plan.BaseDatasetId, ct);
    if (dataset is null || dataset.ValidationStatus != "Accepted")
      return CommandResult<FinalizedPlan>.Fail(ErrorCodes.GateBlocked, "The base dataset is not validated.");

    var lines = await db.AdjustmentPlanLines.AsNoTracking()
      .Where(l => l.PlanId == plan.Id).ToListAsync(ct);
    if (lines.Count == 0)
      return CommandResult<FinalizedPlan>.Fail("plan.rejected", "A plan needs at least one journal line.");

    // Stale-plan guard: the live decision must still match the snapshot.
    var live = await db.JournalSourceReconciliations.AsNoTracking()
      .Where(r => r.FirmId == plan.FirmId && r.EngagementId == plan.EngagementId &&
        r.BaseDatasetId == plan.BaseDatasetId).ToListAsync(ct);
    foreach (var line in lines)
    {
      var current = live.SingleOrDefault(r =>
        r.LogicalJournalNumber == line.LogicalJournalNumber &&
        r.JournalRevision == line.JournalRevision)?.State ?? ReflectionStates.Unknown;
      if (current != line.ReflectionState)
        return CommandResult<FinalizedPlan>.Fail(ErrorCodes.StaleRevision,
          $"Reflection changed for {line.LogicalJournalNumber}; re-plan instead of reusing a stale decision.");
      if (current is ReflectionStates.Unknown or ReflectionStates.PartiallyReflected)
        return CommandResult<FinalizedPlan>.Fail(ErrorCodes.GateBlocked,
          $"Reflection for {line.LogicalJournalNumber} is {current}; a reviewer must resolve it first.");
    }

    var rows = await db.TrialBalanceRows.AsNoTracking()
      .Where(r => r.DatasetId == plan.BaseDatasetId).ToListAsync(ct);
    var balances = rows.ToDictionary(r => r.AccountCode, r => r.Amount, StringComparer.Ordinal);
    var applied = 0;
    foreach (var line in lines.Where(l => l.ReflectionState == ReflectionStates.NotReflected))
    {
      var journal = await db.AdjustmentJournals.AsNoTracking().SingleAsync(j =>
        j.FirmId == plan.FirmId && j.EngagementId == plan.EngagementId &&
        j.JournalNumber == line.LogicalJournalNumber && j.Revision == line.JournalRevision, ct);
      var journalLines = await db.AdjustmentLines.AsNoTracking()
        .Where(l => l.JournalId == journal.Id).ToListAsync(ct);
      balances = new Dictionary<string, decimal>(TrialBalanceCalculator.ApplyJournal(
        balances, journalLines.Select(l => (l.AccountCode, l.Debit, l.Credit))), StringComparer.Ordinal);
      applied++;
    }

    decimal debits = 0, credits = 0;
    foreach (var amount in balances.Values)
    {
      if (amount >= 0) debits += amount; else credits += -amount;
    }
    debits = MoneyPolicy.Normalize(debits);
    credits = MoneyPolicy.Normalize(credits);
    var canonical = string.Join('\n', balances.OrderBy(kv => kv.Key, StringComparer.Ordinal)
      .Select(kv => $"{kv.Key}|{kv.Value:0.000000}"));
    var hash = Hashing.Sha256Hex(canonical);

    plan.Status = "Finalized";
    plan.ResultHash = hash;
    plan.AppliedDebits = debits;
    plan.AppliedCreditsAbs = credits;
    plan.AppliedJournalCount = applied;
    await db.SaveChangesAsync(ct);
    return CommandResult<FinalizedPlan>.Ok(new FinalizedPlan(
      plan.Id, balances, debits, credits, applied, hash));
  }
}
