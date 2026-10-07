using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalPostingReceiptView(Guid CommandId, Guid ClientId, Guid JournalId,
  Guid ActorUserId, string SubmittedRevision, string PostedRevision, string PreviewDigest, string IntentHash,
  string RecordedAt, string Status, ClientOperationalInvoiceOrigin? InvoiceOrigin = null);

public static partial class ClientOperationalLedgerWorkspace
{
  public static async Task<CommandResult<ClientOperationalPostingReceiptView>> GetPostingReceiptAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid commandId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, Reviewers, ct);
    if (!auth.Succeeded) return CommandResult<ClientOperationalPostingReceiptView>.Fail(auth.ErrorCode!, "Access denied.");
    var receipt = await db.ClientOperationalPostingReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.CommandId == commandId && x.ActorUserId == actor.UserId, ct);
    if (receipt is null) return CommandResult<ClientOperationalPostingReceiptView>.Fail("command.receipt-not-found", "No committed receipt is available to this actor in this client scope.");
    var invoiceOrigin = await InvoiceOriginAsync(db, actor.FirmId, clientId, receipt.JournalId, ct, receipt.SubmittedRevision);
    if (!(await AuthorizeAsync(db, actor, clientId, Reviewers, ct)).Succeeded)
      return CommandResult<ClientOperationalPostingReceiptView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<ClientOperationalPostingReceiptView>.Ok(new(receipt.CommandId, receipt.ClientId, receipt.JournalId,
      receipt.ActorUserId, receipt.SubmittedRevision.ToString(CultureInfo.InvariantCulture), receipt.PostedRevision.ToString(CultureInfo.InvariantCulture),
      receipt.PreviewDigest, receipt.IntentHash, receipt.RecordedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), "POSTED", invoiceOrigin));
  }

  private static string PostingIntent(ActorContext actor, Guid clientId, Guid journalId, ClientOperationalJournalDecisionRequest request) =>
    Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
      actor.FirmId, ClientId = clientId, JournalId = journalId, actor.UserId, Operation = "POST",
      request.ExpectedRevision, Decision = request.Decision.Trim().ToUpperInvariant(), Reason = request.Reason.Trim(), request.PreviewDigest })));

  private static long PostingCommandLock(Guid firmId, Guid clientId, Guid commandId) =>
    BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { firmId, clientId, commandId })));
}
