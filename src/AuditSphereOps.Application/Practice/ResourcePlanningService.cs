using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record SaveStaffProfileRequest(Guid UserId, string Department, string Skills, int WeeklyCapacityMinutes, decimal TargetUtilizationPercent);
public sealed record AddCertificationRequest(Guid UserId, string Name, string? Issuer, DateOnly? ExpiresOn);
public sealed record AddAvailabilityRequest(Guid UserId, DateOnly StartDate, DateOnly EndDate, string Kind, int MinutesPerDay);
public sealed record SetAllocationRequest(Guid EngagementId, Guid UserId, DateOnly WeekStart, int PlannedMinutes);

public sealed record ResourceCertificationView(string Name, DateOnly? ExpiresOn, bool Current);
public sealed record ResourceAllocationView(Guid EngagementId, string EngagementLabel, DateOnly WeekStart, int PlannedMinutes);
public sealed record ResourceGridRow(
  Guid UserId, string Name, string Department, IReadOnlyList<string> Skills, IReadOnlyList<ResourceCertificationView> Certifications,
  int WeeklyCapacityMinutes, decimal TargetUtilizationPercent, bool HasProfile, IReadOnlyList<ResourceWeekCell> Weeks,
  IReadOnlyList<ResourceAllocationView> Allocations);
public sealed record ResourceGridView(DateOnly FirstWeek, int Weeks, IReadOnlyList<ResourceGridRow> Rows);

public sealed record BudgetBreakdownRow(string Phase, string RiskArea, int ForecastMinutes, decimal ForecastCost, int ActualMinutes, decimal ActualCost);
public sealed record BudgetBreakdownView(Guid BudgetId, long BudgetVersion, IReadOnlyList<BudgetBreakdownRow> Rows,
  int ForecastMinutes, decimal ForecastCost, int ActualMinutes, decimal ActualCost);

/// <summary>
/// Staff profiles, availability, week allocations and the resource grid. Profiles and plans are planning data, not
/// evidence; the grid separates planned from approved-actual utilization and flags over-allocation instead of
/// hiding it. Only staff already assigned to an engagement can be allocated to it.
/// </summary>
public static class ResourcePlanningService
{
  private static readonly string[] PlanningRoles = ["Administrator", "Partner", "Manager"];
  public const int MaxWeeklyMinutes = 80 * 60;

