using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PbcMailPlan(string Recipient, string Subject, string Body, Guid CorrelationId);

public interface IPbcMailSender
{
  Task SendAsync(PbcMailPlan plan, CancellationToken ct);
}

public sealed class PbcMailDeliveryHandler(
  IAuditSphereDbContextFactory factory,
  IPbcMailSender sender) : IOperationHandler
{
  public const string Kind = "SendPbcMail.v1";
  public OperationDefinition Definition { get; } = new(
    Kind, OperationMode.LIVE, OperationAuthority.LIVE_PROVIDER, Group: "mail");

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      using var document = JsonDocument.Parse(request.PayloadJson);
      var root = document.RootElement;
      if (!root.TryGetProperty("communicationId", out var id) || !id.TryGetGuid(out var value) ||
          value != request.TargetId || request.ExpectedRevision != 1 ||
          request.ClientId is null || request.EngagementId is null)
        throw new OperationBlockedException("invalid-mail-request");
      return JsonSerializer.Serialize(new { communicationId = value.ToString("D") });
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("invalid-mail-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    var mail = await db.PbcCommunications.FromSqlInterpolated($"""
      SELECT * FROM pbc_communications WHERE firm_id = {op.FirmId} AND id = {op.TargetId}
        AND client_id = {op.ClientId} AND engagement_id = {op.EngagementId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (mail is null || mail.Kind != PbcCommunicationKinds.Email ||
        mail.DeliveryState != PbcDeliveryStates.Queued || string.IsNullOrWhiteSpace(mail.RecipientEmail) ||
        string.IsNullOrWhiteSpace(mail.Subject))
      throw new OperationBlockedException("mail-scope-or-state-conflict", authorization: true);
  }

  public async Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    var mail = await db.PbcCommunications.AsNoTracking().SingleAsync(x =>
      x.FirmId == op.FirmId && x.Id == op.TargetId && x.ClientId == op.ClientId &&
      x.EngagementId == op.EngagementId, ct);
    await sender.SendAsync(new(mail.RecipientEmail!, mail.Subject!, mail.Body, op.CorrelationId), ct);
    return Expected(op);
  }

  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("mail-delivery-outcome-uncertain");

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op,
    OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    var expected = Expected(op);
    if (verifiedRemoteResult != expected)
      throw new OperationBlockedException("mail-provider-receipt-conflict");
    var mail = await db.PbcCommunications.SingleAsync(x => x.FirmId == op.FirmId && x.Id == op.TargetId, ct);
    mail.DeliveryState = PbcDeliveryStates.Sent;
    mail.ProviderCorrelationId = expected.Identity;
    mail.DeliveredAt = DateTimeOffset.UtcNow;
    return expected;
  }

  private static OperationResult Expected(DurableOperation op) =>
    new(op.CorrelationId.ToString("D"), Hashing.Sha256Hex(op.PayloadJson));

}

public sealed class PbcMailDiscovery(
  IAuditSphereDbContextFactory factory,
  IOperationStore store,
  PbcMailDeliveryHandler handler,
  WorkerOptions options) : IPendingOperationDiscovery
{
  public async Task<int> EnqueuePendingAsync(CancellationToken ct)
  {
    await using var read = await factory.CreateAsync(ct);
    var pending = await read.PbcCommunications.AsNoTracking().Where(x =>
        x.FirmId == options.FirmId && x.Kind == PbcCommunicationKinds.Email &&
        x.DeliveryState == PbcDeliveryStates.Queued &&
        !read.DurableOperations.Any(o => o.FirmId == x.FirmId && o.TargetId == x.Id &&
          o.OperationKind == PbcMailDeliveryHandler.Kind))
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(25).ToListAsync(ct);
    var count = 0;
    foreach (var mail in pending)
    {
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var result = await store.EnqueueAsync(db, new OperationRequest(
        mail.FirmId, mail.ClientId, mail.EngagementId, PbcMailDeliveryHandler.Kind,
        mail.Id, 1, "pbc-mail:" + mail.Id.ToString("D"),
        JsonSerializer.Serialize(new { communicationId = mail.Id.ToString("D") }), mail.AuthorUserId), handler, ct);
      if (!result.Succeeded) continue;
      await tx.CommitAsync(ct);
      count++;
    }
    return count;
  }
}
