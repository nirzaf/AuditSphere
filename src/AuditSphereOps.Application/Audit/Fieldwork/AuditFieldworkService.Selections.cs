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
  public static async Task<CommandResult<SelectionValue>> CreateSelectionAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateSelectionRequest request, CancellationToken ct = default)
  {
    if (request.Items is null || request.Items.Count == 0 || request.ScheduleId is null && request.PopulationVersionId is null ||
        string.IsNullOrWhiteSpace(request.Method) || string.IsNullOrWhiteSpace(request.Rationale))
      return Invalid<SelectionValue>("A selection requires an approved source population, method, rationale and items.");
    if (request.Items.Select(x => x.StableRowId).Distinct(StringComparer.Ordinal).Count() != request.Items.Count ||
        request.Items.Any(x => string.IsNullOrWhiteSpace(x.StableRowId) || string.IsNullOrWhiteSpace(x.InclusionReason) || !IsCurrency(x.Currency)))
      return Invalid<SelectionValue>("Selection items must have unique stable IDs, signed currency values and inclusion reasons.");

    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<SelectionValue>.Fail(auth.ErrorCode!, auth.Message!);
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ProcedureId && x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId, ct);
    if (procedure is null)
      return Denied<SelectionValue>();
    if (procedure.ApplicabilityStatus != AuditApplicabilityStatuses.Applicable)
      return CommandResult<SelectionValue>.Fail(ErrorCodes.GateBlocked, "Selections require an applicable procedure.");
    if (request.ScheduleId is not null)
    {
      var schedule = await db.AuditSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == request.ScheduleId && x.FirmId == actor.FirmId && x.ClientId == auth.ClientId &&
        x.EngagementId == request.EngagementId, ct);
      if (schedule is null || schedule.Status != AuditScheduleStatuses.Approved)
        return CommandResult<SelectionValue>.Fail(ErrorCodes.GateBlocked, "The source schedule is not approved.");
    }
    else
    {
      var population = await db.PopulationVersions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == request.PopulationVersionId && x.FirmId == actor.FirmId && x.ClientId == auth.ClientId &&
        x.EngagementId == request.EngagementId, ct);
      if (population is null || population.Status != PopulationStatuses.Approved)
        return CommandResult<SelectionValue>.Fail(ErrorCodes.GateBlocked, "The source population is not approved.");
    }
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var generation = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    if (await db.AuditSelections.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
        x.ProcedureId == request.ProcedureId && x.ScheduleId == request.ScheduleId && x.PopulationVersionId == request.PopulationVersionId &&
        x.InputGeneration == generation, ct))
      return CommandResult<SelectionValue>.Fail(ErrorCodes.IdempotencyConflict, "A selection already exists for this procedure and source generation.");

    var rows = request.ScheduleId is null ? [] : await db.AuditScheduleRows.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId &&
                  x.ScheduleId == request.ScheduleId && request.Items.Select(i => i.StableRowId).Contains(x.StableRowId))
      .ToListAsync(ct);
    if (request.ScheduleId is not null && rows.Count != request.Items.Count)
      return Invalid<SelectionValue>("Every selected schedule row must exist in the approved source version.");
    var byStable = rows.ToDictionary(x => x.StableRowId, StringComparer.Ordinal);
    var sourceCurrencies = request.Items.Select(x => x.Currency.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
    if (sourceCurrencies.Length != 1)
      return Invalid<SelectionValue>("A selection cannot sum unrelated currencies.");

    var selection = new AuditSelection
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ScheduleId = request.ScheduleId, PopulationVersionId = request.PopulationVersionId, ProcedureId = request.ProcedureId,
      Method = request.Method.Trim(), Rationale = request.Rationale.Trim(), SelectedCount = request.Items.Count,
      SelectedSignedTotal = request.Items.Sum(x => byStable.TryGetValue(x.StableRowId, out var row) ? row.SignedAmount : x.SignedAmount),
      Status = AuditSelectionStatuses.Submitted, InputGeneration = generation, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditSelections.Add(selection);
    db.AuditSelectionItems.AddRange(request.Items.Select(item => new AuditSelectionItem
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      SelectionId = selection.Id, ScheduleRowId = item.ScheduleRowId ?? (byStable.TryGetValue(item.StableRowId, out var row) ? row.Id : null),
      StableRowId = item.StableRowId.Trim(), SignedAmount = byStable.TryGetValue(item.StableRowId, out var source) ? source.SignedAmount : item.SignedAmount,
      Currency = (byStable.TryGetValue(item.StableRowId, out var sourceCurrency) ? sourceCurrency.Currency : item.Currency).ToUpperInvariant(),
      InclusionReason = item.InclusionReason.Trim(), CreatedAt = selection.CreatedAt
    }));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<SelectionValue>.Ok(new(selection.Id, selection.SelectedCount, selection.SelectedSignedTotal, selection.Status));
  }

  public static async Task<CommandResult<SelectionValue>> ReviewSelectionAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewSelectionRequest request, CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditSelectionStatuses.Reviewed or AuditSelectionStatuses.ChangesRequired) ||
        decision == AuditSelectionStatuses.ChangesRequired && string.IsNullOrWhiteSpace(request.Comment))
      return Invalid<SelectionValue>("Selection review requires a valid decision and a comment for changes.");
    var selection = await db.AuditSelections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SelectionId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, selection, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<SelectionValue>.Fail(auth.ErrorCode!, auth.Message!);
    var live = await db.AuditSelections.SingleOrDefaultAsync(x => x.Id == request.SelectionId && x.FirmId == actor.FirmId, ct);
    if (live is null)
      return Denied<SelectionValue>();
    if (live.CreatedByUserId == actor.UserId)
      return CommandResult<SelectionValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same selection.");
    if (live.InputGeneration != await CurrentGenerationAsync(db, live.ClientId, live.FirmId, ct))
      return CommandResult<SelectionValue>.Fail(ErrorCodes.GenerationStale, "The selected population changed; reselect the items.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    live.Status = decision;
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<SelectionValue>.Ok(new(live.Id, live.SelectedCount, live.SelectedSignedTotal, live.Status));
  }
}
