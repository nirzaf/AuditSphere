using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// T056 read-side contract: the engagement's tailored program view — every adopted
// procedure with its applicability decision, execution status and result revision,
// paged and section-filtered, plus a coverage summary by section.
public sealed record EngagementProcedureView(
  Guid ProcedureId, string SourceProcedureId, int? SectionNumber, string? SectionTitle,
  int Ordinal, string SourceWording, string ApplicabilityStatus, string? ApplicabilityRationale,
  Guid? ApplicabilityDecidedByUserId, DateTimeOffset? ApplicabilityDecidedAt,
  string Status, long CurrentResultRevision, Guid? RiskId);

public sealed record EngagementProgramSectionCoverage(
  int SectionNumber, string SectionTitle, int Total, int Applicable, int NotApplicableApproved,
  int PendingDecision, int Reviewed, int Completed);

public sealed record EngagementProgramView(
  Guid EngagementId, Guid? ProgramVersionId, string? ProgramCode, string? ProgramVersion,
  string? SourceHash, int TotalProcedures,
  IReadOnlyList<EngagementProgramSectionCoverage> Sections);

public sealed record EngagementProgramOption(Guid ProgramVersionId, string ProgramCode, string Version,
  string SourceHash, string? ContentDigest, int ProcedureCount, bool Eligible, string? Blocker);

public sealed record EngagementProgramOptionsView(Guid EngagementId, Guid? CurrentAdoptionId,
  Guid? CurrentProgramVersionId, string? CurrentVersion, string? CurrentSourceHash, string? CurrentContentDigest,
  int BaselineProcedureCount, int AdHocProcedureCount, bool CurrentAdoptionComplete, string? CurrentAdoptionBlocker,
  IReadOnlyList<EngagementProgramOption> Options);

public sealed record EngagementProcedurePage(
  Guid EngagementId, IReadOnlyList<EngagementProcedureView> Items, int TotalCount, int Page, int PageSize);