  public static async Task<CommandResult> SaveProfileAsync(IAuditSphereDbContext db, ActorContext actor, SaveStaffProfileRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    if (request.WeeklyCapacityMinutes is < 0 or > MaxWeeklyMinutes || request.TargetUtilizationPercent is < 0 or > 100 ||
        string.IsNullOrWhiteSpace(request.Department) || request.Department.Trim().Length > 100)
      return CommandResult.Fail("resource.invalid", "A department, weekly capacity (0–80 hours) and target utilization (0–100%) are required.");
    if (!await db.Users.AnyAsync(x => x.Id == request.UserId && x.FirmId == actor.FirmId && x.UserKind == "Staff", ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var profile = await db.StaffProfiles.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.UserId == request.UserId, ct);
    if (profile is null)
    {
      profile = new StaffProfile { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = request.UserId };
      db.StaffProfiles.Add(profile);
    }
    profile.Department = request.Department.Trim();
    profile.Skills = string.Join(',', (request.Skills ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .Select(x => x.ToUpperInvariant()).Distinct().Take(30));
    profile.WeeklyCapacityMinutes = request.WeeklyCapacityMinutes;
    profile.TargetUtilizationPercent = request.TargetUtilizationPercent;
    profile.UpdatedAt = DateTimeOffset.UtcNow;
    profile.UpdatedByUserId = actor.UserId;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> AddCertificationAsync(IAuditSphereDbContext db, ActorContext actor, AddCertificationRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
      return CommandResult<Guid>.Fail("resource.invalid", "A certification name is required.");
    if (!await db.Users.AnyAsync(x => x.Id == request.UserId && x.FirmId == actor.FirmId && x.UserKind == "Staff", ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var certification = new StaffCertification
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = request.UserId, Name = request.Name.Trim(),
      Issuer = string.IsNullOrWhiteSpace(request.Issuer) ? null : request.Issuer.Trim(), ExpiresOn = request.ExpiresOn,
      RecordedAt = DateTimeOffset.UtcNow, RecordedByUserId = actor.UserId
    };
    db.StaffCertifications.Add(certification);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(certification.Id);
  }

  public static async Task<CommandResult<Guid>> AddAvailabilityAsync(IAuditSphereDbContext db, ActorContext actor, AddAvailabilityRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var kind = (request.Kind ?? string.Empty).Trim().ToUpperInvariant();
    if (request.EndDate < request.StartDate || request.MinutesPerDay is < 1 or > 24 * 60 ||
        kind is not (StaffAvailabilityKinds.Leave or StaffAvailabilityKinds.Training or StaffAvailabilityKinds.PublicHoliday))
      return CommandResult<Guid>.Fail("resource.invalid", "A valid date range, kind and minutes per day are required.");
    if (!await db.Users.AnyAsync(x => x.Id == request.UserId && x.FirmId == actor.FirmId && x.UserKind == "Staff", ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var availability = new StaffAvailability
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = request.UserId, StartDate = request.StartDate, EndDate = request.EndDate,
      Kind = kind, MinutesPerDay = request.MinutesPerDay, RecordedAt = DateTimeOffset.UtcNow, RecordedByUserId = actor.UserId
    };
    db.StaffAvailabilities.Add(availability);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(availability.Id);
  }

  public static async Task<CommandResult> SetAllocationAsync(IAuditSphereDbContext db, ActorContext actor, SetAllocationRequest request, CancellationToken ct = default)
  {
    if (request.PlannedMinutes is < 0 or > MaxWeeklyMinutes)
      return CommandResult.Fail("resource.invalid", "Planned time must be between 0 and 80 hours in a week.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.EngagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, PlanningRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    if (!await db.EngagementStaffAssignments.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagement.Id &&
          x.UserId == request.UserId && x.RevokedAt == null, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Staff the person on this engagement before allocating time.");
    var week = ResourceGridCalculator.WeekStart(request.WeekStart);
    var allocation = await db.StaffAllocations.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagement.Id &&
      x.UserId == request.UserId && x.WeekStart == week, ct);
    if (request.PlannedMinutes == 0)
    {
      if (allocation is not null) db.StaffAllocations.Remove(allocation);
    }
    else
    {
      if (allocation is null)
      {
        allocation = new StaffAllocation
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId, EngagementId = engagement.Id,
          UserId = request.UserId, WeekStart = week
        };
        db.StaffAllocations.Add(allocation);
      }
      allocation.PlannedMinutes = request.PlannedMinutes;
      allocation.UpdatedAt = DateTimeOffset.UtcNow;
      allocation.UpdatedByUserId = actor.UserId;
    }
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<ResourceGridView>> GetGridAsync(IAuditSphereDbContext db, ActorContext actor, DateOnly firstWeek, int weeks, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<ResourceGridView>.Fail(auth.ErrorCode!, auth.Message!);
    weeks = Math.Clamp(weeks, 1, 12);
    var start = ResourceGridCalculator.WeekStart(firstWeek);
    var end = start.AddDays(7 * weeks - 1);
    var users = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserKind == "Staff" && !x.Disabled)
      .OrderBy(x => x.DisplayName).Select(x => new { x.Id, x.DisplayName }).ToListAsync(ct);
    var profiles = await db.StaffProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId).ToDictionaryAsync(x => x.UserId, ct);
    var certifications = await db.StaffCertifications.AsNoTracking().Where(x => x.FirmId == actor.FirmId).ToListAsync(ct);
    var availability = await db.StaffAvailabilities.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EndDate >= start && x.StartDate <= end).ToListAsync(ct);
    var allocations = await db.StaffAllocations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.WeekStart >= start && x.WeekStart <= end).ToListAsync(ct);
    var engagementIds = allocations.Select(x => x.EngagementId).Distinct().ToArray();
    var labels = await db.Engagements.AsNoTracking().Where(x => engagementIds.Contains(x.Id))
      .Join(db.PracticeClients.AsNoTracking(), e => e.PracticeClientId, c => c.Id, (e, c) => new { e.Id, Label = (c.CommercialName ?? c.LegalName) + " · " + e.ServiceRoute + " " + e.PeriodEnd })
      .ToDictionaryAsync(x => x.Id, x => x.Label, ct);
    var time = await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Status == PracticeTimeStates.TimeApproved &&
      x.WorkDate >= start && x.WorkDate <= end).Select(x => new { x.UserId, x.WorkDate, x.DurationMinutes }).ToListAsync(ct);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var rows = users.Select(u =>
    {
      var profile = profiles.GetValueOrDefault(u.Id);
      var mine = allocations.Where(x => x.UserId == u.Id).ToList();
      return new ResourceGridRow(u.Id, u.DisplayName, profile?.Department ?? "Unassigned",
        (profile?.Skills ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
        certifications.Where(c => c.UserId == u.Id).OrderBy(c => c.Name).Select(c => new ResourceCertificationView(c.Name, c.ExpiresOn, c.ExpiresOn is null || c.ExpiresOn >= today)).ToList(),
        profile?.WeeklyCapacityMinutes ?? 0, profile?.TargetUtilizationPercent ?? 0, profile is not null,
        ResourceGridCalculator.Build(profile?.WeeklyCapacityMinutes ?? 0, availability.Where(a => a.UserId == u.Id).ToList(), mine,
          time.Where(t => t.UserId == u.Id).Select(t => (t.WorkDate, t.DurationMinutes)).ToList(), start, weeks),
        mine.OrderBy(x => x.WeekStart).Select(x => new ResourceAllocationView(x.EngagementId, labels.GetValueOrDefault(x.EngagementId, "Engagement"), x.WeekStart, x.PlannedMinutes)).ToList());
    }).ToList();
    return CommandResult<ResourceGridView>.Ok(new(start, weeks, rows));
  }

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: PlanningRoles, InternalOnly: true), ct);
}
