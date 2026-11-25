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
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"];
  private static readonly string[] ProfessionalRoles = ["Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"];
  private static readonly string[] ClearanceRoles = ["Partner", "Manager"];
  private static readonly string[] ContinuanceRoles = ["Partner", "Manager"];

  /// <summary>Derives the path and reads the checklist from stored records only (no browser-supplied path).</summary>
  public static async Task<AcceptanceChecklist> LoadAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid? engagementId, long generation, CancellationToken ct,
    string? serviceRoute = null, Guid? opportunityId = null)
  {
    var priorQuery = db.AcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId &&
        (x.Decision == "Accepted" || x.Decision == "AcceptedWithConditions"));
    if (opportunityId.HasValue)
      priorQuery = priorQuery.Where(x => x.Generation < generation ||
        (x.Generation == generation && (x.OpportunityId == null || x.OpportunityId != opportunityId)));
    else if (engagementId.HasValue)
      priorQuery = priorQuery.Where(x => x.Generation < generation ||
        (x.Generation == generation && x.EngagementId != engagementId));
    else
      priorQuery = priorQuery.Where(x => x.Generation < generation);
    if (!string.IsNullOrWhiteSpace(serviceRoute)) priorQuery = priorQuery.Where(x => x.ServiceRoute == serviceRoute);
    var prior = await priorQuery
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
    var currentQuery = db.AcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.EngagementId == engagementId &&
        x.OpportunityId == opportunityId && x.Generation == generation && x.Decision != "Pending");
    if (!string.IsNullOrWhiteSpace(serviceRoute)) currentQuery = currentQuery.Where(x => x.ServiceRoute == serviceRoute);
    var currentDecision = await currentQuery
      .OrderByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
    return new(clientId, generation, path, prior, currentDecision, items, effective, blockers);
  }

  internal static async Task<bool> HasCurrentContinuanceAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId,
    long generation, string? serviceRoute, Guid? opportunityId = null, Guid? engagementId = null,
    CancellationToken ct = default)
  {
    if (firmId == Guid.Empty || clientId == Guid.Empty || generation < 1 || opportunityId == Guid.Empty ||
        engagementId == Guid.Empty || opportunityId.HasValue && engagementId.HasValue) return false;
    var query = db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.PracticeClientId == clientId && x.Generation == generation && x.Decision == "Pending");
    if (!string.IsNullOrWhiteSpace(serviceRoute)) query = query.Where(x => x.ServiceRoute == serviceRoute);
    if (opportunityId.HasValue)
      query = query.Where(x => x.OpportunityId == opportunityId && x.EngagementId == null);
    else if (engagementId.HasValue)
      query = query.Where(x => x.OpportunityId == null &&
        (x.EngagementId == engagementId || x.EngagementId == null));
    else
      query = query.Where(x => x.OpportunityId == null && x.EngagementId == null);
    var pending = await query.Take(3).ToListAsync(ct);
    if (pending.Count > 2) return false;
    var open = 0;
    foreach (var row in pending)
      if (!await IsContinuanceDecisionRecordedAsync(db, row, ct)) open++;
    return open == 1;
  }

  internal static async Task<bool> HasOpenContinuanceAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId,
    long generation, CancellationToken ct = default)
  {
    if (firmId == Guid.Empty || clientId == Guid.Empty || generation < 1) return true;
    var pending = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.PracticeClientId == clientId && x.Generation == generation && x.Decision == "Pending").Take(3).ToListAsync(ct);
    if (pending.Count > 2) return true;
    foreach (var row in pending)
      if (!await IsContinuanceDecisionRecordedAsync(db, row, ct)) return true;
    return false;
  }

  private static Task<bool> IsContinuanceDecisionRecordedAsync(IAuditSphereDbContext db,
    AcceptanceDecision pending, CancellationToken ct)
  {
    var query = db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == pending.FirmId &&
      x.PracticeClientId == pending.PracticeClientId && x.Generation == pending.Generation &&
      x.ServiceRoute == pending.ServiceRoute && x.Decision != "Pending");
    if (pending.OpportunityId.HasValue)
      query = query.Where(x => x.OpportunityId == pending.OpportunityId);
    else if (pending.EngagementId.HasValue)
      query = query.Where(x => x.EngagementId == pending.EngagementId);
    return query.AnyAsync(ct);
  }

  public static async Task<CommandResult<AcceptanceChecklist>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct = default,
    string? serviceRoute = null, Guid? opportunityId = null, Guid? engagementId = null)
  {
    if (clientId == Guid.Empty || opportunityId == Guid.Empty || engagementId == Guid.Empty ||
        opportunityId.HasValue && engagementId.HasValue ||
        (opportunityId.HasValue || engagementId.HasValue) && string.IsNullOrWhiteSpace(serviceRoute))
      return CommandResult<AcceptanceChecklist>.Fail("acceptance.invalid", "Select one exact engagement or opportunity context.");
    if (engagementId.HasValue && !await db.Engagements.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.PracticeClientId == clientId && x.Id == engagementId &&
        x.ServiceRoute == serviceRoute, ct))
      return CommandResult<AcceptanceChecklist>.Fail(ErrorCodes.ScopeDenied, "Assessment unavailable.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, RequiredRoles: ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<AcceptanceChecklist>.Fail(auth.ErrorCode!, auth.Message!);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.Id == clientId && x.FirmId == actor.FirmId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if (generation is null) return CommandResult<AcceptanceChecklist>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    return CommandResult<AcceptanceChecklist>.Ok(await LoadAsync(db, actor.FirmId, clientId, engagementId,
      generation.Value, ct, serviceRoute, opportunityId));
  }

  public static async Task<CommandResult> RecordAnswerAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, string questionCode, string answer, string? evidenceReference,
    CancellationToken ct = default, long? expectedGeneration = null, long? expectedRevision = null,
    string? serviceRoute = null, Guid? opportunityId = null)
  {
    if (opportunityId == Guid.Empty || (opportunityId.HasValue != !string.IsNullOrWhiteSpace(serviceRoute)))
      return CommandResult.Fail("acceptance.invalid", "An exact opportunity and service route must be supplied together.");
    var auth = await AuthorizeAsync(db, actor, clientId, ProfessionalRoles, ct);
    if (!auth.Succeeded) return auth;
    var evidence = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();
    if (evidence is { Length: > 500 }) return CommandResult.Fail("acceptance.invalid", "Evidence references are limited to 500 characters.");
    // Standalone commands commit their own transaction; reviewed callers publish the mutation and receipt together.
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (!await AcceptanceCommandAuthority.LockFirmAsync(db, actor, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    auth = await AcceptanceCommandAuthority.CheckAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: ProfessionalRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    if (expectedGeneration.HasValue && expectedGeneration.Value != guard.InputGeneration)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "Reload the current evaluation before saving.");
    var opportunity = opportunityId.HasValue
      ? await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
          x.Id == opportunityId.Value && x.AcceptanceClientId == clientId && x.ServiceRoute == serviceRoute &&
          x.EntityScopeConfirmedByUserId != null && x.EntityScopeConfirmedAt != null, ct)
      : null;
    if (opportunityId.HasValue && opportunity is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Assessment unavailable.");
    var checklist = await LoadAsync(db, actor.FirmId, clientId, null, guard.InputGeneration, ct,
      opportunity?.ServiceRoute, opportunity?.Id);
    if (checklist.Path == AcceptancePaths.Continuance &&
        !await HasCurrentContinuanceAsync(db, actor.FirmId, clientId, guard.InputGeneration,
          opportunity?.ServiceRoute, opportunity?.Id, ct: ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Start the current continuance review before recording answers.");
    if (checklist.CurrentDecision is not null)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "A decision is already recorded for this generation; its evaluation is immutable.");
    var item = checklist.Items.SingleOrDefault(x => string.Equals(x.Question.Code, questionCode?.Trim(), StringComparison.OrdinalIgnoreCase));
    if (item is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied."); // a question outside this path is not disclosed
    if (expectedRevision.HasValue && expectedRevision.Value != (item.Answer?.Revision ?? 0))
      return CommandResult.Fail(ErrorCodes.StaleRevision, "This answer changed. Reload it before saving.");
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
    auth = await AuthorizeAsync(db, actor, clientId, ProfessionalRoles, ct);
    if (!auth.Succeeded) return auth;
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>Requests a specialist review of an area. Idempotent while an open request for the area exists.</summary>
  public static async Task<CommandResult<Guid>> RequestClearanceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, string area, string specialistName, CancellationToken ct = default, long? expectedGeneration = null)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, ProfessionalRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var areaName = (area ?? string.Empty).Trim();
    var name = (specialistName ?? string.Empty).Trim();
    if (areaName.Length is < 2 or > 100 || name.Length is < 2 or > 200)
      return CommandResult<Guid>.Fail("acceptance.invalid", "Name the review area and the specialist who will perform it.");
    // Standalone commands commit their own transaction; reviewed callers publish the mutation and receipt together.
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (!await AcceptanceCommandAuthority.LockFirmAsync(db, actor, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    auth = await AcceptanceCommandAuthority.CheckAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: ProfessionalRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (expectedGeneration.HasValue && expectedGeneration.Value != guard.InputGeneration)
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "Reload the current evaluation.");
    if (expectedGeneration.HasValue && (await LoadAsync(db, actor.FirmId, clientId, null, guard.InputGeneration, ct)).CurrentDecision is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The current evaluation is immutable.");
    var open = await db.SpecialistClearances.Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId &&
      x.EngagementId == null && x.Area == areaName && x.Status != "CLEARED").OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
    if (open is not null) return CommandResult<Guid>.Ok(open.Id);
    var clearance = new SpecialistClearance
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = clientId, Area = areaName, SpecialistName = name,
      Status = "PENDING", CreatedAt = DateTimeOffset.UtcNow
    };
    db.SpecialistClearances.Add(clearance);
    await db.SaveChangesAsync(ct);
    auth = await AuthorizeAsync(db, actor, clientId, ProfessionalRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(clearance.Id);
  }

  public static async Task<CommandResult> RecordClearanceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clearanceId, string status, string? evidenceReference, string? conditions,
    CancellationToken ct = default, long? expectedGeneration = null, string? expectedStatus = null)
  {
    var normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
    if (normalized is not ("CLEARED" or "HOLD" or "CONDITIONS"))
      return CommandResult.Fail("acceptance.invalid", "Record CLEARED, HOLD or CONDITIONS.");
    var evidence = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();
    var terms = string.IsNullOrWhiteSpace(conditions) ? null : conditions.Trim();
    if (evidence is { Length: > 500 } || terms is { Length: > 2000 }) return CommandResult.Fail("acceptance.invalid", "Review evidence or conditions exceed their bounds.");
    if (normalized == "CLEARED" && evidence is null) return CommandResult.Fail("acceptance.invalid", "A clearance needs an evidence reference.");
    if (normalized == "CONDITIONS" && terms is null) return CommandResult.Fail("acceptance.invalid", "State the conditions.");
    var snapshot = await db.SpecialistClearances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clearanceId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, snapshot.PracticeClientId, ClearanceRoles, ct);
    if (!auth.Succeeded) return auth;
    // Standalone commands commit their own transaction; reviewed callers publish the mutation and receipt together.
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (!await AcceptanceCommandAuthority.LockFirmAsync(db, actor, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {snapshot.PracticeClientId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    auth = await AcceptanceCommandAuthority.CheckAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, snapshot.PracticeClientId, RequiredRoles: ClearanceRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    if (expectedGeneration.HasValue && expectedGeneration.Value != guard.InputGeneration)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "Reload the current evaluation.");
    if (expectedGeneration.HasValue && (await LoadAsync(db, actor.FirmId, snapshot.PracticeClientId, null, guard.InputGeneration, ct)).CurrentDecision is not null)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The current evaluation is immutable.");
    var clearance = await db.SpecialistClearances.SingleAsync(x => x.Id == clearanceId && x.FirmId == actor.FirmId, ct);
    if (expectedStatus is not null && clearance.Status != expectedStatus)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "This specialist review changed. Reload it before saving.");
    if (clearance.Status == "CLEARED") return CommandResult.Fail(ErrorCodes.ProtectedState, "A cleared review is final; request a new review if circumstances change.");
    clearance.Status = normalized;
    clearance.EvidenceReference = evidence;
    clearance.Conditions = terms;
    clearance.SpecialistUserId = actor.UserId;
    clearance.ClearedAt = normalized == "CLEARED" ? DateTimeOffset.UtcNow : null;
    await db.SaveChangesAsync(ct);
    auth = await AuthorizeAsync(db, actor, snapshot.PracticeClientId, ClearanceRoles, ct);
    if (!auth.Succeeded) return auth;
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>
  /// Opens the next review cycle for an accepted client: advances the evaluation generation (so earlier approvals are no
  /// longer current) and creates the pending decision that the continuance delta checklist answers.
  /// </summary>
  public static async Task<CommandResult<long>> StartContinuanceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct = default, long? expectedGeneration = null,
    Guid? opportunityId = null, string? serviceRoute = null)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, ContinuanceRoles, ct);
    if (!auth.Succeeded) return CommandResult<long>.Fail(auth.ErrorCode!, auth.Message!);
    // Standalone commands commit their own transaction; reviewed callers publish the mutation and receipt together.
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (!await AcceptanceCommandAuthority.LockFirmAsync(db, actor, ct))
      return CommandResult<long>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult<long>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    // A caller may have loaded this entity before an external input advanced its generation.
    // The row lock is held, so refresh the tracked instance before comparing or incrementing it.
    await db.ClientSafetyStates.Attach(guard).ReloadAsync(ct);
    auth = await AcceptanceCommandAuthority.CheckAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: ContinuanceRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<long>.Fail(auth.ErrorCode!, auth.Message!);
    if (expectedGeneration.HasValue && expectedGeneration.Value != guard.InputGeneration)
      return CommandResult<long>.Fail(ErrorCodes.GenerationStale, "Reload the current evaluation.");
    if (await HasOpenContinuanceAsync(db, actor.FirmId, clientId, guard.InputGeneration, ct))
      return CommandResult<long>.Fail(ErrorCodes.ProtectedState, "A continuance review is already open for this client generation.");
    if (opportunityId.HasValue)
    {
      var opportunity = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.Id == opportunityId.Value && x.AcceptanceClientId == clientId && x.ServiceRoute == serviceRoute &&
        x.EntityScopeConfirmedAt != null && x.EntityScopeConfirmedByUserId != null, ct);
      if (opportunity is null)
        return CommandResult<long>.Fail(ErrorCodes.ScopeDenied, "Assessment unavailable.");
    }
    else if (!string.IsNullOrWhiteSpace(serviceRoute))
      return CommandResult<long>.Fail("acceptance.invalid", "An opportunity identity is required for a scoped continuance.");
    var acceptedQuery = db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId &&
      x.Generation <= guard.InputGeneration && (x.Decision == "Accepted" || x.Decision == "AcceptedWithConditions"));
    if (!string.IsNullOrWhiteSpace(serviceRoute)) acceptedQuery = acceptedQuery.Where(x => x.ServiceRoute == serviceRoute);
    var accepted = await acceptedQuery
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    if (accepted is null)
      return CommandResult<long>.Fail(ErrorCodes.GateBlocked, "A continuance review starts from retained accepted evidence for this service route.");
    guard.InputGeneration++;
    db.AcceptanceDecisions.Add(new AcceptanceDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = clientId, Decision = "Pending",
      ServiceRoute = accepted.ServiceRoute, Generation = guard.InputGeneration, Path = AcceptancePaths.Continuance,
      PriorDecisionId = accepted.Id, OpportunityId = opportunityId
    });
    await db.SaveChangesAsync(ct);
    auth = await AuthorizeAsync(db, actor, clientId, ContinuanceRoles, ct);
    if (!auth.Succeeded) return CommandResult<long>.Fail(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<long>.Ok(guard.InputGeneration);
  }

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);
}
