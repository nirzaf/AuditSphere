using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record BankReconciliationCreateFields(Guid? ProcedureId, Guid LedgerScheduleId, Guid StatementScheduleId,
  DateOnly AsOfDate, IReadOnlyList<BankReconciliationItemInput> Items);
public sealed record BankReconciliationReviewFields(Guid ReconciliationId, string Decision, string Conclusion);
public sealed record BankReconciliationCommandRequest(Guid RequestId, string ExpectedGeneration, string ReviewBasis,
  BankReconciliationCreateFields? Create = null, BankReconciliationReviewFields? Review = null,
  string? ExpectedRequestHash = null, bool Reviewed = false);
public sealed record BankReconciliationSourceOption(Guid Id, string ScheduleType, string EntityIdentifier, DateOnly? AsOfDate,
  string Currency, string SignedControlTotal, string SourceHash, string Status, string InputGeneration);
public sealed record BankReconciliationItemSnapshot(Guid Id, string StableItemId, string ItemType, string SignedAmount,
  string Currency, string Description, string SourceReference, string EvidenceReference, Guid? SourceScheduleId, Guid? ProposedJournalId);
public sealed record BankReconciliationSnapshot(Guid Id, Guid ClientId, Guid EngagementId, Guid? ProcedureId,
  Guid LedgerScheduleId, Guid StatementScheduleId, DateOnly AsOfDate, string Currency, string LedgerBalance,
  string StatementBalance, string TimingItemTotal, string ProposedCorrectionTotal, string Residual, string Status,
  string InputGeneration, string? Conclusion, Guid CreatedByUserId, Guid? ReviewedByUserId,
  DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt, IReadOnlyList<BankReconciliationItemSnapshot> Items);
public sealed record BankReconciliationCommandState(Guid EngagementId, Guid ClientId, string ExpectedGeneration,
  string ReviewBasis, bool CanCreate, bool CanReview, IReadOnlyList<BankReconciliationSourceOption> Sources,
  BankReconciliationSnapshot? Record, IReadOnlyList<string> Blockers);
public sealed record BankReconciliationCommandPreview(Guid RequestId, string Action, string RequestHash,
  string ReviewBasis, string ExpectedGeneration, bool CanProceed, IReadOnlyList<string> Blockers,
  BankReconciliationSnapshot? CurrentRecord, BankReconciliationValue? Calculated, string Summary);
public sealed record BankReconciliationCommandReceiptView(Guid Id, Guid ClientId, Guid EngagementId,
  Guid BankReconciliationId, Guid ActorId, string ActorEpoch, Guid RequestId, string RequestHash,
  string ReviewBasis, string Action, DateTimeOffset CreatedAt, BankReconciliationCommandPreview Preview,
  BankReconciliationSnapshot Result);
public sealed record BankReconciliationCommandLookup(bool Found, BankReconciliationCommandReceiptView? Receipt);

