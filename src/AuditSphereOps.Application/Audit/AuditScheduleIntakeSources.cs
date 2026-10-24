using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

internal sealed record AuditScheduleIntakeSourceFacts(
  Guid FirmId, Guid ClientId, Guid EngagementId, Guid PbcAcceptanceReceiptId,
  Guid PbcRequestId, long PbcRequestRevision, string CurrentPbcRequestState, Guid PbcUploadIntentId, long UploadIntentRevision,
  Guid AcceptedTbDecisionId, Guid TrialBalanceDatasetId, long DatasetRevision,
  Guid PeriodId, long PeriodRevision, DateOnly PeriodStart, DateOnly PeriodEnd, Guid? BookId, long? BookRevision, string? Basis,
  string LegalEntityKey, string Currency, long InputGeneration, DateOnly SourceDate,
  string ScheduleType, string ProfileVersion, string FileName, string RawFileSha256, long ByteCount,
  string PbcSourceDigest, string AcceptedTbHash, AuditScheduleCsvParseResult Parsed,
  IReadOnlyList<string> SelectedControlAccountCodes, IReadOnlyList<AuditScheduleAccountTieOut> AccountTieOuts,
  decimal SignedTotal, decimal ClosingControl, decimal Residual, string ManifestJson, string ManifestSha256)
{
  public IReadOnlyList<ScheduleRowInput> Rows => Parsed.Rows;
  public bool HasCompleteListingCoverage => AccountTieOuts.All(x => x.ListingRowCount > 0);
  public bool AllAccountsReconciled => HasCompleteListingCoverage && AccountTieOuts.All(x => x.Difference == 0m);
}

/// <summary>
/// Reads an accepted PBC listing against one independently accepted, selected TB closing context.
/// No transaction, writes, approval, provider byte fetch or respondent inference. Publication callers
/// own the established scope/source locks and must resolve again under their write transaction.
/// </summary>
internal static class AuditScheduleIntakeSources
{
  internal const string Version = "audit-schedule-source-capture.v1";
  internal const string ScheduleType = "RECEIVABLES_LEAD";
  internal const int MaximumControlRows = 20_000;
  internal const int MaximumManifestBytes = 16 * 1024 * 1024;
  private static readonly string[] Roles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator", "Reviewer"];

