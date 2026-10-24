using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record NativeTrialBalanceSourceRequest(Guid RequestId, Guid ClientId, Guid EngagementId, Guid PeriodId,
  long ExpectedPeriodRevision, DateOnly AsOfDate, string LegalEntityKey, bool PeriodLedgerReviewed,
  string EvidenceReference, string? ReviewBasis = null, bool Reviewed = false, bool AllowActivityOnly = false);
public sealed record NativeTrialBalanceSourcePreview(string ReviewBasis, string SourceIdentityHash, string ManifestHash,
  string GeneratedRowsHash, int RowCount, string BookScope, bool ActivityOnly, string Currency,
  string PostingSequence, ClientOperationalTrialBalanceView TrialBalance);
public sealed record NativeTrialBalanceSourceView(Guid DatasetId, Guid ClientId, Guid EngagementId, Guid PeriodId,
  string AsOfDate, string LegalEntityKey, string BookScope, string SourceKind, bool ActivityOnly, int RowCount,
  string SourceIdentityHash, string ManifestHash, string GeneratedRowsHash, string ValidationStatus,
  bool IsCurrent, string? CurrentnessBlocker, Guid CapturedByUserId, DateTimeOffset CreatedAt);
public sealed record NativeTrialBalanceSourceArtifact(Guid DatasetId, string FileName, string ContentType,
  string GeneratedRowsJson, string GeneratedRowsHash, string ManifestJson, string ManifestHash,
  string SourceIdentityHash, bool IsCurrent, string? CurrentnessBlocker);

/// <summary>Exact native period-ledger evidence; sealing never accepts a source or approves accounting treatment.</summary>
public static partial class NativeTrialBalanceSourceWorkspace
{
  public const string SourceKind = "Native";
  public const string BookScope = "PERIOD_LEDGER_NO_BOOK";
  private const string Version = "native-tb-source.v1";
  private static readonly string[] Roles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly TrialBalanceImportProfile NativeProfile = new("native-period-ledger.v1", TrialBalanceLayouts.DebitCredit);
  private static string Hash(object value) => Hashing.Sha256Hex(JsonSerializer.Serialize(value));
  private static string HashText(string value) => Hashing.Sha256Hex(Encoding.UTF8.GetBytes(value));
  private static decimal Amount(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string Purpose(ActorContext actor, NativeTrialBalanceSourceRequest request) => Hash(new
  {
    actor.FirmId, actor.UserId, actor.SessionEpoch,
    Request = request with { ReviewBasis = null, Reviewed = false }
  });
  private static string Basis(string purpose, string identity, string manifest) => Hash(new { purpose, identity, manifest });
  internal static bool IsBaseSource(TrialBalanceDataset source) => source.SourceKind is "Raw" or SourceKind;

  public static async Task<CommandResult<NativeTrialBalanceSourcePreview>> PreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, NativeTrialBalanceSourceRequest request, CancellationToken ct = default)
  {
    var valid = Validate(request);
    if (valid is not null) return Fail<NativeTrialBalanceSourcePreview>(ErrorCodes.Accounting.ImportRejected, valid);
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
    var capture = await CaptureAsync(db, actor, request, ct);
    if (!capture.Succeeded) return Fail<NativeTrialBalanceSourcePreview>(capture.ErrorCode!, capture.Message!);
    await tx.CommitAsync(ct);
    await tx.DisposeAsync();
    var final = await AuthorizeAsync(db, actor, request.ClientId, request.EngagementId, true, ct);
    if (!final.Succeeded) return Fail<NativeTrialBalanceSourcePreview>(final.ErrorCode!, final.Message!);
    // A second current read invalidates a preview changed while its snapshot was being released.
    var pins = await ReadPinsAsync(db, actor.FirmId, request.ClientId, request.PeriodId, ct);
    if (!pins.Succeeded || pins.Value != capture.Value!.Manifest.Pins)
      return Fail<NativeTrialBalanceSourcePreview>(ErrorCodes.StaleRevision, "Native ledger inputs changed. Preview again.");
    return CommandResult<NativeTrialBalanceSourcePreview>.Ok(capture.Value.Preview);
  }

