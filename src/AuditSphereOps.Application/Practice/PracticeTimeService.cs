using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record CreateTaskRequest(
  string Title,
  Guid? ClientId = null,
  Guid? EngagementId = null,
  Guid? AssigneeUserId = null,
  Guid? ReportingPeriodId = null,
  DateOnly? DueDate = null);

public sealed record SaveTimeDraftRequest(
  Guid TaskId,
  DateOnly WorkDate,
  int StartMinute,
  int DurationMinutes,
  string Role,
  string Activity,
  string BillableClassification = PracticeTimeStates.Billable,
  string Narrative = "",
  string NarrativeVisibility = PracticeTimeStates.NarrativeInternal,
  string Currency = "QAR");

public sealed record CorrectTimeRequest(
  Guid TimeEntryId,
  DateOnly WorkDate,
  int StartMinute,
  int DurationMinutes,
  string Activity,
  string Narrative,
  string NarrativeVisibility = PracticeTimeStates.NarrativeInternal,
  string? Reason = null);

public sealed record RateCardDraftRequest(
  string Role,
  string Activity,
  string Currency,
  decimal RatePerHour,
  long? ExpectedVersion = null);

public sealed record BudgetLineRequest(string Role, string Activity, int ForecastMinutes);

public sealed record ReviseBudgetRequest(
  Guid EngagementId,
  string Currency,
  IReadOnlyList<BudgetLineRequest> Lines,
  long? ExpectedVersion = null);

public sealed record BudgetActualSummary(
  Guid BudgetId,
  long BudgetVersion,
  int ForecastMinutes,
  decimal ForecastCost,
  int ActualMinutes,
  decimal ActualCost);

