using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record AddReviewNoteRequest(Guid ResultId, string Field, string Excerpt, string Body);
public sealed record ReviewNoteView(Guid NoteId, long ResultRevision, string Field, string Excerpt, int StartOffset, string Body, string AuthorName,
  DateTimeOffset CreatedAt, bool Open, IReadOnlyList<ProcedureReviewNoteEvent> Events);

/// <summary>
/// Inline review notes anchored to an excerpt of one exact submitted result revision. Notes and their thread are
/// append-only; a result cannot be marked reviewed while a note on its procedure is open, and a staffed preparer can
/// only be reviewed by someone staffed above them on the engagement (Senior/Manager before Partner).
/// </summary>
public static class ReviewNotesService
{
  private static readonly string[] ReviewRoles = ["Reviewer", "Senior", "Manager", "Partner", "Administrator"];
  private static readonly string[] ParticipantRoles = ["Reviewer", "Senior", "Staff", "Manager", "Partner", "Administrator", "Auditor"];

  public static async Task<CommandResult<Guid>> AddNoteAsync(IAuditSphereDbContext db, ActorContext actor, AddReviewNoteRequest request, CancellationToken ct = default)
  {
    var field = (request.Field ?? string.Empty).Trim().ToUpperInvariant();
    if (field is not (ReviewNoteFields.WorkPerformed or ReviewNoteFields.Conclusion) || string.IsNullOrWhiteSpace(request.Excerpt) ||
        string.IsNullOrWhiteSpace(request.Body) || request.Excerpt.Length > 500 || request.Body.Length > 4000)
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Choose the field, quote the text the note refers to and write the note.");
    var result = await db.AuditProcedureResults.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ResultId && x.FirmId == actor.FirmId, ct);
    if (result is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, result.ClientId, result.EngagementId, ReviewRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (result.PreparedByUserId == actor.UserId) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review their own work.");
    var writable = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(db, actor, result.EngagementId, "add review note", ct);
    if (!writable.Succeeded) return CommandResult<Guid>.Fail(writable.ErrorCode!, writable.Message!);
    var text = field == ReviewNoteFields.WorkPerformed ? result.WorkPerformed : result.Conclusion;
    var offset = text.IndexOf(request.Excerpt.Trim(), StringComparison.Ordinal);
    if (offset < 0) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The quoted text is not in this revision of the result.");
    var note = new ProcedureReviewNote
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = result.ClientId, EngagementId = result.EngagementId, ProcedureId = result.AuditProcedureId,
      ResultId = result.Id, ResultRevision = result.Revision, Field = field, Excerpt = request.Excerpt.Trim(), StartOffset = offset, Body = request.Body.Trim(),
      AuthorUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ProcedureReviewNotes.Add(note);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(note.Id);
  }

  public static Task<CommandResult> RespondAsync(IAuditSphereDbContext db, ActorContext actor, Guid noteId, string body, CancellationToken ct = default) =>
    AddEventAsync(db, actor, noteId, ReviewNoteEventKinds.Response, body, ParticipantRoles, ct);

  public static Task<CommandResult> ResolveAsync(IAuditSphereDbContext db, ActorContext actor, Guid noteId, string body, CancellationToken ct = default) =>
    AddEventAsync(db, actor, noteId, ReviewNoteEventKinds.Resolved, body, ReviewRoles, ct);

  public static Task<CommandResult> ReopenAsync(IAuditSphereDbContext db, ActorContext actor, Guid noteId, string body, CancellationToken ct = default) =>
    AddEventAsync(db, actor, noteId, ReviewNoteEventKinds.Reopened, body, ReviewRoles, ct);

