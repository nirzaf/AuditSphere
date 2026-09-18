using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed class TrialBalanceValidationHandler : IOperationHandler
{
  public const string Kind = "ValidateTrialBalance.v1";
  public OperationDefinition Definition { get; } = new(Kind, OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION);

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      using var document = JsonDocument.Parse(request.PayloadJson);
      var fields = document.RootElement.EnumerateObject().ToArray();
      if (fields.Length != 1 || fields[0].Name != "datasetId" ||
          !fields[0].Value.TryGetGuid(out var id) || id != request.TargetId ||
          request.ClientId is null || request.EngagementId is null)
        throw new OperationBlockedException("invalid-validation-request");
      return JsonSerializer.Serialize(new { datasetId = id.ToString("D") });
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("invalid-validation-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    // Retain the existing bounded-slice row-writer interlock.
    await db.Database.ExecuteSqlRawAsync("LOCK TABLE trial_balance_rows IN SHARE MODE", ct);
    var rows = await db.TrialBalanceDatasets.FromSqlInterpolated($"""
      SELECT * FROM trial_balance_datasets WHERE id = {op.TargetId} AND firm_id = {op.FirmId}
        AND client_id = {op.ClientId} AND engagement_id = {op.EngagementId} FOR UPDATE
      """).ToListAsync(ct);
    if (rows.Count != 1 || rows[0].Revision != op.ExpectedRevision || rows[0].SourceKind != "Raw" ||
        rows[0].ValidationStatus != "Pending" || !await db.Engagements.AnyAsync(e => e.Id == op.EngagementId &&
          e.FirmId == op.FirmId && e.PracticeClientId == op.ClientId, ct))
      throw new OperationBlockedException("validation-scope-or-revision-conflict", authorization: true);
  }

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op,
    OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    var dataset = await db.TrialBalanceDatasets.SingleAsync(d => d.Id == op.TargetId, ct);
    var rows = await db.TrialBalanceRows.AsNoTracking().Where(r => r.DatasetId == dataset.Id).ToListAsync(ct);
    var total = rows.Sum(r => r.Amount);
    var valid = rows.Count > 0 && total == 0 && !string.IsNullOrWhiteSpace(dataset.Currency) &&
      rows.All(r => r.Currency == dataset.Currency && !string.IsNullOrWhiteSpace(r.AccountCode)) &&
      rows.Select(r => (r.Entity, r.AccountCode)).Distinct().Count() == rows.Count;
    dataset.ControlTotal = total;
    dataset.Balanced = rows.Count > 0 && total == 0;
    dataset.ValidationStatus = valid ? "Accepted" : "Rejected";
    var evidence = JsonSerializer.Serialize(new { dataset.Id, dataset.Revision, dataset.ValidationStatus, dataset.ControlTotal });
    db.OperationEvents.Add(new OperationEvent { Id = Guid.CreateVersion7(), OperationId = op.Id,
      Token = op.AttemptToken, Kind = "tb.validated.v1", Executor = op.LeaseOwner!, OccurredAt = DateTimeOffset.UtcNow });
    return new(dataset.Id.ToString("D"), Hashing.Sha256Hex(evidence));
  }

  public Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("local-operation-has-no-provider-effect");
  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("local-operation-has-no-provider-effect");
}

public sealed class TrialBalanceDiscovery(IAuditSphereDbContextFactory factory, IOperationStore store,
  TrialBalanceValidationHandler handler, WorkerOptions options) : IPendingOperationDiscovery
{
  public async Task<int> EnqueuePendingAsync(CancellationToken ct)
  {
    await using var read = await factory.CreateAsync(ct);
    var pending = await read.TrialBalanceDatasets.AsNoTracking()
      .Where(d => d.FirmId == options.FirmId && d.SourceKind == "Raw" && d.ValidationStatus == "Pending" &&
        !read.DurableOperations.Any(o => o.FirmId == d.FirmId && o.TargetId == d.Id &&
          o.ExpectedRevision == d.Revision && o.OperationKind == TrialBalanceValidationHandler.Kind))
      .OrderBy(d => d.ImportedAt).ThenBy(d => d.Id).Take(25).ToListAsync(ct);
    var count = 0;
    foreach (var dataset in pending)
    {
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var result = await store.EnqueueAsync(db, new(dataset.FirmId, dataset.ClientId, dataset.EngagementId,
        TrialBalanceValidationHandler.Kind, dataset.Id, dataset.Revision,
        $"tb-validation:{dataset.Id:D}:{dataset.Revision}",
        JsonSerializer.Serialize(new { datasetId = dataset.Id.ToString("D") }), dataset.ImportedByUserId), handler, ct);
      if (!result.Succeeded) continue;
      await tx.CommitAsync(ct);
      count++;
    }
    return count;
  }
}
