using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record AssessmentClientView(Guid Id, string LegalName, string? RegistrationNumber, string Status);
public sealed record AssessmentDecisionView(Guid Id, Guid? EngagementId, string Generation, string Decision, string ServiceRoute,
  string Rationale, string? Conditions, string Path, Guid? PriorDecisionId, string? DecidedBy, DateTimeOffset? DecidedAt);
public sealed record AssessmentRepositoryView(string LogicalKey, string State, DateTimeOffset? LastVerifiedAt);
public sealed record AssessmentSectionProgress(string Section, int Answered, int Total);
public sealed record AssessmentSpecialistEvent(Guid Id, Guid ReviewId, string Action, string Area, string Specialist,
  string? Status, string? Evidence, string? Conditions, string Actor, DateTimeOffset OccurredAt);
public sealed record AssessmentWorkspace(AcceptanceWorkspace Checklist, AssessmentClientView Client,
  AssessmentDecisionView? SelectedDecision, bool Historical, AssessmentRepositoryView? Repository,
  int Answered, int Total, int ClearedReviews, int TotalReviews, IReadOnlyList<AssessmentSectionProgress> Sections,
  IReadOnlyList<AssessmentSpecialistEvent> SpecialistTimeline);

/// <summary>A scope checked read projection; exact decision selection never substitutes another client's or newer record.</summary>
public static class AssessmentWorkspaceQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "Staff", "EngagementLeader", "Auditor"];

  public static async Task<CommandResult<AssessmentWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, Guid? decisionId = null, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: Roles, InternalOnly: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded) return Denied();
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    // Use the same firm -> client -> actor order as reviewed administration commands.
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Denied();
    if (await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE id={clientId} AND firm_id={actor.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Denied();
    if (await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id={actor.UserId} AND firm_id={actor.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return Denied();
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded) return Denied();
    var current = await AcceptanceWorkspaceQuery.GetAsync(db, actor, clientId, ct);
    if (!current.Succeeded) return CommandResult<AssessmentWorkspace>.Fail(current.ErrorCode!, "Assessment unavailable.");
    var checklist = current.Value!;
    if (checklist.Questions.Count > 1000 || checklist.Clearances.Count > 1000 || checklist.Blockers.Count > 1000)
      return CommandResult<AssessmentWorkspace>.Fail(ErrorCodes.GateBlocked, "Assessment exceeds the supported view. Contact your administrator.");
    var client = await db.PracticeClients.AsNoTracking().Where(c => c.Id == clientId && c.FirmId == actor.FirmId)
      .Select(c => new AssessmentClientView(c.Id, c.LegalName, c.RegistrationNumber, c.Status)).SingleOrDefaultAsync(ct);
    if (client is null) return Denied();
    var decisions = db.AcceptanceDecisions.AsNoTracking().Where(d => d.FirmId == actor.FirmId && d.PracticeClientId == clientId);
    var currentGeneration = long.Parse(checklist.Generation, CultureInfo.InvariantCulture);
    var selected = decisionId.HasValue ? await decisions.SingleOrDefaultAsync(d => d.Id == decisionId.Value, ct)
      : await decisions.Where(d => d.EngagementId == null && d.Generation == currentGeneration && d.Decision != "Pending").OrderByDescending(d => d.Generation).ThenByDescending(d => d.DecidedAt).ThenBy(d => d.Id).FirstOrDefaultAsync(ct);
    if (decisionId.HasValue && selected is null) return Denied();
    string? decider = null;
    if (selected?.DecidedByUserId is { } userId)
      decider = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.FirmId == actor.FirmId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct);
    if (selected?.PriorDecisionId is { } priorId && !await decisions.AnyAsync(d => d.Id == priorId, ct)) return Denied();
    var decision = selected is null ? null : new AssessmentDecisionView(selected.Id, selected.EngagementId, selected.Generation.ToString(CultureInfo.InvariantCulture),
      selected.Decision, selected.ServiceRoute, selected.Rationale, selected.Conditions, selected.Path, selected.PriorDecisionId, decider, selected.DecidedAt);
    // Repository locations, signed URLs and raw provider diagnostics are deliberately absent from this projection.
    var repositories = await db.ClientWorkspaces.AsNoTracking().Where(w => w.FirmId == actor.FirmId && w.PracticeClientId == clientId && w.Purpose == "PRIMARY")
      .Select(w => new AssessmentRepositoryView(w.LogicalKey, w.State, w.LastVerifiedAt)).Take(2).ToArrayAsync(ct);
    if (repositories.Length > 1) return CommandResult<AssessmentWorkspace>.Fail(ErrorCodes.GateBlocked, "Workspace configuration requires administrator review.");
    var historical = decision is not null && decision.Generation != checklist.Generation;
    if (historical || selected?.EngagementId is not null) checklist = checklist with { CanEdit = false, CanReview = false, CanDecide = false, CanStartContinuance = false };
    var sections = checklist.Questions.GroupBy(q => q.Section).Select(g => new AssessmentSectionProgress(g.Key,
      g.Count(q => !string.IsNullOrWhiteSpace(q.Answer)), g.Count())).ToArray();
    // The append-only command receipts are the authoritative history. Current clearance rows alone
    // cannot reconstruct earlier results because their status is intentionally updated in place.
    var receipts = await db.AssessmentCommandReceipts.AsNoTracking()
      .Where(r => r.FirmId == actor.FirmId && r.ClientId == clientId &&
        (r.Kind == "REQUEST_REVIEW" || r.Kind == "RECORD_REVIEW"))
      .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(200).ToArrayAsync(ct);
    var timelineRows = new List<(AssessmentCommandReceipt Receipt, AssessmentCommandPreview Preview)>();
    foreach (var receipt in receipts)
    {
      AssessmentCommandPreview? preview;
      try { preview = System.Text.Json.JsonSerializer.Deserialize<AssessmentCommandPreview>(receipt.PreviewJson); }
      catch (System.Text.Json.JsonException) { return CommandResult<AssessmentWorkspace>.Fail(ErrorCodes.GateBlocked, "Assessment history requires administrator review."); }
      if (preview is null || preview.ClientId != clientId || preview.RequestId != receipt.RequestId ||
          preview.Fields.Kind != receipt.Kind || receipt.ResourceId == Guid.Empty)
        return CommandResult<AssessmentWorkspace>.Fail(ErrorCodes.GateBlocked, "Assessment history requires administrator review.");
      timelineRows.Add((receipt, preview));
    }
    var actorIds = timelineRows.Select(x => x.Receipt.ActorId).Distinct().ToArray();
    var actors = await db.Users.AsNoTracking().Where(u => u.FirmId == actor.FirmId && actorIds.Contains(u.Id))
      .Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    var reviewIds = timelineRows.Select(x => x.Receipt.ResourceId).Distinct().ToArray();
    var clearanceDetails = await db.SpecialistClearances.AsNoTracking()
      .Where(c => c.FirmId == actor.FirmId && c.PracticeClientId == clientId && reviewIds.Contains(c.Id))
      .Select(c => new { c.Id, c.Area, c.SpecialistName }).ToDictionaryAsync(c => c.Id, ct);
    var timeline = new List<AssessmentSpecialistEvent>(timelineRows.Count);
    foreach (var x in timelineRows.AsEnumerable().Reverse())
    {
      var f = x.Preview.Fields;
      var reviewId = x.Receipt.ResourceId;
      if (!clearanceDetails.TryGetValue(reviewId, out var review))
        return CommandResult<AssessmentWorkspace>.Fail(ErrorCodes.GateBlocked, "Assessment history requires administrator review.");
      var isRequest = x.Receipt.Kind == "REQUEST_REVIEW";
      var validRequest = isRequest && f.Area == review.Area && f.Specialist == review.SpecialistName;
      var validResult = !isRequest && f.ReviewId == reviewId &&
        f.Status is "CLEARED" or "HOLD" or "CONDITIONS";
      if (!validRequest && !validResult)
        return CommandResult<AssessmentWorkspace>.Fail(ErrorCodes.GateBlocked, "Assessment history requires administrator review.");
      timeline.Add(new AssessmentSpecialistEvent(x.Receipt.Id, reviewId, isRequest ? "Review requested" : "Result recorded",
        review.Area, review.SpecialistName, isRequest ? "PENDING" : f.Status, f.Evidence,
        f.Conditions, actors.GetValueOrDefault(x.Receipt.ActorId, "Former or unavailable user"), x.Receipt.CreatedAt));
    }
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded) return Denied();
    var result = new AssessmentWorkspace(checklist, client, decision, historical, repositories.SingleOrDefault(),
      sections.Sum(s => s.Answered), sections.Sum(s => s.Total), checklist.Clearances.Count(c => c.Status == "CLEARED"), checklist.Clearances.Count, sections, timeline);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<AssessmentWorkspace>.Ok(result);
  }

  private static CommandResult<AssessmentWorkspace> Denied() => CommandResult<AssessmentWorkspace>.Fail(ErrorCodes.ScopeDenied, "Assessment unavailable.");
}
