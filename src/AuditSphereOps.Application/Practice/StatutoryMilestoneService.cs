using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record SaveMilestonesRequest(
  Guid EngagementId,
  DateOnly StatutoryFilingCutoff,
  DateOnly? FieldworkStartDate = null,
  DateOnly? DraftReportDate = null,
  DateOnly? FinalReportDate = null,
  string? AdjustmentReason = null,
  string? WarningOverrideReason = null);

public sealed record PreviewMilestonesRequest(
  Guid EngagementId,
  DateOnly StatutoryFilingCutoff,
  DateOnly? FieldworkStartDate = null,
  DateOnly? DraftReportDate = null,
  DateOnly? FinalReportDate = null,
  string? AdjustmentReason = null,
  string? WarningOverrideReason = null);

public sealed record MilestonePlanView(
  Guid Id,
  Guid EngagementId,
  DateOnly PeriodEnd,
  DateOnly StatutoryFilingCutoff,
  DateOnly FieldworkStartDate,
  DateOnly DraftReportDate,
  DateOnly FinalReportDate,
  DateOnly ArchiveDeadlineDate,
  string? AdjustmentReason,
  string? WarningOverrideReason,
  IReadOnlyList<string> Warnings,
  long Revision,
  Guid ScheduledByUserId,
  DateTimeOffset ScheduledAt,
  bool CanConfigure,
  IReadOnlyList<string>? Notes = null);

public static class StatutoryMilestoneService
{
  private static readonly string[] ReadRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ConfigureRoles = ["Partner", "Manager", "SeniorManager", "Administrator"];

  public static async Task<CommandResult<MilestonePlanView?>> GetMilestonesAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid engagementId,
    CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == engagementId && e.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<MilestonePlanView?>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<MilestonePlanView?>.Fail(auth.ErrorCode!, auth.Message!);