  public static async Task<CommandResult<NativeTrialBalanceSourceView>> SealAsync(IClientAccountingDbContext db,
    ActorContext actor, NativeTrialBalanceSourceRequest request, CancellationToken ct = default)
  {
    var valid = Validate(request);
    if (valid is not null) return Fail<NativeTrialBalanceSourceView>(ErrorCodes.Accounting.ImportRejected, valid);
    var auth = await AuthorizeAsync(db, actor, request.ClientId, request.EngagementId, true, ct);
    if (!auth.Succeeded) return Fail<NativeTrialBalanceSourceView>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var requestLock = long.Parse(Hash(new { actor.FirmId, actor.UserId, actor.SessionEpoch, request.RequestId })[..16],
      NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({requestLock})", ct);
    // Same order as source acceptance. Only immutable posted journals are read after the period fence.
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={request.ClientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND id={request.EngagementId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    var existing = await db.NativeTrialBalanceSources.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.ActorEpoch == actor.SessionEpoch && x.RequestId == request.RequestId, ct);
    if (existing is not null)
    {
      if (existing.ActorId != actor.UserId || existing.ActorEpoch != actor.SessionEpoch ||
          existing.ClientId != request.ClientId || existing.EngagementId != request.EngagementId ||
          existing.RequestHash != Purpose(actor, request) || !request.Reviewed ||
          request.ReviewBasis != Basis(existing.RequestHash, existing.SourceIdentityHash, existing.ManifestHash))
        return Fail<NativeTrialBalanceSourceView>(ErrorCodes.IdempotencyConflict, "This request belongs to different reviewed native-source intent.");
      await tx.CommitAsync(ct);
      await tx.DisposeAsync();
      return await ViewAsync(db, actor, existing.DatasetId, ct);
    }
    await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={request.ClientId} AND id={request.PeriodId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    var capture = await CaptureAsync(db, actor, request, ct);
    if (!capture.Succeeded) return Fail<NativeTrialBalanceSourceView>(capture.ErrorCode!, capture.Message!);
    var c = capture.Value!;
    if (!request.Reviewed || request.ReviewBasis != c.Preview.ReviewBasis)
      return Fail<NativeTrialBalanceSourceView>(ErrorCodes.StaleRevision, "Review the exact native-source preview before sealing.");
    var duplicate = await db.NativeTrialBalanceSources.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == request.ClientId && x.EngagementId == request.EngagementId && x.SourceIdentityHash == c.Preview.SourceIdentityHash)
      .Select(x => (Guid?)x.DatasetId).FirstOrDefaultAsync(ct);
    if (duplicate is not null)
      return Fail<NativeTrialBalanceSourceView>(ErrorCodes.Accounting.ImportDuplicate, $"This native source is already sealed as dataset {duplicate:D}. Reuse that source.");
    var id = Guid.CreateVersion7();
    var revision = (await db.TrialBalanceDatasets.Where(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId)
      .MaxAsync(x => (long?)x.Revision, ct) ?? 0) + 1;
    var now = DateTimeOffset.UtcNow;
    var dataset = new TrialBalanceDataset
    {
      Id = id, FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, BookId = null, Basis = c.Manifest.Basis, Currency = c.Manifest.Currency,
      LegalEntityKey = request.LegalEntityKey, SourceKind = SourceKind, Revision = revision,
      RawFileSha256Hex = "", NormalizedDatasetDigest = c.Manifest.RowsDigest, Sha256Hex = c.Preview.SourceIdentityHash,
      ImportProfileVersion = NativeProfile.Version, SourceLayout = NativeProfile.Layout,
      ImportState = TrialBalanceImportStates.Loading, ValidationStatus = "Pending", ImportedByUserId = actor.UserId, ImportedAt = now
    };
    db.TrialBalanceDatasets.Add(dataset);
    db.NativeTrialBalanceSources.Add(new NativeTrialBalanceSource
    {
      DatasetId = id, FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      RequestId = request.RequestId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch, RequestHash = Purpose(actor, request),
      SourceIdentityHash = c.Preview.SourceIdentityHash, ManifestHash = c.Preview.ManifestHash,
      ManifestJson = c.ManifestJson, GeneratedRowsJson = c.RowsJson, GeneratedRowsHash = c.Preview.GeneratedRowsHash,
      RowCount = c.Rows.Count, CreatedAt = now
    });
    foreach (var row in c.Rows)
      db.TrialBalanceRows.Add(new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = id,
        AccountCode = row.AccountCode, AccountName = row.AccountName, Amount = row.Amount, SourceDebit = row.SourceDebit,
        SourceCredit = row.SourceCredit, Currency = row.Currency, Entity = row.Entity, MappingCode = null });
    await db.SaveChangesAsync(ct);
    dataset.ImportState = TrialBalanceImportStates.Sealed;
    await db.SaveChangesAsync(ct);
    auth = await AuthorizeAsync(db, actor, request.ClientId, request.EngagementId, true, ct);
    var current = await CheckCurrentAsync(db, dataset, ct, requireCoverage: false);
    if (!auth.Succeeded) return Fail<NativeTrialBalanceSourceView>(auth.ErrorCode!, auth.Message!);
    if (!current.Succeeded) return Fail<NativeTrialBalanceSourceView>(current.ErrorCode!, current.Message!);
    await tx.CommitAsync(ct);
    await tx.DisposeAsync();
    return await ViewAsync(db, actor, id, ct);
  }

  private static string? Validate(NativeTrialBalanceSourceRequest r) =>
    r.RequestId == Guid.Empty || r.ClientId == Guid.Empty || r.EngagementId == Guid.Empty || r.PeriodId == Guid.Empty ||
    r.ExpectedPeriodRevision < 1 || !r.PeriodLedgerReviewed || string.IsNullOrWhiteSpace(r.LegalEntityKey) ||
    r.LegalEntityKey.Length > 200 || r.LegalEntityKey != r.LegalEntityKey.Trim() ||
    string.IsNullOrWhiteSpace(r.EvidenceReference) || r.EvidenceReference.Length > 2000 ||
    r.EvidenceReference != r.EvidenceReference.Trim()
      ? "Declare the exact scope, bounded entity/evidence, period revision and whole native period-ledger/no-book selection." : null;

  private static async Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, bool write, CancellationToken ct)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, engagementId, Roles, InternalOnly: true, RequireProfessionalWork: write), ct);
    if (!auth.Succeeded || !write) return auth;
    if (await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.State == "FROZEN", ct))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The engagement file is frozen.");
    return await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct)
      ? auth : CommandResult.Fail(ErrorCodes.GateBlocked, "Current accepted BOOKKEEPING authority is required.");
  }
}
