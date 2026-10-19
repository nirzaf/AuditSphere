using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>Firm-wide, independently reviewed internal reminder rules. This service never sends mail.</summary>
public static partial class WorkflowReminderRuleService
{
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff"];
  private static readonly string[] ConfigureRoles = ["Administrator", "Partner"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task<CommandResult<WorkflowReminderRuleCatalog>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, WorkflowReminderOptions options,
    WorkflowReminderRuleCursor? after = null, int pageSize = 25, CancellationToken ct = default)
  {
    if (options is null || options.BatchSize is < 1 or > 100 || pageSize is < 1 or > 100 ||
        after is { RuleId: var id } && (id == Guid.Empty || after.CreatedAt == default))
      return Invalid<WorkflowReminderRuleCatalog>("Provide a valid bounded page and reminder options.");

    var auth = await AuthorizeReadAsync(db, actor, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderRuleCatalog>(auth);
    var configure = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    var query = db.WorkflowReminderRules.AsNoTracking().Where(x => x.FirmId == actor.FirmId);
    if (after is not null)
    {
      var at = after.CreatedAt.ToUniversalTime();
      query = query.Where(x => x.CreatedAt < at ||
        (x.CreatedAt == at && x.Id.CompareTo(after.RuleId) < 0));
    }
    var rules = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Take(pageSize + 1).ToListAsync(ct);
    var hasMore = rules.Count > pageSize;
    if (hasMore) rules.RemoveAt(rules.Count - 1);

    var ruleIds = rules.Select(x => x.Id).ToArray();
    var revisions = await db.WorkflowReminderRuleRevisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && ruleIds.Contains(x.RuleId) &&
        x.Version == db.WorkflowReminderRuleRevisions.AsNoTracking()
          .Where(y => y.FirmId == actor.FirmId && y.RuleId == x.RuleId).Max(y => y.Version))
      .ToListAsync(ct);
    var latest = revisions.GroupBy(x => x.RuleId).ToDictionary(x => x.Key, x => x.First());
    var items = rules.Select(rule =>
    {
      latest.TryGetValue(rule.Id, out var revision);
      return new WorkflowReminderRuleCatalogItem(rule.Id, revision?.Id, revision?.Version, revision?.Name,
        revision?.Status, rule.SuspendedAt.HasValue, rule.Revision, rule.CreatedAt.ToUniversalTime(),
        rule.LastEvaluatedAt?.ToUniversalTime(), rule.LastEvaluationCode);
    }).ToArray();
    WorkflowReminderRuleCursor? next = hasMore && rules.Count > 0
      ? new WorkflowReminderRuleCursor(rules[^1].CreatedAt.ToUniversalTime(), rules[^1].Id)
      : null;
    return CommandResult<WorkflowReminderRuleCatalog>.Ok(new(options.Enabled, configure.Succeeded, items, hasMore, next));
  }