    var canConfigure = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, ConfigureRoles, InternalOnly: true), ct)).Succeeded;

    var plan = await db.EngagementMilestonePlans.AsNoTracking()
      .Where(p => p.FirmId == actor.FirmId && p.EngagementId == engagementId)
      .OrderByDescending(p => p.Revision)
      .FirstOrDefaultAsync(ct);

    if (plan is null)
      return CommandResult<MilestonePlanView?>.Ok(null);

    IReadOnlyList<string> warnings = string.IsNullOrWhiteSpace(plan.WarningsJson)
      ? []
      : JsonSerializer.Deserialize<List<string>>(plan.WarningsJson) ?? [];

    return CommandResult<MilestonePlanView?>.Ok(new MilestonePlanView(
      plan.Id,
      plan.EngagementId,
      plan.PeriodEnd,
      plan.StatutoryFilingCutoff,
      plan.FieldworkStartDate,
      plan.DraftReportDate,
      plan.FinalReportDate,
      plan.ArchiveDeadlineDate,
      plan.AdjustmentReason,
      plan.WarningOverrideReason,
      warnings,
      plan.Revision,
      plan.ScheduledByUserId,
      plan.ScheduledAt,
      canConfigure,
      StatutoryMilestoneCalculator.CalendarNotes(plan.StatutoryFilingCutoff, plan.FieldworkStartDate, plan.DraftReportDate,
        plan.FinalReportDate, plan.ArchiveDeadlineDate)));
  }

  public static async Task<CommandResult<CalculatedMilestones>> CalculatePreviewAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    PreviewMilestonesRequest request,
    CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == request.EngagementId && e.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<CalculatedMilestones>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<CalculatedMilestones>.Fail(auth.ErrorCode!, auth.Message!);

    if (!DateOnly.TryParse(engagement.PeriodEnd, out var periodEnd))
      return CommandResult<CalculatedMilestones>.Fail("milestone.invalid-period", "The engagement period end is missing or malformed.");

    if (request.StatutoryFilingCutoff <= periodEnd)
      return CommandResult<CalculatedMilestones>.Fail("milestone.chronology-error",
        "Statutory filing cutoff must be strictly after the accounting period end date.");

    try
    {
      var standard = StatutoryMilestoneCalculator.CalculateDefault(periodEnd, request.StatutoryFilingCutoff);
      var candidateFieldwork = request.FieldworkStartDate ?? standard.FieldworkStartDate;
      var candidateDraft = request.DraftReportDate ?? standard.DraftReportDate;
      var candidateFinal = request.FinalReportDate ?? standard.FinalReportDate;
      var candidateArchive = candidateFinal.AddDays(StatutoryMilestoneCalculator.ArchiveDaysAfterFinalReport);
      var chronologyError = StatutoryMilestoneCalculator.ValidateChronology(
        periodEnd, request.StatutoryFilingCutoff, candidateFieldwork, candidateDraft, candidateFinal, candidateArchive);
      if (chronologyError != null)
        return CommandResult<CalculatedMilestones>.Fail("milestone.chronology-error", chronologyError);

      var calculated = StatutoryMilestoneCalculator.Evaluate(
        periodEnd,
        request.StatutoryFilingCutoff,
        request.FieldworkStartDate,
        request.DraftReportDate,
        request.FinalReportDate,
        request.AdjustmentReason,
        request.WarningOverrideReason);

      // An amendment is previewed against the current plan: changed dates are listed and open tasks are counted, but
      // nothing is moved. Saving adds a new plan revision; earlier revisions and completed tasks are kept.
      var current = await db.EngagementMilestonePlans.AsNoTracking()
        .Where(p => p.FirmId == actor.FirmId && p.EngagementId == engagement.Id)
        .OrderByDescending(p => p.Revision).FirstOrDefaultAsync(ct);
      if (current is not null)
      {
        var notes = new List<string>(calculated.Notes ?? []);
        void Changed(string label, DateOnly before, DateOnly after)
        {
          if (before != after) notes.Add($"{label} changes from {before:yyyy-MM-dd} to {after:yyyy-MM-dd} (plan revision {current.Revision}).");
        }
        Changed("The statutory filing cutoff", current.StatutoryFilingCutoff, calculated.StatutoryFilingCutoff);
        Changed("Fieldwork start", current.FieldworkStartDate, calculated.FieldworkStartDate);
        Changed("The draft report target", current.DraftReportDate, calculated.DraftReportDate);
        Changed("The final signed report", current.FinalReportDate, calculated.FinalReportDate);
        if (notes.Count > (calculated.Notes?.Count ?? 0))
        {
          var lateOpenTasks = await (from binding in db.WorkflowTaskBindings.AsNoTracking()
            join task in db.WorkTasks.AsNoTracking() on new { binding.FirmId, Id = binding.WorkTaskId } equals new { task.FirmId, task.Id }
            where binding.FirmId == actor.FirmId && binding.EngagementId == engagement.Id &&
              (task.Status == PracticeTimeStates.TaskOpen || task.Status == PracticeTimeStates.TaskInProgress) &&
              task.DueDate > calculated.FinalReportDate
            select task.Id).CountAsync(ct);
          notes.Add(lateOpenTasks == 0
            ? "No open task is due after the proposed final report date."
            : $"{lateOpenTasks} open task(s) are due after the proposed final report date. Their due dates are not moved; reschedule them if needed.");
        }
        calculated = calculated with { Notes = notes };
      }

      return CommandResult<CalculatedMilestones>.Ok(calculated);
    }
    catch (ArgumentException ex)
    {
      return CommandResult<CalculatedMilestones>.Fail("milestone.invalid", ex.Message);
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<CalculatedMilestones>.Fail("milestone.invalid", ex.Message);
    }
  }

  public static async Task<CommandResult<MilestonePlanView>> SaveMilestonesAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    SaveMilestonesRequest request,
    CancellationToken ct = default)
  {
    var snapshot = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == request.EngagementId && e.FirmId == actor.FirmId, ct);
    if (snapshot is null) return CommandResult<MilestonePlanView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, snapshot.PracticeClientId, snapshot.Id, ConfigureRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<MilestonePlanView>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await db.FirmSafetyStates.FromSqlInterpolated(
          $"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult<MilestonePlanView>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (await db.ClientSafetyStates.FromSqlInterpolated(
          $"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={snapshot.PracticeClientId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult<MilestonePlanView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var engagement = await db.Engagements.FromSqlInterpolated(
        $"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND id={request.EngagementId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (engagement is null || engagement.PracticeClientId != snapshot.PracticeClientId)
      return CommandResult<MilestonePlanView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, ConfigureRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<MilestonePlanView>.Fail(auth.ErrorCode!, auth.Message!);

    if (!DateOnly.TryParse(engagement.PeriodEnd, out var periodEnd))
      return CommandResult<MilestonePlanView>.Fail("milestone.invalid-period", "The engagement period end is missing or malformed.");

    if (request.StatutoryFilingCutoff <= periodEnd)
      return CommandResult<MilestonePlanView>.Fail("milestone.chronology-error",
        "Statutory filing cutoff must be strictly after the accounting period end date.");

    CalculatedMilestones calculated;
    try
    {
      var standard = StatutoryMilestoneCalculator.CalculateDefault(periodEnd, request.StatutoryFilingCutoff);
      var candidateFieldwork = request.FieldworkStartDate ?? standard.FieldworkStartDate;
      var candidateDraft = request.DraftReportDate ?? standard.DraftReportDate;
      var candidateFinal = request.FinalReportDate ?? standard.FinalReportDate;
      var candidateArchive = candidateFinal.AddDays(StatutoryMilestoneCalculator.ArchiveDaysAfterFinalReport);
      var chronologyError = StatutoryMilestoneCalculator.ValidateChronology(
        periodEnd, request.StatutoryFilingCutoff, candidateFieldwork, candidateDraft, candidateFinal, candidateArchive);
      if (chronologyError != null)
        return CommandResult<MilestonePlanView>.Fail("milestone.chronology-error", chronologyError);

      calculated = StatutoryMilestoneCalculator.Evaluate(
        periodEnd,
        request.StatutoryFilingCutoff,
        request.FieldworkStartDate,
        request.DraftReportDate,
        request.FinalReportDate,
        request.AdjustmentReason,
        request.WarningOverrideReason);
    }
    catch (ArgumentException ex)
    {
      return CommandResult<MilestonePlanView>.Fail("milestone.invalid", ex.Message);
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<MilestonePlanView>.Fail("milestone.invalid", ex.Message);
    }

    if (calculated.WarningOverrideRequired)
      return CommandResult<MilestonePlanView>.Fail("milestone.override-required",
        "Scheduling warnings require a documented manager or partner override reason before saving.");

    var latestRevision = await db.EngagementMilestonePlans.AsNoTracking()
      .Where(p => p.FirmId == actor.FirmId && p.EngagementId == engagement.Id)
      .MaxAsync(p => (long?)p.Revision, ct) ?? 0;

    var now = DateTimeOffset.UtcNow;
    var plan = new EngagementMilestonePlan
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      ClientId = engagement.PracticeClientId,
      EngagementId = engagement.Id,
      PeriodEnd = periodEnd,
      StatutoryFilingCutoff = calculated.StatutoryFilingCutoff,
      FieldworkStartDate = calculated.FieldworkStartDate,
      DraftReportDate = calculated.DraftReportDate,
      FinalReportDate = calculated.FinalReportDate,
      ArchiveDeadlineDate = calculated.ArchiveDeadlineDate,
      AdjustmentReason = calculated.HasAdjustments ? request.AdjustmentReason?.Trim() : null,
      WarningOverrideReason = calculated.Warnings.Count > 0 ? request.WarningOverrideReason?.Trim() : null,
      WarningsJson = JsonSerializer.Serialize(calculated.Warnings),
      Revision = latestRevision + 1,
      ScheduledByUserId = actor.UserId,
      ScheduledAt = now
    };

    db.EngagementMilestonePlans.Add(plan);
    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);

    return CommandResult<MilestonePlanView>.Ok(new MilestonePlanView(
      plan.Id,
      plan.EngagementId,
      plan.PeriodEnd,
      plan.StatutoryFilingCutoff,
      plan.FieldworkStartDate,
      plan.DraftReportDate,
      plan.FinalReportDate,
      plan.ArchiveDeadlineDate,
      plan.AdjustmentReason,
      plan.WarningOverrideReason,
      calculated.Warnings,
      plan.Revision,
      plan.ScheduledByUserId,
      plan.ScheduledAt,
      CanConfigure: true));
  }
}
