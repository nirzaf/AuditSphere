using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditScheduleSourceWorkspace
{
  private sealed record RetainedSourceRow(string StableRowId, int SourceLineNumber, string OriginalValuesJson);
  private sealed record RetainedAccountTieOut(string AccountCode, string ListingAmount,
    string TrialBalanceAmount, string Difference, int ListingRowCount, int TrialBalanceRowCount);
  private sealed record RetainedScheduleManifest(string? Version, Guid FirmId, Guid ClientId,
    Guid EngagementId, string? ScheduleType, DateOnly SourceDate, string? ProfileVersion,
    string? RawFileSha256, long ByteCount, int RowCount, string? NormalizedRowsSha256,
    string? NormalizedRowsJson, IReadOnlyList<string>? Headers,
    IReadOnlyList<RetainedSourceRow>? OriginalRows, IReadOnlyList<string>? SelectedControlAccountCodes,
    string? ControlBasis, string? SignedTotal, string? ClosingControl, string? Residual,
    IReadOnlyList<RetainedAccountTieOut>? AccountTieOuts, JsonElement Pbc, JsonElement TrialBalance);

  public static async Task<CommandResult<AuditScheduleReviewSourceRowsPage>> GetReviewSourceRowsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid scheduleId,
    string captureHash, int offset = 0, int limit = 50, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || scheduleId == Guid.Empty || !Hash(captureHash) ||
        offset is < 0 or > AuditScheduleCsvProfile.MaxRows || limit is < 1 or > 100)
      return Invalid<AuditScheduleReviewSourceRowsPage>();
    var auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return Failure<Guid, AuditScheduleReviewSourceRowsPage>(auth);
    var clientId = auth.Value;
    var capture = await db.AuditScheduleSourceCaptures.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      x.ScheduleId == scheduleId, ct);
    if (capture is null || capture.ActorUserId == actor.UserId)
      return CommandResult<AuditScheduleReviewSourceRowsPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!string.Equals(capture.ManifestHash, captureHash, StringComparison.Ordinal))
      return CommandResult<AuditScheduleReviewSourceRowsPage>.Fail(ErrorCodes.GenerationStale,
        "The selected source capture changed. Refresh the review candidate.");
    var retained = ReadRetainedScheduleManifest(capture);
    if (!retained.Succeeded) return Failure<RetainedScheduleManifest, AuditScheduleReviewSourceRowsPage>(retained);
    var manifest = retained.Value!;
    var items = new List<AuditScheduleReviewSourceRow>(Math.Min(limit, Math.Max(0, manifest.RowCount - offset)));
    foreach (var row in manifest.OriginalRows!.Skip(offset).Take(limit))
    {
      var cells = ReadOriginalCells(row.OriginalValuesJson, manifest.Headers!);
      if (!cells.Succeeded) return Failure<IReadOnlyList<string>, AuditScheduleReviewSourceRowsPage>(cells);
      items.Add(new(row.StableRowId, row.SourceLineNumber, cells.Value!));
    }
    auth = await AuditFieldworkService.AuthorizeScheduleReviewAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded || auth.Value != clientId)
      return CommandResult<AuditScheduleReviewSourceRowsPage>.Fail(auth.ErrorCode ?? ErrorCodes.ScopeDenied,
        auth.Message ?? "Access denied.");
    return CommandResult<AuditScheduleReviewSourceRowsPage>.Ok(new(capture.ManifestHash, offset, limit,
      manifest.RowCount, offset + items.Count < manifest.RowCount, manifest.Headers!, items.AsReadOnly()));
  }

  private static CommandResult<AuditScheduleReviewSourceSummary> ReadRetainedSourceSummary(
    AuditScheduleSourceCapture capture)
  {
    var retained = ReadRetainedScheduleManifest(capture);
    if (!retained.Succeeded)
      return Failure<RetainedScheduleManifest, AuditScheduleReviewSourceSummary>(retained);
    var manifest = retained.Value!;
    try
    {
      var pbcReceipt = manifest.Pbc.GetProperty("Receipt");
      var pbcRequest = manifest.Pbc.GetProperty("Request");
      var pbcUpload = manifest.Pbc.GetProperty("Upload");
      var trialBalanceDecision = manifest.TrialBalance.GetProperty("Decision");
      var dataset = manifest.TrialBalance.GetProperty("Dataset");
      var period = manifest.TrialBalance.GetProperty("Period");
      var book = manifest.TrialBalance.GetProperty("Book");
      var fileName = pbcReceipt.GetProperty("FileName").GetString();
      var byteCount = pbcReceipt.GetProperty("ByteCount").GetInt64();
      var datasetRevision = dataset.GetProperty("Revision").GetInt64();
      var periodCode = period.GetProperty("PeriodCode").GetString();
      var periodId = period.GetProperty("Id").GetGuid();
      var periodRevision = period.GetProperty("Revision").GetInt64();
      var datasetPeriodId = dataset.GetProperty("PeriodId").GetGuid();
      var datasetBookId = dataset.GetProperty("BookId").ValueKind == JsonValueKind.Null
        ? (Guid?)null : dataset.GetProperty("BookId").GetGuid();
      var datasetBasis = dataset.GetProperty("Basis").ValueKind == JsonValueKind.Null
        ? null : dataset.GetProperty("Basis").GetString();
      var datasetEntity = dataset.GetProperty("LegalEntityKey").GetString();
      var datasetCurrency = dataset.GetProperty("Currency").GetString();
      var pbcContentHash = pbcReceipt.GetProperty("ContentSha256Hex").GetString();
      var pbcEntityScope = pbcRequest.GetProperty("EntityScope").GetString();
      var pbcPeriodStart = DateOnly.ParseExact(pbcRequest.GetProperty("PeriodStart").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
      var pbcPeriodEnd = DateOnly.ParseExact(pbcRequest.GetProperty("PeriodEnd").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
      var periodStart = DateOnly.ParseExact(period.GetProperty("StartDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
      var periodEnd = DateOnly.ParseExact(period.GetProperty("EndDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
      long? bookRevision = null;
      Guid? bookId = null;
      if (book.ValueKind == JsonValueKind.Object)
      {
        bookId = book.GetProperty("Id").GetGuid();
        bookRevision = book.GetProperty("Revision").GetInt64();
      }
      else if (book.ValueKind != JsonValueKind.Null) return Protected<AuditScheduleReviewSourceSummary>();
      if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(periodCode) ||
          byteCount != manifest.ByteCount || datasetRevision < 1 || periodCode.Length > 200 ||
          periodStart > periodEnd || periodId != capture.PeriodId || periodRevision != capture.PeriodRevision ||
          datasetPeriodId != capture.PeriodId || datasetBookId != capture.BookId || bookId != capture.BookId ||
          datasetBasis != capture.Basis ||
          datasetEntity != capture.LegalEntityKey || datasetCurrency != capture.Currency ||
          manifest.SourceDate != capture.SourceDate || pbcContentHash != manifest.RawFileSha256 ||
          pbcEntityScope != capture.LegalEntityKey || pbcPeriodStart != periodStart || pbcPeriodEnd != periodEnd ||
          trialBalanceDecision.GetProperty("Id").GetGuid() != capture.AcceptedSourceDecisionId ||
          trialBalanceDecision.GetProperty("TrialBalanceDatasetId").GetGuid() != capture.TrialBalanceDatasetId ||
          dataset.GetProperty("Id").GetGuid() != capture.TrialBalanceDatasetId ||
          pbcReceipt.GetProperty("Id").GetGuid() != capture.PbcAcceptanceReceiptId ||
          pbcUpload.GetProperty("FileName").GetString() != fileName ||
          pbcUpload.GetProperty("DeclaredByteCount").GetInt64() != byteCount ||
          pbcUpload.GetProperty("FinalSha256Hex").GetString() != manifest.RawFileSha256)
        return Protected<AuditScheduleReviewSourceSummary>();

      var accounts = manifest.AccountTieOuts!.Select(x => new AuditScheduleSourceAccountView(
        x.AccountCode, x.ListingAmount, x.TrialBalanceAmount, x.Difference,
        x.ListingRowCount.ToString(CultureInfo.InvariantCulture),
        x.TrialBalanceRowCount.ToString(CultureInfo.InvariantCulture))).ToArray();
      var complete = manifest.AccountTieOuts!.All(x => x.ListingRowCount > 0);
      var reconciled = complete && manifest.AccountTieOuts!.All(x => decimal.TryParse(x.Difference,
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var difference) && difference == 0m);
      return CommandResult<AuditScheduleReviewSourceSummary>.Ok(new(manifest.ScheduleType!,
        capture.PbcAcceptanceReceiptId, capture.AcceptedSourceDecisionId, capture.TrialBalanceDatasetId,
        datasetRevision, capture.PeriodId, periodCode, periodStart, periodEnd, capture.PeriodRevision,
        capture.BookId, bookRevision, capture.Basis, capture.LegalEntityKey, capture.Currency,
        capture.SourceDate, fileName, manifest.ProfileVersion!, manifest.RawFileSha256!, byteCount,
        manifest.RowCount, manifest.ControlBasis!, manifest.SelectedControlAccountCodes!,
        manifest.SignedTotal!, manifest.ClosingControl!, manifest.Residual!, complete, reconciled,
        Array.AsReadOnly(accounts)));
    }
    catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
    {
      return Protected<AuditScheduleReviewSourceSummary>();
    }
  }

  private static CommandResult<RetainedScheduleManifest> ReadRetainedScheduleManifest(
    AuditScheduleSourceCapture capture)
  {
    try
    {
      if (capture.ManifestJson.Length > AuditScheduleIntakeSources.MaximumManifestBytes ||
          !Hash(capture.ManifestHash) || Hashing.Sha256Hex(capture.ManifestJson) != capture.ManifestHash)
        return Protected<RetainedScheduleManifest>();
      var manifest = JsonSerializer.Deserialize<RetainedScheduleManifest>(capture.ManifestJson);
      if (manifest is null || manifest.Version != AuditScheduleIntakeSources.Version ||
          manifest.FirmId != capture.FirmId || manifest.ClientId != capture.ClientId ||
          manifest.EngagementId != capture.EngagementId || manifest.ScheduleType != AuditScheduleIntakeSources.ScheduleType ||
          manifest.SourceDate != capture.SourceDate || manifest.ProfileVersion != AuditScheduleCsvProfile.Version ||
          !Hash(manifest.RawFileSha256!) || manifest.ByteCount is < 1 or > AuditScheduleCsvProfile.MaxBytes ||
          manifest.RowCount is < 1 or > AuditScheduleCsvProfile.MaxRows ||
          manifest.OriginalRows is null || manifest.OriginalRows.Count != manifest.RowCount ||
          manifest.Headers is null || manifest.Headers.Count is < 1 or > AuditScheduleCsvProfile.MaxColumns ||
          manifest.Headers.Any(string.IsNullOrEmpty) ||
          manifest.Headers.Distinct(StringComparer.Ordinal).Count() != manifest.Headers.Count || manifest.SelectedControlAccountCodes is null ||
          manifest.SelectedControlAccountCodes.Count is < 1 or > AuditScheduleCsvProfile.MaxRows ||
          manifest.SelectedControlAccountCodes.Any(string.IsNullOrWhiteSpace) ||
          manifest.AccountTieOuts is null || manifest.AccountTieOuts.Count != manifest.SelectedControlAccountCodes.Count ||
          manifest.ControlBasis != "TB_CLOSING_SIGNED_NET" ||
          !Hash(manifest.NormalizedRowsSha256!) || manifest.NormalizedRowsJson is null ||
          Hashing.Sha256Hex(manifest.NormalizedRowsJson) != manifest.NormalizedRowsSha256 ||
          manifest.OriginalRows.Any(x => x is null || string.IsNullOrWhiteSpace(x.StableRowId) ||
            x.SourceLineNumber < 2 || string.IsNullOrEmpty(x.OriginalValuesJson)) ||
          manifest.OriginalRows.Select(x => x.StableRowId).Distinct(StringComparer.Ordinal).Count() != manifest.RowCount ||
          manifest.OriginalRows.Select(x => x.SourceLineNumber).Distinct().Count() != manifest.RowCount ||
          manifest.SelectedControlAccountCodes.Distinct(StringComparer.Ordinal).Count() != manifest.SelectedControlAccountCodes.Count ||
          manifest.AccountTieOuts.Any(x => x is null || x.ListingRowCount < 0 || x.TrialBalanceRowCount < 0 ||
            string.IsNullOrWhiteSpace(x.AccountCode)) ||
          !manifest.AccountTieOuts.Select(x => x.AccountCode).SequenceEqual(manifest.SelectedControlAccountCodes.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
          manifest.Pbc.ValueKind != JsonValueKind.Object || manifest.TrialBalance.ValueKind != JsonValueKind.Object)
        return Protected<RetainedScheduleManifest>();
      return CommandResult<RetainedScheduleManifest>.Ok(manifest);
    }
    catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
    {
      return Protected<RetainedScheduleManifest>();
    }
  }

  private static CommandResult<IReadOnlyList<string>> ReadOriginalCells(string originalValues,
    IReadOnlyList<string> headers)
  {
    try
    {
      using var document = JsonDocument.Parse(originalValues);
      if (document.RootElement.ValueKind != JsonValueKind.Object) return Protected<IReadOnlyList<string>>();
      var cells = new string[headers.Count];
      for (var index = 0; index < headers.Count; index++)
      {
        if (!document.RootElement.TryGetProperty(headers[index], out var value) || value.ValueKind != JsonValueKind.String)
          return Protected<IReadOnlyList<string>>();
        cells[index] = value.GetString()!;
      }
      if (document.RootElement.EnumerateObject().Count() != headers.Count)
        return Protected<IReadOnlyList<string>>();
      return CommandResult<IReadOnlyList<string>>.Ok(Array.AsReadOnly(cells));
    }
    catch (JsonException)
    {
      return Protected<IReadOnlyList<string>>();
    }
  }

  private static CommandResult<T> Protected<T>() => CommandResult<T>.Fail(ErrorCodes.ProtectedState,
    "The retained source details are inconsistent and cannot be displayed.");
}