public static class EngagementAuditProgramQuery
{
  private static readonly string[] ReadRoles =
    ["Auditor", "Reviewer", "Manager", "Partner", "Administrator", "AccountingPreparer", "AccountingReviewer"];
  private static readonly string[] AdoptionRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];

  /// <summary>Shows exact published programme identities only within an authorized engagement scope.</summary>
  public static async Task<CommandResult<EngagementProgramOptionsView>> GetOptionsAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty)
      return CommandResult<EngagementProgramOptionsView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null)
      return CommandResult<EngagementProgramOptionsView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
        AdoptionRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!authorization.Succeeded)
      return CommandResult<EngagementProgramOptionsView>.Fail(authorization.ErrorCode!, authorization.Message!);

    var adoptions = await db.EngagementAuditPrograms.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId &&
        x.Status == EngagementAuditProgramStatuses.Adopted)
      .OrderByDescending(x => x.AdoptedAt).ThenByDescending(x => x.Id).Take(2).ToListAsync(ct);
    var current = adoptions.Count == 1 ? adoptions[0] : null;
    var currentProgram = current is null ? null : await db.AuditProgramVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == current.ProgramVersionId, ct);
    var currentProcedures = current is null ? [] : await db.AuditProcedures.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId &&
        x.EngagementProgramId == current.Id).ToListAsync(ct);
    var currentTemplates = current is null ? [] : await db.AuditProgramProcedures.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ProgramVersionId == current.ProgramVersionId)
      .OrderBy(x => x.SectionNumber).ThenBy(x => x.Ordinal).ToListAsync(ct);
    var isCurrentComplete = current is not null && currentProgram is not null &&
      string.Equals(currentProgram.SourceHash, AuditProgramCatalog.SourceHash, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(currentProgram.ContentDigest, AuditProgramCatalog.ContentDigest, StringComparison.OrdinalIgnoreCase) &&
      AuditProgramService.MatchesControlledCatalog(currentTemplates) &&
      AuditProgramService.HasCompleteAdoption(currentProcedures, currentTemplates) &&
      await AuditProgramService.HasCurrentDefinitionHistoryAsync(db,
        currentProcedures.Where(x => x.ProgramProcedureId.HasValue).ToList(), ct);

    var versions = await db.AuditProgramVersions.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ProgramCode == AuditProgramCatalog.ProgramCode && x.Status == AuditProgramStatuses.Published)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(50).ToListAsync(ct);
    var versionIds = versions.Select(x => x.Id).ToArray();
    var allTemplates = versionIds.Length == 0 ? [] : await db.AuditProgramProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && versionIds.Contains(x.ProgramVersionId))
      .OrderBy(x => x.SectionNumber).ThenBy(x => x.Ordinal).ToListAsync(ct);
    var templateMap = allTemplates.ToLookup(x => x.ProgramVersionId);
    var options = versions.Select(version =>
    {
      var templates = templateMap[version.Id].ToArray();
      var eligible = string.Equals(version.SourceHash, AuditProgramCatalog.SourceHash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(version.ContentDigest, AuditProgramCatalog.ContentDigest, StringComparison.OrdinalIgnoreCase) &&
        AuditProgramService.MatchesControlledCatalog(templates);
      return new EngagementProgramOption(version.Id, version.ProgramCode, version.Version, version.SourceHash,
        version.ContentDigest, templates.Length, eligible,
        eligible ? null : "This published version is legacy-unverified or differs from the controlled source.");
    }).ToArray();

    var currentBaseline = currentProcedures.Count(x => x.ProgramProcedureId.HasValue);
    var currentAdHoc = currentProcedures.Count - currentBaseline;
    var blocker = adoptions.Count > 1 ? "More than one active programme exists; controlled reconciliation is required."
      : current is not null && !isCurrentComplete ? "The retained adoption is incomplete or does not match its published source; it was not repaired."
      : null;
    var recheck = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
        AdoptionRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!recheck.Succeeded)
      return CommandResult<EngagementProgramOptionsView>.Fail(recheck.ErrorCode!, recheck.Message!);
    return CommandResult<EngagementProgramOptionsView>.Ok(new(engagementId, current?.Id, current?.ProgramVersionId,
      currentProgram?.Version, currentProgram?.SourceHash, currentProgram?.ContentDigest,
      currentBaseline, currentAdHoc, isCurrentComplete, blocker, options));
  }

  /// <summary>Tailored procedure list for one engagement, optionally limited to one
  /// source section, with the exact preserved source wording and decision evidence.</summary>
  public static async Task<CommandResult<EngagementProcedurePage>> GetProceduresAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, int? sectionNumber = null,
    int page = 1, int pageSize = 100, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || page < 1 || pageSize is < 1 or > 500 ||
        sectionNumber is < 1 or > 20)
      return CommandResult<EngagementProcedurePage>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The engagement procedure page request is invalid.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: ReadRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementProcedurePage>.Fail(auth.ErrorCode!, auth.Message!);

    var query = db.AuditProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId);
    if (sectionNumber.HasValue)
      query = query.Where(x => x.SourceSectionNumber == sectionNumber.Value);

    var totalCount = await query.CountAsync(ct);
    var rows = await query
      .OrderBy(x => x.SourceSectionNumber ?? 0).ThenBy(x => x.SourceProcedureId)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .ToListAsync(ct);
    var applicability = rows.Count == 0 ? new Dictionary<Guid, string>() :
      await AuditProgramService.EffectiveApplicabilityStatusesAsync(db, actor.FirmId,
        rows[0].ClientId, engagementId, rows, ct);
    var programProcedureIds = rows.Where(x => x.ProgramProcedureId.HasValue)
      .Select(x => x.ProgramProcedureId!.Value).Distinct().ToArray();
    var ordinals = programProcedureIds.Length == 0 ? new Dictionary<Guid, int>() :
      await db.AuditProgramProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        programProcedureIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Ordinal, ct);
    var items = rows.Select(x => new EngagementProcedureView(
      x.Id, x.SourceProcedureId, x.SourceSectionNumber, x.SourceSectionTitle,
      x.ProgramProcedureId is { } sourceId ? ordinals.GetValueOrDefault(sourceId) : 0,
      x.SourceWording ?? string.Empty, applicability[x.Id], x.ApplicabilityRationale,
      x.ApplicabilityDecidedByUserId, x.ApplicabilityDecidedAt,
      x.Status, x.CurrentResultRevision, x.RiskId)).ToList();

    return CommandResult<EngagementProcedurePage>.Ok(new EngagementProcedurePage(
      engagementId, items, totalCount, page, pageSize));
  }

  /// <summary>Coverage summary for the engagement's tailored program: per-section totals,
  /// decision state and review progress. Reports facts only; it is not an audit conclusion.</summary>
  public static async Task<CommandResult<EngagementProgramView>> GetProgramAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty)
      return CommandResult<EngagementProgramView>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An engagement id is required.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: ReadRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementProgramView>.Fail(auth.ErrorCode!, auth.Message!);

    var procedures = await db.AuditProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .ToListAsync(ct);
    var applicability = procedures.Count == 0 ? new Dictionary<Guid, string>() :
      await AuditProgramService.EffectiveApplicabilityStatusesAsync(db, actor.FirmId,
        procedures[0].ClientId, engagementId, procedures, ct);
    var program = await db.EngagementAuditPrograms.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .Select(x => new { x.Id, x.ProgramVersionId })
      .FirstOrDefaultAsync(ct);
    var version = program is null ? null : await db.AuditProgramVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == program.ProgramVersionId, ct);

    var sections = procedures
      .Where(x => x.SourceSectionNumber.HasValue)
      .GroupBy(x => (SectionNumber: x.SourceSectionNumber!.Value, Title: x.SourceSectionTitle ?? string.Empty))
      .OrderBy(g => g.Key.SectionNumber)
      .Select(g => new EngagementProgramSectionCoverage(
        g.Key.SectionNumber, g.Key.Title, g.Count(),
        g.Count(x => applicability[x.Id] == AuditApplicabilityStatuses.Applicable),
        g.Count(x => applicability[x.Id] == AuditApplicabilityStatuses.NotApplicableApproved),
        g.Count(x => applicability[x.Id] is AuditApplicabilityStatuses.Pending or
          AuditApplicabilityStatuses.NotApplicablePendingReview or AuditApplicabilityStatuses.NotApplicableUnverified),
        g.Count(x => x.Status == AuditProcedureStatuses.Reviewed ||
          x.Status == AuditProcedureStatuses.InReview || x.Status == AuditProcedureStatuses.Submitted),
        g.Count(x => x.Status == AuditProcedureStatuses.Reviewed)))
      .ToList();

    return CommandResult<EngagementProgramView>.Ok(new EngagementProgramView(
      engagementId, program?.ProgramVersionId, version?.ProgramCode, version?.Version,
      version?.SourceHash, procedures.Count, sections));
  }
}
