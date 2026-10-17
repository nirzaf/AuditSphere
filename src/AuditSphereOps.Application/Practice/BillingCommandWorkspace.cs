using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record BillingCommandOutcome(string Action, Guid ResultId, Guid? ReceiptId, Guid? InvoiceId,
  string? Amount, DateTimeOffset CreatedAt);
public sealed record BillingCommandReceiptView(Guid RequestId, string Action, string RequestHash,
  BillingCommandOutcome Outcome, DateTimeOffset CreatedAt);

internal sealed record BillingCommandReplay<T>(bool Found, T? Value, string? ErrorCode = null, string? Message = null);

/// <summary>Shared exact identity, replay and actor-scoped recovery for the three generic billing mutations.</summary>
public static class BillingCommandWorkspace
{
  private static readonly string[] ReceiptRoles = ["FinanceManager", "FinanceReviewer"];
  private static readonly string[] CreditRoles = ["FinanceManager"];

  public static Guid DeriveRequestId(string stableIdentity)
  {
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stableIdentity));
    bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
    bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
    return new Guid(bytes.AsSpan(0, 16));
  }

  public static string IntentHash(string action, Guid firmId, Guid clientId, params string?[] values)
  {
    var canonical = JsonSerializer.Serialize(new { version = 1, action, firmId, clientId, values });
    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
  }

  internal static async Task<BillingCommandReplay<T>> FindReplayAsync<T>(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, Guid requestId, string action, string requestHash, string[] roles, Func<string, T> decode,
    CancellationToken ct)
  {
    var receipt = await db.BillingCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (receipt is null) return new(false, default);
    if (receipt.ClientId != clientId || receipt.Action != action || receipt.RequestHash != requestHash)
      return new(true, default, ErrorCodes.IdempotencyConflict, "Changed billing intent cannot reuse this request identity.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return new(true, default, auth.ErrorCode, auth.Message);
    try { return new(true, decode(receipt.OutcomeJson)); }
    catch (JsonException) { return new(true, default, ErrorCodes.GateBlocked, "The retained billing outcome is unavailable; keep this request unresolved."); }
  }

  public static async Task<CommandResult<BillingCommandReceiptView>> GetAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid requestId, CancellationToken ct)
  {
    if (requestId == Guid.Empty)
      return CommandResult<BillingCommandReceiptView>.Fail("billing.command-invalid", "A valid billing request identity is required.");
    var row = await db.BillingCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is null) return CommandResult<BillingCommandReceiptView>.Fail(ErrorCodes.ScopeDenied, "Billing command receipt is unavailable.");
    var roles = row.Action == "ISSUE_CREDIT_NOTE" ? CreditRoles : ReceiptRoles;
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, row.ClientId, RequiredRoles: roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<BillingCommandReceiptView>.Fail(ErrorCodes.ScopeDenied, "Billing command receipt is unavailable.");
    try
    {
      var outcome = JsonSerializer.Deserialize<BillingCommandOutcome>(row.OutcomeJson);
      return outcome is null
        ? CommandResult<BillingCommandReceiptView>.Fail(ErrorCodes.GateBlocked, "The retained billing outcome is unavailable.")
        : CommandResult<BillingCommandReceiptView>.Ok(new(row.RequestId, row.Action, row.RequestHash, outcome, row.CreatedAt));
    }
    catch (JsonException)
    {
      return CommandResult<BillingCommandReceiptView>.Fail(ErrorCodes.GateBlocked, "The retained billing outcome is unavailable.");
    }
  }

  internal static BillingCommandReceipt Create(ActorContext actor, Guid clientId, Guid requestId, string action,
    string hash, BillingCommandOutcome outcome) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, ActorId = actor.UserId,
    ActorEpoch = actor.SessionEpoch, RequestId = requestId, Action = action, RequestHash = hash,
    OutcomeJson = JsonSerializer.Serialize(outcome), CreatedAt = outcome.CreatedAt
  };

  internal static string Decimal(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

  internal static Guid DecodeResultId(string json) =>
    JsonSerializer.Deserialize<BillingCommandOutcome>(json)?.ResultId ?? throw new JsonException();

  internal static ReceiptAllocationCommandOutcome DecodeAllocation(string json)
  {
    var outcome = JsonSerializer.Deserialize<BillingCommandOutcome>(json) ?? throw new JsonException();
    if (outcome.ReceiptId is not { } receiptId || outcome.InvoiceId is not { } invoiceId || outcome.Amount is null)
      throw new JsonException();
    return new(outcome.ResultId, receiptId, invoiceId, outcome.Amount);
  }
}
