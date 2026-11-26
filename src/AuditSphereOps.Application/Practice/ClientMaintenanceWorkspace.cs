using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static class ClientMaintenanceKinds
{
  public const string Profile = "PROFILE";
  public const string Contact = "CONTACT";
}

public sealed record ClientProfileMaintenanceFields(string LegalName, string? CommercialName, string? RegistrationNumber,
  string? TaxRegistrationNumber, string? EntityType, string? Jurisdiction, string? RestrictedProfile);

public sealed record ClientContactMaintenanceFields(string FullName, string Email, string Role, string? Phone, string? Title,
  string? SignatoryAuthority, string? ApprovedScope, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, bool Primary, bool IsActive);

public sealed record ClientMaintenanceRequest(Guid RequestId, string ReviewBasis,
  ClientProfileMaintenanceFields? Profile = null, ClientContactMaintenanceFields? Contact = null,
  bool Reviewed = false, string? ExpectedRequestHash = null);

public sealed record ClientMaintenanceState(Guid ClientId, string OperationKind, string ClientName, string SafetyGeneration,
  string ReviewBasis, ClientProfileMaintenanceFields? Profile, Guid? ContactId,
  ClientContactMaintenanceFields? Contact, IReadOnlyList<ContactPrimaryView> CurrentPrimary);

public sealed record ClientMaintenancePreview(Guid ClientId, string OperationKind, Guid? ContactId, Guid RequestId,
  string ReviewBasis, string RequestHash, string BeforeJson, string AfterJson,
  IReadOnlyList<ContactPrimaryView> ReplacedPrimary, IReadOnlyList<ContactPrimaryView> ResultPrimary);

public sealed record ClientMaintenanceReceiptValue(Guid Id, Guid ClientId, Guid? ContactId, string OperationKind,
  Guid ActorId, long ActorEpoch, Guid RequestId, string RequestHash, string ReviewBasis,
  string PreviousGeneration, string ResultGeneration, string BeforeJson, string AfterJson, DateTimeOffset CreatedAt);

public sealed record ClientMaintenanceLookup(bool Found, ClientMaintenanceReceiptValue? Receipt);

