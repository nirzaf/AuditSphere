using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// T055 read-side contracts: browse published audit-program library versions, their
// sections and their exact source procedures, plus procedure search. Firm-level
// (program templates are firm-owned, not engagement-scoped) and read-only.
public sealed record AuditProgramVersionSummary(
  Guid ProgramVersionId, string ProgramCode, string Version, string Status,
  string SourceHash, int ProcedureCount, int SectionCount,
  Guid? ApprovedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt);

public sealed record AuditProgramSectionSummary(
  int SectionNumber, string SectionTitle, int ProcedureCount, IReadOnlyList<string> Assertions);

public sealed record AuditProgramLibraryView(
  IReadOnlyList<AuditProgramVersionSummary> Versions,
  AuditProgramVersionSummary? SelectedVersion,
  IReadOnlyList<AuditProgramSectionSummary> Sections);

public sealed record AuditProgramProcedureView(
  Guid ProcedureId, string SourceProcedureId, int SectionNumber, string SectionTitle,
  int Ordinal, string SourceWording, string? ApplicabilityCondition, string? ExpectedEvidence);

public sealed record AuditProgramSectionPage(
  int SectionNumber, string SectionTitle,
  IReadOnlyList<AuditProgramProcedureView> Items, int TotalCount, int Page, int PageSize);

public static class AuditProgramLibraryQuery
{
  private static readonly string[] ReadRoles =
    ["Auditor", "Reviewer", "Manager", "Partner", "Administrator", "AccountingPreparer", "AccountingReviewer"];

  /// <summary>Published and draft library versions for the firm, with the section
  /// breakdown of the selected (or latest published) version.</summary>
  public static async Task<CommandResult<AuditProgramLibraryView>> GetLibraryAsync(
    IAuditSphereDbContext db, ActorContext actor, string? version = null,
    CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AuditProgramLibraryView>.Fail(auth.ErrorCode!, auth.Message!);

    var versions = await db.AuditProgramVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .ToListAsync(ct);
    if (versions.Count == 0)
      return CommandResult<AuditProgramLibraryView>.Ok(new AuditProgramLibraryView([], null, []));

    var selected = !string.IsNullOrWhiteSpace(version)
      ? versions.FirstOrDefault(x => string.Equals(x.Version, version.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? versions.FirstOrDefault(x => x.Status == "PUBLISHED")
        ?? versions[0]
      : versions.FirstOrDefault(x => x.Status == "PUBLISHED") ?? versions[0];

    var procedures = await db.AuditProgramProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProgramVersionId == selected.Id)
      .ToListAsync(ct);
    var sections = procedures
      .GroupBy(x => (x.SectionNumber, x.SectionTitle))
      .OrderBy(g => g.Key.SectionNumber)
      .Select(g => new AuditProgramSectionSummary(
        g.Key.SectionNumber, g.Key.SectionTitle, g.Count(),
        g.Select(x => x.ApplicabilityCondition).Where(x => !string.IsNullOrWhiteSpace(x))
          .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray()!))
      .ToList();

    // One grouped count for every version instead of a query per version.
    var countsByVersion = await db.AuditProgramProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .GroupBy(x => x.ProgramVersionId)
      .Select(g => new { ProgramVersionId = g.Key, Count = g.Count() })
      .ToDictionaryAsync(x => x.ProgramVersionId, x => x.Count, ct);
    var versionSummaries = versions.Select(x => new AuditProgramVersionSummary(
      x.Id, x.ProgramCode, x.Version, x.Status, x.SourceHash,
      countsByVersion.GetValueOrDefault(x.Id),
      x.Id == selected.Id ? sections.Count : 0,
      x.ApprovedByUserId, x.CreatedAt, x.ApprovedAt)).ToList();

    return CommandResult<AuditProgramLibraryView>.Ok(new AuditProgramLibraryView(
      versionSummaries,
      versionSummaries.First(x => x.ProgramVersionId == selected.Id),
      sections));
  }

  /// <summary>Paged procedures for one section of a library version, optionally filtered
  /// by a free-text term over the source identifier and exact wording.</summary>
  public static async Task<CommandResult<AuditProgramSectionPage>> GetSectionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid programVersionId, int sectionNumber,
    string? search = null, int page = 1, int pageSize = 100, CancellationToken ct = default)
  {
    if (programVersionId == Guid.Empty || sectionNumber is < 1 or > 20 || page < 1 || pageSize is < 1 or > 500)
      return CommandResult<AuditProgramSectionPage>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The library section request is invalid.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AuditProgramSectionPage>.Fail(auth.ErrorCode!, auth.Message!);

    var version = await db.AuditProgramVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == programVersionId && x.FirmId == actor.FirmId, ct);
    if (version is null)
      return CommandResult<AuditProgramSectionPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var query = db.AuditProgramProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProgramVersionId == version.Id &&
        x.SectionNumber == sectionNumber);
    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim();
      query = query.Where(x => x.SourceProcedureId.Contains(term) || x.SourceWording.Contains(term));
    }
    var totalCount = await query.CountAsync(ct);
    var items = await query
      .OrderBy(x => x.Ordinal).ThenBy(x => x.SourceProcedureId)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .Select(x => new AuditProgramProcedureView(
        x.Id, x.SourceProcedureId, x.SectionNumber, x.SectionTitle,
        x.Ordinal, x.SourceWording, x.ApplicabilityCondition, x.ExpectedEvidence))
      .ToListAsync(ct);
    var title = items.Count > 0
      ? items[0].SectionTitle
      : await db.AuditProgramProcedures.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ProgramVersionId == version.Id &&
          x.SectionNumber == sectionNumber)
        .Select(x => x.SectionTitle).FirstOrDefaultAsync(ct) ?? string.Empty;

    return CommandResult<AuditProgramSectionPage>.Ok(new AuditProgramSectionPage(
      sectionNumber, title, items, totalCount, page, pageSize));
  }
}
