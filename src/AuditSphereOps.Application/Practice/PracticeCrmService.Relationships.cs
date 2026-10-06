using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record UpdateClientProfileRequest(
  Guid ClientId,
  string LegalName,
  string? CommercialName = null,
  string? RegistrationNumber = null,
  string? TaxRegistrationNumber = null,
  string? EntityType = null,
  string? Jurisdiction = null,
  string? RestrictedProfile = null,
  long? ExpectedGeneration = null);

public sealed record UpdateClientContactRequest(
  Guid ContactId,
  string FullName,
  string Email,
  string Role,
  string? Phone = null,
  string? Title = null,
  string? SignatoryAuthority = null,
  string? ApprovedScope = null,
  DateTimeOffset? ValidFrom = null,
  DateTimeOffset? ValidTo = null,
  bool Primary = false,
  bool IsActive = true);

public sealed record CreateClientRelationshipRequest(
  Guid PrimaryClientId,
  Guid RelatedClientId,
  string RelationshipKind,
  decimal? OwnershipPercentage = null,
  DateOnly? EffectiveFrom = null,
  DateOnly? EffectiveTo = null,
  string? Notes = null);

public sealed record RevokeClientRelationshipRequest(
  Guid RelationshipId,
  string Reason);

public sealed record ClientRelationshipDto(
  Guid Id,
  Guid PrimaryClientId,
  string PrimaryClientName,
  Guid RelatedClientId,
  string RelatedClientName,
  string RelationshipKind,
  decimal? OwnershipPercentage,
  DateOnly? EffectiveFrom,
  DateOnly? EffectiveTo,
  string? Notes,
  DateTimeOffset CreatedAt,
  bool IsActive);

public sealed record ClientOrganizationHierarchyDto(
  Guid ClientId,
  string LegalName,
  string? TaxRegistrationNumber,
  string? EntityType,
  IReadOnlyList<ClientRelationshipDto> Parents,
  IReadOnlyList<ClientRelationshipDto> Subsidiaries,
  IReadOnlyList<ClientRelationshipDto> Affiliates);