  internal static async Task<CommandResult<AuditScheduleIntakeSourceFacts>> ResolveAsync(
    IClientAccountingDbContext db, ActorContext actor, IAuditScheduleSourceContentReader reader,
    Guid clientId, Guid engagementId, Guid pbcAcceptanceReceiptId, Guid acceptedTbDecisionId,
    DateOnly sourceDate, AuditScheduleCsvColumns columns, IReadOnlyList<string> selectedControlAccountCodes,
    CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (clientId == Guid.Empty || engagementId == Guid.Empty || pbcAcceptanceReceiptId == Guid.Empty ||
        acceptedTbDecisionId == Guid.Empty || columns is null || reader is null ||
        selectedControlAccountCodes is null || selectedControlAccountCodes.Count is < 1 or > 20_000)
      return Fail<AuditScheduleIntakeSourceFacts>("Select an exact accepted listing, TB and explicit control accounts.");
    var codes = selectedControlAccountCodes.ToArray();
    if (codes.Any(x => string.IsNullOrEmpty(x) || x.Length > 100 || x != x.Trim() || x.Contains('\0')) ||
        codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
      return Fail<AuditScheduleIntakeSourceFacts>("Control account selection must be nonempty, exact and distinct.");
    Array.Sort(codes, StringComparer.Ordinal);
    var authority = await AuthorizeAsync(db, actor, clientId, engagementId, ct);
    if (!authority.Succeeded) return Denied<AuditScheduleIntakeSourceFacts>(authority);
    var pbcResult = await LoadPbcAsync(db, actor.FirmId, clientId, engagementId, pbcAcceptanceReceiptId, ct);
    if (!pbcResult.Succeeded) return CopyFailure<PbcFacts, AuditScheduleIntakeSourceFacts>(pbcResult);
    var tbResult = await LoadTbAsync(db, actor.FirmId, clientId, engagementId, acceptedTbDecisionId, sourceDate, ct);
    if (!tbResult.Succeeded) return CopyFailure<TbFacts, AuditScheduleIntakeSourceFacts>(tbResult);
    var pbc = pbcResult.Value!;
    var tb = tbResult.Value!;
    // PBC request context is freeform storage. V1 accepts only this explicit canonical identity;
    // merely retaining arbitrary metadata never proves the file belongs to the selected context.
    if (!string.Equals(pbc.EntityScope, tb.LegalEntityKey, StringComparison.Ordinal) ||
        !DateOnly.TryParseExact(pbc.PeriodStart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
          DateTimeStyles.None, out var requestStart) ||
        !DateOnly.TryParseExact(pbc.PeriodEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture,
          DateTimeStyles.None, out var requestEnd) ||
        requestStart != tb.PeriodStart || requestEnd != tb.PeriodEnd)
      return Fail<AuditScheduleIntakeSourceFacts>("This listing profile requires exact PBC entity and ISO reporting-period context.");
    var download = await PbcService.PrepareDownloadAsync(db, actor, pbc.UploadIntentId, ct);
    if (!download.Succeeded) return CopyFailure<PbcDownload, AuditScheduleIntakeSourceFacts>(download);
    if (download.Value is null || download.Value.FileName != pbc.FileName || download.Value.ByteCount != pbc.ByteCount)
      return Stale<AuditScheduleIntakeSourceFacts>();
    var content = await reader.ReadVerifiedAsync(download.Value, pbc.Hash, pbc.ByteCount, ct);
    if (!content.Succeeded) return CopyFailure<byte[], AuditScheduleIntakeSourceFacts>(content);
    if (content.Value is null || content.Value.LongLength != pbc.ByteCount)
      return Fail<AuditScheduleIntakeSourceFacts>("The accepted listing content is unavailable.");

    AuditScheduleCsvParseResult parsed;
    try { parsed = AuditScheduleCsvProfile.Parse(pbc.FileName, content.Value, tb.Currency, columns); }
    catch (FormatException)
    {
      return Fail<AuditScheduleIntakeSourceFacts>("The accepted listing does not match the bounded CSV profile or column map.");
    }
    ct.ThrowIfCancellationRequested();
    if (parsed.RawFileSha256 != pbc.Hash) return Stale<AuditScheduleIntakeSourceFacts>();
    var controlsResult = await LoadControlsAsync(db, tb, codes, ct);
    if (!controlsResult.Succeeded) return CopyFailure<ControlFacts, AuditScheduleIntakeSourceFacts>(controlsResult);
    var controls = controlsResult.Value!;
    IReadOnlyList<AuditScheduleAccountTieOut> ties;
    decimal signedTotal, closingControl, residual;
    try
    {
      ties = AuditScheduleControlReconciliation.Reconcile(codes,
        parsed.Rows.Select(x => new AuditScheduleAccountAmount(x.AccountCode, x.SignedAmount)).ToArray(),
        controls.Rows.Select(x => new AuditScheduleAccountAmount(x.AccountCode, x.Amount)).ToArray());
      signedTotal = parsed.Rows.Sum(x => x.SignedAmount);
      closingControl = controls.Rows.Sum(x => x.Amount);
      residual = signedTotal - closingControl;
      if (!Money(signedTotal) || !Money(closingControl) || !Money(residual) || ties.Any(x => !Money(x.Difference)))
        return Fail<AuditScheduleIntakeSourceFacts>("Source controls and differences must fit numeric(19,6) exactly.");
    }
    catch (Exception error) when (error is ArgumentException or OverflowException)
    {
      return Fail<AuditScheduleIntakeSourceFacts>("The explicit account scope or signed closing controls are incomplete or unsupported.");
    }

    var manifest = JsonSerializer.Serialize(new
    {
      Version, actor.FirmId, ClientId = clientId, EngagementId = engagementId, ScheduleType, SourceDate = sourceDate,
      Pbc = JsonSerializer.Deserialize<JsonElement>(pbc.Json),
      TrialBalance = JsonSerializer.Deserialize<JsonElement>(tb.Json),
      ProfileVersion = parsed.ProfileVersion, Columns = columns, parsed.Headers,
      parsed.RawFileSha256, pbc.ByteCount, RowCount = parsed.Rows.Count, parsed.NormalizedRowsSha256,
      parsed.NormalizedRowsJson,
      OriginalRows = parsed.Rows.Select(x => new { x.StableRowId, x.SourceLineNumber, x.OriginalValuesJson }),
      SelectedControlAccountCodes = codes, ControlBasis = "TB_CLOSING_SIGNED_NET",
      SignedTotal = Exact(signedTotal), ClosingControl = Exact(closingControl), Residual = Exact(residual),
      AccountTieOuts = ties.Select(x => new
      {
        x.AccountCode, ListingAmount = Exact(x.ListingAmount), TrialBalanceAmount = Exact(x.TrialBalanceAmount),
        Difference = Exact(x.Difference), x.ListingRowCount, x.TrialBalanceRowCount
      }),
      ControlRows = JsonSerializer.Deserialize<JsonElement>(controls.Json)
    });
    if (Encoding.UTF8.GetByteCount(manifest) > MaximumManifestBytes)
      return Fail<AuditScheduleIntakeSourceFacts>("The complete source manifest exceeds the supported 16 MiB window.");

    // Fresh stored facts after all byte reads. This is a read fence, not an atomic filesystem/DB snapshot.
    var currentPbc = await LoadPbcAsync(db, actor.FirmId, clientId, engagementId, pbcAcceptanceReceiptId, ct);
    if (!currentPbc.Succeeded) return CopyFailure<PbcFacts, AuditScheduleIntakeSourceFacts>(currentPbc);
    var currentTb = await LoadTbAsync(db, actor.FirmId, clientId, engagementId, acceptedTbDecisionId, sourceDate, ct);
    if (!currentTb.Succeeded) return CopyFailure<TbFacts, AuditScheduleIntakeSourceFacts>(currentTb);
    if (currentPbc.Value != pbc || currentTb.Value != tb) return Stale<AuditScheduleIntakeSourceFacts>();
    var currentControls = await LoadControlsAsync(db, tb, codes, ct);
    if (!currentControls.Succeeded) return CopyFailure<ControlFacts, AuditScheduleIntakeSourceFacts>(currentControls);
    if (currentControls.Value!.Json != controls.Json) return Stale<AuditScheduleIntakeSourceFacts>();
    authority = await AuthorizeAsync(db, actor, clientId, engagementId, ct);
    if (!authority.Succeeded) return Denied<AuditScheduleIntakeSourceFacts>(authority);
    return CommandResult<AuditScheduleIntakeSourceFacts>.Ok(new(actor.FirmId, clientId, engagementId,
      pbcAcceptanceReceiptId, pbc.RequestId, pbc.RequestRevision, pbc.RequestState, pbc.UploadIntentId, pbc.UploadRevision,
      acceptedTbDecisionId, tb.DatasetId, tb.DatasetRevision, tb.PeriodId, tb.PeriodRevision, tb.PeriodStart, tb.PeriodEnd, tb.BookId,
      tb.BookRevision, tb.Basis, tb.LegalEntityKey, tb.Currency, tb.Generation, sourceDate, ScheduleType,
      parsed.ProfileVersion, pbc.FileName, pbc.Hash, pbc.ByteCount, pbc.SourceDigest, tb.Hash, parsed,
      Array.AsReadOnly(codes), ties, signedTotal, closingControl, residual, manifest, Hashing.Sha256Hex(manifest)));
  }

  // Reuses the actual accepted-source fact owners without inventing a byte reader or
  // reparsing retained rows. PrepareDownload verifies real staged chunk/count/hash receipts.
  internal static async Task<CommandResult> CheckRetainedSourcesCurrentForAuthorizedScopeAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    AuditScheduleSourceCapture capture, CancellationToken ct)
  {
    if (capture.FirmId != firmId || capture.ClientId != clientId || capture.EngagementId != engagementId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    try
    {
      if (Hashing.Sha256Hex(capture.ManifestJson) != capture.ManifestHash)
        return CommandResult.Fail(ErrorCodes.ProtectedState, "The retained schedule source manifest is inconsistent.");
      using var document = JsonDocument.Parse(capture.ManifestJson);
      var manifest = document.RootElement;
      if (!manifest.TryGetProperty("Pbc", out var originalPbc) ||
          !manifest.TryGetProperty("TrialBalance", out var originalTb))
        return CommandResult.Fail(ErrorCodes.ProtectedState, "The retained source descriptors are unavailable.");
      var pbc = await LoadPbcAsync(db, firmId, clientId, engagementId,
        capture.PbcAcceptanceReceiptId, ct);
      if (!pbc.Succeeded) return CommandResult.Fail(pbc.ErrorCode!, pbc.Message!);
      var tb = await LoadTbAsync(db, firmId, clientId, engagementId,
        capture.AcceptedSourceDecisionId, capture.SourceDate, ct);
      if (!tb.Succeeded) return CommandResult.Fail(tb.ErrorCode!, tb.Message!);
      if (!SameRetainedFacts(originalPbc, pbc.Value!.Json) || !SameRetainedFacts(originalTb, tb.Value!.Json) ||
          tb.Value.DatasetId != capture.TrialBalanceDatasetId || tb.Value.PeriodId != capture.PeriodId ||
          tb.Value.PeriodRevision != capture.PeriodRevision || tb.Value.Generation != capture.InputGeneration)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The captured schedule source or reporting context changed.");
      var staged = await PbcService.VerifyAcceptedStagedContentForAuthorizedScopeAsync(db, firmId, clientId,
        engagementId, pbc.Value.UploadIntentId, pbc.Value.FileName, pbc.Value.Hash, pbc.Value.ByteCount, ct);
      if (!staged.Succeeded) return staged;
      // Recheck stored facts after filesystem awaits, even for a caller-owned transaction.
      var finalPbc = await LoadPbcAsync(db, firmId, clientId, engagementId,
        capture.PbcAcceptanceReceiptId, ct);
      if (!finalPbc.Succeeded) return CommandResult.Fail(finalPbc.ErrorCode!, finalPbc.Message!);
      var finalTb = await LoadTbAsync(db, firmId, clientId, engagementId,
        capture.AcceptedSourceDecisionId, capture.SourceDate, ct);
      if (!finalTb.Succeeded) return CommandResult.Fail(finalTb.ErrorCode!, finalTb.Message!);
      if (!SameRetainedFacts(originalPbc, finalPbc.Value!.Json) || !SameRetainedFacts(originalTb, finalTb.Value!.Json) ||
          finalTb.Value.Generation != capture.InputGeneration || finalTb.Value.PeriodRevision != capture.PeriodRevision)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The captured schedule source changed while reading evidence.");
      return CommandResult.Ok();
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      ct.ThrowIfCancellationRequested();
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The accepted schedule source content is unavailable.");
    }
    catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
    {
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The retained schedule source descriptors are inconsistent.");
    }
  }

  private static bool SameRetainedFacts(JsonElement retained, string currentJson)
  {
    // Nesting may escape the same string differently. Compare all scalar/array/object facts,
    // separately from the original manifest bytes, whose retained hash is verified above.
    using var current = JsonDocument.Parse(currentJson);
    return JsonElement.DeepEquals(retained, current.RootElement);
  }

  private sealed record PbcFacts(Guid RequestId, long RequestRevision, string RequestState, Guid UploadIntentId,
    long UploadRevision, string FileName, string Hash, long ByteCount, string SourceDigest,
    string EntityScope, string PeriodStart, string PeriodEnd, string Json);

  private static async Task<CommandResult<PbcFacts>> LoadPbcAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid receiptId, CancellationToken ct)
  {
    var receipt = await db.PbcAcceptanceReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == receiptId &&
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId, ct);
    if (receipt is null) return Scope<PbcFacts>();
    var request = await db.PbcRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == receipt.PbcRequestId &&
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId, ct);
    var upload = await db.PbcUploadIntents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == receipt.PbcUploadIntentId &&
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.PbcRequestId == receipt.PbcRequestId, ct);
    // Closure is a supported one-revision workflow transition, not a new source acceptance.
    // Preserve its actual state/revision in the read fence, outside the immutable source manifest.
    var supportedRequest = request is not null &&
      (request.State == PbcStates.Accepted && request.Revision == receipt.AcceptedRequestRevision ||
       request.State == PbcStates.Closed && receipt.AcceptedRequestRevision < long.MaxValue &&
       request.Revision == receipt.AcceptedRequestRevision + 1);
    if (request is null || upload is null || !supportedRequest ||
        request.AcceptedByUserId != receipt.ReviewerUserId ||
        request.ReviewerUserId != receipt.ReviewerUserId || request.AcceptedAt != receipt.AcceptedAt ||
        receipt.ReviewerUserId == Guid.Empty || receipt.ReviewerUserId == receipt.UploaderUserId ||
        upload.State != PbcUploadStates.Received || upload.Revision != receipt.UploadIntentRevision ||
        upload.RequestRevision != receipt.SourceRequestRevision || upload.RequestRevision < 1 ||
        upload.UploaderUserId != receipt.UploaderUserId || upload.FileName != receipt.FileName ||
        upload.DeclaredByteCount != receipt.ByteCount || upload.ReceivedByteCount != receipt.ByteCount ||
        receipt.ByteCount is < 1 or > AuditScheduleCsvProfile.MaxBytes ||
        !receipt.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
        upload.CompletedAt is null || upload.TransferOperationId != receipt.TransferOperationId ||
        upload.ProviderRegisteredAt != receipt.ProviderRegisteredAt ||
        !Hash(receipt.ContentSha256Hex) || !Hash(receipt.SourceDigest) ||
        !SameHash(upload.DeclaredSha256Hex, receipt.ContentSha256Hex) ||
        !SameHash(upload.FinalSha256Hex, receipt.ContentSha256Hex) ||
        !SameHash(upload.ProviderReceiptDigest, receipt.ProviderReceiptDigest) ||
        !SameHash(receipt.ProviderReceiptDigest, receipt.ContentSha256Hex))
      return Fail<PbcFacts>("The exact PBC source is not a current accepted, provider-registered upload.");
    var latestUploadId = await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && x.EngagementId == engagementId && x.PbcRequestId == request.Id)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct);
    if (latestUploadId != upload.Id || await db.PbcUploadIntents.AsNoTracking().AnyAsync(x =>
        x.FirmId == firmId && x.PbcRequestId == request.Id &&
        (x.State == PbcUploadStates.Started || x.State == PbcUploadStates.Chunking || x.State == PbcUploadStates.Staged), ct))
      return Stale<PbcFacts>();
    var operation = await db.DurableOperations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == receipt.TransferOperationId && x.FirmId == firmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && x.TargetId == upload.Id && x.OperationKind == PbcDocumentTransferHandler.Kind, ct);
    if (operation is null || operation.Status != OperationState.COMPLETED ||
        operation.ExpectedRevision != upload.Revision - 1 ||
        string.IsNullOrWhiteSpace(operation.ResultIdentity) || operation.ResultIdentity != receipt.ProviderIdentity ||
        !SameHash(operation.ResultDigest, receipt.ContentSha256Hex))
      return Fail<PbcFacts>("The exact completed PBC transfer registration is unavailable.");
    // Match the existing PbcService pbc-accepted-source-v1 shape and property order exactly.
    var sourceDigest = Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      Version = "pbc-accepted-source-v1", request.FirmId, request.ClientId, request.EngagementId,
      RequestId = request.Id, AcceptedRequestRevision = receipt.AcceptedRequestRevision,
      SourceRequestRevision = upload.RequestRevision,
      UploadIntentId = upload.Id, UploadIntentRevision = upload.Revision,
      upload.UploaderUserId, upload.FileName, ByteCount = upload.DeclaredByteCount,
      ContentSha256Hex = upload.FinalSha256Hex!.ToLowerInvariant(), TransferOperationId = operation.Id,
      ProviderIdentity = operation.ResultIdentity!, ProviderReceiptDigest = upload.ProviderReceiptDigest!.ToLowerInvariant(),
      ProviderRegisteredAt = receipt.ProviderRegisteredAt, ReviewerUserId = receipt.ReviewerUserId, AcceptedAt = receipt.AcceptedAt
    }));
    if (sourceDigest != receipt.SourceDigest) return Fail<PbcFacts>("The retained PBC accepted-source digest is inconsistent.");
    var json = JsonSerializer.Serialize(new
    {
      Receipt = new
      {
        receipt.Id, receipt.FirmId, receipt.ClientId, receipt.EngagementId, receipt.PbcRequestId,
        receipt.AcceptedRequestRevision, receipt.SourceRequestRevision, receipt.ReviewerUserId, receipt.AcceptedAt,
        receipt.PbcUploadIntentId, receipt.UploadIntentRevision, receipt.UploaderUserId, receipt.FileName,
        receipt.ByteCount, receipt.ContentSha256Hex, receipt.TransferOperationId, receipt.ProviderIdentity,
        receipt.ProviderReceiptDigest, receipt.ProviderRegisteredAt, receipt.SourceDigest
      },
      Request = new
      {
        request.Id, request.EntityScope, request.PeriodStart, request.PeriodEnd,
        request.Area, request.RequestedFormat, request.ReviewerUserId, request.AcceptedByUserId, request.AcceptedAt
      },
      Upload = new
      {
        upload.Id, upload.RequestRevision, upload.Revision, upload.State, upload.UploaderUserId, upload.FileName,
        upload.ContentType, upload.DeclaredByteCount, upload.DeclaredSha256Hex, upload.ReceivedByteCount,
        upload.FinalSha256Hex, upload.CreatedAt, upload.CompletedAt, upload.TransferOperationId,
        upload.ProviderReceiptDigest, upload.ProviderRegisteredAt
      },
      Transfer = new
      {
        operation.Id, operation.OperationKind, operation.TargetId, operation.ExpectedRevision,
        Status = operation.Status.ToString(), operation.ResultIdentity, operation.ResultDigest, operation.CompletedAt
      }
    });
    return CommandResult<PbcFacts>.Ok(new(request.Id, request.Revision, request.State, upload.Id, upload.Revision,
      receipt.FileName, receipt.ContentSha256Hex, receipt.ByteCount, receipt.SourceDigest,
      request.EntityScope, request.PeriodStart, request.PeriodEnd, json));
  }

  private sealed record TbFacts(Guid DatasetId, long DatasetRevision, Guid PeriodId, long PeriodRevision,
    Guid? BookId, long? BookRevision, string? Basis, string Currency, string LegalEntityKey,
    long Generation, string Hash, DateOnly PeriodStart, DateOnly PeriodEnd, string Json);

  private static async Task<CommandResult<TbFacts>> LoadTbAsync(IClientAccountingDbContext db, Guid firmId,
    Guid clientId, Guid engagementId, Guid decisionId, DateOnly sourceDate, CancellationToken ct)
  {
    var decision = await db.SourceAcceptanceDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == decisionId &&
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId, ct);
    if (decision is null) return Scope<TbFacts>();
    if (decision.Decision != "ACCEPTED" || decision.SourceKind != AccountingSourceKinds.TrialBalance ||
        decision.TrialBalanceDatasetId is not { } datasetId || decision.ImportBatchId is not null)
      return Fail<TbFacts>("Select an independently accepted TB source.");
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId &&
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId, ct);
    if (dataset is null || dataset.ImportState != TrialBalanceImportStates.Sealed ||
        dataset.ValidationStatus != "Accepted" || !dataset.Balanced || dataset.Revision < 1 ||
        dataset.SourceKind is not ("Raw" or NativeTrialBalanceSourceWorkspace.SourceKind) ||
        dataset.PeriodId is not { } periodId || string.IsNullOrWhiteSpace(dataset.LegalEntityKey) ||
        !Hash(decision.SourceIdentityHash) || MappedTrialBalanceSource.Digest(dataset) != decision.SourceIdentityHash ||
        decision.AcceptedByUserId == Guid.Empty || decision.AcceptedByUserId == dataset.ImportedByUserId)
      return Fail<TbFacts>("The accepted TB revision is not a sealed, balanced, independent source.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId &&
      x.FirmId == firmId && x.ClientId == clientId, ct);
    if (period is null || period.Status != AccountingWorkflowStates.Active || period.Revision < 1 || period.StartDate > period.EndDate || period.EndDate != sourceDate ||
        period.Currency != dataset.Currency || dataset.Basis is not null && dataset.Basis != period.Basis)
      return Fail<TbFacts>("The listing source date must match the exact TB reporting-period end and context.");
    ClientReportingBook? book = null;
    if (dataset.BookId is { } bookId)
    {
      book = await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bookId &&
        x.FirmId == firmId && x.ClientId == clientId && x.PeriodId == periodId, ct);
      if (book is null || book.Status != AccountingWorkflowStates.Active || book.Revision < 1 || book.Currency != dataset.Currency ||
          dataset.Basis is not null && book.Basis != dataset.Basis)
        return Fail<TbFacts>("The exact TB reporting book is unavailable or inconsistent.");
    }
    var native = await NativeTrialBalanceSourceWorkspace.CheckCurrentAsync(db, dataset, ct);
    if (!native.Succeeded) return Denied<TbFacts>(native);
    var selected = await AccountingSourceAcceptanceService.GetSelectedTrialBalanceForAuthorizedScopeAsync(db, firmId,
      clientId, engagementId, period.Id, dataset.BookId, dataset.Basis, dataset.Currency, dataset.LegalEntityKey, ct);
    if (!selected.Succeeded) return CopyFailure<SelectedSourceDto?, TbFacts>(selected);
    if (selected.Value is null || selected.Value.DecisionId != decision.Id || selected.Value.TrialBalanceDatasetId != dataset.Id ||
        !selected.Value.IsCurrent || selected.Value.SourceIdentityHash != decision.SourceIdentityHash || selected.Value.InputGeneration < 1)
      return Stale<TbFacts>();
    var json = JsonSerializer.Serialize(new
    {
      Decision = new
      {
        decision.Id, decision.FirmId, decision.ClientId, decision.EngagementId, decision.SourceKind,
        decision.TrialBalanceDatasetId, decision.ImportBatchId, decision.SourceIdentityHash,
        decision.Decision, decision.EvidenceReference, decision.AcceptedByUserId, decision.CreatedAt
      },
      Dataset = new
      {
        dataset.Id, dataset.PeriodId, dataset.BookId, dataset.Basis, dataset.ImportBatchId,
        dataset.SourceKind, dataset.Revision, dataset.LegalEntityKey, dataset.Currency, dataset.RawFileSha256Hex,
        dataset.NormalizedDatasetDigest, dataset.Sha256Hex, dataset.ImportProfileVersion, dataset.SourceLayout,
        dataset.Balanced, dataset.ValidationStatus, dataset.ImportState, ControlTotal = Exact(dataset.ControlTotal),
        dataset.ImportedAt, dataset.ImportedByUserId
      },
      Period = new { period.Id, period.PeriodCode, period.StartDate, period.EndDate, period.Basis, period.Currency, period.Status, period.Revision },
      Book = book is null ? null : new { book.Id, book.Code, book.Basis, book.Currency, book.Status, book.Revision },
      InputGeneration = selected.Value.InputGeneration
    });
    return CommandResult<TbFacts>.Ok(new(dataset.Id, dataset.Revision, period.Id, period.Revision,
      dataset.BookId, book?.Revision, dataset.Basis, dataset.Currency, dataset.LegalEntityKey,
      selected.Value.InputGeneration, decision.SourceIdentityHash, period.StartDate, period.EndDate, json));
  }

  private sealed record ControlRow(Guid Id, Guid DatasetId, string AccountCode, string AccountName,
    decimal Amount, string? SourceDebit, string? SourceCredit, string Currency, string Entity, string? MappingCode);
  private sealed record ControlFacts(IReadOnlyList<ControlRow> Rows, string Json);

  private static async Task<CommandResult<ControlFacts>> LoadControlsAsync(
    IClientAccountingDbContext db, TbFacts tb, string[] codes, CancellationToken ct)
  {
    // Never filter contradictory entity/currency rows out of the selected account population.
    var rows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == tb.DatasetId && codes.Contains(x.AccountCode))
      .OrderBy(x => x.Id).Take(MaximumControlRows + 1).ToListAsync(ct);
    if (rows.Count > MaximumControlRows)
      return Fail<ControlFacts>("The complete selected-account TB population exceeds the supported window.");
    var selected = new HashSet<string>(codes, StringComparer.Ordinal);
    if (rows.Any(x => !selected.Contains(x.AccountCode) || x.Currency != tb.Currency ||
        x.Entity != tb.LegalEntityKey || !Money(x.Amount) ||
        x.SourceDebit is { } debit && !Money(debit) || x.SourceCredit is { } credit && !Money(credit)))
      return Fail<ControlFacts>("Selected TB rows have inconsistent entity, currency or exact amount evidence.");
    var witnesses = rows.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ThenBy(x => x.Id).Select(x =>
      new ControlRow(x.Id, x.DatasetId, x.AccountCode, x.AccountName, x.Amount,
        x.SourceDebit is { } d ? Exact(d) : null, x.SourceCredit is { } c ? Exact(c) : null,
        x.Currency, x.Entity, x.MappingCode)).ToArray();
    var json = JsonSerializer.Serialize(witnesses.Select(x => new
    {
      x.Id, x.DatasetId, x.AccountCode, x.AccountName, Amount = Exact(x.Amount),
      x.SourceDebit, x.SourceCredit, x.Currency, x.Entity, x.MappingCode
    }));
    return CommandResult<ControlFacts>.Ok(new(Array.AsReadOnly(witnesses), json));
  }

  private static Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, engagementId, Roles, InternalOnly: true), ct);
  private static bool Hash(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);
  private static bool SameHash(string? left, string right) => Hash(left) && Hash(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
  private static bool Money(decimal value) => value is >= -AuditScheduleCsvProfile.MaxMoneyMagnitude and <= AuditScheduleCsvProfile.MaxMoneyMagnitude && decimal.Round(value, 6) == value;
  private static string Exact(decimal value) => value.ToString("F6", CultureInfo.InvariantCulture);
  private static CommandResult<T> Fail<T>(string message) => CommandResult<T>.Fail(ErrorCodes.GateBlocked, message);
  private static CommandResult<T> Scope<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  private static CommandResult<T> Stale<T>() => CommandResult<T>.Fail(ErrorCodes.GenerationStale, "The exact listing source or selected TB context changed. Refresh and review again.");
  private static CommandResult<T> Denied<T>(CommandResult result) => CommandResult<T>.Fail(result.ErrorCode!, result.Message!);
  private static CommandResult<TOut> CopyFailure<TIn, TOut>(CommandResult<TIn> result) => CommandResult<TOut>.Fail(result.ErrorCode!, result.Message!);
}
