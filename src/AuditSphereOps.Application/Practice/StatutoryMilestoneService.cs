using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
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
  bool CanConfigure);

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
      canConfigure));
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

    var chronologyError = StatutoryMilestoneCalculator.ValidateChronology(
      periodEnd,
      request.StatutoryFilingCutoff,
      request.FieldworkStartDate ?? periodEnd.AddDays(7),
      request.DraftReportDate ?? request.StatutoryFilingCutoff.AddDays(-28),
      request.FinalReportDate ?? request.StatutoryFilingCutoff.AddDays(-15),
      (request.FinalReportDate ?? request.StatutoryFilingCutoff.AddDays(-15)).AddDays(StatutoryMilestoneCalculator.ArchiveDaysAfterFinalReport));

    if (chronologyError != null)
      return CommandResult<CalculatedMilestones>.Fail("milestone.chronology-error", chronologyError);

    try
    {
      var calculated = StatutoryMilestoneCalculator.Evaluate(
        periodEnd,
        request.StatutoryFilingCutoff,
        request.FieldworkStartDate,
        request.DraftReportDate,
        request.FinalReportDate,
        request.AdjustmentReason,
        request.WarningOverrideReason);

      return CommandResult<CalculatedMilestones>.Ok(calculated);
    }
    catch (Exception ex)
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
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == request.EngagementId && e.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<MilestonePlanView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, ConfigureRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<MilestonePlanView>.Fail(auth.ErrorCode!, auth.Message!);

    if (!DateOnly.TryParse(engagement.PeriodEnd, out var periodEnd))
      return CommandResult<MilestonePlanView>.Fail("milestone.invalid-period", "The engagement period end is missing or malformed.");

    var standard = StatutoryMilestoneCalculator.CalculateDefault(periodEnd, request.StatutoryFilingCutoff);
    var candidateFieldwork = request.FieldworkStartDate ?? standard.FieldworkStartDate;
    var candidateDraft = request.DraftReportDate ?? standard.DraftReportDate;
    var candidateFinal = request.FinalReportDate ?? standard.FinalReportDate;
    var candidateArchive = candidateFinal.AddDays(StatutoryMilestoneCalculator.ArchiveDaysAfterFinalReport);

    var chronologyError = StatutoryMilestoneCalculator.ValidateChronology(
      periodEnd,
      request.StatutoryFilingCutoff,
      candidateFieldwork,
      candidateDraft,
      candidateFinal,
      candidateArchive);

    if (chronologyError != null)
      return CommandResult<MilestonePlanView>.Fail("milestone.chronology-error", chronologyError);

    CalculatedMilestones calculated;
    try
    {
      calculated = StatutoryMilestoneCalculator.Evaluate(
        periodEnd,
        request.StatutoryFilingCutoff,
        request.FieldworkStartDate,
        request.DraftReportDate,
        request.FinalReportDate,
        request.AdjustmentReason,
        request.WarningOverrideReason);
    }
    catch (Exception ex)
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