public static partial class PracticeCrmService
{
  public static async Task<CommandResult> UpdateClientProfileAsync(
    IAuditSphereDbContext db, ActorContext actor, UpdateClientProfileRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.LegalName))
      return CommandResult.Fail("crm.invalid", "Legal name is required.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: request.ClientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var client = await db.PracticeClients.SingleOrDefaultAsync(x => x.Id == request.ClientId && x.FirmId == actor.FirmId, ct);
    if (client is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var safety = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {client.Id} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (safety is null) return CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");

    if (request.ExpectedGeneration.HasValue && safety.InputGeneration != request.ExpectedGeneration.Value)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "Client record changed; reload current details.");

    client.LegalName = request.LegalName.Trim();
    client.CommercialName = TrimOrNull(request.CommercialName);
    client.RegistrationNumber = TrimOrNull(request.RegistrationNumber);
    client.TaxRegistrationNumber = TrimOrNull(request.TaxRegistrationNumber);
    client.EntityType = TrimOrNull(request.EntityType);
    client.Jurisdiction = TrimOrNull(request.Jurisdiction);
    client.RestrictedProfile = TrimOrNull(request.RestrictedProfile);
    safety.InputGeneration++;

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> UpdateClientContactAsync(
    IAuditSphereDbContext db, ActorContext actor, UpdateClientContactRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Role))
      return CommandResult.Fail("crm.invalid", "Full name, email and role are required.");
    if (!System.Net.Mail.MailAddress.TryCreate(request.Email.Trim(), out _))
      return CommandResult.Fail("crm.invalid", "Invalid email address format.");
    if (request.ValidFrom.HasValue && request.ValidTo.HasValue && request.ValidFrom.Value > request.ValidTo.Value)
      return CommandResult.Fail("crm.invalid", "Valid from must precede or equal valid to.");

    var contact = await db.ClientContacts.SingleOrDefaultAsync(x => x.Id == request.ContactId && x.FirmId == actor.FirmId, ct);
    if (contact is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Contact not found.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: contact.PracticeClientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;

    contact.FullName = request.FullName.Trim();
    contact.Email = request.Email.Trim().ToLowerInvariant();
    contact.Role = request.Role.Trim();
    contact.Phone = TrimOrNull(request.Phone);
    contact.Title = TrimOrNull(request.Title);
    contact.SignatoryAuthority = TrimOrNull(request.SignatoryAuthority);
    contact.ApprovedScope = TrimOrNull(request.ApprovedScope);
    contact.ValidFrom = request.ValidFrom;
    contact.ValidTo = request.ValidTo;
    contact.Primary = request.Primary;
    contact.IsActive = request.IsActive;

    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreateClientRelationshipAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateClientRelationshipRequest request, CancellationToken ct = default)
  {
    if (request.PrimaryClientId == request.RelatedClientId)
      return CommandResult<Guid>.Fail("crm.invalid", "A client cannot form an organization relationship with itself.");

    var kind = request.RelationshipKind.Trim().ToUpperInvariant();
    if (!ClientRelationshipKinds.All.Contains(kind))
      return CommandResult<Guid>.Fail("crm.invalid", $"Relationship kind must be one of {string.Join(", ", ClientRelationshipKinds.All)}.");

    if (request.OwnershipPercentage is < 0 or > 100)
      return CommandResult<Guid>.Fail("crm.invalid", "Ownership percentage must be between 0 and 100.");

    if (request.EffectiveFrom.HasValue && request.EffectiveTo.HasValue && request.EffectiveFrom.Value > request.EffectiveTo.Value)
      return CommandResult<Guid>.Fail("crm.invalid", "Effective from must precede or equal effective to.");

    var authPrimary = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: request.PrimaryClientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!authPrimary.Succeeded) return CommandResult<Guid>.Fail(authPrimary.ErrorCode!, authPrimary.Message!);

    var authRelated = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: request.RelatedClientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!authRelated.Succeeded) return CommandResult<Guid>.Fail(authRelated.ErrorCode!, authRelated.Message!);

    var primaryClient = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PrimaryClientId && x.FirmId == actor.FirmId, ct);
    var relatedClient = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RelatedClientId && x.FirmId == actor.FirmId, ct);
    if (primaryClient is null || relatedClient is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Both entities must exist in the authorized firm scope.");

    // Check duplicate active relationship of same kind
    var duplicate = await db.ClientRelationships.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId &&
      x.PrimaryClientId == request.PrimaryClientId &&
      x.RelatedClientId == request.RelatedClientId &&
      x.RelationshipKind == kind &&
      x.RevokedAt == null, ct);
    if (duplicate)
      return CommandResult<Guid>.Fail("crm.duplicate", "An active relationship of this kind already exists between these entities.");

    // Ownership-tree cycle check for Parent/Subsidiary relationships
    if (kind is ClientRelationshipKinds.Parent or ClientRelationshipKinds.Subsidiary)
    {
      Guid parentId = kind == ClientRelationshipKinds.Parent ? request.PrimaryClientId : request.RelatedClientId;
      Guid childId = kind == ClientRelationshipKinds.Parent ? request.RelatedClientId : request.PrimaryClientId;

      var activeEdges = await db.ClientRelationships.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.RevokedAt == null &&
          (x.RelationshipKind == ClientRelationshipKinds.Parent || x.RelationshipKind == ClientRelationshipKinds.Subsidiary))
        .Select(x => new
        {
          Parent = x.RelationshipKind == ClientRelationshipKinds.Parent ? x.PrimaryClientId : x.RelatedClientId,
          Child = x.RelationshipKind == ClientRelationshipKinds.Parent ? x.RelatedClientId : x.PrimaryClientId
        })
        .ToListAsync(ct);

      // Check if parentId is reachable from childId (which would mean adding parent -> child creates a cycle)
      if (IsReachable(activeEdges.Select(e => (e.Parent, e.Child)), start: childId, target: parentId))
      {
        return CommandResult<Guid>.Fail("crm.relationship-cycle", "Adding this relationship creates an ownership-tree cycle.");
      }
    }

    var relationship = new ClientRelationship
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      PrimaryClientId = request.PrimaryClientId,
      RelatedClientId = request.RelatedClientId,
      RelationshipKind = kind,
      OwnershipPercentage = request.OwnershipPercentage,
      EffectiveFrom = request.EffectiveFrom,
      EffectiveTo = request.EffectiveTo,
      Notes = TrimOrNull(request.Notes),
      CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };

    db.ClientRelationships.Add(relationship);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(relationship.Id);
  }

  public static async Task<CommandResult> RevokeClientRelationshipAsync(
    IAuditSphereDbContext db, ActorContext actor, RevokeClientRelationshipRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Reason))
      return CommandResult.Fail("crm.invalid", "A revocation reason is required.");

    var rel = await db.ClientRelationships.SingleOrDefaultAsync(x => x.Id == request.RelationshipId && x.FirmId == actor.FirmId, ct);
    if (rel is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Relationship not found.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: rel.PrimaryClientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;

    if (rel.RevokedAt.HasValue) return CommandResult.Ok();

    rel.RevokedAt = DateTimeOffset.UtcNow;
    rel.RevokedByUserId = actor.UserId;
    rel.RevocationReason = request.Reason.Trim();

    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<ClientOrganizationHierarchyDto>> GetClientHierarchyAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<ClientOrganizationHierarchyDto>.Fail(auth.ErrorCode!, auth.Message!);

    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clientId && x.FirmId == actor.FirmId, ct);
    if (client is null) return CommandResult<ClientOrganizationHierarchyDto>.Fail(ErrorCodes.ScopeDenied, "Client not found.");

    var allRels = await db.ClientRelationships.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && (x.PrimaryClientId == clientId || x.RelatedClientId == clientId) && x.RevokedAt == null)
      .ToListAsync(ct);

    // Counterpart nodes are separate client records with their own scope: the hierarchy may only
    // reveal relationship metadata for counterpart clients the actor can currently access. An
    // inaccessible counterpart is omitted entirely (no name, identifier or existence disclosure),
    // the consistent safe policy for restricted group nodes (STE-REM-03).
    var counterpartIds = allRels
      .Select(r => r.PrimaryClientId == clientId ? r.RelatedClientId : r.PrimaryClientId)
      .Distinct()
      .ToList();
    var authorizedCounterparts = new HashSet<Guid>();
    foreach (var counterpartId in counterpartIds)
    {
      var counterpartAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, ClientId: counterpartId, RequiredRoles: CommercialRoles, InternalOnly: true), ct);
      if (counterpartAuth.Succeeded) authorizedCounterparts.Add(counterpartId);
    }

    var clientIds = allRels.Select(r => r.PrimaryClientId).Concat(allRels.Select(r => r.RelatedClientId)).Distinct().ToList();
    var names = await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);

    var parents = new List<ClientRelationshipDto>();
    var subsidiaries = new List<ClientRelationshipDto>();
    var affiliates = new List<ClientRelationshipDto>();

    foreach (var r in allRels)
    {
      var counterpartId = r.PrimaryClientId == clientId ? r.RelatedClientId : r.PrimaryClientId;
      if (!authorizedCounterparts.Contains(counterpartId)) continue;

      var dto = new ClientRelationshipDto(
        r.Id,
        r.PrimaryClientId,
        names.GetValueOrDefault(r.PrimaryClientId, "Unknown"),
        r.RelatedClientId,
        names.GetValueOrDefault(r.RelatedClientId, "Unknown"),
        r.RelationshipKind,
        r.OwnershipPercentage,
        r.EffectiveFrom,
        r.EffectiveTo,
        r.Notes,
        r.CreatedAt,
        IsActive: r.RevokedAt == null);

      if (r.RelationshipKind == ClientRelationshipKinds.Affiliate)
      {
        affiliates.Add(dto);
      }
      else if (r.PrimaryClientId == clientId)
      {
        if (r.RelationshipKind == ClientRelationshipKinds.Parent) subsidiaries.Add(dto); // Client is parent of related => related is subsidiary
        else parents.Add(dto); // Client is subsidiary of related => related is parent
      }
      else // r.RelatedClientId == clientId
      {
        if (r.RelationshipKind == ClientRelationshipKinds.Parent) parents.Add(dto); // Related is parent of client
        else subsidiaries.Add(dto); // Related is subsidiary of client
      }
    }

    return CommandResult<ClientOrganizationHierarchyDto>.Ok(new(
      client.Id,
      client.LegalName,
      client.TaxRegistrationNumber,
      client.EntityType,
      parents,
      subsidiaries,
      affiliates));
  }

  private static bool IsReachable(IEnumerable<(Guid From, Guid To)> edges, Guid start, Guid target)
  {
    var adj = new Dictionary<Guid, List<Guid>>();
    foreach (var (from, to) in edges)
    {
      if (!adj.TryGetValue(from, out var list))
      {
        list = [];
        adj[from] = list;
      }
      list.Add(to);
    }

    var visited = new HashSet<Guid>();
    var queue = new Queue<Guid>();
    queue.Enqueue(start);
    visited.Add(start);

    while (queue.Count > 0)
    {
      var current = queue.Dequeue();
      if (current == target) return true;

      if (adj.TryGetValue(current, out var neighbors))
      {
        foreach (var next in neighbors)
        {
          if (visited.Add(next))
          {
            queue.Enqueue(next);
          }
        }
      }
    }

    return false;
  }
}
