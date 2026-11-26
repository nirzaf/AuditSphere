using System.Data.Common;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AuditSphereOps.Application.Accounting;

public sealed record StatementLayoutSaveInput(Guid RequestId, Guid? LayoutVersionId, string? ExpectedLayoutHash,
  StatementLayoutDraftRequest Draft, bool Reviewed);
public sealed record StatementLayoutPublishInput(Guid RequestId, string ExpectedLayoutHash, string Reason, bool Reviewed);
public sealed record StatementLayoutCatalogueItem(Guid LayoutVersionId, string FrameworkPolicy, string Name,
  int VersionNumber, string Status, string LayoutHash, int LineCount, Guid AuthorId, Guid? PublisherId,
  DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, bool CanEdit, bool CanPublish);
public sealed record StatementLayoutCataloguePage(Guid ClientId, int Page, int PageSize, int TotalCount,
  bool HasMore, bool CanCreate, bool CanPublish, IReadOnlyList<StatementLayoutCatalogueItem> Items);
public sealed record StatementLayoutDetail(Guid LayoutVersionId, Guid ClientId, string FrameworkPolicy, string Name,
  int VersionNumber, string Status, string LayoutHash, int LineCount, Guid AuthorId, Guid? PublisherId,
  Guid? SupersedesLayoutId, string? PublishReason, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt,
  bool CanEdit, bool CanPublish, IReadOnlyList<StatementLineDefinition> Lines,
  IReadOnlyList<StatementLayoutValidationError> Errors);
public sealed record StatementLayoutCommandResult(StatementLayoutValue Layout,
  StatementLayoutValidationValue Validation);
public sealed record StatementLayoutCommandReceiptView(Guid Id, Guid FirmId, Guid ClientId, Guid ActorId,
  string ActorEpoch, Guid RequestId, string Action, string RequestHash, Guid LayoutVersionId,
  string? ExpectedLayoutHash, string ResultLayoutHash, string ResultStatus, int ResultVersionNumber,
  string InputJson, string ResultJson, DateTimeOffset CreatedAt);
public sealed record StatementLayoutCommandLookup(bool Found, StatementLayoutCommandReceiptView? Receipt);

