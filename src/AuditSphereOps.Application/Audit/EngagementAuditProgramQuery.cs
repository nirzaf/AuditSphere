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

public sealed record EngagementProcedurePage(
  Guid EngagementId, IReadOnlyList<EngagementProcedureView> Items, int TotalCount, int Page, int PageSize);

public static class EngagementAuditProgramQuery
{
  private static readonly string[] ReadRoles =
    ["Auditor", "Reviewer", "Manager", "Partner", "Administrator", "AccountingPreparer", "AccountingReviewer"];

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
    var items = await query
      .OrderBy(x => x.SourceSectionNumber ?? 0).ThenBy(x => x.SourceProcedureId)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .Select(x => new EngagementProcedureView(
        x.Id, x.SourceProcedureId, x.SourceSectionNumber, x.SourceSectionTitle,
        db.AuditProgramProcedures.Where(p => p.Id == x.ProgramProcedureId).Select(p => p.Ordinal)
          .FirstOrDefault(),
        x.SourceWording ?? string.Empty, x.ApplicabilityStatus, x.ApplicabilityRationale,
        x.ApplicabilityDecidedByUserId, x.ApplicabilityDecidedAt,
        x.Status, x.CurrentResultRevision, x.RiskId))
      .ToListAsync(ct);

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
      .Select(x => new { x.SourceSectionNumber, x.SourceSectionTitle, x.ApplicabilityStatus, x.Status })
      .ToListAsync(ct);
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
        g.Count(x => x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable),
        g.Count(x => x.ApplicabilityStatus == AuditApplicabilityStatuses.NotApplicableApproved),
        g.Count(x => x.ApplicabilityStatus == AuditApplicabilityStatuses.Pending ||
          x.ApplicabilityStatus == AuditApplicabilityStatuses.NotApplicablePendingReview),
        g.Count(x => x.Status == AuditProcedureStatuses.Reviewed ||
          x.Status == AuditProcedureStatuses.InReview || x.Status == AuditProcedureStatuses.Submitted),
        g.Count(x => x.Status == AuditProcedureStatuses.Reviewed)))
      .ToList();

    return CommandResult<EngagementProgramView>.Ok(new EngagementProgramView(
      engagementId, program?.ProgramVersionId, version?.ProgramCode, version?.Version,
      version?.SourceHash, procedures.Count, sections));
  }
}