/// <summary>
/// Guarded native practice time and budget commands. Approved facts are never edited:
/// a correction supersedes the old row and starts a new reviewable revision.
/// </summary>
public static class PracticeTimeService
{
  private static readonly string[] WorkRoles = ["Staff", "Manager", "Partner", "Administrator"];
  private static readonly string[] ApprovalRoles = ["Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateTaskAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateTaskRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Title))
      return CommandResult<Guid>.Fail("time.invalid", "Task title is required.");
    if (request.EngagementId.HasValue && !request.ClientId.HasValue)
      return CommandResult<Guid>.Fail("time.invalid", "An engagement task requires its client scope.");
    if (request.ReportingPeriodId.HasValue && !request.ClientId.HasValue)
      return CommandResult<Guid>.Fail("time.invalid", "A reporting-period task requires its client scope.");
    var accountingDb = request.ReportingPeriodId.HasValue ? db as IClientAccountingDbContext : null;
    if (request.ReportingPeriodId.HasValue && accountingDb is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Accounting period data is unavailable.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, WorkRoles,
        InternalOnly: true, RequireFirmWide: request.ClientId is null && request.EngagementId is null), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var engagement = request.EngagementId.HasValue
      ? await db.Engagements.AsNoTracking().SingleOrDefaultAsync(
        x => x.Id == request.EngagementId && x.FirmId == actor.FirmId, ct)
      : null;
    var period = request.ReportingPeriodId.HasValue
      ? await accountingDb!.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(
        x => x.Id == request.ReportingPeriodId && x.FirmId == actor.FirmId, ct)
      : null;
    if (request.EngagementId.HasValue &&
        (engagement is null || engagement.PracticeClientId != request.ClientId))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (request.ReportingPeriodId.HasValue &&
        (period is null || period.ClientId != request.ClientId))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await LockClientAsync(db, actor.FirmId, request.ClientId, ct);
    if (!guard.Succeeded) return CommandResult<Guid>.Fail(guard.ErrorCode!, guard.Message!);
    if (request.AssigneeUserId.HasValue)
    {
      var assignee = await ValidateAssigneeAsync(db, actor.FirmId, request.AssigneeUserId,
        request.ClientId, request.EngagementId, ct);
      if (assignee is not null) return CommandResult<Guid>.Fail(assignee.ErrorCode!, assignee.Message!);
    }

    var task = new WorkTask
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      ClientId = request.ClientId, EngagementId = request.EngagementId,
      ReportingPeriodId = request.ReportingPeriodId,
      Title = request.Title.Trim(), AssigneeUserId = request.AssigneeUserId,
      DueDate = request.DueDate,
      CreatedAt = DateTimeOffset.UtcNow,
      Status = request.AssigneeUserId.HasValue
        ? PracticeTimeStates.TaskInProgress : PracticeTimeStates.TaskOpen
    };
    db.WorkTasks.Add(task);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(task.Id);
  }

  public static Task<CommandResult> AssignTaskAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, Guid assigneeUserId,
    CancellationToken ct = default) => SetAssigneeAsync(db, actor, taskId, assigneeUserId, false, ct);

  public static Task<CommandResult> ReassignTaskAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, Guid assigneeUserId,
    CancellationToken ct = default) => SetAssigneeAsync(db, actor, taskId, assigneeUserId, true, ct);

  public static async Task<CommandResult> CompleteTaskAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var firm = await LockFirmAsync(db, actor.FirmId, ct);
    if (firm is null) return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var taskSnapshot = await db.WorkTasks.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == taskId && x.FirmId == actor.FirmId, ct);
    if (taskSnapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await LockClientAsync(db, actor.FirmId, taskSnapshot.ClientId, ct);
    if (!guard.Succeeded) return guard;
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, taskId, ct);
    if (task is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeTaskAsync(db, actor, task, WorkRoles, ct);
    if (!auth.Succeeded) return auth;
    if (task.Status == PracticeTimeStates.TaskCompleted) return CommandResult.Ok();
    if (task.Status == PracticeTimeStates.TaskCancelled)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "A cancelled task cannot be completed.");
    if (task.AssigneeUserId != actor.UserId &&
        !await IsElevatedAsync(db, actor, task.ClientId, task.EngagementId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Only the assignee or an authorized manager can complete this task.");
    task.Status = PracticeTimeStates.TaskCompleted;
    task.CompletedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> SaveTimeDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, SaveTimeDraftRequest request, CancellationToken ct = default)
  {
    var validation = ValidateTime(request);
    if (validation is not null) return CommandResult<Guid>.Fail("time.invalid", validation);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var firm = await LockFirmAsync(db, actor.FirmId, ct);
    if (firm is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var taskSnapshot = await db.WorkTasks.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == request.TaskId && x.FirmId == actor.FirmId, ct);
    if (taskSnapshot is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await LockClientAsync(db, actor.FirmId, taskSnapshot.ClientId, ct);
    if (!guard.Succeeded) return CommandResult<Guid>.Fail(guard.ErrorCode!, guard.Message!);
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, request.TaskId, ct);
    if (task is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeTaskAsync(db, actor, task, WorkRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (task.Status is PracticeTimeStates.TaskCompleted or PracticeTimeStates.TaskCancelled)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Time cannot be added to a closed task.");
    if (task.AssigneeUserId.HasValue && task.AssigneeUserId != actor.UserId &&
        !await IsElevatedAsync(db, actor, task.ClientId, task.EngagementId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Only the assignee or an authorized manager can record time.");
    if (await HasOverlapAsync(db, actor.FirmId, actor.UserId, request, null, ct))
      return CommandResult<Guid>.Fail("time.overlap", "The time entry overlaps another active entry.");

    var rate = await ResolveRateAsync(db, actor.FirmId, request.Role, request.Activity,
      request.Currency, request.BillableClassification, ct);
    if (!rate.Succeeded) return CommandResult<Guid>.Fail(rate.ErrorCode!, rate.Message!);
    var entry = new TimeEntry
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      ClientId = task.ClientId, EngagementId = task.EngagementId, TaskId = task.Id,
      UserId = actor.UserId, WorkDate = request.WorkDate, StartMinute = request.StartMinute,
      DurationMinutes = request.DurationMinutes, Role = request.Role.Trim(), Activity = request.Activity.Trim(),
      BillableClassification = request.BillableClassification.Trim().ToUpperInvariant(),
      Narrative = request.Narrative.Trim(), NarrativeVisibility = request.NarrativeVisibility.Trim().ToUpperInvariant(),
      RateCardVersionId = rate.Value?.Id, RatePerHour = rate.Value?.RatePerHour,
      Currency = string.Equals(request.BillableClassification.Trim(), PracticeTimeStates.Billable,
        StringComparison.OrdinalIgnoreCase) ? request.Currency.Trim().ToUpperInvariant() : null,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.TimeEntries.Add(entry);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(entry.Id);
  }

  public static async Task<CommandResult> SubmitTimeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid timeEntryId, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.TimeEntries.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == timeEntryId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var firm = await LockFirmAsync(db, actor.FirmId, ct);
    if (firm is null) return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, snapshot.ClientId, ct);
    if (!guard.Succeeded) return guard;
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, snapshot.TaskId, ct);
    if (task is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeTaskAsync(db, actor, task, WorkRoles, ct);
    if (!auth.Succeeded) return auth;
    var entry = await LoadTimeForUpdateAsync(db, actor.FirmId, timeEntryId, ct);
    if (entry is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (entry.Status == PracticeTimeStates.TimeSubmitted) return CommandResult.Ok();
    if (entry.Status != PracticeTimeStates.TimeDraft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft time entry can be submitted.");
    if (entry.UserId != actor.UserId &&
        !await IsElevatedAsync(db, actor, task.ClientId, task.EngagementId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Only the entry owner or an authorized manager can submit time.");
    entry.Status = PracticeTimeStates.TimeSubmitted;
    entry.SubmittedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ApproveTimeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid timeEntryId, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.TimeEntries.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == timeEntryId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var firm = await LockFirmAsync(db, actor.FirmId, ct);
    if (firm is null) return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, snapshot.ClientId, ct);
    if (!guard.Succeeded) return guard;
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, snapshot.TaskId, ct);
    if (task is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeTaskAsync(db, actor, task, ApprovalRoles, ct);
    if (!auth.Succeeded) return auth;
    var entry = await LoadTimeForUpdateAsync(db, actor.FirmId, timeEntryId, ct);
    if (entry is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (entry.Status == PracticeTimeStates.TimeApproved) return CommandResult.Ok();
    if (entry.Status != PracticeTimeStates.TimeSubmitted)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only submitted time can be approved.");
    if (entry.UserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Time preparers cannot approve their own entry.");
    entry.Status = PracticeTimeStates.TimeApproved;
    entry.ApprovedByUserId = actor.UserId;
    entry.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CorrectTimeAsync(
    IAuditSphereDbContext db, ActorContext actor, CorrectTimeRequest request, CancellationToken ct = default)
  {
    var validation = ValidateCorrection(request);
    if (validation is not null) return CommandResult<Guid>.Fail("time.invalid", validation);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.TimeEntries.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == request.TimeEntryId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var firm = await LockFirmAsync(db, actor.FirmId, ct);
    if (firm is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, snapshot.ClientId, ct);
    if (!guard.Succeeded) return CommandResult<Guid>.Fail(guard.ErrorCode!, guard.Message!);
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, snapshot.TaskId, ct);
    if (task is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeTaskAsync(db, actor, task, WorkRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (snapshot.UserId != actor.UserId &&
        !await IsElevatedAsync(db, actor, task.ClientId, task.EngagementId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Only the entry owner or an authorized manager can correct time.");
    var entry = await LoadTimeForUpdateAsync(db, actor.FirmId, request.TimeEntryId, ct);
    if (entry is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (entry.Status != PracticeTimeStates.TimeApproved)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Only approved time can be corrected.");
    var draft = new SaveTimeDraftRequest(entry.TaskId, request.WorkDate, request.StartMinute,
      request.DurationMinutes, entry.Role, request.Activity, entry.BillableClassification,
      request.Narrative, request.NarrativeVisibility, entry.Currency ?? "QAR");
    if (await HasOverlapAsync(db, actor.FirmId, entry.UserId, draft, entry.Id, ct))
      return CommandResult<Guid>.Fail("time.overlap", "The corrected entry overlaps another active entry.");
    entry.Status = PracticeTimeStates.TimeSuperseded;
    var correction = new TimeEntry
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = entry.ClientId,
      EngagementId = entry.EngagementId, TaskId = entry.TaskId, UserId = entry.UserId,
      WorkDate = request.WorkDate, StartMinute = request.StartMinute, DurationMinutes = request.DurationMinutes,
      Role = entry.Role, Activity = request.Activity.Trim(), BillableClassification = entry.BillableClassification,
      Narrative = request.Narrative.Trim(), NarrativeVisibility = request.NarrativeVisibility.Trim().ToUpperInvariant(),
      Currency = entry.Currency,
      Revision = entry.Revision + 1, SupersedesId = entry.Id,
      RateCardVersionId = entry.RateCardVersionId, RatePerHour = entry.RatePerHour,
      CorrectionReason = TrimOrNull(request.Reason), CreatedAt = DateTimeOffset.UtcNow
    };
    db.TimeEntries.Add(correction);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(correction.Id);
  }

  public static async Task<CommandResult<Guid>> ReviseRateCardAsync(
    IAuditSphereDbContext db, ActorContext actor, RateCardDraftRequest request, CancellationToken ct = default)
  {
    var validation = ValidateRateCard(request);
    if (validation is not null) return CommandResult<Guid>.Fail("time.invalid", validation);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ApprovalRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var role = request.Role.Trim();
    var activity = request.Activity.Trim();
    var currency = request.Currency.Trim().ToUpperInvariant();
    var previous = await db.RateCardVersions.Where(x => x.FirmId == actor.FirmId &&
      x.Role == role && x.Activity == activity && x.Currency == currency)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (request.ExpectedVersion.HasValue && request.ExpectedVersion != (previous?.Version ?? 0))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The rate card changed; reload the current version.");
    var card = new RateCardVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Version = (previous?.Version ?? 0) + 1,
      Role = role, Activity = activity, Currency = currency, RatePerHour = request.RatePerHour,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.RateCardVersions.Add(card);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(card.Id);
  }

  public static async Task<CommandResult> ApproveRateCardAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid rateCardId, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.RateCardVersions.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == rateCardId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ApprovalRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return auth;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var card = await db.RateCardVersions.FromSqlInterpolated(
      $"SELECT * FROM rate_card_versions WHERE id = {rateCardId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (card is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (card.Status == PracticeTimeStates.RateApproved) return CommandResult.Ok();
    if (card.Status != PracticeTimeStates.RateDraft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft rate card can be approved.");
    if (card.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Rate card preparers cannot approve their own version.");
    var previous = await db.RateCardVersions.Where(x => x.FirmId == actor.FirmId &&
      x.Role == card.Role && x.Activity == card.Activity && x.Currency == card.Currency &&
      x.Status == PracticeTimeStates.RateApproved && x.Id != card.Id).ToListAsync(ct);
    foreach (var old in previous) old.Status = PracticeTimeStates.RateSuperseded;
    card.Status = PracticeTimeStates.RateApproved;
    card.ApprovedByUserId = actor.UserId;
    card.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> ReviseBudgetAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviseBudgetRequest request, CancellationToken ct = default)
  {
    var validation = ValidateBudget(request);
    if (validation is not null) return CommandResult<Guid>.Fail("time.invalid", validation);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == request.EngagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, ApprovalRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, engagement.PracticeClientId, ct);
    if (!guard.Succeeded) return CommandResult<Guid>.Fail(guard.ErrorCode!, guard.Message!);
    var previous = await db.EngagementBudgets.Where(x => x.FirmId == actor.FirmId &&
      x.EngagementId == engagement.Id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (request.ExpectedVersion.HasValue && request.ExpectedVersion != (previous?.Version ?? 0))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The budget changed; reload the current version.");
    var resolved = new List<(BudgetLineRequest Request, RateCardVersion Card)>();
    foreach (var line in request.Lines)
    {
      var card = await db.RateCardVersions.Where(x => x.FirmId == actor.FirmId &&
        x.Role == line.Role.Trim() && x.Activity == line.Activity.Trim() &&
        x.Currency == request.Currency.Trim().ToUpperInvariant() && x.Status == PracticeTimeStates.RateApproved)
        .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
      if (card is null)
        return CommandResult<Guid>.Fail("time.rate-missing", "An approved rate card is required for every budget line.");
      resolved.Add((line, card));
    }
    if (previous is not null) previous.Status = PracticeTimeStates.BudgetSuperseded;
    var budget = new EngagementBudget
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, EngagementId = engagement.Id,
      Version = (previous?.Version ?? 0) + 1, Currency = request.Currency.Trim().ToUpperInvariant(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.EngagementBudgets.Add(budget);
    foreach (var (line, card) in resolved)
      db.BudgetLines.Add(new BudgetLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, EngagementBudgetId = budget.Id,
        RateCardVersionId = card.Id, Role = line.Role.Trim(), Activity = line.Activity.Trim(),
        ForecastMinutes = line.ForecastMinutes, RatePerHour = card.RatePerHour,
        ForecastCost = MoneyPolicy.Normalize(line.ForecastMinutes * card.RatePerHour / 60m)
      });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(budget.Id);
  }

  public static async Task<CommandResult> ApproveBudgetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid budgetId, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.EngagementBudgets.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == budgetId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == snapshot.EngagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, ApprovalRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, engagement.PracticeClientId, ct);
    if (!guard.Succeeded) return guard;
    var budget = await db.EngagementBudgets.FromSqlInterpolated(
      $"SELECT * FROM engagement_budgets WHERE id = {budgetId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (budget is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (budget.Status == PracticeTimeStates.BudgetApproved) return CommandResult.Ok();
    if (budget.Status != PracticeTimeStates.BudgetDraft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft budget can be approved.");
    if (budget.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Budget preparers cannot approve their own version.");
    budget.Status = PracticeTimeStates.BudgetApproved;
    budget.ApprovedByUserId = actor.UserId;
    budget.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<BudgetActualSummary>> GetBudgetActualAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid? budgetId = null,
    CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<BudgetActualSummary>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, WorkRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<BudgetActualSummary>.Fail(auth.ErrorCode!, auth.Message!);
    var budget = budgetId.HasValue
      ? await db.EngagementBudgets.AsNoTracking().SingleOrDefaultAsync(
        x => x.Id == budgetId && x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct)
      : await db.EngagementBudgets.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.EngagementId == engagementId && x.Status == PracticeTimeStates.BudgetApproved)
        .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (budget is null) return CommandResult<BudgetActualSummary>.Fail(ErrorCodes.GateBlocked, "No budget is available.");
    var lines = await db.BudgetLines.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.EngagementBudgetId == budget.Id).ToListAsync(ct);
    var entries = await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.EngagementId == engagementId && x.Status == PracticeTimeStates.TimeApproved).ToListAsync(ct);
    return CommandResult<BudgetActualSummary>.Ok(new BudgetActualSummary(
      budget.Id, budget.Version, lines.Sum(x => x.ForecastMinutes),
      MoneyPolicy.Normalize(lines.Sum(x => x.ForecastCost)), entries.Sum(x => x.DurationMinutes),
      MoneyPolicy.Normalize(entries.Sum(x => x.RatePerHour.HasValue
        ? x.DurationMinutes * x.RatePerHour.Value / 60m : 0m))));
  }

  private static async Task<CommandResult> SetAssigneeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, Guid assigneeUserId,
    bool reassign, CancellationToken ct)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var taskSnapshot = await db.WorkTasks.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == taskId && x.FirmId == actor.FirmId, ct);
    if (taskSnapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await LockClientAsync(db, actor.FirmId, taskSnapshot.ClientId, ct);
    if (!guard.Succeeded) return guard;
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, taskId, ct);
    if (task is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeTaskAsync(db, actor, task, ApprovalRoles, ct);
    if (!auth.Succeeded) return auth;
    if (task.Status is PracticeTimeStates.TaskCompleted or PracticeTimeStates.TaskCancelled)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "A closed task cannot be assigned.");
    if (!reassign && task.AssigneeUserId.HasValue)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The task is already assigned; use reassign.");
    if (reassign && task.AssigneeUserId == assigneeUserId) return CommandResult.Ok();
    var assignee = await ValidateAssigneeAsync(db, actor.FirmId, assigneeUserId,
      task.ClientId, task.EngagementId, ct);
    if (assignee is not null) return assignee;
    task.AssigneeUserId = assigneeUserId;
    if (task.Status == PracticeTimeStates.TaskOpen) task.Status = PracticeTimeStates.TaskInProgress;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult> AuthorizeTaskAsync(
    IAuditSphereDbContext db, ActorContext actor, WorkTask task, string[] roles, CancellationToken ct)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, task.ClientId, task.EngagementId, roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    return CommandResult.Ok();
  }

  private static async Task<CommandResult<RateCardVersion?>> ResolveRateAsync(
    IAuditSphereDbContext db, Guid firmId, string role, string activity, string currency,
    string billableClassification, CancellationToken ct)
  {
    if (!string.Equals(billableClassification.Trim(), PracticeTimeStates.Billable, StringComparison.OrdinalIgnoreCase))
      return CommandResult<RateCardVersion?>.Ok(null);
    var card = await db.RateCardVersions.Where(x => x.FirmId == firmId &&
      x.Role == role.Trim() && x.Activity == activity.Trim() &&
      x.Currency == currency.Trim().ToUpperInvariant() && x.Status == PracticeTimeStates.RateApproved)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    return card is null
      ? CommandResult<RateCardVersion?>.Fail("time.rate-missing", "An approved rate card is required for billable time.")
      : CommandResult<RateCardVersion?>.Ok(card);
  }

  private static async Task<bool> HasOverlapAsync(
    IAuditSphereDbContext db, Guid firmId, Guid userId, SaveTimeDraftRequest request,
    Guid? excludedId, CancellationToken ct)
  {
    var end = request.StartMinute + request.DurationMinutes;
    return await db.TimeEntries.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.UserId == userId &&
      x.WorkDate == request.WorkDate && x.Status != PracticeTimeStates.TimeSuperseded &&
      (!excludedId.HasValue || x.Id != excludedId.Value) &&
      x.StartMinute < end && x.StartMinute + x.DurationMinutes > request.StartMinute, ct);
  }

  private static async Task<CommandResult?> ValidateAssigneeAsync(
    IAuditSphereDbContext db, Guid firmId, Guid? userId, Guid? clientId, Guid? engagementId,
    CancellationToken ct)
  {
    if (!userId.HasValue) return null;
    var active = await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.FirmId == firmId &&
      !x.Disabled && x.UserKind == "Staff", ct);
    if (!active) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The assignee is not an active staff user in this firm.");
    var assigned = await db.RoleGrants.AsNoTracking().AnyAsync(x => x.FirmId == firmId &&
      x.UserId == userId && x.RevokedAt == null &&
      ((engagementId.HasValue && (x.EngagementId == engagementId ||
        (x.EngagementId == null && (x.ClientId == null || x.ClientId == clientId)))) ||
       (!engagementId.HasValue && clientId.HasValue && (x.ClientId == null || x.ClientId == clientId)) ||
       (!engagementId.HasValue && !clientId.HasValue && x.ClientId == null && x.EngagementId == null)), ct);
    return assigned ? null : CommandResult.Fail(ErrorCodes.ScopeDenied, "The assignee has no active grant for this scope.");
  }

  private static async Task<bool> IsElevatedAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid? clientId, Guid? engagementId, CancellationToken ct) =>
    await db.RoleGrants.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId &&
      x.RevokedAt == null && ApprovalRoles.Contains(x.Role) &&
      ((engagementId.HasValue && (x.EngagementId == engagementId ||
        (x.EngagementId == null && (x.ClientId == null || x.ClientId == clientId)))) ||
       (!engagementId.HasValue && clientId.HasValue && (x.ClientId == null || x.ClientId == clientId)) ||
       (!engagementId.HasValue && !clientId.HasValue && x.ClientId == null && x.EngagementId == null)), ct);

  private static Task<FirmSafetyState?> LockFirmAsync(
    IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static Task<WorkTask?> LoadTaskForUpdateAsync(
    IAuditSphereDbContext db, Guid firmId, Guid taskId, CancellationToken ct) =>
    db.WorkTasks.FromSqlInterpolated(
      $"SELECT * FROM work_tasks WHERE id = {taskId} AND firm_id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static Task<TimeEntry?> LoadTimeForUpdateAsync(
    IAuditSphereDbContext db, Guid firmId, Guid entryId, CancellationToken ct) =>
    db.TimeEntries.FromSqlInterpolated(
      $"SELECT * FROM time_entries WHERE id = {entryId} AND firm_id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static async Task<CommandResult> LockClientAsync(
    IAuditSphereDbContext db, Guid firmId, Guid? clientId, CancellationToken ct)
  {
    if (!clientId.HasValue) return CommandResult.Ok();
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    return guard is null
      ? CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.")
      : CommandResult.Ok();
  }

  private static string? ValidateTime(SaveTimeDraftRequest request)
  {
    var error = Required(request.Role, "Time role") ?? Required(request.Activity, "Time activity");
    if (error is not null) return error;
    if (request.StartMinute is < 0 or > 1439 || request.DurationMinutes is < 1 or > 1440 ||
        request.StartMinute + request.DurationMinutes > 1440)
      return "Time must fit within one bounded 24-hour work date.";
    if (!Classifications.Contains(request.BillableClassification.Trim().ToUpperInvariant()))
      return "Billable classification is invalid.";
    if (!NarrativeVisibilities.Contains(request.NarrativeVisibility.Trim().ToUpperInvariant()))
      return "Narrative visibility is invalid.";
    return string.Equals(request.BillableClassification.Trim(), PracticeTimeStates.Billable,
      StringComparison.OrdinalIgnoreCase) ? CurrencyError(request.Currency) : null;
  }

  private static string? ValidateCorrection(CorrectTimeRequest request)
  {
    if (request.StartMinute is < 0 or > 1439 || request.DurationMinutes is < 1 or > 1440 ||
        request.StartMinute + request.DurationMinutes > 1440)
      return "Time must fit within one bounded 24-hour work date.";
    if (string.IsNullOrWhiteSpace(request.Activity)) return "Time activity is required.";
    return NarrativeVisibilities.Contains(request.NarrativeVisibility.Trim().ToUpperInvariant())
      ? null : "Narrative visibility is invalid.";
  }

  private static string? ValidateRateCard(RateCardDraftRequest request) =>
    Required(request.Role, "Rate role") ?? Required(request.Activity, "Rate activity") ??
    CurrencyError(request.Currency) ?? ExactMoneyError(request.RatePerHour, "Hourly rate");

  private static string? ValidateBudget(ReviseBudgetRequest request)
  {
    if (!request.Lines.Any() || request.Lines.Count > 200) return "A budget needs 1 to 200 lines.";
    var currencyError = CurrencyError(request.Currency);
    if (currencyError is not null) return currencyError;
    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in request.Lines)
    {
      if (string.IsNullOrWhiteSpace(line.Role) || string.IsNullOrWhiteSpace(line.Activity))
        return "Budget role and activity are required.";
      if (line.ForecastMinutes is < 1 or > 10_000_000)
        return "Forecast minutes are outside the supported bound.";
      if (!keys.Add($"{line.Role.Trim()}\n{line.Activity.Trim()}"))
        return "A budget cannot repeat the same role and activity line.";
    }
    return null;
  }

  private static readonly string[] Classifications =
    [PracticeTimeStates.Billable, PracticeTimeStates.NonBillable, PracticeTimeStates.NoCharge];
  private static readonly string[] NarrativeVisibilities =
    [PracticeTimeStates.NarrativeInternal, PracticeTimeStates.NarrativeClientVisible];

  private static string? Required(string value, string name) =>
    string.IsNullOrWhiteSpace(value) ? $"{name} is required." : null;

  private static string? CurrencyError(string currency) =>
    currency.Trim().Length == 3 && currency.All(char.IsLetter)
      ? null : "Currency must be a three-letter code.";

  private static string? ExactMoneyError(decimal amount, string name) =>
    amount >= 0 && MoneyPolicy.Normalize(amount) == amount
      ? null : $"{name} must be non-negative and have at most 6 decimals.";

  private static string? TrimOrNull(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