/// <summary>Reviewed staff producer and recovery boundary for client-owned statement layouts.</summary>
public static class StatementLayoutWorkspace
{
  public const int PageSize = 50;
  public const int MaximumInputJsonLength = 120000;
  public const int MaximumResultJsonLength = 1000000;
  private const int MaximumLines = 500;
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "Staff", "Manager", "Partner", "Administrator"];
  private static readonly string[] PublisherRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReaderRoles = ["AccountingPreparer", "AccountingReviewer", "Staff", "Manager", "Partner", "Administrator"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task<CommandResult<StatementLayoutCataloguePage>> CatalogueAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, int page, CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || page is < 0 or > 100000)
      return Fail<StatementLayoutCataloguePage>(ErrorCodes.Accounting.MappingInvalid, "Choose a valid client layout page.");
    var auth = await AuthorizeAsync(db, actor, clientId, ReaderRoles, ct);
    if (!auth.Succeeded) return Fail<StatementLayoutCataloguePage>(auth.ErrorCode!, auth.Message!);
    var canCreate = (await AuthorizeAsync(db, actor, clientId, PreparerRoles, ct)).Succeeded;
    var canPublish = (await AuthorizeAsync(db, actor, clientId, PublisherRoles, ct)).Succeeded;
    var latest = db.StatementLayoutVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId)
      .Where(x => !db.StatementLayoutVersions.Any(newer => newer.FirmId == x.FirmId && newer.ClientId == x.ClientId &&
        newer.FrameworkPolicy == x.FrameworkPolicy && newer.Name == x.Name && newer.VersionNumber > x.VersionNumber));
    var total = await latest.CountAsync(ct);
    var items = await latest.OrderBy(x => x.FrameworkPolicy).ThenBy(x => x.Name)
      .Skip(page * PageSize).Take(PageSize + 1)
      .Select(x => new StatementLayoutCatalogueItem(x.Id, x.FrameworkPolicy, x.Name, x.VersionNumber, x.Status,
        x.LayoutHash, x.LineCount, x.CreatedByUserId, x.PublishedByUserId, x.CreatedAt, x.PublishedAt,
        canCreate && x.CreatedByUserId == actor.UserId && x.Status != StatementLayoutStatuses.Published,
        canPublish && x.CreatedByUserId != actor.UserId && x.Status == StatementLayoutStatuses.Validated &&
          !db.StatementLayoutLines.Any(line => line.FirmId == x.FirmId && line.LayoutVersionId == x.Id &&
            ((line.Section != StatementLayoutSections.FinancialPosition &&
              line.Section != StatementLayoutSections.ProfitOrLoss && line.Section != StatementLayoutSections.OtherComprehensiveIncome) ||
              line.LineKind == StatementLayoutLineKinds.Ratio))))
      .ToListAsync(ct);
    var hasMore = items.Count > PageSize;
    if (hasMore) items.RemoveAt(items.Count - 1);
    auth = await AuthorizeAsync(db, actor, clientId, ReaderRoles, ct);
    return auth.Succeeded
      ? CommandResult<StatementLayoutCataloguePage>.Ok(new(clientId, page, PageSize, total, hasMore, canCreate, canPublish, items))
      : Fail<StatementLayoutCataloguePage>(auth.ErrorCode!, auth.Message!);
  }

  public static async Task<CommandResult<StatementLayoutDetail>> DetailAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid layoutVersionId, CancellationToken ct = default)
  {
    if (layoutVersionId == Guid.Empty)
      return Fail<StatementLayoutDetail>(ErrorCodes.Accounting.MappingInvalid, "Choose a valid statement layout.");
    var version = await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == layoutVersionId && x.FirmId == actor.FirmId, ct);
    if (version is null) return Fail<StatementLayoutDetail>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, version.ClientId, ReaderRoles, ct);
    if (!auth.Succeeded) return Fail<StatementLayoutDetail>(auth.ErrorCode!, auth.Message!);
    var canEdit = version.CreatedByUserId == actor.UserId && version.Status != StatementLayoutStatuses.Published &&
      (await AuthorizeAsync(db, actor, version.ClientId, PreparerRoles, ct)).Succeeded;
    var publisherAuthorized = version.CreatedByUserId != actor.UserId && version.Status == StatementLayoutStatuses.Validated &&
      (await AuthorizeAsync(db, actor, version.ClientId, PublisherRoles, ct)).Succeeded;
    try
    {
      var lines = await StatementLayoutService.LoadLinesAsync(db, actor.FirmId, version.Id, ct);
      if (lines.Count > MaximumLines)
        return Fail<StatementLayoutDetail>(ErrorCodes.GateBlocked, "This layout exceeds the supported detail bound and requires administrator review.");
      var errors = StatementLayoutEngine.Validate(lines);
      var canPublish = publisherAuthorized && errors.Count == 0 && PresentationScopeValid(lines);
      var current = await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(
        x => x.Id == version.Id && x.FirmId == actor.FirmId && x.ClientId == version.ClientId, ct);
      if (current is null) return Fail<StatementLayoutDetail>(ErrorCodes.ScopeDenied, "Access denied.");
      if (!SameRevision(version, current))
        return Fail<StatementLayoutDetail>(ErrorCodes.StaleRevision, "The layout changed while it was being read. Refresh and review again.");
      auth = await AuthorizeAsync(db, actor, version.ClientId, ReaderRoles, ct);
      return auth.Succeeded
        ? CommandResult<StatementLayoutDetail>.Ok(new(version.Id, version.ClientId, version.FrameworkPolicy,
          version.Name, version.VersionNumber, version.Status, version.LayoutHash, version.LineCount,
          version.CreatedByUserId, version.PublishedByUserId, version.SupersedesLayoutId,
          version.PublishReason, version.CreatedAt, version.PublishedAt, canEdit, canPublish, lines, errors))
        : Fail<StatementLayoutDetail>(auth.ErrorCode!, auth.Message!);
    }
    catch (JsonException)
    {
      return Fail<StatementLayoutDetail>(ErrorCodes.Accounting.MappingIncomplete, "The retained layout definition cannot be read safely.");
    }
  }

  public static Task<CommandResult<StatementLayoutCommandReceiptView>> SaveDraftAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, StatementLayoutSaveInput? input,
    CancellationToken ct = default)
  {
    var draft = CanonicalDraft(input?.Draft, clientId);
    if (input is null || clientId == Guid.Empty || input.RequestId == Guid.Empty || !input.Reviewed || draft is null ||
        (input.LayoutVersionId is null && input.ExpectedLayoutHash is not null) ||
        (input.LayoutVersionId is not null && !HashValid(input.ExpectedLayoutHash)))
      return Task.FromResult(Fail<StatementLayoutCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid,
        "Review a bounded statement-layout draft with the exact client and current version."));
    var canonical = input with { Draft = draft };
    var inputJson = JsonSerializer.Serialize(new { Action = "SAVE", Input = canonical }, Json);
    if (inputJson.Length > MaximumInputJsonLength)
      return Task.FromResult(Fail<StatementLayoutCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid,
        "The statement-layout request exceeds the supported size."));
    return ExecuteAsync(db, actor, clientId, "SAVE", input.RequestId, input.ExpectedLayoutHash,
      inputJson, RequestHash(actor, clientId, "SAVE", inputJson), input.LayoutVersionId,
      async (lockedDb, lockedActor, ct2) =>
      {
        var identity = draft;
        var unpublished = await lockedDb.StatementLayoutVersions.Where(x => x.FirmId == lockedActor.FirmId &&
          x.ClientId == clientId && x.FrameworkPolicy == identity.FrameworkPolicy && x.Name == identity.Name &&
          x.Status != StatementLayoutStatuses.Published).OrderByDescending(x => x.VersionNumber).ToListAsync(ct2);
        StatementLayoutVersion? latest = await lockedDb.StatementLayoutVersions.AsNoTracking().Where(x =>
          x.FirmId == lockedActor.FirmId && x.ClientId == clientId && x.FrameworkPolicy == identity.FrameworkPolicy &&
          x.Name == identity.Name).OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync(ct2);

        if (input.LayoutVersionId is { } requestedId)
        {
          if (unpublished.Count != 1 || unpublished[0].Id != requestedId)
            return Fail<StatementLayoutValue>(ErrorCodes.StaleRevision, "The selected draft is no longer the current editable version.");
          var current = unpublished[0];
          if (current.CreatedByUserId != lockedActor.UserId)
            return Fail<StatementLayoutValue>(ErrorCodes.ScopeDenied, "Only the original author can revise this draft.");
          if (current.LayoutHash != input.ExpectedLayoutHash)
            return Fail<StatementLayoutValue>(ErrorCodes.StaleRevision, "The draft changed. Reload the current version and review again.");
          if (current.ClientId != clientId || current.FrameworkPolicy != identity.FrameworkPolicy || current.Name != identity.Name ||
              current.SupersedesLayoutId != identity.SupersedesLayoutId)
            return Fail<StatementLayoutValue>(ErrorCodes.StaleRevision, "A draft update cannot change its client, framework, name or predecessor.");
        }
        else
        {
          if (unpublished.Count != 0)
            return Fail<StatementLayoutValue>(ErrorCodes.StaleRevision, "An unpublished draft already exists. Open that exact version to revise it.");
          var expectedPredecessor = latest?.Id;
          if (latest is not null && latest.Status != StatementLayoutStatuses.Published)
            return Fail<StatementLayoutValue>(ErrorCodes.StaleRevision, "The latest layout version is not published and cannot be bypassed.");
          if (identity.SupersedesLayoutId != expectedPredecessor)
            return Fail<StatementLayoutValue>(ErrorCodes.StaleRevision, "Select the exact latest published predecessor for this layout identity.");
        }

        var saved = await StatementLayoutService.SaveLayoutDraftAsync(lockedDb, lockedActor, identity, ct2);
        return saved.Succeeded ? saved : Fail<StatementLayoutValue>(saved.ErrorCode!, saved.Message!);
      }, ct);
  }

  public static Task<CommandResult<StatementLayoutCommandReceiptView>> PublishAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid layoutVersionId,
    StatementLayoutPublishInput? input, CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || layoutVersionId == Guid.Empty || input is null || input.RequestId == Guid.Empty ||
        !input.Reviewed || !HashValid(input.ExpectedLayoutHash) || string.IsNullOrWhiteSpace(input.Reason) ||
        input.Reason.Trim().Length > 2000)
      return Task.FromResult(Fail<StatementLayoutCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid,
        "Review the exact current layout hash and a publication reason of at most 2000 characters."));
    var canonical = input with { Reason = input.Reason.Trim() };
    var inputJson = JsonSerializer.Serialize(new { Action = "PUBLISH", ClientId = clientId, LayoutVersionId = layoutVersionId, Input = canonical }, Json);
    if (inputJson.Length > MaximumInputJsonLength)
      return Task.FromResult(Fail<StatementLayoutCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid,
        "The statement-layout request exceeds the supported size."));
    return ExecuteAsync(db, actor, clientId, "PUBLISH", input.RequestId, input.ExpectedLayoutHash,
      inputJson, RequestHash(actor, clientId, "PUBLISH", inputJson), layoutVersionId,
      async (lockedDb, lockedActor, ct2) =>
      {
        var version = await lockedDb.StatementLayoutVersions.SingleOrDefaultAsync(x => x.FirmId == lockedActor.FirmId &&
          x.ClientId == clientId && x.Id == layoutVersionId, ct2);
        if (version is null) return Fail<StatementLayoutValue>(ErrorCodes.ScopeDenied, "Access denied.");
        if (version.LayoutHash != canonical.ExpectedLayoutHash)
          return Fail<StatementLayoutValue>(ErrorCodes.StaleRevision, "The layout changed. Reload the current version and review again.");
        var lines = await StatementLayoutService.LoadLinesAsync(lockedDb, lockedActor.FirmId, version.Id, ct2);
        if (!PresentationScopeValid(lines))
          return Fail<StatementLayoutValue>(ErrorCodes.Accounting.MappingIncomplete,
            "The current statement renderer does not support ratio or supplementary-section layout lines.");
        return await StatementLayoutService.PublishLayoutAsync(lockedDb, lockedActor, layoutVersionId, canonical.Reason, ct2);
      }, ct);
  }

  public static async Task<CommandResult<StatementLayoutCommandLookup>> LookupAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid requestId, CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || requestId == Guid.Empty)
      return Fail<StatementLayoutCommandLookup>(ErrorCodes.Accounting.MappingInvalid, "Choose a valid retained layout request.");
    var auth = await AuthorizeAsync(db, actor, clientId, ReaderRoles, ct);
    if (!auth.Succeeded) return Fail<StatementLayoutCommandLookup>(auth.ErrorCode!, auth.Message!);
    var row = await db.StatementLayoutCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is null)
    {
      auth = await AuthorizeAsync(db, actor, clientId, ReaderRoles, ct);
      return auth.Succeeded ? CommandResult<StatementLayoutCommandLookup>.Ok(new(false, null))
        : Fail<StatementLayoutCommandLookup>(auth.ErrorCode!, auth.Message!);
    }
    auth = await AuthorizeAsync(db, actor, clientId, ReaderRoles, ct);
    if (!auth.Succeeded) return Fail<StatementLayoutCommandLookup>(auth.ErrorCode!, auth.Message!);
    if (row.ActorEpoch != actor.SessionEpoch)
      return Fail<StatementLayoutCommandLookup>(ErrorCodes.GenerationStale, "This request belongs to an earlier session lifetime.");
    auth = await AuthorizeAsync(db, actor, clientId, ReaderRoles, ct);
    return auth.Succeeded
      ? CommandResult<StatementLayoutCommandLookup>.Ok(new(true, Receipt(row)))
      : Fail<StatementLayoutCommandLookup>(auth.ErrorCode!, auth.Message!);
  }

  private static async Task<CommandResult<StatementLayoutCommandReceiptView>> ExecuteAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, string action, Guid requestId,
    string? expectedLayoutHash, string inputJson, string requestHash, Guid? layoutVersionId,
    Func<IClientAccountingDbContext, ActorContext, CancellationToken, Task<CommandResult<StatementLayoutValue>>> mutate,
    CancellationToken ct)
  {
    var roles = action == "PUBLISH" ? PublisherRoles : PreparerRoles;
    var auth = await AuthorizeAsync(db, actor, clientId, roles, ct);
    if (!auth.Succeeded) return Fail<StatementLayoutCommandReceiptView>(auth.ErrorCode!, auth.Message!);
    if (db is not DbContext context)
      return Fail<StatementLayoutCommandReceiptView>(ErrorCodes.GateBlocked, "The statement-layout command requires the supported EF transaction boundary.");

    var owned = db.Database.CurrentTransaction is null;
    await using var ownedTransaction = owned ? await db.Database.BeginTransactionAsync(ct) : null;
    var transaction = db.Database.CurrentTransaction!;
    var savepoint = "statement_layout_" + Guid.NewGuid().ToString("N");
    await transaction.CreateSavepointAsync(savepoint, ct);
    var tracked = Capture(context);
    var active = true;
    async Task RollBackAsync()
    {
      if (!active) return;
      await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
      Restore(context, tracked);
      await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
      active = false;
    }
    async Task CompleteAsync()
    {
      if (!active) return;
      await transaction.ReleaseSavepointAsync(savepoint, ct);
      active = false;
      if (owned) await transaction.CommitAsync(ct);
    }

    try
    {
      if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      {
        await RollBackAsync();
        return Fail<StatementLayoutCommandReceiptView>(ErrorCodes.ScopeDenied, "This statement-layout workspace is unavailable.");
      }
      auth = await AuthorizeAsync(db, actor, clientId, roles, ct);
      if (!auth.Succeeded)
      {
        await RollBackAsync();
        return Fail<StatementLayoutCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      }
      var client = await db.PracticeClients.FromSqlInterpolated(
        $"SELECT * FROM practice_clients WHERE firm_id = {actor.FirmId} AND id = {clientId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if (client is null)
      {
        await RollBackAsync();
        return Fail<StatementLayoutCommandReceiptView>(ErrorCodes.ScopeDenied, "This statement-layout workspace is unavailable.");
      }
      auth = await AuthorizeAsync(db, actor, clientId, roles, ct);
      if (!auth.Succeeded)
      {
        await RollBackAsync();
        return Fail<StatementLayoutCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      }
      if (layoutVersionId is { } id)
      {
        var locked = await db.StatementLayoutVersions.FromSqlInterpolated(
          $"SELECT * FROM statement_layout_versions WHERE firm_id = {actor.FirmId} AND client_id = {clientId} AND id = {id} FOR UPDATE")
          .AsNoTracking().SingleOrDefaultAsync(ct);
        if (locked is null)
        {
          await RollBackAsync();
          return Fail<StatementLayoutCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
        }
      }
      auth = await AuthorizeAsync(db, actor, clientId, roles, ct);
      if (!auth.Succeeded)
      {
        await RollBackAsync();
        return Fail<StatementLayoutCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      }

      var prior = await db.StatementLayoutCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
      if (prior is not null)
      {
        if (prior.ActorEpoch != actor.SessionEpoch)
        {
          await RollBackAsync();
          return Fail<StatementLayoutCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to an earlier session lifetime.");
        }
        if (prior.Action != action || prior.RequestHash != requestHash || prior.InputJson != inputJson ||
            prior.ExpectedLayoutHash != expectedLayoutHash || (layoutVersionId is { } retryId && prior.LayoutVersionId != retryId))
        {
          await RollBackAsync();
          return Fail<StatementLayoutCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed statement-layout intent cannot reuse this request identity.");
        }
        auth = await AuthorizeAsync(db, actor, clientId, roles, ct);
        if (!auth.Succeeded)
        {
          await RollBackAsync();
          return Fail<StatementLayoutCommandReceiptView>(auth.ErrorCode!, auth.Message!);
        }
        var priorView = Receipt(prior);
        await CompleteAsync();
        return CommandResult<StatementLayoutCommandReceiptView>.Ok(priorView);
      }

      var changed = await mutate(db, actor, ct);
      if (!changed.Succeeded)
      {
        await RollBackAsync();
        return Fail<StatementLayoutCommandReceiptView>(changed.ErrorCode!, changed.Message!);
      }
      var value = changed.Value!;
      var validationResult = await StatementLayoutService.ValidateLayoutAsync(db, actor, value.LayoutVersionId, ct);
      if (!validationResult.Succeeded)
      {
        await RollBackAsync();
        return Fail<StatementLayoutCommandReceiptView>(validationResult.ErrorCode!, validationResult.Message!);
      }
      var resultJson = JsonSerializer.Serialize(new StatementLayoutCommandResult(value, validationResult.Value!), Json);
      if (resultJson.Length > MaximumResultJsonLength)
      {
        await RollBackAsync();
        return Fail<StatementLayoutCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "The layout validation result exceeds the supported size.");
      }
      var receipt = new StatementLayoutCommandReceipt
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, ActorId = actor.UserId,
        ActorEpoch = actor.SessionEpoch, RequestId = requestId, Action = action, RequestHash = requestHash,
        LayoutVersionId = value.LayoutVersionId, ExpectedLayoutHash = expectedLayoutHash,
        ResultLayoutHash = value.LayoutHash, ResultStatus = value.Status, ResultVersionNumber = checked((int)value.Revision),
        InputJson = inputJson, ResultJson = resultJson, CreatedAt = DateTimeOffset.UtcNow
      };
      db.StatementLayoutCommandReceipts.Add(receipt);
      await db.SaveChangesAsync(ct);
      var persisted = await db.StatementLayoutCommandReceipts.AsNoTracking().SingleAsync(x =>
        x.Id == receipt.Id && x.FirmId == actor.FirmId && x.ClientId == clientId && x.ActorId == actor.UserId, ct);
      auth = await AuthorizeAsync(db, actor, clientId, roles, ct);
      if (!auth.Succeeded)
      {
        await RollBackAsync();
        return Fail<StatementLayoutCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      }
      var view = Receipt(persisted);
      await CompleteAsync();
      return CommandResult<StatementLayoutCommandReceiptView>.Ok(view);
    }
    catch (Exception ex)
    {
      await RollBackAsync();
      if (ex.GetBaseException() is DbException cause &&
          cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
        return Fail<StatementLayoutCommandReceiptView>(ErrorCodes.IdempotencyConflict,
          "A concurrent statement-layout request changed the outcome. Recover the retained request before retrying.");
      throw;
    }
  }

  private static StatementLayoutDraftRequest? CanonicalDraft(StatementLayoutDraftRequest? draft, Guid clientId)
  {
    if (draft is null || draft.ClientId != clientId || draft.ClientId == Guid.Empty ||
        !Bounded(draft.FrameworkPolicy, 100) || !Bounded(draft.Name, 200) || draft.Lines is null ||
        draft.Lines.Count is < 1 or > MaximumLines) return null;
    var lines = new List<StatementLineDefinition>(draft.Lines.Count);
    foreach (var line in draft.Lines)
    {
      if (line is null || !Bounded(line.LineCode, 60) || !Bounded(line.Label, 300) ||
          !Bounded(line.Kind, 40) || !Bounded(line.Section, 40) || !Bounded(line.DisplaySign, 20) ||
          !NullableBounded(line.TaxonomyNodeCode, 100) || !NullableBounded(line.DenominatorLineCode, 60) ||
          !ReferencesValid(line.ChildLineCodes) || !ReferencesValid(line.ReferencedLineCodes) ||
          line.Operations is { Count: > MaximumLines } || line.Operations?.Any(x => !Bounded(x, 20)) == true) return null;
      lines.Add(line with
      {
        LineCode = line.LineCode.Trim().ToUpperInvariant(), Label = line.Label.Trim(),
        Kind = line.Kind.Trim().ToUpperInvariant(), Section = line.Section.Trim().ToUpperInvariant(),
        DisplaySign = line.DisplaySign.Trim().ToUpperInvariant(), TaxonomyNodeCode = line.TaxonomyNodeCode?.Trim(),
        DenominatorLineCode = line.DenominatorLineCode?.Trim(),
        ChildLineCodes = CanonicalReferences(line.ChildLineCodes),
        ReferencedLineCodes = CanonicalReferences(line.ReferencedLineCodes),
        Operations = line.Operations?.Select(x => x.Trim().ToUpperInvariant()).ToArray()
      });
    }
    return new StatementLayoutDraftRequest(clientId, draft.FrameworkPolicy.Trim(), draft.Name.Trim(), lines,
      draft.SupersedesLayoutId == Guid.Empty ? null : draft.SupersedesLayoutId);
  }

  private static bool ReferencesValid(IReadOnlyList<string>? values) => values is null ||
    values.Count <= MaximumLines && values.All(x => x is not null && x.Length <= 60);
  private static IReadOnlyList<string>? CanonicalReferences(IReadOnlyList<string>? values) =>
    values?.Select(x => x.Trim().ToUpperInvariant()).ToArray();
  private static bool Bounded(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= limit;
  private static bool NullableBounded(string? value, int limit) => value is null || value.Trim().Length <= limit;
  private static bool HashValid(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static string RequestHash(ActorContext actor, Guid clientId, string action, string inputJson) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, ClientId = clientId, actor.UserId, actor.SessionEpoch, Action = action, InputJson = inputJson }, Json));
  private static bool SameRevision(StatementLayoutVersion a, StatementLayoutVersion b) => a.Id == b.Id &&
    a.ClientId == b.ClientId && a.FrameworkPolicy == b.FrameworkPolicy && a.Name == b.Name &&
    a.VersionNumber == b.VersionNumber && a.Status == b.Status && a.LayoutHash == b.LayoutHash &&
    a.LineCount == b.LineCount && a.ValidationSummary == b.ValidationSummary &&
    a.PublishedByUserId == b.PublishedByUserId && a.PublishedAt == b.PublishedAt;
  private static bool PresentationScopeValid(IReadOnlyList<StatementLineDefinition> lines) =>
    lines.All(x => FinancialStatementPresentationPolicy.SupportedSections.Contains(x.Section) &&
      x.Kind != StatementLayoutLineKinds.Ratio);
  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId,
    string[] roles, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
    new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);
  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);
  private static StatementLayoutCommandReceiptView Receipt(StatementLayoutCommandReceipt row) => new(row.Id,
    row.FirmId, row.ClientId, row.ActorId, row.ActorEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture), row.RequestId, row.Action, row.RequestHash,
    row.LayoutVersionId, row.ExpectedLayoutHash, row.ResultLayoutHash, row.ResultStatus,
    row.ResultVersionNumber, row.InputJson, row.ResultJson, row.CreatedAt);
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);

  private sealed record TrackedLayout(object Entity, EntityState State, PropertyValues Current, PropertyValues Original);
  private static IReadOnlyList<TrackedLayout> Capture(DbContext db) => db.ChangeTracker.Entries()
    .Where(x => x.Entity is StatementLayoutVersion or StatementLayoutLine or StatementLayoutCommandReceipt)
    .Select(x => new TrackedLayout(x.Entity, x.State, x.CurrentValues.Clone(), x.OriginalValues.Clone())).ToArray();
  private static void Restore(DbContext db, IReadOnlyList<TrackedLayout> prior)
  {
    foreach (var entry in db.ChangeTracker.Entries()
      .Where(x => x.Entity is StatementLayoutVersion or StatementLayoutLine or StatementLayoutCommandReceipt).ToArray())
    {
      var before = prior.FirstOrDefault(x => ReferenceEquals(x.Entity, entry.Entity));
      if (before is null) entry.State = EntityState.Detached;
      else
      {
        entry.CurrentValues.SetValues(before.Current);
        entry.OriginalValues.SetValues(before.Original);
        entry.State = before.State;
      }
    }
  }
}
