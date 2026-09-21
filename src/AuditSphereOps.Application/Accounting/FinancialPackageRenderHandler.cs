using System.Text.Json;
using System.Text.Json.Serialization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>Durable local rendering of one immutable financial-package revision.</summary>
public sealed class FinancialPackageRenderHandler : IOperationHandler
{
  public const string Kind = "RenderFinancialPackage.v1";
  public const string ArtifactVersion = "financial-package-text.v1";
  public OperationDefinition Definition { get; } =
    new(Kind, OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION);

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      var payload = ReadPayload(request.PayloadJson, request.TargetId);
      if (request.ClientId != payload.ClientId || request.EngagementId != payload.EngagementId)
        throw new InvalidOperationException("operation scope mismatch");
      return SerializePayload(payload.ClientId, payload.EngagementId, payload.PackageId);
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException)
    {
      throw new OperationBlockedException("invalid-financial-package-render-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    if (op.ClientId is not { } clientId || op.EngagementId is not { } engagementId || op.OriginatorId is null)
      throw new OperationBlockedException("package-render-scope-or-revision-conflict", authorization: true);
    var payload = ReadPayload(op.PayloadJson, op.TargetId);
    var package = await db.FinancialPackages.FromSqlInterpolated($"""
      SELECT * FROM financial_packages WHERE id = {op.TargetId} AND firm_id = {op.FirmId}
        AND client_id = {clientId} AND engagement_id = {engagementId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (package is null || package.Id != payload.PackageId || package.Revision != op.ExpectedRevision ||
        string.IsNullOrWhiteSpace(package.CalculationHash))
      throw new OperationBlockedException("package-render-scope-or-revision-conflict", authorization: true);
  }

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op,
    OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    if (op.ClientId is not { } clientId || op.EngagementId is not { } engagementId || op.OriginatorId is not { } userId)
      throw new OperationBlockedException("package-render-scope-or-revision-conflict", authorization: true);
    var payload = ReadPayload(op.PayloadJson, op.TargetId);
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.FirmId == op.FirmId, ct);
    if (user is null)
      throw new OperationBlockedException("package-render-scope-or-revision-conflict", authorization: true);
    var result = await FinancialStatementService.RenderPackageArtifactAsync(db,
      new ActorContext(user.Id, user.FirmId, user.SessionEpoch, ["AccountingPreparer"]), payload.PackageId, ct);
    if (!result.Succeeded || result.Value is null)
      throw new OperationBlockedException(result.ErrorCode ?? "financial-package-render-blocked",
        authorization: result.ErrorCode == ErrorCodes.ScopeDenied);
    if (payload.ClientId != clientId || payload.EngagementId != engagementId)
      throw new OperationBlockedException("package-render-scope-or-revision-conflict", authorization: true);
    db.OperationEvents.Add(new OperationEvent
    {
      Id = Guid.CreateVersion7(), OperationId = op.Id, Token = op.AttemptToken,
      Kind = "financial.package-rendered.v1", Executor = op.LeaseOwner!, OccurredAt = DateTimeOffset.UtcNow
    });
    return new(payload.PackageId.ToString("D"), result.Value.ArtifactSha256Hex);
  }

  public Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("local-operation-has-no-provider-effect");

  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("local-operation-has-no-provider-effect");

  public static string SerializePayload(Guid clientId, Guid engagementId, Guid packageId) =>
    JsonSerializer.Serialize(new RenderPayload(clientId, engagementId, packageId, ArtifactVersion), JsonOptions);

  private static RenderPayload ReadPayload(string json, Guid targetId)
  {
    var payload = JsonSerializer.Deserialize<RenderPayload>(json, JsonOptions)
      ?? throw new InvalidOperationException("empty financial-package render payload");
    if (payload.ClientId == Guid.Empty || payload.EngagementId == Guid.Empty ||
        payload.PackageId == Guid.Empty || payload.PackageId != targetId ||
        payload.ArtifactVersion != ArtifactVersion)
      throw new InvalidOperationException("invalid financial-package render payload");
    return payload;
  }

  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
  };

  private sealed record RenderPayload(Guid ClientId, Guid EngagementId, Guid PackageId, string ArtifactVersion);
}
