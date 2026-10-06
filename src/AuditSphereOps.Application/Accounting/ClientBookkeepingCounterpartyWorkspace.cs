using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientCounterpartyCreateRequest(string LegalName, string DisplayName, string Role, string Address,
  string Country, string TaxIdentifier, string ContactDetails, string PaymentTerms, string DefaultCurrency,
  string ExternalSystem, string ExternalReference);
public sealed record ClientCounterpartyView(Guid Id, Guid ClientId, string LegalName, string DisplayName, string Role,
  string Address, string Country, string TaxIdentifier, string ContactDetails, string PaymentTerms, string DefaultCurrency,
  string ExternalSystem, string ExternalReference, Guid CreatedByUserId, string CreatedAt, string Revision, Guid? EffectiveAmendmentId);
public sealed record ClientCounterpartyList(Guid ClientId, string? Role, int Page, int PageSize, int Total,
  bool BookkeepingActive, IReadOnlyList<ClientCounterpartyView> Counterparties);

public static partial class ClientBookkeepingCounterpartyWorkspace
{
  private static readonly string[] Roles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static string Trim(string? value) => (value ?? "").Trim();
  private static string Key(string value) => value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, RequiredRoles: Roles, InternalOnly: true), ct);
  private static async Task<bool> Active(IClientAccountingDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    await db.ClientAccountingProfiles.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct) &&
    await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct);

  public static async Task<CommandResult<Guid>> CreateAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    ClientCounterpartyCreateRequest request, CancellationToken ct = default)
  {
    var legal = Trim(request.LegalName); var display = Trim(request.DisplayName); var role = Trim(request.Role).ToUpperInvariant();
    var country = Trim(request.Country).ToUpperInvariant(); var currency = Trim(request.DefaultCurrency).ToUpperInvariant();
    var address = Trim(request.Address); var tax = Trim(request.TaxIdentifier); var contact = Trim(request.ContactDetails);
    var terms = Trim(request.PaymentTerms); var system = Trim(request.ExternalSystem); var reference = Trim(request.ExternalReference);
    var normalizedName = Key(legal);
    if (legal.Length is 0 or > 300 || display.Length is 0 or > 300 || normalizedName.Length > 300 ||
        role is not ("CUSTOMER" or "SUPPLIER" or "BOTH") || country.Length != 2 || country.Any(x => x is < 'A' or > 'Z') ||
        (currency.Length != 0 && (currency.Length != 3 || currency.Any(x => x is < 'A' or > 'Z'))) ||
        address.Length > 2000 || tax.Length > 200 || contact.Length > 1000 || terms.Length > 500 ||
        system.Length > 100 || reference.Length > 200 || (system.Length == 0) != (reference.Length == 0))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide bounded names, party role, country and optional details; external system and reference must be supplied together.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (client is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!await Active(db, actor, clientId, ct)) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A current accepted native bookkeeping service is required for new counterparties.");
    if (currency.Length != 0 && !await db.ClientAccountingProfiles.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.FunctionalCurrency == currency, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only the native functional currency is supported as a currency default.");
    var externalKey = system.Length == 0 ? null : Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { System = Key(system), Reference = Key(reference) })));
    if (externalKey is not null && await db.ClientBookkeepingCounterparties.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.NormalizedExternalIdentity == externalKey, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This client already has this external party identity; inspect the saved record.");
    if (await db.ClientBookkeepingCounterparties.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.NormalizedLegalName == normalizedName && x.Country == country, ct))
      return CommandResult<Guid>.Fail("counterparty.possible-duplicate", "A party with this normalized legal name and country already exists. Reviewed duplicate resolution is required and is not yet available.");
    var party = new ClientBookkeepingCounterparty { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      LegalName = legal, NormalizedLegalName = normalizedName, DisplayName = display, Role = role, Address = address, Country = country,
      TaxIdentifier = tax, ContactDetails = contact, PaymentTerms = terms, DefaultCurrency = currency, ExternalSystem = system,
      ExternalReference = reference, NormalizedExternalIdentity = externalKey, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientBookkeepingCounterparties.Add(party); await db.SaveChangesAsync(ct);
    if (!await Active(db, actor, clientId, ct)) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The bookkeeping service changed during creation.");
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct); return CommandResult<Guid>.Ok(party.Id);
  }

  public static async Task<CommandResult<ClientCounterpartyList>> ListAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, string? role = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
  {
    if (page is < 0 or > 10000 || pageSize is < 1 or > 100 || (role is not null && role is not ("CUSTOMER" or "SUPPLIER" or "BOTH")))
      return CommandResult<ClientCounterpartyList>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a supported party filter and page.");
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<ClientCounterpartyList>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    var query = db.ClientBookkeepingCounterparties.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId);
    if (role is not null) query = query.Where(x => x.Role == role || (role != "BOTH" && x.Role == "BOTH"));
    var total = await query.CountAsync(ct);
    var rows = await query.OrderBy(x => x.NormalizedLegalName).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
    var views = new List<ClientCounterpartyView>();
    foreach (var row in rows) views.Add(await EffectiveView(db, row, ct));
    await snapshot.CommitAsync(ct);
    var active = await Active(db, actor, clientId, ct);
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<ClientCounterpartyList>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<ClientCounterpartyList>.Ok(new(clientId, role, page, pageSize, total, active, views));
  }
}