/// <summary>Reviewed, generation-fenced edits to client master and contact fields.</summary>
public static class ClientMaintenanceWorkspace
{
  private const int MaximumReceiptJsonCharacters = 10_000_000; // The existing authenticated JSON request limit is 10 MiB.
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "RelationshipManager"];

  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static Task<CommandResult> Authorize(IClientMaintenanceDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: Roles, InternalOnly: true), ct);
  private static bool HashValid(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static string? Optional(string? value, int? maximum = null)
  {
    if (value is null) return null;
    var trimmed = value.Trim();
    if (trimmed.Length == 0) return null;
    if (maximum.HasValue && trimmed.Length > maximum.Value) return null;
    return trimmed;
  }

  private static ClientProfileMaintenanceFields? Normalize(ClientProfileMaintenanceFields? fields)
  {
    if (fields is null || string.IsNullOrWhiteSpace(fields.LegalName) ||
        fields.TaxRegistrationNumber?.Trim().Length > 64 || fields.EntityType?.Trim().Length > 64) return null;
    return new(fields.LegalName.Trim(), Optional(fields.CommercialName), Optional(fields.RegistrationNumber),
      Optional(fields.TaxRegistrationNumber, 64), Optional(fields.EntityType, 64), Optional(fields.Jurisdiction),
      Optional(fields.RestrictedProfile));
  }

  private static ClientContactMaintenanceFields? Normalize(ClientContactMaintenanceFields? fields)
  {
    if (fields is null || string.IsNullOrWhiteSpace(fields.FullName) ||
        string.IsNullOrWhiteSpace(fields.Email) || string.IsNullOrWhiteSpace(fields.Role) ||
        fields.Phone?.Trim().Length > 32 || fields.Title?.Trim().Length > 100 ||
        fields.SignatoryAuthority?.Trim().Length > 64 ||
        fields.ValidFrom.HasValue && fields.ValidTo.HasValue && fields.ValidFrom.Value > fields.ValidTo.Value)
      return null;
    var email = fields.Email.Trim();
    if (!MailAddress.TryCreate(email, out _)) return null;
    return new(fields.FullName.Trim(), email.ToLowerInvariant(), fields.Role.Trim(), Optional(fields.Phone, 32),
      Optional(fields.Title, 100), Optional(fields.SignatoryAuthority, 64), Optional(fields.ApprovedScope),
      fields.ValidFrom, fields.ValidTo, fields.Primary, fields.IsActive);
  }

  private static ClientMaintenanceReceiptValue Receipt(ClientMaintenanceReceipt r) => new(r.Id, r.ClientId, r.ContactId,
    r.OperationKind, r.ActorId, r.ActorEpoch, r.RequestId, r.RequestHash, r.ReviewBasis,
    r.PreviousGeneration.ToString(CultureInfo.InvariantCulture), r.ResultGeneration.ToString(CultureInfo.InvariantCulture),
    r.BeforeJson, r.AfterJson, r.CreatedAt);

  private static string Basis(ActorContext actor, Guid clientId, string kind, Guid? contactId, long generation,
    ClientProfileMaintenanceFields profile, ClientContactMaintenanceFields? contact, IReadOnlyList<ContactPrimaryView> primary) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      actor.FirmId, clientId, kind, contactId, Generation = generation, Profile = profile, Contact = contact, Primary = primary
    }));

  private static string RequestHash(ActorContext actor, Guid clientId, string kind, Guid? contactId,
    ClientMaintenanceRequest request, ClientProfileMaintenanceFields? profile, ClientContactMaintenanceFields? contact) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      actor.FirmId, actor.UserId, actor.SessionEpoch, clientId, kind, contactId, request.RequestId,
      request.ReviewBasis, Profile = profile, Contact = contact
    }));

  public static async Task<CommandResult<ClientMaintenanceState>> StateAsync(IClientMaintenanceDbContext db, ActorContext actor,
    Guid clientId, string operationKind, Guid? contactId = null, CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || operationKind is not (ClientMaintenanceKinds.Profile or ClientMaintenanceKinds.Contact) ||
        operationKind == ClientMaintenanceKinds.Profile && contactId is not null ||
        operationKind == ClientMaintenanceKinds.Contact && (!contactId.HasValue || contactId == Guid.Empty))
      return Fail<ClientMaintenanceState>("request.invalid", "A valid client maintenance context is required.");
    var auth = await Authorize(db, actor, clientId, ct);
    if (!auth.Succeeded) return Fail<ClientMaintenanceState>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");

    var client = await db.PracticeClients.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == clientId, ct);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if (client is null || generation is null)
      return Fail<ClientMaintenanceState>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");
    if (generation == long.MaxValue)
      return Fail<ClientMaintenanceState>(ErrorCodes.GateBlocked, "Client revision capacity is exhausted. Contact an administrator.");

    var profile = new ClientProfileMaintenanceFields(client.LegalName, client.CommercialName, client.RegistrationNumber,
      client.TaxRegistrationNumber, client.EntityType, client.Jurisdiction, client.RestrictedProfile);
    ClientContactMaintenanceFields? contact = null;
    if (operationKind == ClientMaintenanceKinds.Contact)
    {
      var row = await db.ClientContacts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.PracticeClientId == clientId && x.Id == contactId, ct);
      if (row is null) return Fail<ClientMaintenanceState>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");
      contact = new(row.FullName, row.Email, row.Role, row.Phone, row.Title, row.SignatoryAuthority, row.ApprovedScope,
        row.ValidFrom, row.ValidTo, row.Primary, row.IsActive);
    }
    var primary = operationKind == ClientMaintenanceKinds.Contact
      ? await db.ClientContacts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId && x.Primary)
        .OrderBy(x => x.Id).Select(x => new ContactPrimaryView(x.Id, x.FullName)).Take(51).ToListAsync(ct)
      : [];
    if (primary.Count > 50)
      return Fail<ClientMaintenanceState>(ErrorCodes.GateBlocked, "The primary contact set requires administrator review.");
    var basis = Basis(actor, clientId, operationKind, contactId, generation.Value, profile, contact, primary);
    auth = await Authorize(db, actor, clientId, ct);
    return auth.Succeeded
      ? CommandResult<ClientMaintenanceState>.Ok(new(clientId, operationKind, client.LegalName,
        generation.Value.ToString(CultureInfo.InvariantCulture), basis, profile, contactId, contact, primary))
      : Fail<ClientMaintenanceState>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");
  }

  public static async Task<CommandResult<ClientMaintenancePreview>> PreviewAsync(IClientMaintenanceDbContext db, ActorContext actor,
    Guid clientId, string operationKind, Guid? contactId, ClientMaintenanceRequest? request, CancellationToken ct = default)
  {
    var profile = Normalize(request?.Profile);
    var contact = Normalize(request?.Contact);
    if (request is null || request.RequestId == Guid.Empty || !HashValid(request.ReviewBasis) ||
        operationKind == ClientMaintenanceKinds.Profile && (profile is null || request.Contact is not null) ||
        operationKind == ClientMaintenanceKinds.Contact && (contact is null || request.Profile is not null) ||
        operationKind is not (ClientMaintenanceKinds.Profile or ClientMaintenanceKinds.Contact))
      return Fail<ClientMaintenancePreview>("crm.invalid", "Enter valid client details before review.");
    var state = await StateAsync(db, actor, clientId, operationKind, contactId, ct);
    if (!state.Succeeded) return Fail<ClientMaintenancePreview>(state.ErrorCode!, state.Message!);
    if (state.Value!.ReviewBasis != request.ReviewBasis)
      return Fail<ClientMaintenancePreview>(ErrorCodes.GenerationStale, "Client context changed. Refresh and review again.");

    var beforePrimary = state.Value.CurrentPrimary;
    var afterPrimary = operationKind == ClientMaintenanceKinds.Contact
      ? contact!.Primary ? [new ContactPrimaryView(contactId!.Value, contact.FullName)]
        : beforePrimary.Where(x => x.Id != contactId).ToArray()
      : Array.Empty<ContactPrimaryView>();
    var before = JsonSerializer.Serialize(new { state.Value.Profile, state.Value.Contact, Primary = beforePrimary });
    var after = JsonSerializer.Serialize(new { Profile = profile, Contact = contact, Primary = afterPrimary });
    if (before.Length > MaximumReceiptJsonCharacters || after.Length > MaximumReceiptJsonCharacters)
      return Fail<ClientMaintenancePreview>("crm.invalid", "The reviewed client details exceed the supported request size.");
    return CommandResult<ClientMaintenancePreview>.Ok(new(clientId, operationKind, contactId, request.RequestId,
      request.ReviewBasis, RequestHash(actor, clientId, operationKind, contactId, request, profile, contact),
      before, after, operationKind == ClientMaintenanceKinds.Contact && contact!.Primary ? beforePrimary : [], afterPrimary));
  }

  public static async Task<CommandResult<ClientMaintenanceReceiptValue>> ExecuteAsync(IClientMaintenanceDbContext db, ActorContext actor,
    Guid clientId, string operationKind, Guid? contactId, ClientMaintenanceRequest? request, CancellationToken ct = default)
  {
    var profile = Normalize(request?.Profile);
    var contact = Normalize(request?.Contact);
    if (request is null || !request.Reviewed || request.RequestId == Guid.Empty || !HashValid(request.ReviewBasis) ||
        !HashValid(request.ExpectedRequestHash) ||
        operationKind == ClientMaintenanceKinds.Profile && (profile is null || request.Contact is not null) ||
        operationKind == ClientMaintenanceKinds.Contact && (contact is null || request.Profile is not null) ||
        operationKind is not (ClientMaintenanceKinds.Profile or ClientMaintenanceKinds.Contact) ||
        request.ExpectedRequestHash != RequestHash(actor, clientId, operationKind, contactId, request, profile, contact))
      return Fail<ClientMaintenanceReceiptValue>("crm.invalid", "Preview and explicitly confirm the exact client maintenance intent.");

    var auth = await Authorize(db, actor, clientId, ct);
    if (!auth.Succeeded) return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await PracticeCrmService.LockFirmAsync(db, actor.FirmId, ct) is null)
      return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");
    auth = await Authorize(db, actor, clientId, ct);
    if (!auth.Succeeded) return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    var safety = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (safety is null) return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");
    auth = await Authorize(db, actor, clientId, ct);
    if (!auth.Succeeded) return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");

    var prior = await db.ClientMaintenanceReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    if (prior is not null)
    {
      if (prior.ClientId != clientId || prior.ContactId != contactId || prior.OperationKind != operationKind ||
          prior.ActorEpoch != actor.SessionEpoch || prior.RequestHash != request.ExpectedRequestHash)
        return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.IdempotencyConflict, "A changed client maintenance intent cannot reuse this request identity.");
      auth = await Authorize(db, actor, clientId, ct);
      if (!auth.Succeeded) return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");
      await tx.CommitAsync(ct);
      return CommandResult<ClientMaintenanceReceiptValue>.Ok(Receipt(prior));
    }

    if (safety.InputGeneration == long.MaxValue)
      return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.GateBlocked, "Client revision capacity is exhausted. Contact an administrator.");
    var state = await StateAsync(db, actor, clientId, operationKind, contactId, ct);
    if (!state.Succeeded) return Fail<ClientMaintenanceReceiptValue>(state.ErrorCode!, state.Message!);
    if (state.Value!.ReviewBasis != request.ReviewBasis)
      return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.GenerationStale, "Client context changed. Refresh and review again.");

    var beforePrimary = state.Value.CurrentPrimary;
    var afterPrimary = operationKind == ClientMaintenanceKinds.Contact
      ? contact!.Primary ? [new ContactPrimaryView(contactId!.Value, contact.FullName)]
        : beforePrimary.Where(x => x.Id != contactId).ToArray()
      : Array.Empty<ContactPrimaryView>();
    var beforeJson = JsonSerializer.Serialize(new { state.Value.Profile, state.Value.Contact, Primary = beforePrimary });
    var afterJson = JsonSerializer.Serialize(new { Profile = profile, Contact = contact, Primary = afterPrimary });
    if (beforeJson.Length > MaximumReceiptJsonCharacters || afterJson.Length > MaximumReceiptJsonCharacters)
      return Fail<ClientMaintenanceReceiptValue>("crm.invalid", "The reviewed client details exceed the supported request size.");

    var previousGeneration = safety.InputGeneration;
    var updated = operationKind == ClientMaintenanceKinds.Profile
      ? await PracticeCrmService.UpdateClientProfileAsync(db, actor, new UpdateClientProfileRequest(clientId,
        profile!.LegalName, profile.CommercialName, profile.RegistrationNumber, profile.TaxRegistrationNumber,
        profile.EntityType, profile.Jurisdiction, profile.RestrictedProfile, previousGeneration), ct)
      : await UpdateContactAsync(db, actor, clientId, contactId!.Value, contact!, safety, ct);
    if (!updated.Succeeded) return Fail<ClientMaintenanceReceiptValue>(updated.ErrorCode!, updated.Message!);
    if (safety.InputGeneration != previousGeneration + 1)
      return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.GenerationStale, "Client record changed during maintenance. Refresh and review again.");

    var row = new ClientMaintenanceReceipt
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, ContactId = contactId,
      OperationKind = operationKind, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
      RequestId = request.RequestId, RequestHash = request.ExpectedRequestHash!, ReviewBasis = request.ReviewBasis,
      PreviousGeneration = previousGeneration, ResultGeneration = previousGeneration + 1,
      BeforeJson = beforeJson, AfterJson = afterJson, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientMaintenanceReceipts.Add(row);
    await db.SaveChangesAsync(ct);
    auth = await Authorize(db, actor, clientId, ct);
    if (!auth.Succeeded) return Fail<ClientMaintenanceReceiptValue>(ErrorCodes.ScopeDenied, "This client maintenance workspace is unavailable.");
    await tx.CommitAsync(ct);
    return CommandResult<ClientMaintenanceReceiptValue>.Ok(Receipt(row));
  }

  private static async Task<CommandResult> UpdateContactAsync(IClientMaintenanceDbContext db, ActorContext actor, Guid clientId,
    Guid contactId, ClientContactMaintenanceFields fields, ClientSafetyState safety, CancellationToken ct)
  {
    if (fields.Primary)
      await db.ClientContacts.Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId && x.Primary)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Primary, false), ct);
    safety.InputGeneration++;
    return await PracticeCrmService.UpdateClientContactAsync(db, actor, new UpdateClientContactRequest(contactId,
      fields.FullName, fields.Email, fields.Role, fields.Phone, fields.Title, fields.SignatoryAuthority,
      fields.ApprovedScope, fields.ValidFrom, fields.ValidTo, fields.Primary, fields.IsActive), ct);
  }

  public static async Task<CommandResult<ClientMaintenanceLookup>> LookupAsync(IClientMaintenanceDbContext db, ActorContext actor,
    Guid clientId, string operationKind, Guid? contactId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    var auth = await Authorize(db, actor, clientId, ct);
    if (!auth.Succeeded || requestId == Guid.Empty || !HashValid(requestHash) ||
        operationKind is not (ClientMaintenanceKinds.Profile or ClientMaintenanceKinds.Contact))
      return Fail<ClientMaintenanceLookup>(ErrorCodes.ScopeDenied, "The retained client maintenance request is unavailable.");
    var exists = await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == clientId, ct);
    if (!exists) return Fail<ClientMaintenanceLookup>(ErrorCodes.ScopeDenied, "The retained client maintenance request is unavailable.");
    var row = await db.ClientMaintenanceReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is not null && (row.ClientId != clientId || row.ContactId != contactId || row.OperationKind != operationKind ||
        row.ActorEpoch != actor.SessionEpoch || row.RequestHash != requestHash))
      return Fail<ClientMaintenanceLookup>(ErrorCodes.IdempotencyConflict, "This reference belongs to a different exact client maintenance intent.");
    auth = await Authorize(db, actor, clientId, ct);
    return auth.Succeeded
      ? CommandResult<ClientMaintenanceLookup>.Ok(new(row is not null, row is null ? null : Receipt(row)))
      : Fail<ClientMaintenanceLookup>(ErrorCodes.ScopeDenied, "The retained client maintenance request is unavailable.");
  }
}
