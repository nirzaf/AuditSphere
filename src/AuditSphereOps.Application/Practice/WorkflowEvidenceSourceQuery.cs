using System.Data;
using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowEvidenceSourceItem(Guid Id, string Label, string State, string? Revision);
public sealed record WorkflowEvidenceSourcePage(Guid EngagementId, string Kind, int Page, int PageSize,
  bool HasMore, IReadOnlyList<WorkflowEvidenceSourceItem> Items, WorkflowEvidenceSourceItem? Selected);

/// <summary>
/// Lists exact-engagement sources for reusable workflow evidence bindings. State is the source's recorded status;
/// selecting a source does not evaluate, approve or accept it.
/// </summary>
public static class WorkflowEvidenceSourceQuery
{
  private const int MaximumPage = 10_000;
  private const int MaximumSearchLength = 120;
  private static readonly int[] PageSizes = [10, 25, 50];

  public static async Task<CommandResult<WorkflowEvidenceSourcePage>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string kind, string? query,
    int page = 0, int pageSize = 25, Guid? selectedId = null, CancellationToken ct = default)
  {
    var search = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
    var searchTerm = search?.ToUpperInvariant();
    if (engagementId == Guid.Empty ||
        kind is not (WorkflowEvidenceKinds.AcceptedPbc or WorkflowEvidenceKinds.ReviewedProcedure) ||
        (search?.Length ?? 0) > MaximumSearchLength || page is < 0 or > MaximumPage ||
        !PageSizes.Contains(pageSize))
      return CommandResult<WorkflowEvidenceSourcePage>.Fail("request.invalid",
        "Choose one supported evidence kind, a search of at most 120 characters, page 0 to 10000, and page size 10, 25 or 50.");

    if (db.Database.CurrentTransaction is not null)
      return CommandResult<WorkflowEvidenceSourcePage>.Fail(ErrorCodes.GateBlocked,
        "Evidence source search requires an independent read snapshot.");

    Guid clientId;
    bool hasMore;
    IReadOnlyList<WorkflowEvidenceSourceItem> items;
    WorkflowEvidenceSourceItem? selected;
    {
      await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
      var scopedClientId = await db.Engagements.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Id == engagementId)
        .Select(x => (Guid?)x.PracticeClientId).SingleOrDefaultAsync(ct);
      if (scopedClientId is null)
        return CommandResult<WorkflowEvidenceSourcePage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      clientId = scopedClientId.Value;

      var auth = await AuthorizeAsync(db, actor, clientId, engagementId, ct);
      if (!auth.Succeeded)
        return CommandResult<WorkflowEvidenceSourcePage>.Fail(auth.ErrorCode!, auth.Message!);

      var offset = page * pageSize;
      if (kind == WorkflowEvidenceKinds.AcceptedPbc)
      {
        var sources = db.PbcRequests.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
          x.ClientId == clientId && x.EngagementId == engagementId);
        if (searchTerm is not null)
          sources = sources.Where(x => x.Area.ToUpper().Contains(searchTerm) || x.Objective.ToUpper().Contains(searchTerm));
        var rows = await sources.OrderBy(x => x.Id).Skip(offset).Take(pageSize + 1)
          .Select(x => new { x.Id, x.Area, x.Objective, x.State, x.Revision }).ToListAsync(ct);
        hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(pageSize);
        items = rows.Select(x => new WorkflowEvidenceSourceItem(x.Id, PbcLabel(x.Area, x.Objective),
          x.State, RevisionText(x.Revision))).ToArray();

        var selectedRow = selectedId.HasValue
          ? await db.PbcRequests.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
              x.ClientId == clientId && x.EngagementId == engagementId && x.Id == selectedId.Value)
            .Select(x => new { x.Id, x.Area, x.Objective, x.State, x.Revision }).SingleOrDefaultAsync(ct)
          : null;
        selected = selectedRow is null ? null : new WorkflowEvidenceSourceItem(selectedRow.Id,
          PbcLabel(selectedRow.Area, selectedRow.Objective), selectedRow.State, RevisionText(selectedRow.Revision));
      }
      else
      {
        var sources = db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
          x.ClientId == clientId && x.EngagementId == engagementId);
        if (searchTerm is not null)
          sources = sources.Where(x => x.SourceProcedureId.ToUpper().Contains(searchTerm) || x.Title.ToUpper().Contains(searchTerm));
        var rows = await sources.OrderBy(x => x.Id).Skip(offset).Take(pageSize + 1)
          .Select(x => new { x.Id, x.SourceProcedureId, x.Title, x.Status, x.CurrentResultRevision })
          .ToListAsync(ct);
        hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(pageSize);
        items = rows.Select(x => new WorkflowEvidenceSourceItem(x.Id,
          ProcedureLabel(x.SourceProcedureId, x.Title), x.Status, RevisionText(x.CurrentResultRevision)))
          .ToArray();

        var selectedRow = selectedId.HasValue
          ? await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
              x.ClientId == clientId && x.EngagementId == engagementId && x.Id == selectedId.Value)
            .Select(x => new { x.Id, x.SourceProcedureId, x.Title, x.Status, x.CurrentResultRevision })
            .SingleOrDefaultAsync(ct)
          : null;
        selected = selectedRow is null ? null : new WorkflowEvidenceSourceItem(selectedRow.Id,
          ProcedureLabel(selectedRow.SourceProcedureId, selectedRow.Title), selectedRow.Status,
          RevisionText(selectedRow.CurrentResultRevision));
      }

      await snapshot.CommitAsync(ct);
    }

    // AuthorizationDecision re-reads the current session epoch, grant and engagement/client linkage after the
    // snapshot is disposed. Passing the snapshotted client ID makes a changed engagement scope fail closed.
    var finalAuth = await AuthorizeAsync(db, actor, clientId, engagementId, ct);
    if (!finalAuth.Succeeded)
      return CommandResult<WorkflowEvidenceSourcePage>.Fail(finalAuth.ErrorCode!, finalAuth.Message!);

    return CommandResult<WorkflowEvidenceSourcePage>.Ok(new WorkflowEvidenceSourcePage(
      engagementId, kind, page, pageSize, hasMore, items, selected));
  }

  private static Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, WorkflowTaskEvidenceQuery.ReadRoles,
        InternalOnly: true), ct);

  private static string PbcLabel(string area, string objective)
  {
    var cleanArea = area.Trim();
    var cleanObjective = objective.Trim();
    if (cleanArea.Length == 0) return cleanObjective;
    return cleanObjective.Length == 0 ? cleanArea : $"{cleanArea} — {cleanObjective}";
  }

  private static string ProcedureLabel(string sourceProcedureId, string title)
  {
    var cleanCode = sourceProcedureId.Trim();
    var cleanTitle = title.Trim();
    if (cleanCode.Length == 0) return cleanTitle;
    return cleanTitle.Length == 0 ? cleanCode : $"{cleanCode} — {cleanTitle}";
  }

  private static string? RevisionText(long revision) => revision > 0
    ? revision.ToString(CultureInfo.InvariantCulture)
    : null;
}