  public static async Task<CommandResult<WorkflowReminderRuleRevisionView>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId, Guid revisionId, CancellationToken ct = default)
  {
    var auth = await AuthorizeReadAsync(db, actor, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);
    var rule = await db.WorkflowReminderRules.AsNoTracking().SingleOrDefaultAsync(
      x => x.FirmId == actor.FirmId && x.Id == ruleId, ct);
    var revision = await db.WorkflowReminderRuleRevisions.AsNoTracking().SingleOrDefaultAsync(
      x => x.FirmId == actor.FirmId && x.RuleId == ruleId && x.Id == revisionId, ct);
    if (rule is null || revision is null) return Denied<WorkflowReminderRuleRevisionView>();
    var configure = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    return CommandResult<WorkflowReminderRuleRevisionView>.Ok(BuildView(rule, revision, actor, configure.Succeeded));
  }

  /// <summary>Reads the current view for a caller's retained draft-creation request.</summary>
  public static async Task<CommandResult<WorkflowReminderRuleRevisionView>> ReceiptAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid requestKey, CancellationToken ct = default)
  {
    if (requestKey == Guid.Empty)
      return Invalid<WorkflowReminderRuleRevisionView>("Provide the reminder draft request key.");

    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);

    var receipt = await (
      from revision in db.WorkflowReminderRuleRevisions.AsNoTracking()
      join rule in db.WorkflowReminderRules.AsNoTracking()
        on new { revision.FirmId, revision.RuleId } equals new { rule.FirmId, RuleId = rule.Id }
      where revision.FirmId == actor.FirmId && revision.CreationRequestKey == requestKey &&
        revision.CreatedByUserId == actor.UserId
      select new { Revision = revision, Rule = rule })
      .SingleOrDefaultAsync(ct);

    // A configuration grant can be revoked while the receipt rows are being read.
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);
    if (receipt is null || !IsDigest(receipt.Revision.CreationRequestDigest))
      return Denied<WorkflowReminderRuleRevisionView>();

    return CommandResult<WorkflowReminderRuleRevisionView>.Ok(
      BuildView(receipt.Rule, receipt.Revision, actor, true));
  }

  public static async Task<CommandResult<WorkflowReminderRuleRevisionView>> SaveDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, WorkflowReminderRuleDraftRequest request,
    CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);
    if (request is null) return Invalid<WorkflowReminderRuleRevisionView>("Provide a complete reminder rule definition.");
    var normalized = NormalizeAndValidate(request.Definition);
    if (!normalized.Succeeded || normalized.Value is null)
      return Invalid<WorkflowReminderRuleRevisionView>(normalized.Message ?? "The reminder rule definition is invalid.");

    await using var tx = await BeginIfNeededAsync(db, ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<WorkflowReminderRuleRevisionView>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);

    var definition = normalized.Value;
    var creationDigest = CreationRequestDigest(request.RuleId, actor.UserId, definition);
    if (request.RequestKey != Guid.Empty)
    {
      var prior = await db.WorkflowReminderRuleRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.CreationRequestKey == request.RequestKey, ct);
      if (prior is not null)
      {
        auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
        if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);
        if (prior.CreatedByUserId != actor.UserId ||
            (request.RuleId.HasValue && prior.RuleId != request.RuleId.Value) ||
            prior.CreationRequestDigest != creationDigest)
          return CommandResult<WorkflowReminderRuleRevisionView>.Fail(ErrorCodes.IdempotencyConflict,
            "This reminder request key is already bound to different creation intent.");

        var priorRule = await db.WorkflowReminderRules.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == actor.FirmId && x.Id == prior.RuleId, ct);
        auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
        if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);
        if (priorRule is null) return Denied<WorkflowReminderRuleRevisionView>();
        await CommitIfOwnedAsync(tx, ct);
        return CommandResult<WorkflowReminderRuleRevisionView>.Ok(BuildView(priorRule, prior, actor, true));
      }
    }

    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);

    var now = DateTimeOffset.UtcNow;
    WorkflowReminderRule rule;
    WorkflowReminderRuleRevision revision;
    if (request.RuleId is null)
    {
      if (request.ExpectedRevision.HasValue)
        return Stale<WorkflowReminderRuleRevisionView>("A new reminder rule must not include an existing revision.");
      if (request.RequestKey == Guid.Empty)
        return Invalid<WorkflowReminderRuleRevisionView>("A request key is required to create a reminder rule.");
      rule = new WorkflowReminderRule
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, CreatedByUserId = actor.UserId,
        CreatedAt = now, Revision = 1
      };
      revision = NewRevision(actor, rule.Id, 1, definition, now, request.RequestKey, creationDigest);
      db.WorkflowReminderRules.Add(rule);
      db.WorkflowReminderRuleRevisions.Add(revision);
      db.WorkflowReminderRuleDecisions.Add(Decision(actor, rule.Id, revision.Id,
        WorkflowReminderDecisionKinds.DraftCreated, revision.ContentDigest, "Draft revision created.", now));
      await db.SaveChangesAsync(ct);
      await CommitIfOwnedAsync(tx, ct);
      return CommandResult<WorkflowReminderRuleRevisionView>.Ok(BuildView(rule, revision, actor, true));
    }

    var ruleId = request.RuleId.Value;
    var lockedRule = await LockRuleAsync(db, actor.FirmId, ruleId, ct);
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);
    if (lockedRule is null) return Denied<WorkflowReminderRuleRevisionView>();
    rule = lockedRule;
    var revisions = await db.WorkflowReminderRuleRevisions.Where(x => x.FirmId == actor.FirmId && x.RuleId == rule.Id)
      .OrderByDescending(x => x.Version).ToListAsync(ct);
    var latest = revisions.FirstOrDefault();
    if (latest is null) return CommandResult<WorkflowReminderRuleRevisionView>.Fail(ErrorCodes.ProtectedState, "The reminder rule has no saved revision.");

    if (latest.Status == WorkflowReminderRuleStatuses.Draft)
    {
      var draft = await LockRevisionAsync(db, actor.FirmId, rule.Id, latest.Id, ct);
      auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
      if (!auth.Succeeded) return Fail<WorkflowReminderRuleRevisionView>(auth);
      if (draft is null) return Denied<WorkflowReminderRuleRevisionView>();
      if (!request.ExpectedRevision.HasValue || request.ExpectedRevision.Value != rule.Revision)
        return Stale<WorkflowReminderRuleRevisionView>("The reminder rule changed; reload it before saving.");
      if (!TryGetVerifiedDefinition(draft, out _))
        return CommandResult<WorkflowReminderRuleRevisionView>.Fail("workflow.reminder.invalid", "The saved reminder draft does not match its content digest.");
      ApplyDefinition(draft, definition);
      draft.ContentDigest = Digest(definition);
      draft.LastEditedByUserId = actor.UserId;
      draft.UpdatedAt = now;
      draft.Revision++;
      rule.Revision++;
      db.WorkflowReminderRuleDecisions.Add(Decision(actor, rule.Id, draft.Id,
        WorkflowReminderDecisionKinds.DraftEdited, draft.ContentDigest, "Draft definition edited.", now));
      await db.SaveChangesAsync(ct);
      await CommitIfOwnedAsync(tx, ct);
      return CommandResult<WorkflowReminderRuleRevisionView>.Ok(BuildView(rule, draft, actor, true));
    }

    if (latest.Status == WorkflowReminderRuleStatuses.Reviewed)
      return CommandResult<WorkflowReminderRuleRevisionView>.Fail(ErrorCodes.ProtectedState,
        "A reviewed reminder revision is immutable. Publish it or create a later revision after publication.");
    if (!request.ExpectedRevision.HasValue || request.ExpectedRevision.Value != rule.Revision)
      return Stale<WorkflowReminderRuleRevisionView>("The reminder rule changed; reload it before creating a revision.");
    if (rule.CurrentPublishedRevisionId is null || latest.Status is not (WorkflowReminderRuleStatuses.Published or WorkflowReminderRuleStatuses.Superseded))
      return CommandResult<WorkflowReminderRuleRevisionView>.Fail(ErrorCodes.ProtectedState,
        "A new reminder revision can be created only after a rule has been published.");
    if (latest.Status == WorkflowReminderRuleStatuses.Published && latest.Id != rule.CurrentPublishedRevisionId)
      return CommandResult<WorkflowReminderRuleRevisionView>.Fail(ErrorCodes.GateBlocked, "The current published reminder revision is inconsistent.");

    if (request.RequestKey == Guid.Empty)
      return Invalid<WorkflowReminderRuleRevisionView>("A request key is required to create a reminder revision.");
    revision = NewRevision(actor, rule.Id, revisions[0].Version + 1, definition, now,
      request.RequestKey, creationDigest);
    rule.Revision++;
    db.WorkflowReminderRuleRevisions.Add(revision);
    db.WorkflowReminderRuleDecisions.Add(Decision(actor, rule.Id, revision.Id,
      WorkflowReminderDecisionKinds.DraftCreated, revision.ContentDigest, "Draft revision created.", now));
    await db.SaveChangesAsync(ct);
    await CommitIfOwnedAsync(tx, ct);
    return CommandResult<WorkflowReminderRuleRevisionView>.Ok(BuildView(rule, revision, actor, true));
  }

  public static async Task<CommandResult> ReviewAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId, Guid revisionId,
    WorkflowReminderRuleDecisionRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (!ValidRationale(request?.Rationale, out var rationale))
      return CommandResult.Fail("workflow.reminder.invalid", "Review rationale must contain 5 to 1,000 characters.");

    await using var tx = await BeginIfNeededAsync(db, ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var rule = await LockRuleAsync(db, actor.FirmId, ruleId, ct);
    var revision = rule is null ? null : await LockRevisionAsync(db, actor.FirmId, ruleId, revisionId, ct);
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (rule is null || revision is null) return Denied();
    if (revision.Status != WorkflowReminderRuleStatuses.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft reminder revision can be reviewed.");
    if (revision.Revision != request!.ExpectedRevision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The reminder draft changed; review the current revision.");
    if (revision.CreatedByUserId == actor.UserId || revision.LastEditedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "A reminder rule author or last editor cannot review that revision.");
    if (!TryGetVerifiedDefinition(revision, out var canonicalDefinition))
      return CommandResult.Fail("workflow.reminder.invalid", "The saved reminder definition is invalid or its digest does not match.");
    var digest = Digest(canonicalDefinition);
    if (!IsDigest(request.ContentDigest) || digest != revision.ContentDigest || digest != request.ContentDigest)
      return CommandResult.Fail("workflow.reminder.invalid", "The saved reminder definition or review digest is invalid.");

    var now = DateTimeOffset.UtcNow;
    revision.Status = WorkflowReminderRuleStatuses.Reviewed;
    revision.ReviewedByUserId = actor.UserId;
    revision.ReviewedAt = now;
    revision.Revision++;
    revision.UpdatedAt = now;
    rule.Revision++;
    db.WorkflowReminderRuleDecisions.Add(Decision(actor, rule.Id, revision.Id,
      WorkflowReminderDecisionKinds.Reviewed, digest, rationale, now));
    await db.SaveChangesAsync(ct);
    await CommitIfOwnedAsync(tx, ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> PublishAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId, Guid revisionId,
    WorkflowReminderRuleDecisionRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (!ValidRationale(request?.Rationale, out var rationale))
      return CommandResult.Fail("workflow.reminder.invalid", "Publication rationale must contain 5 to 1,000 characters.");

    await using var tx = await BeginIfNeededAsync(db, ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var rule = await LockRuleAsync(db, actor.FirmId, ruleId, ct);
    var revision = rule is null ? null : await LockRevisionAsync(db, actor.FirmId, ruleId, revisionId, ct);
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (rule is null || revision is null) return Denied();
    if (revision.Status != WorkflowReminderRuleStatuses.Reviewed || revision.Revision != request!.ExpectedRevision ||
        revision.ReviewedByUserId is null || revision.ReviewedByUserId == revision.CreatedByUserId ||
        revision.ReviewedByUserId == revision.LastEditedByUserId)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Publish requires the exact independently reviewed reminder revision.");
    if (!TryGetVerifiedDefinition(revision, out var canonicalDefinition))
      return CommandResult.Fail("workflow.reminder.invalid", "The reviewed reminder definition is invalid or its digest does not match.");
    var digest = Digest(canonicalDefinition);
    if (!IsDigest(request.ContentDigest) || digest != revision.ContentDigest || digest != request.ContentDigest)
      return CommandResult.Fail("workflow.reminder.invalid", "The reviewed reminder definition or publication digest is invalid.");

    WorkflowReminderRuleRevision? current = null;
    if (rule.CurrentPublishedRevisionId is { } currentId)
    {
      current = currentId == revision.Id ? revision : await LockRevisionAsync(db, actor.FirmId, rule.Id, currentId, ct);
      if (current is null || current.Status != WorkflowReminderRuleStatuses.Published)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "The current published reminder revision is inconsistent.");
    }

    var now = DateTimeOffset.UtcNow;
    if (current is not null)
    {
      current.Status = WorkflowReminderRuleStatuses.Superseded;
      current.Revision++;
      current.UpdatedAt = now;
      db.WorkflowReminderRuleDecisions.Add(Decision(actor, rule.Id, current.Id,
        WorkflowReminderDecisionKinds.Superseded, current.ContentDigest,
        $"Superseded by revision {revision.Version}.", now));
    }
    revision.Status = WorkflowReminderRuleStatuses.Published;
    revision.PublishedByUserId = actor.UserId;
    revision.PublishedSessionEpoch = actor.SessionEpoch;
    revision.PublishedAt = now;
    revision.UpdatedAt = now;
    revision.Revision++;
    rule.CurrentPublishedRevisionId = revision.Id;
    rule.Revision++;
    db.WorkflowReminderRuleDecisions.Add(Decision(actor, rule.Id, revision.Id,
      WorkflowReminderDecisionKinds.Published, digest, rationale, now));
    await db.SaveChangesAsync(ct);
    await CommitIfOwnedAsync(tx, ct);
    return CommandResult.Ok();
  }

  public static Task<CommandResult> SuspendAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId, WorkflowReminderRuleStateRequest request,
    CancellationToken ct = default) => ChangeStateAsync(db, actor, ruleId, request, suspend: true, ct);

  public static Task<CommandResult> ResumeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId, WorkflowReminderRuleStateRequest request,
    CancellationToken ct = default) => ChangeStateAsync(db, actor, ruleId, request, suspend: false, ct);

  public static async Task<CommandResult<WorkflowReminderRuleHistory>> HistoryAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId, Guid? afterId = null,
    int pageSize = 25, CancellationToken ct = default)
  {
    if (pageSize is < 1 or > 100 || afterId == Guid.Empty)
      return Invalid<WorkflowReminderRuleHistory>("Provide a valid bounded history cursor.");
    var auth = await AuthorizeReadAsync(db, actor, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderRuleHistory>(auth);
    var exists = await db.WorkflowReminderRules.AsNoTracking()
      .AnyAsync(x => x.FirmId == actor.FirmId && x.Id == ruleId, ct);
    if (!exists) return Denied<WorkflowReminderRuleHistory>();
    var query = db.WorkflowReminderRuleDecisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.RuleId == ruleId);
    if (afterId is { } cursor)
    {
      var cursorExists = await db.WorkflowReminderRuleDecisions.AsNoTracking()
        .AnyAsync(x => x.FirmId == actor.FirmId && x.RuleId == ruleId && x.Id == cursor, ct);
      if (!cursorExists) return Invalid<WorkflowReminderRuleHistory>("The history cursor is not part of this reminder rule.");
      query = query.Where(x => x.Id.CompareTo(cursor) < 0);
    }
    var decisions = await query.OrderByDescending(x => x.Id).Take(pageSize + 1).ToListAsync(ct);
    var hasMore = decisions.Count > pageSize;
    if (hasMore) decisions.RemoveAt(decisions.Count - 1);
    var items = decisions.Select(x => new WorkflowReminderRuleDecisionView(x.Id, x.RuleRevisionId, x.Kind,
      x.ContentDigest, x.Rationale, x.ActorUserId, x.OccurredAt.ToUniversalTime())).ToArray();
    return CommandResult<WorkflowReminderRuleHistory>.Ok(new(items, hasMore, hasMore && decisions.Count > 0 ? decisions[^1].Id : null));
  }

  internal static CommandResult<WorkflowReminderRuleDefinition> NormalizeAndValidate(
    WorkflowReminderRuleDefinition? definition)
  {
    if (definition is null)
      return Invalid<WorkflowReminderRuleDefinition>("Provide every required reminder rule field.");
    var name = (definition.Name ?? string.Empty).Trim();
    var source = (definition.SourceKind ?? string.Empty).Trim().ToUpperInvariant();
    var recipient = (definition.RecipientKind ?? string.Empty).Trim().ToUpperInvariant();
    var zone = (definition.TimeZoneId ?? string.Empty).Trim();
    var quietStart = (definition.QuietHoursStart ?? string.Empty).Trim();
    var quietEnd = (definition.QuietHoursEnd ?? string.Empty).Trim();
    if (name.Length is < 2 or > 160 ||
        source is not (WorkflowReminderSourceKinds.TaskDue or WorkflowReminderSourceKinds.PbcDue) ||
        recipient is not (WorkflowReminderRecipientKinds.ResponsibleStaff or WorkflowReminderRecipientKinds.EngagementReviewer) ||
        definition.OffsetDays is < -30 or > 365 || definition.RepeatEveryDays is < 0 or > 90 ||
        zone.Length is < 1 or > 100 || !ValidTime(quietStart) || !ValidTime(quietEnd) ||
        definition.EffectiveFromUtc == default)
      return Invalid<WorkflowReminderRuleDefinition>("Enter a name, supported source and recipient, bounded thresholds, timezone, quiet hours, and effective date.");
    try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone); }
    catch (TimeZoneNotFoundException) { return Invalid<WorkflowReminderRuleDefinition>("Choose a supported timezone."); }
    catch (InvalidTimeZoneException) { return Invalid<WorkflowReminderRuleDefinition>("Choose a valid timezone."); }

    return CommandResult<WorkflowReminderRuleDefinition>.Ok(new WorkflowReminderRuleDefinition(name, source, recipient,
      definition.OffsetDays, definition.RepeatEveryDays, zone, quietStart, quietEnd,
      definition.EffectiveFromUtc.ToUniversalTime()));
  }

  internal static string Digest(WorkflowReminderRuleDefinition definition) =>
    Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition, Json)));

  private static string CreationRequestDigest(
    Guid? requestedRuleId, Guid creatorUserId, WorkflowReminderRuleDefinition definition) =>
    Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
      new { requestedRuleId, creatorUserId, definition }, Json)));

  private static async Task<CommandResult> ChangeStateAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId, WorkflowReminderRuleStateRequest request,
    bool suspend, CancellationToken ct)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (!ValidRationale(request?.Rationale, out var rationale))
      return CommandResult.Fail("workflow.reminder.invalid", "State-change rationale must contain 5 to 1,000 characters.");
    if (request!.PublishedRevisionId == Guid.Empty || !IsDigest(request.ContentDigest))
      return CommandResult.Fail("workflow.reminder.invalid", "Reference the exact published revision and its content digest.");

    await using var tx = await BeginIfNeededAsync(db, ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var rule = await LockRuleAsync(db, actor.FirmId, ruleId, ct);
    var revision = rule is null ? null : await LockRevisionAsync(db, actor.FirmId, ruleId, request.PublishedRevisionId, ct);
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return auth;
    if (rule is null || revision is null) return Denied();
    if (rule.Revision != request.ExpectedRevision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The reminder rule state changed; reload before changing it.");
    if (rule.CurrentPublishedRevisionId != revision.Id || revision.Status != WorkflowReminderRuleStatuses.Published ||
        !TryGetVerifiedDefinition(revision, out _) || request.ContentDigest != revision.ContentDigest)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The current published reminder revision changed or its digest is invalid.");
    if (suspend == rule.SuspendedAt.HasValue)
      return CommandResult.Fail(ErrorCodes.ProtectedState, suspend ? "The reminder rule is already suspended." : "The reminder rule is not suspended.");

    var now = DateTimeOffset.UtcNow;
    if (suspend)
    {
      rule.SuspendedAt = now;
      rule.SuspendedByUserId = actor.UserId;
      rule.SuspensionReason = rationale;
    }
    else
    {
      rule.SuspendedAt = null;
      rule.SuspendedByUserId = null;
      rule.SuspensionReason = null;
    }
    rule.Revision++;
    db.WorkflowReminderRuleDecisions.Add(Decision(actor, rule.Id, revision.Id,
      suspend ? WorkflowReminderDecisionKinds.Suspended : WorkflowReminderDecisionKinds.Resumed,
      revision.ContentDigest, rationale, now));
    await db.SaveChangesAsync(ct);
    await CommitIfOwnedAsync(tx, ct);
    return CommandResult.Ok();
  }

  private static WorkflowReminderRuleRevisionView BuildView(
    WorkflowReminderRule rule, WorkflowReminderRuleRevision revision, ActorContext actor, bool canManage)
  {
    var digestMatches = TryGetVerifiedDefinition(revision, out var definition);
    var currentPublished = rule.CurrentPublishedRevisionId == revision.Id && revision.Status == WorkflowReminderRuleStatuses.Published;
    var independentReview = revision.ReviewedByUserId is not null &&
      revision.ReviewedByUserId != revision.CreatedByUserId && revision.ReviewedByUserId != revision.LastEditedByUserId;
    var canReview = canManage && revision.Status == WorkflowReminderRuleStatuses.Draft && digestMatches &&
      revision.CreatedByUserId != actor.UserId && revision.LastEditedByUserId != actor.UserId;
    var canPublish = canManage && revision.Status == WorkflowReminderRuleStatuses.Reviewed && independentReview && digestMatches;
    var canSuspend = canManage && currentPublished && !rule.SuspendedAt.HasValue && digestMatches;
    var canResume = canManage && currentPublished && rule.SuspendedAt.HasValue && digestMatches;
    string? blocker = !digestMatches ? "Saved rule content does not match its canonical digest."
      : revision.Status == WorkflowReminderRuleStatuses.Published && !currentPublished ? "This published revision is no longer current."
      : currentPublished && rule.SuspendedAt.HasValue ? "This rule is suspended."
      : revision.Status is not WorkflowReminderRuleStatuses.Published ? "Only a published rule can create reminder intents."
      : null;
    return new WorkflowReminderRuleRevisionView(rule.Id, revision.Id, revision.Version, definition,
      revision.Status, revision.ContentDigest, revision.Revision, rule.Revision, revision.CreatedByUserId,
      revision.LastEditedByUserId, revision.ReviewedByUserId, revision.PublishedByUserId,
      revision.CreatedAt.ToUniversalTime(), revision.UpdatedAt.ToUniversalTime(),
      revision.ReviewedAt?.ToUniversalTime(), revision.PublishedAt?.ToUniversalTime(), rule.SuspendedAt.HasValue,
      canManage && revision.Status == WorkflowReminderRuleStatuses.Draft && digestMatches,
      canReview, canPublish, canSuspend, canResume, blocker);
  }

  private static WorkflowReminderRuleRevision NewRevision(
    ActorContext actor, Guid ruleId, int version, WorkflowReminderRuleDefinition definition, DateTimeOffset now,
    Guid creationRequestKey, string creationRequestDigest)
  {
    var revision = new WorkflowReminderRuleRevision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, RuleId = ruleId, Version = version,
      Status = WorkflowReminderRuleStatuses.Draft, CreatedByUserId = actor.UserId,
      LastEditedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now, Revision = 1,
      CreationRequestKey = creationRequestKey, CreationRequestDigest = creationRequestDigest
    };
    ApplyDefinition(revision, definition);
    revision.ContentDigest = Digest(definition);
    return revision;
  }

  private static WorkflowReminderRuleDefinition ToDefinition(WorkflowReminderRuleRevision revision) =>
    new(revision.Name, revision.SourceKind, revision.RecipientKind, revision.OffsetDays, revision.RepeatEveryDays,
      revision.TimeZoneId, revision.QuietHoursStart, revision.QuietHoursEnd, revision.EffectiveFromUtc);

  private static bool TryGetVerifiedDefinition(
    WorkflowReminderRuleRevision revision, out WorkflowReminderRuleDefinition definition)
  {
    var normalized = NormalizeAndValidate(ToDefinition(revision));
    if (normalized.Succeeded && normalized.Value is { } value && Digest(value) == revision.ContentDigest)
    {
      definition = value;
      return true;
    }
    definition = ToDefinition(revision);
    return false;
  }

  private static void ApplyDefinition(WorkflowReminderRuleRevision revision, WorkflowReminderRuleDefinition definition)
  {
    revision.Name = definition.Name;
    revision.SourceKind = definition.SourceKind;
    revision.RecipientKind = definition.RecipientKind;
    revision.OffsetDays = definition.OffsetDays;
    revision.RepeatEveryDays = definition.RepeatEveryDays;
    revision.TimeZoneId = definition.TimeZoneId;
    revision.QuietHoursStart = definition.QuietHoursStart;
    revision.QuietHoursEnd = definition.QuietHoursEnd;
    revision.EffectiveFromUtc = definition.EffectiveFromUtc;
  }

  private static WorkflowReminderRuleDecision Decision(
    ActorContext actor, Guid ruleId, Guid revisionId, string kind, string digest, string rationale, DateTimeOffset now) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = actor.FirmId, RuleId = ruleId, RuleRevisionId = revisionId,
    Kind = kind, ContentDigest = digest, Rationale = rationale, ActorUserId = actor.UserId, OccurredAt = now
  };

  private static bool ValidRationale(string? value, out string rationale)
  {
    rationale = (value ?? string.Empty).Trim();
    return rationale.Length is >= 5 and <= 1000;
  }

  private static bool ValidTime(string value) =>
    value.Length == 5 && TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
      DateTimeStyles.None, out _);

  private static bool IsDigest(string? value) => value is { Length: 64 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);

  internal static Task<CommandResult> AuthorizeReadAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default) =>
    AuthorizeAsync(db, actor, ReadRoles, ct);

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static Task<WorkflowReminderRule?> LockRuleAsync(
    IAuditSphereDbContext db, Guid firmId, Guid ruleId, CancellationToken ct) =>
    db.WorkflowReminderRules.FromSqlInterpolated($"SELECT * FROM workflow_reminder_rules WHERE firm_id = {firmId} AND id = {ruleId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static Task<WorkflowReminderRuleRevision?> LockRevisionAsync(
    IAuditSphereDbContext db, Guid firmId, Guid ruleId, Guid revisionId, CancellationToken ct) =>
    db.WorkflowReminderRuleRevisions.FromSqlInterpolated($"SELECT * FROM workflow_reminder_rule_revisions WHERE firm_id = {firmId} AND rule_id = {ruleId} AND id = {revisionId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginIfNeededAsync(
    IAuditSphereDbContext db, CancellationToken ct) => db.Database.CurrentTransaction is null
      ? BeginAsync(db, ct)
      : Task.FromResult<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?>(null);

  private static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(
    IAuditSphereDbContext db, CancellationToken ct) => await db.Database.BeginTransactionAsync(ct);

  private static async Task CommitIfOwnedAsync(
    Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx, CancellationToken ct)
  {
    if (tx is not null) await tx.CommitAsync(ct);
  }

  private static CommandResult Denied() => CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  private static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  private static CommandResult<T> Invalid<T>(string message) => CommandResult<T>.Fail("workflow.reminder.invalid", message);
  private static CommandResult<T> Stale<T>(string message) => CommandResult<T>.Fail(ErrorCodes.StaleRevision, message);
  private static CommandResult<T> Fail<T>(CommandResult result) => CommandResult<T>.Fail(result.ErrorCode!, result.Message!);
}
