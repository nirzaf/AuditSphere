using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// M24 statement-layout authoring: bounded, typed layout drafts validated by the pure
// layout engine, then independently published as an immutable version. A published
// layout is never edited; a change creates a successor version.
public sealed record StatementLayoutDraftRequest(
  Guid ClientId, string FrameworkPolicy, string Name,
  IReadOnlyList<StatementLineDefinition> Lines, Guid? SupersedesLayoutId = null);

public sealed record StatementLayoutValue(
  Guid LayoutVersionId, string Status, int LineCount, string LayoutHash, long Revision);

public sealed record StatementLayoutValidationValue(
  Guid LayoutVersionId, bool IsValid, string LayoutHashDirectory,
  IReadOnlyList<StatementLayoutValidationError> Errors);

public static class StatementLayoutService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "Staff", "Manager", "Partner", "Administrator"];
  private static readonly string[] PublisherRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  /// <summary>Creates or replaces a draft layout for a client/framework/name. The draft is
  /// validated immediately; an invalid grammar is stored with its errors so the author can
  /// correct it, but it can never be published.</summary>
  public static async Task<CommandResult<StatementLayoutValue>> SaveLayoutDraftAsync(
    IClientAccountingDbContext db, ActorContext actor, StatementLayoutDraftRequest request,
    CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.FrameworkPolicy) ||
        string.IsNullOrWhiteSpace(request.Name) || request.FrameworkPolicy.Trim().Length > 100 ||
        request.Name.Trim().Length > 200 || request.Lines is null || request.Lines.Count == 0)
      return CommandResult<StatementLayoutValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A layout draft needs the client, framework policy, name and at least one line.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, RequiredRoles: PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<StatementLayoutValue>.Fail(auth.ErrorCode!, auth.Message!);

    var framework = request.FrameworkPolicy.Trim();
    var name = request.Name.Trim();
    var lines = Normalize(request.Lines);
    var errors = StatementLayoutEngine.Validate(lines);
    // Schema-level defects (an unsupported kind or section, or an empty layout) can never
    // be stored: they are refused outright. Structural defects (unknown references, cycles,
    // double counting) are stored as an invalid draft so the author can correct them, and
    // publication stays blocked while any error remains.
    var schemaDefects = errors.Where(x =>
      x.Code is "line.kind" or "line.section" or "layout.empty" or "line.display-sign").ToArray();
    if (schemaDefects.Length > 0)
      return CommandResult<StatementLayoutValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        $"The layout uses unsupported definitions: {string.Join("; ", schemaDefects.Select(x => $"{x.Code}:{x.Message}"))}");
    var hash = ComputeLayoutHash(lines);
    var summary = errors.Count == 0 ? null : string.Join("; ", errors.Select(x => $"{x.Code}:{x.Message}"));
    if (summary is { Length: > 4000 }) summary = summary[..4000];

    var existing = await db.StatementLayoutVersions
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.FrameworkPolicy == framework && x.Name == name && x.Status != StatementLayoutStatuses.Published)
      .OrderByDescending(x => x.VersionNumber)
      .FirstOrDefaultAsync(ct);
    if (existing is not null && existing.CreatedByUserId != actor.UserId)
      return CommandResult<StatementLayoutValue>.Fail(ErrorCodes.ScopeDenied,
        "Only the authoring preparer can revise this layout draft.");

    StatementLayoutVersion version;
    if (existing is null)
    {
      var predecessor = await db.StatementLayoutVersions.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
          x.FrameworkPolicy == framework && x.Name == name)
        .OrderByDescending(x => x.VersionNumber)
        .Select(x => (int?)x.VersionNumber).FirstOrDefaultAsync(ct) ?? 0;
      version = new StatementLayoutVersion
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
        FrameworkPolicy = framework, Name = name, VersionNumber = predecessor + 1,
        Status = StatementLayoutStatuses.Draft, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow,
        SupersedesLayoutId = request.SupersedesLayoutId
      };
      db.StatementLayoutVersions.Add(version);
    }
    else
    {
      version = existing;
      db.StatementLayoutLines.RemoveRange(
        await db.StatementLayoutLines.Where(x => x.FirmId == actor.FirmId && x.LayoutVersionId == version.Id).ToListAsync(ct));
    }
    version.Status = errors.Count == 0 ? StatementLayoutStatuses.Validated : StatementLayoutStatuses.Draft;
    version.LineCount = lines.Count;
    version.LayoutHash = hash;
    version.ValidationSummary = summary;
    var now = DateTimeOffset.UtcNow;
    foreach (var line in lines)
      db.StatementLayoutLines.Add(new StatementLayoutLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, LayoutVersionId = version.Id,
        LineCode = line.LineCode, Label = line.Label, LineKind = line.Kind, Section = line.Section,
        LineOrder = line.Order, DisplaySign = line.DisplaySign, TaxonomyNodeCode = line.TaxonomyNodeCode,
        ChildLineCodesJson = Serialize(line.ChildLineCodes), ReferencedLineCodesJson = Serialize(line.ReferencedLineCodes),
        OperationsJson = Serialize(line.Operations), DenominatorLineCode = line.DenominatorLineCode,
        ZeroDenominatorYieldsZero = line.ZeroDenominatorYieldsZero, IsSubtotal = line.IsSubtotal, CreatedAt = now
      });
    await db.SaveChangesAsync(ct);
    return CommandResult<StatementLayoutValue>.Ok(new StatementLayoutValue(
      version.Id, version.Status, version.LineCount, version.LayoutHash, version.VersionNumber));
  }

  /// <summary>Re-validates a stored layout. Evaluation of a published layout must already
  /// have passed, so a stored layout that fails now is reported rather than published.</summary>
  public static async Task<CommandResult<StatementLayoutValidationValue>> ValidateLayoutAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid layoutVersionId,
    CancellationToken ct = default)
  {
    var version = await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == layoutVersionId && x.FirmId == actor.FirmId, ct);
    if (version is null)
      return CommandResult<StatementLayoutValidationValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, version.ClientId, RequiredRoles: PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<StatementLayoutValidationValue>.Fail(auth.ErrorCode!, auth.Message!);
    var lines = await LoadLinesAsync(db, actor.FirmId, version.Id, ct);
    var errors = StatementLayoutEngine.Validate(lines);
    return CommandResult<StatementLayoutValidationValue>.Ok(new StatementLayoutValidationValue(
      version.Id, errors.Count == 0, version.LayoutHash, errors));
  }

  /// <summary>Independently publishes a validated layout. An invalid or already published
  /// version is refused; publishing records the publisher and timestamp immutably.</summary>
  public static async Task<CommandResult<StatementLayoutValue>> PublishLayoutAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid layoutVersionId, string reason,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
      return CommandResult<StatementLayoutValue>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Publishing a layout requires a reason of at most 2000 characters.");
    var version = await db.StatementLayoutVersions.SingleOrDefaultAsync(x =>
      x.Id == layoutVersionId && x.FirmId == actor.FirmId, ct);
    if (version is null)
      return CommandResult<StatementLayoutValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, version.ClientId, RequiredRoles: PublisherRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<StatementLayoutValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (version.Status == StatementLayoutStatuses.Published)
      return CommandResult<StatementLayoutValue>.Fail(ErrorCodes.ProtectedState, "This layout version is already published.");
    if (version.CreatedByUserId == actor.UserId)
      return CommandResult<StatementLayoutValue>.Fail(ErrorCodes.ScopeDenied,
        "Separation of duties: the layout author cannot publish their own version.");

    var lines = await LoadLinesAsync(db, actor.FirmId, version.Id, ct);
    var errors = StatementLayoutEngine.Validate(lines);
    if (errors.Count > 0)
      return CommandResult<StatementLayoutValue>.Fail(ErrorCodes.Accounting.MappingIncomplete,
        $"The layout is not publishable: {errors[0].Code} {errors[0].Message}");

    version.Status = StatementLayoutStatuses.Published;
    version.PublishedByUserId = actor.UserId;
    version.PublishedAt = DateTimeOffset.UtcNow;
    version.PublishReason = reason.Trim();
    version.ValidationSummary = null;
    await db.SaveChangesAsync(ct);
    return CommandResult<StatementLayoutValue>.Ok(new StatementLayoutValue(
      version.Id, version.Status, version.LineCount, version.LayoutHash, version.VersionNumber));
  }

  /// <summary>Paged lines of a layout version in presentation order.</summary>
  public static async Task<CommandResult<IReadOnlyList<StatementLayoutLine>>> GetLayoutLinesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid layoutVersionId, CancellationToken ct = default)
  {
    var version = await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == layoutVersionId && x.FirmId == actor.FirmId, ct);
    if (version is null)
      return CommandResult<IReadOnlyList<StatementLayoutLine>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, version.ClientId, RequiredRoles: PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<StatementLayoutLine>>.Fail(auth.ErrorCode!, auth.Message!);
    var lines = await db.StatementLayoutLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.LayoutVersionId == version.Id)
      .OrderBy(x => x.Section).ThenBy(x => x.LineOrder)
      .ToListAsync(ct);
    return CommandResult<IReadOnlyList<StatementLayoutLine>>.Ok(lines);
  }

  /// <summary>Evaluates the layout against supplied taxonomy balances, returning canonical
  /// amounts per line code. Display signs are applied by the caller and never alter these.</summary>
  public static async Task<CommandResult<IReadOnlyDictionary<string, decimal>>> EvaluateLayoutAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid layoutVersionId,
    IReadOnlyDictionary<string, decimal> taxonomyBalances, CancellationToken ct = default)
  {
    var version = await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == layoutVersionId && x.FirmId == actor.FirmId, ct);
    if (version is null)
      return CommandResult<IReadOnlyDictionary<string, decimal>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, version.ClientId, RequiredRoles: PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyDictionary<string, decimal>>.Fail(auth.ErrorCode!, auth.Message!);
    var lines = await LoadLinesAsync(db, actor.FirmId, version.Id, ct);
    try
    {
      return CommandResult<IReadOnlyDictionary<string, decimal>>.Ok(
        StatementLayoutEngine.Evaluate(lines, taxonomyBalances ?? new Dictionary<string, decimal>()));
    }
    catch (ArgumentException ex)
    {
      return CommandResult<IReadOnlyDictionary<string, decimal>>.Fail(ErrorCodes.Accounting.MappingIncomplete, ex.Message);
    }
  }

  private static List<StatementLineDefinition> Normalize(IReadOnlyList<StatementLineDefinition> lines) =>
    lines.Select(x => x with
    {
      LineCode = x.LineCode.Trim().ToUpperInvariant(),
      Section = x.Section.Trim().ToUpperInvariant()
    }).ToList();

  private static async Task<List<StatementLineDefinition>> LoadLinesAsync(
    IClientAccountingDbContext db, Guid firmId, Guid layoutVersionId, CancellationToken ct)
  {
    var stored = await db.StatementLayoutLines.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.LayoutVersionId == layoutVersionId)
      .OrderBy(x => x.Section).ThenBy(x => x.LineOrder)
      .ToListAsync(ct);
    return stored.Select(x => new StatementLineDefinition(
      x.LineCode, x.Label, x.LineKind, x.Section, x.LineOrder, x.DisplaySign, x.TaxonomyNodeCode,
      Deserialize(x.ChildLineCodesJson), Deserialize(x.ReferencedLineCodesJson), Deserialize(x.OperationsJson),
      x.ZeroDenominatorYieldsZero, x.DenominatorLineCode, x.IsSubtotal)).ToList();
  }

  private static string? Serialize(IReadOnlyList<string>? values) =>
    values is null || values.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(values);

  private static IReadOnlyList<string>? Deserialize(string? json) =>
    string.IsNullOrWhiteSpace(json) ? null : System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);

  private static string ComputeLayoutHash(IReadOnlyList<StatementLineDefinition> lines) =>
    Hashing.Sha256Hex(string.Join('\n', lines
      .OrderBy(x => x.Section, StringComparer.Ordinal).ThenBy(x => x.Order)
      .Select(x => string.Join('|', x.LineCode, x.Kind, x.Section, x.Order, x.DisplaySign,
        x.TaxonomyNodeCode ?? string.Empty,
        string.Join(',', x.ChildLineCodes ?? []), string.Join(',', x.ReferencedLineCodes ?? []),
        string.Join(',', x.Operations ?? []), x.DenominatorLineCode ?? string.Empty))));
}
