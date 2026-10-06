using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record AssignContactRoutingRequest(
  Guid PracticeClientId,
  Guid ClientContactId,
  string Purpose,
  DateOnly? EffectiveFrom = null,
  DateOnly? EffectiveTo = null,
  bool IsPrimaryForPurpose = true);

public sealed record RevokeContactRoutingRequest(
  Guid RoutingId,
  string Reason);

public enum RecipientResolutionStatus
{
  Resolved,
  NoRecipient,
  Ambiguous,
  Overridden
}

public sealed record RecipientResolutionResult(
  RecipientResolutionStatus Status,
  Guid? ClientContactId,
  string? FullName,
  string? Email,
  string? Role,
  string? Title,
  string? Purpose,
  string? Message,
  bool WasOverridden = false,
  Guid? OverriddenByUserId = null,
  string? OverrideReason = null)
{
  public static RecipientResolutionResult Resolved(ClientContact contact, string purpose) =>
    new(RecipientResolutionStatus.Resolved, contact.Id, contact.FullName, contact.Email, contact.Role, contact.Title, purpose, "Recipient resolved successfully.");

  public static RecipientResolutionResult Overridden(ClientContact contact, string purpose, Guid actorUserId, string reason) =>
    new(RecipientResolutionStatus.Overridden, contact.Id, contact.FullName, contact.Email, contact.Role, contact.Title, purpose, "Recipient resolved via authorized override.",
      WasOverridden: true, OverriddenByUserId: actorUserId, OverrideReason: reason);

  public static RecipientResolutionResult NoRecipient(string purpose) =>
    new(RecipientResolutionStatus.NoRecipient, null, null, null, null, null, purpose, $"No active recipient is designated for {purpose}. Actionable contact assignment required.");

  public static RecipientResolutionResult Ambiguous(string purpose, int count) =>
    new(RecipientResolutionStatus.Ambiguous, null, null, null, null, null, purpose, $"Multiple ({count}) active contacts configured for {purpose} without a designated primary recipient. Explicit selection required.");
}

public sealed record RecordCorrespondenceDispatchRequest(
  Guid PracticeClientId,
  Guid? EngagementId,
  string Purpose,
  Guid RecipientContactId,
  string DocumentType,
  string DocumentReference,
  long DocumentRevision,
  string DocumentSha256,
  bool WasOverridden = false,
  string? OverrideReason = null);

public sealed record ClientContactRoutingDto(
  Guid Id,
  Guid ClientContactId,
  string ContactName,
  string ContactEmail,
  string? ContactPhone,
  string? ContactRole,
  string? ContactTitle,
  string? SignatoryAuthority,
  string Purpose,
  DateOnly? EffectiveFrom,
  DateOnly? EffectiveTo,
  bool IsPrimaryForPurpose,
  DateTimeOffset CreatedAt,
  bool IsActive);

public static partial class PracticeCrmService
{
  public static async Task<CommandResult<IReadOnlyList<ClientContactRoutingDto>>> GetClientContactRoutingsAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<ClientContactRoutingDto>>.Fail(auth.ErrorCode!, auth.Message!);

    var query = from r in db.ClientContactRoutings.AsNoTracking()
                join c in db.ClientContacts.AsNoTracking() on r.ClientContactId equals c.Id
                where r.FirmId == actor.FirmId && r.PracticeClientId == clientId && r.RevokedAt == null
                orderby r.Purpose, r.IsPrimaryForPurpose descending, r.CreatedAt descending
                select new ClientContactRoutingDto(
                  r.Id,
                  c.Id,
                  c.FullName,
                  c.Email,
                  c.Phone,
                  c.Role,
                  c.Title,
                  c.SignatoryAuthority,
                  r.Purpose,
                  r.EffectiveFrom,
                  r.EffectiveTo,
                  r.IsPrimaryForPurpose,
                  r.CreatedAt,
                  c.IsActive);