/// <summary>Scoped bank-reconciliation commands with exact preview, independent review and actor-owned recovery.</summary>
public static class BankReconciliationWorkspace
{
  private const string CreateAction = "CREATE";
  private const string ReviewAction = "REVIEW";
  private const int MaximumSources = 200;
  private const int MaximumItems = 1000;
  private static readonly string[] PreparerRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewerRoles = ["Reviewer", "Manager", "Partner", "Administrator"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  private sealed record Basis(Guid ClientId, long EngagementGeneration, long InputGeneration,
    bool ProfessionalWorkBlocked, IReadOnlyList<AuditSchedule> Sources, AuditBankReconciliation? Record,
    IReadOnlyList<AuditBankReconciliationItem> Items);

  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string Long(long value) => value.ToString(CultureInfo.InvariantCulture);
  private static string Amount(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
  private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool PositiveLong(string? value, out long parsed) =>
    long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
  private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);

  private static async Task<CommandResult<(Guid ClientId, long EngagementGeneration)>> ParentAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var row = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    return row is null
      ? Fail<(Guid, long)>(ErrorCodes.ScopeDenied, "Access denied.")
      : CommandResult<(Guid, long)>.Ok((row.PracticeClientId, row.Generation));
  }

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId,
    Guid engagementId, string action, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, engagementId, action == CreateAction ? PreparerRoles : ReviewerRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);

  private static async Task<CommandResult<Basis>> ReadBasisAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid engagementId, Guid? reconciliationId, CancellationToken ct)
  {
    var parent = await ParentAsync(db, actor, engagementId, ct);
    if (!parent.Succeeded) return Fail<Basis>(parent.ErrorCode!, parent.Message!);
    var clientId = parent.Value.ClientId;
    var readable = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, engagementId, PreparerRoles.Concat(ReviewerRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), InternalOnly: true), ct);
    if (!readable.Succeeded) return Fail<Basis>(readable.ErrorCode!, readable.Message!);
    var inputGeneration = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct) ?? 0;
    var blocked = await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == engagementId)
      .Select(x => x.ProfessionalWorkBlocked).SingleAsync(ct);
    var sources = await db.AuditSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && (x.ScheduleType.ToUpper() == "BANK_LEDGER" || x.ScheduleType.ToUpper() == "BANK_STATEMENT"))
      .OrderBy(x => x.ScheduleType).ThenBy(x => x.AsOfDate).ThenBy(x => x.Id).Take(MaximumSources + 1).ToListAsync(ct);
    if (sources.Count > MaximumSources)
      return Fail<Basis>(ErrorCodes.GateBlocked, "The approved bank-source catalogue exceeds its bounded review limit.");
    AuditBankReconciliation? record = null;
    IReadOnlyList<AuditBankReconciliationItem> items = [];
    if (reconciliationId is { } id)
    {
      if (id == Guid.Empty) return Fail<Basis>(ErrorCodes.AuditPlanning.Invalid, "Select a bank reconciliation.");
      record = await db.AuditBankReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.EngagementId == engagementId && x.Id == id, ct);
      if (record is null) return Fail<Basis>(ErrorCodes.ScopeDenied, "Access denied.");
      items = await db.AuditBankReconciliationItems.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.EngagementId == engagementId && x.BankReconciliationId == id)
        .OrderBy(x => x.StableItemId).ThenBy(x => x.Id).Take(MaximumItems + 1).ToListAsync(ct);
      if (items.Count is 0 or > MaximumItems)
        return Fail<Basis>(ErrorCodes.ManifestMismatch, "The retained bank reconciliation items are incomplete or exceed their supported bound.");
    }
    return CommandResult<Basis>.Ok(new(clientId, parent.Value.EngagementGeneration, inputGeneration, blocked, sources, record, items));
  }

  private static BankReconciliationSourceOption Source(AuditSchedule row) => new(row.Id, row.ScheduleType.ToUpperInvariant(),
    row.EntityIdentifier, row.AsOfDate is { } date ? DateOnly.FromDateTime(date.UtcDateTime) : null,
    row.Currency, Amount(row.SignedControlTotal), row.SourceHash, row.Status, Long(row.InputGeneration));

  private static BankReconciliationItemSnapshot Item(AuditBankReconciliationItem row) => new(row.Id, row.StableItemId,
    row.ItemType, Amount(row.SignedAmount), row.Currency, row.Description, row.SourceReference, row.EvidenceReference,
    row.SourceScheduleId, row.ProposedJournalId);

  private static BankReconciliationSnapshot Snapshot(Basis basis, AuditBankReconciliation row) => new(row.Id,
    row.ClientId, row.EngagementId, row.ProcedureId, row.LedgerScheduleId, row.StatementScheduleId, row.AsOfDate,
    row.Currency, Amount(row.LedgerBalance), Amount(row.StatementBalance), Amount(row.TimingItemTotal),
    Amount(row.ProposedCorrectionTotal), Amount(row.Residual), row.Status, Long(row.InputGeneration), row.Conclusion,
    row.CreatedByUserId, row.ReviewedByUserId, row.CreatedAt, row.ReviewedAt,
    basis.Record?.Id == row.Id ? basis.Items.Select(Item).ToArray() : []);

  private static string BasisHash(ActorContext actor, Guid engagementId, Basis basis) => Hashing.Sha256Hex(JsonSerializer.Serialize(new
  {
    Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch, engagementId, basis.ClientId,
    basis.EngagementGeneration, basis.InputGeneration, basis.ProfessionalWorkBlocked,
    Sources = basis.Sources.Select(x => new { x.Id, x.ScheduleType, x.EntityIdentifier, x.AsOfDate, x.PeriodStart, x.PeriodEnd,
      x.Currency, x.SourceHash, x.Status, x.SignedControlTotal, x.InputGeneration }).ToArray(),
    Record = basis.Record is null ? null : new { basis.Record.Id, basis.Record.ProcedureId, basis.Record.LedgerScheduleId,
      basis.Record.StatementScheduleId, basis.Record.AsOfDate, basis.Record.Currency, basis.Record.LedgerBalance,
      basis.Record.StatementBalance, basis.Record.TimingItemTotal, basis.Record.ProposedCorrectionTotal,
      basis.Record.Residual, basis.Record.Status, basis.Record.InputGeneration, basis.Record.Conclusion,
      basis.Record.CreatedByUserId, basis.Record.ReviewedByUserId, basis.Record.CreatedAt, basis.Record.ReviewedAt },
    Items = basis.Items.Select(x => new { x.Id, x.StableItemId, x.ItemType, x.SignedAmount, x.Currency,
      x.Description, x.SourceReference, x.EvidenceReference, x.SourceScheduleId, x.ProposedJournalId }).ToArray()
  }));

  public static async Task<CommandResult<BankReconciliationCommandState>> StateAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, Guid? reconciliationId = null, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty) return Fail<BankReconciliationCommandState>(ErrorCodes.AuditPlanning.Invalid, "Select an engagement.");
    var basisResult = await ReadBasisAsync(db, actor, engagementId, reconciliationId, ct);
    if (!basisResult.Succeeded) return Fail<BankReconciliationCommandState>(basisResult.ErrorCode!, basisResult.Message!);
    var basis = basisResult.Value!;
    var create = await AuthorizeAsync(db, actor, basis.ClientId, engagementId, CreateAction, ct);
    var review = await AuthorizeAsync(db, actor, basis.ClientId, engagementId, ReviewAction, ct);
    var currentSources = basis.Sources.Where(x => x.Status == AuditScheduleStatuses.Approved && x.InputGeneration == basis.InputGeneration).ToArray();
    var blockers = new List<string>();
    if (basis.ProfessionalWorkBlocked) blockers.Add("Professional work is blocked for this engagement.");
    if (!currentSources.Any(x => x.ScheduleType.Equals("BANK_LEDGER", StringComparison.OrdinalIgnoreCase))) blockers.Add("A current approved BANK_LEDGER source is required.");
    if (!currentSources.Any(x => x.ScheduleType.Equals("BANK_STATEMENT", StringComparison.OrdinalIgnoreCase))) blockers.Add("A current approved BANK_STATEMENT source is required.");
    if (reconciliationId is { } && basis.Record is { } target && target.CreatedByUserId == actor.UserId)
      blockers.Add("The preparer cannot independently review the same reconciliation.");
    if (reconciliationId is { } && basis.Record?.Status == AuditBankReconciliationStatuses.Approved)
      blockers.Add("An approved bank reconciliation is immutable.");
    var record = basis.Record is null ? null : Snapshot(basis, basis.Record);
    return CommandResult<BankReconciliationCommandState>.Ok(new(engagementId, basis.ClientId,
      Long(Math.Max(1, basis.InputGeneration)), BasisHash(actor, engagementId, basis), create.Succeeded,
      review.Succeeded, currentSources.Select(Source).ToArray(), record, blockers));
  }

  private static BankReconciliationCreateFields? Canonical(BankReconciliationCreateFields? fields)
  {
    if (fields is null || fields.LedgerScheduleId == Guid.Empty || fields.StatementScheduleId == Guid.Empty ||
        fields.LedgerScheduleId == fields.StatementScheduleId || fields.ProcedureId == Guid.Empty ||
        fields.AsOfDate == default || fields.Items is null || fields.Items.Count is 0 or > MaximumItems ||
        fields.Items.Any(x => x is null || !Text(x.StableItemId, 200) || !Text(x.ItemType, 30) ||
          !Text(x.Description, 1000) || !Text(x.SourceReference, 500) || !Text(x.EvidenceReference, 2000) ||
          !AuditBankReconciliationItemTypes.All.Contains(x.ItemType.Trim().ToUpperInvariant()) ||
          decimal.Round(x.SignedAmount, 6) != x.SignedAmount ||
          x.SignedAmount is < -9_999_999_999_999.999999m or > 9_999_999_999_999.999999m) ||
        fields.Items.Select(x => x.StableItemId.Trim()).Distinct(StringComparer.Ordinal).Count() != fields.Items.Count)
      return null;
    return fields with { Items = fields.Items.Select(x => x with { StableItemId = x.StableItemId.Trim(),
      ItemType = x.ItemType.Trim().ToUpperInvariant(), Description = x.Description.Trim(),
      SourceReference = x.SourceReference.Trim(), EvidenceReference = x.EvidenceReference.Trim() }).ToArray() };
  }

  private static object CanonicalInput(string action, BankReconciliationCreateFields? create, BankReconciliationReviewFields? review) =>
    action == CreateAction
      ? new { Action = action, Fields = new { create!.ProcedureId, create.LedgerScheduleId, create.StatementScheduleId,
          create.AsOfDate, Items = create.Items.Select(x => new { x.StableItemId, x.ItemType, Amount = Amount(x.SignedAmount),
            x.Description, x.SourceReference, x.EvidenceReference, x.SourceScheduleId, x.ProposedJournalId }).ToArray() } }
      : new { Action = action, Fields = review };

  private static string RequestHash(ActorContext actor, Guid engagementId, Guid clientId, string action,
    string expectedGeneration, string reviewBasis, BankReconciliationCreateFields? create, BankReconciliationReviewFields? review) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch,
      clientId, engagementId, expectedGeneration, reviewBasis, Input = CanonicalInput(action, create, review) }));

  public static async Task<CommandResult<BankReconciliationCommandPreview>> PreviewCreateAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, BankReconciliationCommandRequest? request, CancellationToken ct = default)
  {
    if (request is null)
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Provide one bounded bank-reconciliation creation intent for preview.");
    if (request.RequestId == Guid.Empty || !Hash(request.ReviewBasis))
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Use a valid request identity and review basis for bank-reconciliation preview.");
    if (!PositiveLong(request.ExpectedGeneration, out _))
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Use the current positive decimal generation for bank-reconciliation preview.");
    if (request.ExpectedRequestHash is not null || request.Reviewed || request.Review is not null)
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Preview exactly one unreviewed bank-reconciliation creation intent.");
    var fields = Canonical(request.Create);
    if (fields is null)
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Provide bounded bank-reconciliation source and item fields for preview.");
    var state = await StateAsync(db, actor, engagementId, ct: ct);
    if (!state.Succeeded) return Fail<BankReconciliationCommandPreview>(state.ErrorCode!, state.Message!);
    var current = state.Value!;
    if (request.ReviewBasis != current.ReviewBasis || request.ExpectedGeneration != current.ExpectedGeneration)
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.GenerationStale, "The bank-reconciliation sources changed; refresh before preview.");
    if (!current.CanCreate) return Fail<BankReconciliationCommandPreview>(ErrorCodes.GateBlocked, "Current preparation authority is required.");
    var calculated = await AuditFieldworkService.PreviewBankReconciliationAsync(db, actor,
      new(engagementId, fields.ProcedureId, fields.LedgerScheduleId, fields.StatementScheduleId, fields.AsOfDate, fields.Items), ct);
    if (!calculated.Succeeded) return Fail<BankReconciliationCommandPreview>(calculated.ErrorCode!, calculated.Message!);
    var hash = RequestHash(actor, engagementId, current.ClientId, CreateAction, request.ExpectedGeneration,
      current.ReviewBasis, fields, null);
    var blockers = current.Blockers.Where(x => !x.StartsWith("A current approved BANK_", StringComparison.Ordinal)).ToArray();
    var preview = new BankReconciliationCommandPreview(request.RequestId, CreateAction, hash, current.ReviewBasis,
      current.ExpectedGeneration, blockers.Length == 0, blockers, null, calculated.Value,
      $"Create a {calculated.Value!.Currency} bank reconciliation dated {fields.AsOfDate:yyyy-MM-dd} with residual {Amount(calculated.Value.Residual)}.");
    if (JsonSerializer.Serialize(CanonicalInput(CreateAction, fields, null), Json).Length > AuditBankReconciliationCommandReceipt.MaximumInputJsonLength ||
        JsonSerializer.Serialize(preview, Json).Length > AuditBankReconciliationCommandReceipt.MaximumSnapshotJsonLength)
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "The bank reconciliation exceeds its retained evidence limit.");
    return CommandResult<BankReconciliationCommandPreview>.Ok(preview);
  }

  public static async Task<CommandResult<BankReconciliationCommandPreview>> PreviewReviewAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, Guid reconciliationId, BankReconciliationCommandRequest? request,
    CancellationToken ct = default)
  {
    if (request is null || request.RequestId == Guid.Empty || request.Create is not null || request.Review is null ||
        request.Review.ReconciliationId != reconciliationId || !Hash(request.ReviewBasis) ||
        !PositiveLong(request.ExpectedGeneration, out _) || request.ExpectedRequestHash is not null || request.Reviewed ||
        !Text(request.Review.Decision, 30) || !Text(request.Review.Conclusion, 4000))
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Provide one bounded bank-reconciliation review intent for preview.");
    var reviewFields = request.Review with { Decision = request.Review.Decision.Trim().ToUpperInvariant(), Conclusion = request.Review.Conclusion.Trim() };
    if (reviewFields.Decision is not (AuditBankReconciliationStatuses.Approved or AuditBankReconciliationStatuses.ChangesRequired))
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Choose an available bank-reconciliation review decision.");
    var state = await StateAsync(db, actor, engagementId, reconciliationId, ct);
    if (!state.Succeeded) return Fail<BankReconciliationCommandPreview>(state.ErrorCode!, state.Message!);
    var current = state.Value!;
    var record = current.Record!;
    if (request.ReviewBasis != current.ReviewBasis || request.ExpectedGeneration != current.ExpectedGeneration)
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.GenerationStale, "The bank-reconciliation review basis changed; refresh before preview.");
    if (!current.CanReview) return Fail<BankReconciliationCommandPreview>(ErrorCodes.GateBlocked, "Current independent review authority is required.");
    if (record.CreatedByUserId == actor.UserId) return Fail<BankReconciliationCommandPreview>(ErrorCodes.ScopeDenied, "The preparer cannot independently review the same reconciliation.");
    if (record.Status == AuditBankReconciliationStatuses.Approved) return Fail<BankReconciliationCommandPreview>(ErrorCodes.ProtectedState, "An approved bank reconciliation is immutable.");
    if (record.InputGeneration != current.ExpectedGeneration)
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.GenerationStale, "The bank reconciliation inputs are stale.");
    if (!current.Sources.Any(x => x.Id == record.LedgerScheduleId && x.ScheduleType == "BANK_LEDGER") ||
        !current.Sources.Any(x => x.Id == record.StatementScheduleId && x.ScheduleType == "BANK_STATEMENT"))
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.GenerationStale, "The approved bank sources changed; refresh the reconciliation.");
    if (reviewFields.Decision == AuditBankReconciliationStatuses.Approved && record.Residual != "0.000000")
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.GateBlocked, "A nonzero bank residual cannot be approved.");
    var hash = RequestHash(actor, engagementId, current.ClientId, ReviewAction, request.ExpectedGeneration,
      current.ReviewBasis, null, reviewFields);
    var blockers = current.Blockers.Where(x => !x.Contains("BANK_", StringComparison.Ordinal)).ToArray();
    var preview = new BankReconciliationCommandPreview(request.RequestId, ReviewAction, hash, current.ReviewBasis,
      current.ExpectedGeneration, blockers.Length == 0, blockers, record, null,
      $"Record {reviewFields.Decision.ToLowerInvariant()} for bank reconciliation {reconciliationId}.");
    if (JsonSerializer.Serialize(CanonicalInput(ReviewAction, null, reviewFields), Json).Length > AuditBankReconciliationCommandReceipt.MaximumInputJsonLength ||
        JsonSerializer.Serialize(preview, Json).Length > AuditBankReconciliationCommandReceipt.MaximumSnapshotJsonLength)
      return Fail<BankReconciliationCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "The bank reconciliation review exceeds its retained evidence limit.");
    return CommandResult<BankReconciliationCommandPreview>.Ok(preview);
  }

  private static BankReconciliationCommandReceiptView View(AuditBankReconciliationCommandReceipt row) => new(row.Id,
    row.ClientId, row.EngagementId, row.BankReconciliationId, row.ActorId, Long(row.ActorEpoch), row.RequestId,
    row.RequestHash, row.ReviewBasis, row.Action, row.CreatedAt,
    JsonSerializer.Deserialize<BankReconciliationCommandPreview>(row.PreviewJson, Json)!,
    JsonSerializer.Deserialize<BankReconciliationSnapshot>(row.ResultJson, Json)!);

  private static async Task LockScopeAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var locked = await AuditPlanningCommandWorkspace.LockAsync(db, actor, engagementId, true, ct);
    if (!locked.Succeeded) throw new BankReconciliationLockException(locked.ErrorCode!, locked.Message!);
    await AuditFieldworkService.LockConfirmationScopeAsync(db, actor, engagementId, ct);
  }

  private static async Task LockRequestAsync(IAuditSphereDbContext db, ActorContext actor, Guid requestId, CancellationToken ct)
  {
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "audit-bank-reconciliation-command-v1", actor.FirmId, actor.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
  }

  private sealed class BankReconciliationLockException(string code, string message) : Exception(message)
  {
    public string Code { get; } = code;
  }

  private static async Task<CommandResult<BankReconciliationCommandReceiptView>> ExecuteAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, BankReconciliationCommandRequest? request, string action, CancellationToken ct)
  {
    var create = action == CreateAction ? Canonical(request?.Create) : null;
    var review = action == ReviewAction && request?.Review is { } r && Text(r.Decision, 30) && Text(r.Conclusion, 4000)
      ? r with { Decision = r.Decision.Trim().ToUpperInvariant(), Conclusion = r.Conclusion.Trim() } : null;
    if (request is null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) ||
        !Hash(request.ExpectedRequestHash) || !PositiveLong(request.ExpectedGeneration, out _) || !request.Reviewed ||
        action == CreateAction && (create is null || request.Review is not null) ||
        action == ReviewAction && (review is null || request.Create is not null))
      return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.AuditPlanning.Invalid, "Preview and confirm one exact bank-reconciliation command.");
    var inputJson = JsonSerializer.Serialize(CanonicalInput(action, create, review), Json);
    if (inputJson.Length > AuditBankReconciliationCommandReceipt.MaximumInputJsonLength)
      return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.AuditPlanning.Invalid, "The retained bank-reconciliation command exceeds its evidence limit.");
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    try
    {
      await LockScopeAsync(db, actor, engagementId, ct);
      var parent = await ParentAsync(db, actor, engagementId, ct);
      if (!parent.Succeeded) return Fail<BankReconciliationCommandReceiptView>(parent.ErrorCode!, parent.Message!);
      var auth = await AuthorizeAsync(db, actor, parent.Value.ClientId, engagementId, action, ct);
      if (!auth.Succeeded) return Fail<BankReconciliationCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      Guid? targetId = review?.ReconciliationId;
      AuditBankReconciliation? target = null;
      if (targetId is { } id)
      {
        target = await db.AuditBankReconciliations.FromSqlInterpolated(
          $"SELECT * FROM audit_bank_reconciliations WHERE firm_id = {actor.FirmId} AND client_id = {parent.Value.ClientId} AND engagement_id = {engagementId} AND id = {id} FOR UPDATE")
          .SingleOrDefaultAsync(ct);
        if (target is null) return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
      }
      await LockRequestAsync(db, actor, request.RequestId, ct);
      var retained = await db.AuditBankReconciliationCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
      var hash = RequestHash(actor, engagementId, parent.Value.ClientId, action, request.ExpectedGeneration,
        request.ReviewBasis, create, review);
      if (retained is not null)
      {
        if (retained.ActorEpoch != actor.SessionEpoch)
          return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to an earlier session lifetime.");
        if (retained.ClientId != parent.Value.ClientId || retained.EngagementId != engagementId || retained.Action != action ||
            retained.ReviewBasis != request.ReviewBasis || retained.RequestHash != hash || retained.RequestHash != request.ExpectedRequestHash ||
            retained.InputJson != inputJson || targetId is not null && retained.BankReconciliationId != targetId)
          return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this bank-reconciliation request identity.");
        var receiptView = View(retained);
        var allowed = await AuthorizeAsync(db, actor, retained.ClientId, engagementId, action, ct);
        if (!allowed.Succeeded) return Fail<BankReconciliationCommandReceiptView>(allowed.ErrorCode!, allowed.Message!);
        if (tx is not null) await tx.CommitAsync(ct);
        return CommandResult<BankReconciliationCommandReceiptView>.Ok(receiptView);
      }
      var state = await StateAsync(db, actor, engagementId, targetId, ct);
      if (!state.Succeeded) return Fail<BankReconciliationCommandReceiptView>(state.ErrorCode!, state.Message!);
      var current = state.Value!;
      if (current.ClientId != parent.Value.ClientId || current.ExpectedGeneration != request.ExpectedGeneration || current.ReviewBasis != request.ReviewBasis)
        return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.GenerationStale, "The bank-reconciliation basis changed after preview; review the current source again.");
      if (hash != request.ExpectedRequestHash)
        return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted bank-reconciliation command differs from its exact preview.");
      BankReconciliationCommandPreview preview;
      BankReconciliationValue resultValue;
      if (action == CreateAction)
      {
        var previewResult = await PreviewCreateAsync(db, actor, engagementId,
          request with { Create = create, ExpectedRequestHash = null, Reviewed = false });
        if (!previewResult.Succeeded) return Fail<BankReconciliationCommandReceiptView>(previewResult.ErrorCode!, previewResult.Message!);
        preview = previewResult.Value!;
        if (!preview.CanProceed || preview.RequestHash != request.ExpectedRequestHash)
          return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.GenerationStale, "The bank reconciliation changed after review; prepare a fresh preview.");
        var created = await AuditFieldworkService.CreateBankReconciliationAsync(db, actor,
          new(engagementId, create!.ProcedureId, create.LedgerScheduleId, create.StatementScheduleId, create.AsOfDate, create.Items), ct);
        if (!created.Succeeded) return Fail<BankReconciliationCommandReceiptView>(created.ErrorCode!, created.Message!);
        resultValue = created.Value!;
        targetId = resultValue.BankReconciliationId;
      }
      else
      {
        var previewResult = await PreviewReviewAsync(db, actor, engagementId, review!.ReconciliationId,
          request with { Review = review, ExpectedRequestHash = null, Reviewed = false });
        if (!previewResult.Succeeded) return Fail<BankReconciliationCommandReceiptView>(previewResult.ErrorCode!, previewResult.Message!);
        preview = previewResult.Value!;
        if (!preview.CanProceed || preview.RequestHash != request.ExpectedRequestHash)
          return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.GenerationStale, "The bank reconciliation changed after review; prepare a fresh preview.");
        var reviewed = await AuditFieldworkService.ReviewBankReconciliationAsync(db, actor,
          new(review!.ReconciliationId, review.Decision, review.Conclusion), ct);
        if (!reviewed.Succeeded) return Fail<BankReconciliationCommandReceiptView>(reviewed.ErrorCode!, reviewed.Message!);
        resultValue = reviewed.Value!;
      }
      var finalBasis = await ReadBasisAsync(db, actor, engagementId, targetId, ct);
      if (!finalBasis.Succeeded) return Fail<BankReconciliationCommandReceiptView>(finalBasis.ErrorCode!, finalBasis.Message!);
      var resultRow = finalBasis.Value!.Record;
      if (resultRow is null) return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.ManifestMismatch, "The bank-reconciliation outcome could not be read back.");
      var snapshot = Snapshot(finalBasis.Value, resultRow);
      var previewJson = JsonSerializer.Serialize(preview, Json);
      var resultJson = JsonSerializer.Serialize(snapshot, Json);
      if (previewJson.Length > AuditBankReconciliationCommandReceipt.MaximumSnapshotJsonLength ||
          resultJson.Length > AuditBankReconciliationCommandReceipt.MaximumSnapshotJsonLength)
        return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.AuditPlanning.Invalid, "The retained bank-reconciliation outcome exceeds its evidence limit.");
      var receipt = new AuditBankReconciliationCommandReceipt
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = parent.Value.ClientId, EngagementId = engagementId,
        BankReconciliationId = targetId!.Value, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
        RequestId = request.RequestId, RequestHash = hash, ReviewBasis = request.ReviewBasis, Action = action,
        InputJson = inputJson, PreviewJson = previewJson, ResultJson = resultJson, CreatedAt = DateTimeOffset.UtcNow
      };
      db.AuditBankReconciliationCommandReceipts.Add(receipt);
      await db.SaveChangesAsync(ct);
      auth = await AuthorizeAsync(db, actor, parent.Value.ClientId, engagementId, action, ct);
      if (!auth.Succeeded) return Fail<BankReconciliationCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      var persisted = await db.AuditBankReconciliationCommandReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id, ct);
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<BankReconciliationCommandReceiptView>.Ok(View(persisted));
    }
    catch (BankReconciliationLockException ex)
    {
      if (tx is not null) await tx.RollbackAsync(ct);
      return Fail<BankReconciliationCommandReceiptView>(ex.Code, ex.Message);
    }
    catch (Exception ex) when (ex.GetBaseException() is DbException cause &&
      cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
    {
      if (tx is not null) await tx.RollbackAsync(ct);
      return Fail<BankReconciliationCommandReceiptView>(ErrorCodes.IdempotencyConflict,
        "A concurrent bank-reconciliation command changed the outcome. Look up the retained request before retrying.");
    }
  }

  public static Task<CommandResult<BankReconciliationCommandReceiptView>> ExecuteCreateAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, BankReconciliationCommandRequest? request, CancellationToken ct = default) =>
    ExecuteAsync(db, actor, engagementId, request, CreateAction, ct);

  public static Task<CommandResult<BankReconciliationCommandReceiptView>> ExecuteReviewAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, BankReconciliationCommandRequest? request, CancellationToken ct = default) =>
    ExecuteAsync(db, actor, engagementId, request, ReviewAction, ct);

  public static async Task<CommandResult<BankReconciliationCommandLookup>> LookupAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, Guid requestId, string requestHash, string action, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || requestId == Guid.Empty || !Hash(requestHash) || action is not (CreateAction or ReviewAction))
      return Fail<BankReconciliationCommandLookup>(ErrorCodes.AuditPlanning.Invalid, "Use the exact bank-reconciliation request reference.");
    var parent = await ParentAsync(db, actor, engagementId, ct);
    if (!parent.Succeeded) return Fail<BankReconciliationCommandLookup>(parent.ErrorCode!, parent.Message!);
    var auth = await AuthorizeAsync(db, actor, parent.Value.ClientId, engagementId, action, ct);
    if (!auth.Succeeded) return Fail<BankReconciliationCommandLookup>(auth.ErrorCode!, auth.Message!);
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    try { await LockScopeAsync(db, actor, engagementId, ct); }
    catch (BankReconciliationLockException ex)
    {
      if (tx is not null) await tx.RollbackAsync(ct);
      return Fail<BankReconciliationCommandLookup>(ex.Code, ex.Message);
    }
    var row = await db.AuditBankReconciliationCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == parent.Value.ClientId && x.EngagementId == engagementId &&
      x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is not null && (row.ActorEpoch != actor.SessionEpoch || row.Action != action || row.RequestHash != requestHash))
      return Fail<BankReconciliationCommandLookup>(row.ActorEpoch != actor.SessionEpoch ? ErrorCodes.GenerationStale : ErrorCodes.IdempotencyConflict,
        "This request belongs to another session or exact intent.");
    auth = await AuthorizeAsync(db, actor, parent.Value.ClientId, engagementId, action, ct);
    if (!auth.Succeeded) return Fail<BankReconciliationCommandLookup>(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<BankReconciliationCommandLookup>.Ok(new(row is not null, row is null ? null : View(row)));
  }
}
