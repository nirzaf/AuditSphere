using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record ContactCreationFields(string Name, string Email, string Role, bool Primary);
public sealed record ContactCreationRequest(Guid RequestId, string ReviewBasis, ContactCreationFields Fields,
  bool Reviewed = false, string? ExpectedRequestHash = null);
public sealed record ContactPrimaryView(Guid Id, string Name);
public sealed record ContactCreationState(Guid ClientId, string ClientName, string SafetyGeneration,
  string ReviewBasis, IReadOnlyList<ContactPrimaryView> CurrentPrimary);
public sealed record ContactCreationPreview(Guid ClientId, Guid RequestId, string ReviewBasis, string RequestHash,
  ContactCreationFields Fields, IReadOnlyList<ContactPrimaryView> ReplacedPrimary);
public sealed record ContactCreationReceipt(Guid Id, Guid ClientId, Guid ContactId, Guid ActorId, Guid RequestId,
  string RequestHash, string ReviewBasis, string ResultGeneration, DateTimeOffset CreatedAt);
public sealed record ContactCreationLookup(bool Found, ContactCreationReceipt? Receipt);

/// <summary>Local contact review, guarded creation and immutable receipt. Contact email is never an identity binding.</summary>
public static class ClientContactCreationWorkspace
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "RelationshipManager"];
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static Task<CommandResult> Authorize(IAuditSphereDbContext db, ActorContext actor, Guid id, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, ClientId: id, RequiredRoles: Roles, InternalOnly: true), ct);
  private static bool HashValid(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool TextValid(string? s, int limit) => !string.IsNullOrWhiteSpace(s) && s.Length <= limit && !s.Any(char.IsControl);
  private static ContactCreationFields? Fields(ContactCreationFields? f)
  {
    if (f is null || !TextValid(f.Name, 200) || !TextValid(f.Email, 254) || !TextValid(f.Role, 200)) return null;
    var email = f.Email.Trim(); var at = email.IndexOf('@');
    return at > 0 && at == email.LastIndexOf('@') && at < email.Length - 1 && !email.Any(char.IsWhiteSpace)
      ? new(f.Name.Trim(), email, f.Role.Trim(), f.Primary) : null;
  }
  // Request identity binds immutable contact intent; a fresh revision preview can safely retry that same intent.
  // Each new publication still requires the current review basis; the committed basis is retained in its receipt.
  private static string RequestHash(ActorContext a, Guid id, ContactCreationRequest r, ContactCreationFields f) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, id, r.RequestId, Fields = f }));
  private static ContactCreationReceipt Receipt(ClientContactCreation r) => new(r.Id,r.ClientId,r.ContactId,r.ActorId,r.RequestId,
    r.RequestHash,r.ReviewBasis,r.ResultGeneration.ToString(CultureInfo.InvariantCulture),r.CreatedAt);

  public static async Task<CommandResult<ContactCreationState>> StateAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, CancellationToken ct = default)
  {
    var auth = await Authorize(db,a,id,ct);
    if (!auth.Succeeded) return Fail<ContactCreationState>(ErrorCodes.ScopeDenied,"This client contact workspace is unavailable.");
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.Id==id,ct);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.Id==id)
      .Select(x=>(long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if (client is null || generation is null) return Fail<ContactCreationState>(ErrorCodes.ScopeDenied,"This client contact workspace is unavailable.");
    if (generation == long.MaxValue) return Fail<ContactCreationState>(ErrorCodes.GateBlocked,"Client revision capacity is exhausted. Contact an administrator.");
    var primary = await db.ClientContacts.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.PracticeClientId==id&&x.Primary)
      .OrderBy(x=>x.Id).Select(x=>new ContactPrimaryView(x.Id,x.FullName)).Take(51).ToListAsync(ct);
    if (primary.Count > 50) return Fail<ContactCreationState>(ErrorCodes.GateBlocked,"The primary contact set requires administrator review.");
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId,id,client.LegalName,Generation=generation.Value,Primary=primary }));
    auth = await Authorize(db,a,id,ct);
    return auth.Succeeded ? CommandResult<ContactCreationState>.Ok(new(id,client.LegalName,generation.Value.ToString(CultureInfo.InvariantCulture),basis,primary))
      : Fail<ContactCreationState>(ErrorCodes.ScopeDenied,"This client contact workspace is unavailable.");
  }

  public static async Task<CommandResult<ContactCreationPreview>> PreviewAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, ContactCreationRequest? r, CancellationToken ct = default)
  {
    var fields = Fields(r?.Fields);
    if (r is null || r.RequestId==Guid.Empty || !HashValid(r.ReviewBasis) || fields is null)
      return Fail<ContactCreationPreview>("request.invalid","Enter a valid contact name, email and role before review.");
    var state = await StateAsync(db,a,id,ct);
    if (!state.Succeeded) return Fail<ContactCreationPreview>(state.ErrorCode!,state.Message!);
    if (state.Value!.ReviewBasis != r.ReviewBasis) return Fail<ContactCreationPreview>(ErrorCodes.GenerationStale,"Client context changed. Refresh and review again.");
    return CommandResult<ContactCreationPreview>.Ok(new(id,r.RequestId,r.ReviewBasis,RequestHash(a,id,r,fields),fields,
      fields.Primary ? state.Value.CurrentPrimary : Array.Empty<ContactPrimaryView>()));
  }

  public static async Task<CommandResult<ContactCreationReceipt>> ExecuteAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, ContactCreationRequest? r, CancellationToken ct = default)
  {
    var fields = Fields(r?.Fields);
    if (r is null || !r.Reviewed || r.RequestId==Guid.Empty || !HashValid(r.ReviewBasis) || fields is null ||
      !HashValid(r.ExpectedRequestHash) || r.ExpectedRequestHash!=RequestHash(a,id,r,fields))
      return Fail<ContactCreationReceipt>("request.invalid","Preview and explicitly confirm the exact contact intent.");
    var auth = await Authorize(db,a,id,ct);
    if (!auth.Succeeded) return Fail<ContactCreationReceipt>(ErrorCodes.ScopeDenied,"This client contact workspace is unavailable.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (guard is null) return Fail<ContactCreationReceipt>(ErrorCodes.ScopeDenied,"This client contact workspace is unavailable.");
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    var prior = await db.ClientContactCreations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActorId==a.UserId&&x.RequestId==r.RequestId,ct);
    if (prior is not null)
    {
      if (prior.ClientId!=id || prior.RequestHash!=r.ExpectedRequestHash)
        return Fail<ContactCreationReceipt>(ErrorCodes.IdempotencyConflict,"A changed contact intent cannot reuse this request identity.");
      auth = await Authorize(db,a,id,ct);
      if (!auth.Succeeded) return Fail<ContactCreationReceipt>(ErrorCodes.ScopeDenied,"This client contact workspace is unavailable.");
      await tx.CommitAsync(ct); return CommandResult<ContactCreationReceipt>.Ok(Receipt(prior));
    }
    var state = await StateAsync(db,a,id,ct);
    if (!state.Succeeded) return Fail<ContactCreationReceipt>(state.ErrorCode!,state.Message!);
    if (state.Value!.ReviewBasis!=r.ReviewBasis) return Fail<ContactCreationReceipt>(ErrorCodes.GenerationStale,"Client context changed. Refresh and review again.");
    var before = guard.InputGeneration; var now = DateTimeOffset.UtcNow;
    var created = await PracticeCrmService.CreateClientContactAsync(db,a,new(id,fields.Name,fields.Email,fields.Role,
      ValidFrom:now,Primary:fields.Primary,ExpectedSafetyGeneration:before),ct);
    if (!created.Succeeded) return Fail<ContactCreationReceipt>(created.ErrorCode!,created.Message!);
    var row = new ClientContactCreation { Id=Guid.CreateVersion7(),FirmId=a.FirmId,ClientId=id,ContactId=created.Value,
      ActorId=a.UserId,ActorEpoch=a.SessionEpoch,RequestId=r.RequestId,RequestHash=r.ExpectedRequestHash!,ReviewBasis=r.ReviewBasis,
      PreviousGeneration=before,ResultGeneration=before+1,InputJson=JsonSerializer.Serialize(fields),
      PreviousPrimaryJson=JsonSerializer.Serialize(fields.Primary?state.Value.CurrentPrimary:Array.Empty<ContactPrimaryView>()),CreatedAt=now };
    db.ClientContactCreations.Add(row); await db.SaveChangesAsync(ct);
    auth = await Authorize(db,a,id,ct);
    if (!auth.Succeeded) return Fail<ContactCreationReceipt>(ErrorCodes.ScopeDenied,"This client contact workspace is unavailable.");
    await tx.CommitAsync(ct); return CommandResult<ContactCreationReceipt>.Ok(Receipt(row));
  }

  public static async Task<CommandResult<ContactCreationLookup>> LookupAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    var auth = await Authorize(db,a,id,ct);
    if (!auth.Succeeded || requestId==Guid.Empty || !HashValid(requestHash))
      return Fail<ContactCreationLookup>(ErrorCodes.ScopeDenied,"The retained contact request is unavailable.");
    var exists = await db.PracticeClients.AsNoTracking().AnyAsync(x=>x.FirmId==a.FirmId&&x.Id==id,ct);
    if (!exists) return Fail<ContactCreationLookup>(ErrorCodes.ScopeDenied,"The retained contact request is unavailable.");
    var row = await db.ClientContactCreations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActorId==a.UserId&&x.RequestId==requestId,ct);
    if (row is not null && (row.ClientId!=id || row.RequestHash!=requestHash))
      return Fail<ContactCreationLookup>(ErrorCodes.IdempotencyConflict,"This reference belongs to a different exact contact intent.");
    auth = await Authorize(db,a,id,ct);
    return auth.Succeeded ? CommandResult<ContactCreationLookup>.Ok(new(row is not null,row is null?null:Receipt(row)))
      : Fail<ContactCreationLookup>(ErrorCodes.ScopeDenied,"The retained contact request is unavailable.");
  }
}