    var list = await query.ToListAsync(ct);
    return CommandResult<IReadOnlyList<ClientContactRoutingDto>>.Ok(list);
  }

  public static async Task<CommandResult<Guid>> AssignContactRoutingAsync(
    IAuditSphereDbContext db, ActorContext actor, AssignContactRoutingRequest request, CancellationToken ct = default)
  {
    var purpose = request.Purpose.Trim().ToUpperInvariant();
    if (!CorrespondencePurposes.All.Contains(purpose))
      return CommandResult<Guid>.Fail("crm.invalid", $"Purpose must be one of {string.Join(", ", CorrespondencePurposes.All)}.");

    if (request.EffectiveFrom.HasValue && request.EffectiveTo.HasValue && request.EffectiveFrom.Value > request.EffectiveTo.Value)
      return CommandResult<Guid>.Fail("crm.invalid", "Effective from must precede or equal effective to.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: request.PracticeClientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var contact = await db.ClientContacts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ClientContactId && x.PracticeClientId == request.PracticeClientId && x.FirmId == actor.FirmId, ct);
    if (contact is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Contact not found for this client.");

    if (!contact.IsActive)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Deactivated contacts cannot receive correspondence routing assignments.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);

    // If setting as primary for purpose, unset any existing primary active routings for this client and purpose
    if (request.IsPrimaryForPurpose)
    {
      var existingPrimaries = await db.ClientContactRoutings
        .Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == request.PracticeClientId &&
          x.Purpose == purpose && x.IsPrimaryForPurpose && x.RevokedAt == null)
        .ToListAsync(ct);

      foreach (var ep in existingPrimaries)
      {
        ep.IsPrimaryForPurpose = false;
      }
    }

    var routing = new ClientContactRouting
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      PracticeClientId = request.PracticeClientId,
      ClientContactId = request.ClientContactId,
      Purpose = purpose,
      EffectiveFrom = request.EffectiveFrom,
      EffectiveTo = request.EffectiveTo,
      IsPrimaryForPurpose = request.IsPrimaryForPurpose,
      CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };

    db.ClientContactRoutings.Add(routing);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return CommandResult<Guid>.Ok(routing.Id);
  }

  public static async Task<CommandResult> RevokeContactRoutingAsync(
    IAuditSphereDbContext db, ActorContext actor, RevokeContactRoutingRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Reason))
      return CommandResult.Fail("crm.invalid", "A revocation reason is required.");

    var routing = await db.ClientContactRoutings.SingleOrDefaultAsync(x => x.Id == request.RoutingId && x.FirmId == actor.FirmId, ct);
    if (routing is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Routing assignment not found.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: routing.PracticeClientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;

    if (routing.RevokedAt.HasValue) return CommandResult.Ok();

    routing.RevokedAt = DateTimeOffset.UtcNow;
    routing.RevokedByUserId = actor.UserId;
    routing.RevocationReason = request.Reason.Trim();

    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<RecipientResolutionResult> ResolveCorrespondenceRecipientAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, string purpose, DateOnly asOfDate,
    Guid? overrideContactId = null, string? overrideReason = null, CancellationToken ct = default)
  {
    var normalizedPurpose = purpose.Trim().ToUpperInvariant();

    if (overrideContactId.HasValue)
    {
      if (string.IsNullOrWhiteSpace(overrideReason))
      {
        return new RecipientResolutionResult(RecipientResolutionStatus.Ambiguous, null, null, null, null, null,
          normalizedPurpose, "A documented reason is mandatory when performing an authorized recipient override.");
      }

      var overrideContact = await db.ClientContacts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == overrideContactId.Value && x.PracticeClientId == clientId && x.FirmId == actor.FirmId, ct);

      if (overrideContact is null)
      {
        return new RecipientResolutionResult(RecipientResolutionStatus.NoRecipient, null, null, null, null, null,
          normalizedPurpose, "The override recipient contact does not belong to the authorized client.");
      }

      if (!overrideContact.IsActive)
      {
        return new RecipientResolutionResult(RecipientResolutionStatus.NoRecipient, null, null, null, null, null,
          normalizedPurpose, "The designated override recipient is deactivated.");
      }

      return RecipientResolutionResult.Overridden(overrideContact, normalizedPurpose, actor.UserId, overrideReason.Trim());
    }

    var activeRoutings = await (
      from r in db.ClientContactRoutings.AsNoTracking()
      where r.FirmId == actor.FirmId && r.PracticeClientId == clientId && r.Purpose == normalizedPurpose && r.RevokedAt == null
      where (r.EffectiveFrom == null || r.EffectiveFrom <= asOfDate) &&
            (r.EffectiveTo == null || r.EffectiveTo >= asOfDate)
      join c in db.ClientContacts.AsNoTracking() on r.ClientContactId equals c.Id
      where c.IsActive
      select new { Routing = r, Contact = c }
    ).ToListAsync(ct);

    if (activeRoutings.Count == 0)
    {
      return RecipientResolutionResult.NoRecipient(normalizedPurpose);
    }

    if (activeRoutings.Count == 1)
    {
      return RecipientResolutionResult.Resolved(activeRoutings[0].Contact, normalizedPurpose);
    }

    var primaryMatch = activeRoutings.Where(x => x.Routing.IsPrimaryForPurpose).ToList();
    if (primaryMatch.Count == 1)
    {
      return RecipientResolutionResult.Resolved(primaryMatch[0].Contact, normalizedPurpose);
    }

    return RecipientResolutionResult.Ambiguous(normalizedPurpose, activeRoutings.Count);
  }

  public static async Task<CommandResult<Guid>> RecordCorrespondenceDispatchAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordCorrespondenceDispatchRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.DocumentType) || string.IsNullOrWhiteSpace(request.DocumentReference))
      return CommandResult<Guid>.Fail("crm.invalid", "Document type and reference are required.");

    if (string.IsNullOrWhiteSpace(request.DocumentSha256) || request.DocumentSha256.Trim().Length != 64)
      return CommandResult<Guid>.Fail("crm.invalid", "A valid 64-character SHA-256 document digest is required.");

    if (request.DocumentRevision < 1)
      return CommandResult<Guid>.Fail("crm.invalid", "Document revision must be at least 1.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: request.PracticeClientId, EngagementId: request.EngagementId,
        RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var contact = await db.ClientContacts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.RecipientContactId && x.PracticeClientId == request.PracticeClientId && x.FirmId == actor.FirmId, ct);
    if (contact is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Recipient contact not found for this client.");

    if (!contact.IsActive)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Cannot dispatch correspondence to a deactivated contact.");

    var purpose = request.Purpose.Trim().ToUpperInvariant();

    var record = new CorrespondenceDispatchRecord
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      PracticeClientId = request.PracticeClientId,
      EngagementId = request.EngagementId,
      Purpose = purpose,
      RecipientContactId = contact.Id,
      RecipientName = contact.FullName,
      RecipientEmail = contact.Email,
      DocumentType = request.DocumentType.Trim(),
      DocumentReference = request.DocumentReference.Trim(),
      DocumentRevision = request.DocumentRevision,
      DocumentSha256 = request.DocumentSha256.Trim().ToLowerInvariant(),
      WasOverridden = request.WasOverridden,
      OverriddenByUserId = request.WasOverridden ? actor.UserId : null,
      OverrideReason = TrimOrNull(request.OverrideReason),
      DispatchedByUserId = actor.UserId,
      DispatchedAt = DateTimeOffset.UtcNow
    };

    db.CorrespondenceDispatchRecords.Add(record);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(record.Id);
  }
}
