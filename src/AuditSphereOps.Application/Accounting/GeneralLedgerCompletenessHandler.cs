using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>Durable local calculation for a sealed TB/GL completeness bridge.</summary>
public sealed class GeneralLedgerCompletenessHandler : IOperationHandler
{
  public const string Kind = "BuildGeneralLedgerCompleteness.v1";
  public OperationDefinition Definition { get; } =
    new(Kind, OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION);

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      var payload = ReadPayload(request.PayloadJson, request.TargetId);
      if (request.ClientId != payload.ClientId || request.EngagementId != payload.EngagementId)
        throw new InvalidOperationException("operation scope mismatch");
      return JsonSerializer.Serialize(new
      {
        clientId = payload.ClientId.ToString("D"), engagementId = payload.EngagementId.ToString("D"),
        periodId = payload.PeriodId.ToString("D"), bookId = payload.BookId?.ToString("D"),
        trialBalanceDatasetId = payload.TrialBalanceDatasetId.ToString("D"), importBatchId = payload.ImportBatchId.ToString("D"),
        evidenceReference = payload.EvidenceReference,
        openingTrialBalanceDatasetId = payload.OpeningTrialBalanceDatasetId?.ToString("D")
      });
    }
    catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
    {
      throw new OperationBlockedException("invalid-completeness-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    if (op.ClientId is not { } clientId || op.EngagementId is not { } engagementId)
      throw new OperationBlockedException("completeness-scope-or-revision-conflict", authorization: true);
    var payload = ReadPayload(op.PayloadJson, op.TargetId);
    var accountingDb = RequireAccountingContext(db);
    var dataset = await accountingDb.TrialBalanceDatasets.FromSqlInterpolated($"""
      SELECT * FROM trial_balance_datasets WHERE id = {op.TargetId} AND firm_id = {op.FirmId}
        AND client_id = {clientId} AND engagement_id = {engagementId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    var batch = await accountingDb.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == payload.ImportBatchId && x.FirmId == op.FirmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && x.Status == "SEALED", ct);
    var period = await accountingDb.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == payload.PeriodId && x.FirmId == op.FirmId && x.ClientId == clientId, ct);
    var openingDataset = payload.OpeningTrialBalanceDatasetId is { } openingDatasetId
      ? await accountingDb.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == openingDatasetId && x.FirmId == op.FirmId && x.ClientId == clientId && x.EngagementId == engagementId &&
        x.ValidationStatus == "Accepted" && x.ImportState == TrialBalanceImportStates.Sealed, ct)
      : null;
    if (dataset is null || dataset.Revision != op.ExpectedRevision || dataset.ValidationStatus != "Accepted" ||
        dataset.ImportState != TrialBalanceImportStates.Sealed || batch is null || period is null ||
        (payload.OpeningTrialBalanceDatasetId.HasValue && openingDataset is null) ||
        batch.PeriodId != payload.PeriodId || batch.BookId != payload.BookId ||
        !string.Equals(dataset.Currency, batch.Currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(dataset.LegalEntityKey, batch.LegalEntityKey, StringComparison.Ordinal) ||
        (payload.BookId.HasValue && !await accountingDb.ClientReportingBooks.AnyAsync(x =>
          x.Id == payload.BookId && x.FirmId == op.FirmId && x.ClientId == clientId && x.PeriodId == payload.PeriodId, ct)))
      throw new OperationBlockedException("completeness-scope-or-revision-conflict", authorization: true);
  }

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op,
    OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    if (op.ClientId is not { } clientId || op.EngagementId is not { } engagementId || op.OriginatorId is not { } userId)
      throw new OperationBlockedException("completeness-scope-or-revision-conflict", authorization: true);
    var payload = ReadPayload(op.PayloadJson, op.TargetId);
    var accountingDb = RequireAccountingContext(db);
    var existing = await accountingDb.GeneralLedgerCompletenessBridges.SingleOrDefaultAsync(x =>
      x.FirmId == op.FirmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      x.TrialBalanceDatasetId == payload.TrialBalanceDatasetId && x.ImportBatchId == payload.ImportBatchId, ct);
    if (existing is not null)
    {
      if (!string.Equals(existing.EvidenceReference, payload.EvidenceReference, StringComparison.Ordinal) ||
          existing.OpeningTrialBalanceDatasetId != payload.OpeningTrialBalanceDatasetId)
        throw new OperationBlockedException("completeness-duplicate");
      return new(existing.Id.ToString("D"), existing.AccountResidualDigest);
    }

    var user = await accountingDb.Users.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == userId && x.FirmId == op.FirmId, ct);
    if (user is null)
      throw new OperationBlockedException("completeness-scope-or-revision-conflict", authorization: true);
    var result = await AccountingAnalysisService.CreateGeneralLedgerCompletenessBridgeAsync(
      accountingDb, new ActorContext(user.Id, user.FirmId, user.SessionEpoch, ["AccountingPreparer"]),
      new GeneralLedgerCompletenessRequest(clientId, engagementId, payload.PeriodId, payload.BookId,
        payload.TrialBalanceDatasetId, payload.ImportBatchId, payload.EvidenceReference, payload.OpeningTrialBalanceDatasetId), ct);
    if (!result.Succeeded)
      throw new OperationBlockedException(result.ErrorCode ?? "completeness-calculation-blocked",
        authorization: result.ErrorCode == ErrorCodes.ScopeDenied);
    var bridge = await accountingDb.GeneralLedgerCompletenessBridges.AsNoTracking().SingleAsync(x =>
      x.Id == result.Value && x.FirmId == op.FirmId, ct);
    accountingDb.OperationEvents.Add(new OperationEvent
    {
      Id = Guid.CreateVersion7(), OperationId = op.Id, Token = op.AttemptToken,
      Kind = "gl.completeness-built.v1", Executor = op.LeaseOwner!, OccurredAt = DateTimeOffset.UtcNow
    });
    return new(bridge.Id.ToString("D"), bridge.AccountResidualDigest);
  }

  public Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("local-operation-has-no-provider-effect");

  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("local-operation-has-no-provider-effect");

  private static IClientAccountingDbContext RequireAccountingContext(IAuditSphereDbContext db) =>
    db as IClientAccountingDbContext ?? throw new OperationBlockedException("accounting-context-required");

  private static CompletenessPayload ReadPayload(string json, Guid targetId)
  {
    using var document = JsonDocument.Parse(json);
    var fields = document.RootElement.EnumerateObject().ToArray();
    var names = new[] { "clientId", "engagementId", "periodId", "bookId", "trialBalanceDatasetId", "importBatchId", "evidenceReference", "openingTrialBalanceDatasetId" };
    if (fields.Length != names.Length || fields.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != names.Length ||
        fields.Any(x => !names.Contains(x.Name, StringComparer.Ordinal)))
      throw new InvalidOperationException("invalid completeness fields");
    var root = document.RootElement;
    var payload = new CompletenessPayload(
      root.GetProperty("clientId").GetGuid(), root.GetProperty("engagementId").GetGuid(),
      root.GetProperty("periodId").GetGuid(), root.GetProperty("bookId").ValueKind == JsonValueKind.Null
        ? null : root.GetProperty("bookId").GetGuid(), root.GetProperty("trialBalanceDatasetId").GetGuid(),
      root.GetProperty("importBatchId").GetGuid(), root.GetProperty("evidenceReference").GetString() ?? string.Empty,
      root.GetProperty("openingTrialBalanceDatasetId").ValueKind == JsonValueKind.Null
        ? null : root.GetProperty("openingTrialBalanceDatasetId").GetGuid());
    if (payload.TrialBalanceDatasetId != targetId || payload.ClientId == Guid.Empty || payload.EngagementId == Guid.Empty ||
        payload.PeriodId == Guid.Empty || payload.ImportBatchId == Guid.Empty || string.IsNullOrWhiteSpace(payload.EvidenceReference) ||
        payload.EvidenceReference.Trim().Length > 2000)
      throw new InvalidOperationException("invalid completeness payload");
    return payload with { EvidenceReference = payload.EvidenceReference.Trim() };
  }

  private sealed record CompletenessPayload(Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? BookId,
    Guid TrialBalanceDatasetId, Guid ImportBatchId, string EvidenceReference, Guid? OpeningTrialBalanceDatasetId);
}
