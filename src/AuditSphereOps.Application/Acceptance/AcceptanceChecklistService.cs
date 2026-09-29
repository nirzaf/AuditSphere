using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record ChecklistItem(ChecklistQuestion Question, ChecklistAnswer? Answer, ChecklistAnswer? PriorAnswer, bool Adverse, string? AnsweredBy);

public sealed record AcceptanceChecklist(
  Guid ClientId, long Generation, string Path, AcceptanceDecision? PriorDecision, AcceptanceDecision? CurrentDecision,
  IReadOnlyList<ChecklistItem> Items, IReadOnlyList<SpecialistClearance> Clearances, IReadOnlyList<AcceptanceBlocker> Blockers)
{
  public bool Ready => Blockers.Count == 0;
}

/// <summary>
/// Acceptance evaluation for the two paths in the engagement acceptance specification. A new client answers the full
/// onboarding bank (KYC, AML, independence, ...) with supporting evidence; a recurring client answers only the delta
/// bank (management, borrowings, fraud, litigation and other changes) against the prior accepted decision. An adverse
/// answer needs a cleared specialist review recorded after it, so completing every field never accepts an adverse case.
/// </summary>
public static class AcceptanceChecklistService
{
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "Staff", "EngagementLeader", "Auditor"];
  private static readonly string[] ProfessionalRoles = ["Partner", "Manager", "Staff", "EngagementLeader", "Auditor"];
  private static readonly string[] ClearanceRoles = ["Partner", "Manager"];
  private static readonly string[] ContinuanceRoles = ["Partner", "Manager"];

  /// <summary>Derives the path and reads the checklist from stored records only (no browser-supplied path).</summary>
  public static async Task<AcceptanceChecklist> LoadAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid? engagementId, long generation, CancellationToken ct)
  {
    var prior = await db.AcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.Generation < generation &&
        (x.Decision == "Accepted" || x.Decision == "AcceptedWithConditions"))
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
    var path = prior is null ? AcceptancePaths.NewClient : AcceptancePaths.Continuance;
    var bank = AcceptanceRules.BankFor(path);

    var questions = await (from q in db.QuestionDefinitions.AsNoTracking()
                           join t in db.QuestionnaireTemplates.AsNoTracking() on q.TemplateId equals t.Id
                           where t.IsActive && t.Bank == bank
                           orderby q.SortOrder
                           select new ChecklistQuestion(t.Bank, q.QuestionCode, q.Section, q.PromptText, q.Category, q.AnswerType,
                             q.RequiresEvidence, q.AdverseAnswer, q.EscalatesOnChange, q.SortOrder, t.Version)).ToListAsync(ct);
    var responses = await db.EvaluationResponses.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.Bank == bank).ToListAsync(ct);
    var current = responses.Where(x => x.Generation == generation).GroupBy(x => x.QuestionId)
      .ToDictionary(g => g.Key, g => g.OrderByDescending(y => y.Revision).First());
    var previous = responses.Where(x => x.Generation < generation).GroupBy(x => x.QuestionId)
      .ToDictionary(g => g.Key, g => g.OrderByDescending(y => y.Revision).First());
    var authors = current.Values.Select(x => x.AnsweredByUserId).Distinct().ToList();
    var names = await db.Users.AsNoTracking().Where(x => x.FirmId == firmId && authors.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

    ChecklistAnswer? Map(EvaluationResponse? r) => r is null ? null : new(r.Bank, r.QuestionId, r.Answer, r.EvidenceReference, r.AnsweredAt, r.Revision);
    var answers = current.ToDictionary(x => (bank, x.Key), x => Map(x.Value)!);
    var clearances = await db.SpecialistClearances.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    // A superseded clearance for an area is history: only the newest clearance per area gates the decision.
    var effective = clearances.GroupBy(x => x.Area, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    var blockers = AcceptanceRules.Evaluate(questions, answers,
      effective.Select(c => new ChecklistClearance(c.Area, c.Status, c.EvidenceReference, c.ClearedAt)).ToList());

    var items = questions.Select(q =>
    {
      answers.TryGetValue((q.Bank, q.Code), out var answer);
      previous.TryGetValue(q.Code, out var old);
      return new ChecklistItem(q, answer, Map(old), AcceptanceRules.IsAdverse(q, answer),
        current.TryGetValue(q.Code, out var row) ? names.GetValueOrDefault(row.AnsweredByUserId) : null);
    }).ToList();
    var currentDecision = await db.AcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.Generation == generation && x.Decision != "Pending")
      .OrderByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
    return new(clientId, generation, path, prior, currentDecision, items, effective, blockers);
  }

  public static async Task<CommandResult<AcceptanceChecklist>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, ReadRoles, ct);
    if (!auth.Succeeded) return CommandResult<AcceptanceChecklist>.Fail(auth.ErrorCode!, auth.Message!);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.Id == clientId && x.FirmId == actor.FirmId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if (generation is null) return CommandResult<AcceptanceChecklist>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    return CommandResult<AcceptanceChecklist>.Ok(await LoadAsync(db, actor.FirmId, clientId, null, generation.Value, ct));
  }

  public static async Task<CommandResult> RecordAnswerAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, string questionCode, string answer, string? evidenceReference,
    CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, ProfessionalRoles, ct);
    if (!auth.Succeeded) return auth;
    var evidence = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();
    if (evidence is { Length: > 500 }) return CommandResult.Fail("acceptance.invalid", "Evidence references are limited to 500 characters.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var checklist = await LoadAsync(db, actor.FirmId, clientId, null, guard.InputGeneration, ct);
    if (checklist.CurrentDecision is not null)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "A decision is already recorded for this generation; its evaluation is immutable.");
    var item = checklist.Items.SingleOrDefault(x => string.Equals(x.Question.Code, questionCode?.Trim(), StringComparison.OrdinalIgnoreCase));
    if (item is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied."); // a question outside this path is not disclosed
    var value = item.Question.AnswerType == "BOOLEAN" ? AcceptanceRules.NormalizeBoolean(answer) : (string.IsNullOrWhiteSpace(answer) ? null : answer.Trim());
    if (value is null || value.Length > 2000) return CommandResult.Fail("acceptance.invalid", "Answer Yes or No.");
    if (item.Question.RequiresEvidence && evidence is null)
      return CommandResult.Fail("acceptance.invalid", $"{item.Question.Code} needs a supporting evidence reference.");
    if (item.Answer is { } latest && latest.Answer == value && latest.EvidenceReference == evidence) return CommandResult.Ok(); // idempotent

    var revision = (await db.EvaluationResponses.Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId &&
      x.Bank == item.Question.Bank && x.QuestionId == item.Question.Code).MaxAsync(x => (long?)x.Revision, ct) ?? 0) + 1;
    db.EvaluationResponses.Add(new EvaluationResponse
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = clientId, Bank = item.Question.Bank, QuestionId = item.Question.Code,
      Answer = value, EvidenceReference = evidence, Generation = guard.InputGeneration, Revision = revision,
      AnsweredByUserId = actor.UserId, AnsweredAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>Requests a specialist review of an area. Idempotent while an open request for the area exists.</summary>
  public static async Task<CommandResult<Guid>> RequestClearanceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, string area, string specialistName, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, ProfessionalRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var areaName = (area ?? string.Empty).Trim();
    var name = (specialistName ?? string.Empty).Trim();
    if (areaName.Length is < 2 or > 100 || name.Length is < 2 or > 200)
      return CommandResult<Guid>.Fail("acceptance.invalid", "Name the review area and the specialist who will perform it.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var open = await db.SpecialistClearances.FirstOrDefaultAsync(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId &&
      x.EngagementId == null && x.Area == areaName && x.Status != "CLEARED", ct);
    if (open is not null) return CommandResult<Guid>.Ok(open.Id);
    var clearance = new SpecialistClearance
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = clientId, Area = areaName, SpecialistName = name,
      Status = "PENDING", CreatedAt = DateTimeOffset.UtcNow
    };
    db.SpecialistClearances.Add(clearance);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(clearance.Id);
  }

  public static async Task<CommandResult> RecordClearanceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clearanceId, string status, string? evidenceReference, string? conditions,
    CancellationToken ct = default)
  {
    var normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
    if (normalized is not ("CLEARED" or "HOLD" or "CONDITIONS"))
      return CommandResult.Fail("acceptance.invalid", "Record CLEARED, HOLD or CONDITIONS.");
    var evidence = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();
    var terms = string.IsNullOrWhiteSpace(conditions) ? null : conditions.Trim();
    if (normalized == "CLEARED" && evidence is null) return CommandResult.Fail("acceptance.invalid", "A clearance needs an evidence reference.");
    if (normalized == "CONDITIONS" && terms is null) return CommandResult.Fail("acceptance.invalid", "State the conditions.");
    var clearance = await db.SpecialistClearances.SingleOrDefaultAsync(x => x.Id == clearanceId && x.FirmId == actor.FirmId, ct);
    if (clearance is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, clearance.PracticeClientId, ClearanceRoles, ct);
    if (!auth.Succeeded) return auth;
    if (clearance.Status == "CLEARED") return CommandResult.Fail(ErrorCodes.ProtectedState, "A cleared review is final; request a new review if circumstances change.");
    clearance.Status = normalized;
    clearance.EvidenceReference = evidence;
    clearance.Conditions = terms;
    clearance.SpecialistUserId = actor.UserId;
    clearance.ClearedAt = normalized == "CLEARED" ? DateTimeOffset.UtcNow : null;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>
  /// Opens the next review cycle for an accepted client: advances the evaluation generation (so earlier approvals are no
  /// longer current) and creates the pending decision that the continuance delta checklist answers.
  /// </summary>
  public static async Task<CommandResult<long>> StartContinuanceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, ContinuanceRoles, ct);
    if (!auth.Succeeded) return CommandResult<long>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult<long>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var accepted = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId &&
      x.Generation == guard.InputGeneration && (x.Decision == "Accepted" || x.Decision == "AcceptedWithConditions"))
      .OrderByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
    if (accepted is null)
      return CommandResult<long>.Fail(ErrorCodes.GateBlocked, "A continuance review starts from an accepted decision on the current evaluation.");
    guard.InputGeneration++;
    db.AcceptanceDecisions.Add(new AcceptanceDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = clientId, Decision = "Pending",
      ServiceRoute = accepted.ServiceRoute, Generation = guard.InputGeneration, Path = AcceptancePaths.Continuance, PriorDecisionId = accepted.Id
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<long>.Ok(guard.InputGeneration);
  }

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);
}
