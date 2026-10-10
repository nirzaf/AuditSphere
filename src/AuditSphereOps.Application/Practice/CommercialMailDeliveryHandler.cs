using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>
/// Delivers queued commercial emails (the official advance-payment receipt, proposal dispatches) through the isolated
/// Acceptance "mail" worker and the same provider adapters as PBC mail. The operation is keyed to the notification, so
/// a verified provider receipt is terminal and never sent twice; refused and unknown outcomes are durable receipt
/// states that only an explicit operator re-arm can re-attempt.
/// </summary>
public sealed class CommercialMailDeliveryHandler(IAuditSphereDbContextFactory factory, IPbcMailSender sender) : IOperationHandler
{
  public const string Kind = "SendCommercialMail.v1";
  public OperationDefinition Definition { get; } = new(Kind, OperationMode.LIVE, OperationAuthority.LIVE_PROVIDER, Group: "mail");

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      using var document = JsonDocument.Parse(request.PayloadJson);
      // The client binding is optional: a pre-conversion proposal dispatch has no client yet, but the
      // notification identity itself keys the operation and the send is firm-scoped.
      if (!document.RootElement.TryGetProperty("notificationId", out var id) || !id.TryGetGuid(out var value) ||
          value != request.TargetId || request.ExpectedRevision != 1)
        throw new OperationBlockedException("invalid-mail-request");
      return JsonSerializer.Serialize(new { notificationId = value.ToString("D") });
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("invalid-mail-request");
    }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    var mail = await db.CommercialNotifications.FromSqlInterpolated($"""
      SELECT * FROM commercial_notifications WHERE firm_id = {op.FirmId} AND id = {op.TargetId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    // A verified provider receipt is terminal: the send is never repeated. A refused or unknown outcome may be
    // re-attempted only through an explicit operator re-arm, which re-enters this lock through the same path.
    if (mail is null || mail.DeliveryState is CommercialDeliveryStates.ProviderAccepted or CommercialDeliveryStates.Delivered ||
      string.IsNullOrWhiteSpace(mail.Recipient) || string.IsNullOrWhiteSpace(mail.Subject))
      throw new OperationBlockedException("mail-scope-or-state-conflict", authorization: true);
  }

  public async Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    var mail = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.FirmId == op.FirmId && x.Id == op.TargetId, ct);
    // The send attempt is durable before the provider call: queued, in-flight, accepted, refused and unknown
    // outcomes remain distinguishable whatever happens next (STE 4.1.2 delivery receipts).
    await MarkDeliveryStateAsync(factory, op.FirmId, op.TargetId, CommercialDeliveryStates.Dispatched, ct);
    try
    {
      await sender.SendAsync(new(mail.Recipient, mail.Subject, mail.Body, op.CorrelationId), ct);
    }
    catch (SafeRetryException)
    {
      throw; // transient pre-provider failure: the attempt stays DISPATCHED and the operation retries
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (OperationBlockedException)
    {
      await MarkDeliveryStateAsync(factory, op.FirmId, op.TargetId, CommercialDeliveryStates.Rejected, ct);
      throw; // the provider refused the send; the email was not accepted
    }
    catch (Exception)
    {
      await MarkDeliveryStateAsync(factory, op.FirmId, op.TargetId, CommercialDeliveryStates.Unknown, ct);
      throw; // the outcome is uncertain: reconciliation — never a blind resend — must resolve it
    }
    return Expected(op);
  }

  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) =>
    throw new OperationBlockedException("mail-delivery-outcome-uncertain");

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op, OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    var expected = Expected(op);
    if (verifiedRemoteResult != expected) throw new OperationBlockedException("mail-provider-receipt-conflict");
    var mail = await db.CommercialNotifications.SingleAsync(x => x.FirmId == op.FirmId && x.Id == op.TargetId, ct);
    mail.DeliveryState = CommercialDeliveryStates.ProviderAccepted;
    mail.DeliveredAt = DateTimeOffset.UtcNow;
    return expected;
  }

  private static async Task MarkDeliveryStateAsync(IAuditSphereDbContextFactory factory, Guid firmId, Guid notificationId, string state, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    await db.CommercialNotifications
      .Where(x => x.FirmId == firmId && x.Id == notificationId)
      .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DeliveryState, state), ct);
  }

  private static OperationResult Expected(DurableOperation op) => new(op.CorrelationId.ToString("D"), Hashing.Sha256Hex(op.PayloadJson));
}

public sealed class CommercialMailDiscovery(
  IAuditSphereDbContextFactory factory, IOperationStore store, CommercialMailDeliveryHandler handler, WorkerOptions options) : IPendingOperationDiscovery
{
  public async Task<int> EnqueuePendingAsync(CancellationToken ct)
  {
    await using var read = await factory.CreateAsync(ct);
    // Receipt and proposal dispatches alike carry the owning client when one exists, so the mail
    // worker discovers every queued commercial email without joining through the fee-milestone path;
    // a pre-conversion proposal dispatch legitimately has no client binding yet.
    var pending = await read.CommercialNotifications.AsNoTracking()
      .Where(n => n.FirmId == options.FirmId && n.DeliveryState == "QUEUED" &&
        !read.DurableOperations.Any(o => o.FirmId == n.FirmId && o.TargetId == n.Id && o.OperationKind == CommercialMailDeliveryHandler.Kind))
      .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)
      .Select(n => new { n.Id, ClientId = n.PracticeClientId })
      .Take(25).ToListAsync(ct);
    var count = 0;
    foreach (var mail in pending)
    {
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var result = await store.EnqueueAsync(db, new OperationRequest(options.FirmId, mail.ClientId, null, CommercialMailDeliveryHandler.Kind,
        mail.Id, 1, "commercial-mail:" + mail.Id.ToString("D"), JsonSerializer.Serialize(new { notificationId = mail.Id.ToString("D") })), handler, ct);
      if (!result.Succeeded) continue;
      await tx.CommitAsync(ct);
      count++;
    }
    return count;
  }
}
