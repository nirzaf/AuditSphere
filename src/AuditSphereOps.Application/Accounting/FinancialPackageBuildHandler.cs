using System.Text.Json;
using System.Text.Json.Serialization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>Durable local calculation for one exact financial-package build request.</summary>
public sealed class FinancialPackageBuildHandler : IOperationHandler
{
  public const string Kind = "BuildFinancialPackage.v1";
  public OperationDefinition Definition { get; } =
    new(Kind, OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION);

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      var payload = ReadPayload(request.PayloadJson, request.TargetId);
      if (request.ClientId != payload.ClientId || request.EngagementId != payload.EngagementId)
        throw new InvalidOperationException("operation scope mismatch");
      return SerializePayload(payload.ClientId, payload.EngagementId, payload.Request);
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException)
    {
      throw new OperationBlockedException("invalid-financial-package-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    if (op.ClientId is not { } clientId || op.EngagementId is not { } engagementId || op.OriginatorId is null)
      throw new OperationBlockedException("package-scope-or-revision-conflict", authorization: true);
    var payload = ReadPayload(op.PayloadJson, op.TargetId);
    var mapping = await db.MappingVersions.FromSqlInterpolated($"""
      SELECT * FROM mapping_versions WHERE id = {op.TargetId} AND firm_id = {op.FirmId}
        AND client_id = {clientId} AND engagement_id = {engagementId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    var plan = await db.AdjustmentPlans.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == payload.Request.AdjustmentPlanId && x.FirmId == op.FirmId &&
      x.ClientId == clientId && x.EngagementId == engagementId, ct);
    if (mapping is null || mapping.Version != op.ExpectedRevision || mapping.Status != AccountingPackageStates.MappingApproved ||
        mapping.Id != payload.Request.MappingVersionId || plan is null || plan.Status != "Finalized" ||
        plan.BaseDatasetId != mapping.DatasetId || payload.Request.AdjustmentPlanId != plan.Id)
      throw new OperationBlockedException("package-scope-or-revision-conflict", authorization: true);
  }

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op,
    OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    if (op.ClientId is not { } clientId || op.EngagementId is not { } engagementId || op.OriginatorId is not { } userId)
      throw new OperationBlockedException("package-scope-or-revision-conflict", authorization: true);
    var payload = ReadPayload(op.PayloadJson, op.TargetId);
    var accountingDb = db as IAuditSphereDbContext ?? throw new OperationBlockedException("accounting-context-required");
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.FirmId == op.FirmId, ct);
    if (user is null)
      throw new OperationBlockedException("package-scope-or-revision-conflict", authorization: true);
    var result = await FinancialStatementService.BuildFinancialPackageAsync(accountingDb,
      new ActorContext(user.Id, user.FirmId, user.SessionEpoch, ["AccountingPreparer"]), payload.Request, ct);
    if (!result.Succeeded || result.Value is null)
      throw new OperationBlockedException(result.ErrorCode ?? "financial-package-build-blocked",
        authorization: result.ErrorCode == ErrorCodes.ScopeDenied);
    if (result.Value.PackageId == Guid.Empty || payload.ClientId != clientId || payload.EngagementId != engagementId)
      throw new OperationBlockedException("package-scope-or-revision-conflict", authorization: true);
    db.OperationEvents.Add(new OperationEvent
    {
      Id = Guid.CreateVersion7(), OperationId = op.Id, Token = op.AttemptToken,
      Kind = "financial.package-built.v1", Executor = op.LeaseOwner!, OccurredAt = DateTimeOffset.UtcNow
    });
    return new(result.Value.PackageId.ToString("D"), result.Value.CalculationHash);
  }

  public Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("local-operation-has-no-provider-effect");

  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("local-operation-has-no-provider-effect");

  public static string SerializePayload(Guid clientId, Guid engagementId, BuildFinancialPackageRequest request) =>
    JsonSerializer.Serialize(new BuildPayload(clientId, engagementId, request), JsonOptions);

  private static BuildPayload ReadPayload(string json, Guid targetId)
  {
    var payload = JsonSerializer.Deserialize<BuildPayload>(json, JsonOptions)
      ?? throw new InvalidOperationException("empty financial-package payload");
    if (payload.ClientId == Guid.Empty || payload.EngagementId == Guid.Empty ||
        payload.Request.MappingVersionId == Guid.Empty || payload.Request.MappingVersionId != targetId ||
        payload.Request.AdjustmentPlanId == Guid.Empty)
      throw new InvalidOperationException("invalid financial-package payload");
    return payload;
  }

  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
  };

  private sealed record BuildPayload(Guid ClientId, Guid EngagementId, BuildFinancialPackageRequest Request);
}