  public static async Task<IReadOnlyList<ReviewNoteView>> ListAsync(IAuditSphereDbContext db, ActorContext actor, Guid procedureId, CancellationToken ct = default)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == procedureId && x.FirmId == actor.FirmId, ct);
    if (procedure is null) return [];
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, procedure.ClientId, procedure.EngagementId, ParticipantRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return [];
    var notes = await db.ProcedureReviewNotes.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProcedureId == procedureId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    var ids = notes.Select(x => x.Id).ToArray();
    var events = await db.ProcedureReviewNoteEvents.AsNoTracking().Where(x => ids.Contains(x.NoteId)).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    var authors = notes.Select(x => x.AuthorUserId).Distinct().ToArray();
    var names = await db.Users.AsNoTracking().Where(x => authors.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    return notes.Select(n =>
    {
      var mine = events.Where(e => e.NoteId == n.Id).ToList();
      return new ReviewNoteView(n.Id, n.ResultRevision, n.Field, n.Excerpt, n.StartOffset, n.Body, names.GetValueOrDefault(n.AuthorUserId, "Reviewer"), n.CreatedAt, IsOpen(mine), mine);
    }).ToList();
  }

  /// <summary>Open notes on a procedure (across all revisions).</summary>
  internal static async Task<int> OpenCountAsync(IAuditSphereDbContext db, Guid firmId, Guid procedureId, CancellationToken ct)
  {
    var ids = await db.ProcedureReviewNotes.AsNoTracking().Where(x => x.FirmId == firmId && x.ProcedureId == procedureId).Select(x => x.Id).ToListAsync(ct);
    if (ids.Count == 0) return 0;
    var events = await db.ProcedureReviewNoteEvents.AsNoTracking().Where(x => ids.Contains(x.NoteId)).ToListAsync(ct);
    return ids.Count(id => IsOpen(events.Where(e => e.NoteId == id).ToList()));
  }

  /// <summary>
  /// Review hierarchy: when the preparer is staffed on the engagement, the reviewer must be staffed at a higher level.
  /// Unstaffed legacy engagements keep the existing independence rule only.
  /// </summary>
  internal static async Task<CommandResult> RequireReviewerAboveAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, Guid preparerId, Guid reviewerId, CancellationToken ct)
  {
    var staffing = await db.EngagementStaffAssignments.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId && x.RevokedAt == null &&
      (x.UserId == preparerId || x.UserId == reviewerId)).ToListAsync(ct);
    var preparer = staffing.SingleOrDefault(x => x.UserId == preparerId);
    if (preparer is null) return CommandResult.Ok();
    var reviewer = staffing.SingleOrDefault(x => x.UserId == reviewerId);
    return reviewer is not null && StaffingLevels.Rank(reviewer.StaffingLevel) > StaffingLevels.Rank(preparer.StaffingLevel)
      ? CommandResult.Ok()
      : CommandResult.Fail(ErrorCodes.ScopeDenied, $"Work prepared by a {StaffingLevels.Label(preparer.StaffingLevel)} must be reviewed by someone staffed above that level on this engagement.");
  }

  private static bool IsOpen(IReadOnlyList<ProcedureReviewNoteEvent> events) =>
    events.LastOrDefault(e => e.Kind is ReviewNoteEventKinds.Resolved or ReviewNoteEventKinds.Reopened)?.Kind != ReviewNoteEventKinds.Resolved;

  private static async Task<CommandResult> AddEventAsync(IAuditSphereDbContext db, ActorContext actor, Guid noteId, string kind, string body, string[] roles, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(body) || body.Length > 4000) return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "A message is required.");
    var note = await db.ProcedureReviewNotes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == noteId && x.FirmId == actor.FirmId, ct);
    if (note is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, note.ClientId, note.EngagementId, roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    if (kind != ReviewNoteEventKinds.Response)
    {
      var preparer = await db.AuditProcedureResults.AsNoTracking().Where(x => x.Id == note.ResultId).Select(x => x.PreparedByUserId).SingleAsync(ct);
      if (preparer == actor.UserId) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer responds to notes; a reviewer resolves them.");
    }
    db.ProcedureReviewNoteEvents.Add(new ProcedureReviewNoteEvent
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, NoteId = noteId, Kind = kind, Body = body.Trim(), AuthorUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
