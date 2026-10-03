using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record ClientConversionFields(string LegalName, string? CommercialName = null,
  string? RegistrationNumber = null, string? Jurisdiction = null, string? RestrictedProfile = null);
public sealed record ClientConversionRequest(Guid RequestId, ClientConversionFields Fields,
  string? ReviewBasis = null, string? RequestHash = null, bool Reviewed = false);
public sealed record ClientConversionState(Guid ProposalId, string ProposalRevision, string ProposalStatus,
  bool CanConvert, Guid? ConvertedClientId, string SuggestedLegalName, string ReviewBasis);
public sealed record ClientConversionPreview(Guid ProposalId, Guid RequestId, string RequestHash, string ReviewBasis,
  ClientConversionFields Fields, string ProposalRevision, string ServiceRoute, Guid? ExistingClientId,
  string? ExistingClientName, string? ExistingClientStatus, string ContactName, string ContactEmail,
  string PortalEffect, string ProfessionalEffect);
public sealed record ClientConversionReceipt(Guid Id, Guid ProposalId, Guid ClientId, Guid ActorId, Guid RequestId,
  string RequestHash, string ReviewBasis, ClientConversionPreview Preview, DateTimeOffset CreatedAt);
public sealed record ClientConversionLookup(bool Found, ClientConversionReceipt? Receipt);

/// <summary>Reviewed accepted-proposal conversion; application authority, idempotency and receipt recovery remain server-owned.</summary>
public static class ClientConversionWorkspace
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "RelationshipManager"];
  private static Task<CommandResult> Authorize(IAuditSphereDbContext db, ActorContext a, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db,a,new(a.FirmId, RequiredRoles:Roles, InternalOnly:true, RequireFirmWide:true),ct);
  private static CommandResult<T> Unavailable<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied,"Client conversion is unavailable.");
  private static bool HashValid(string? s) => s is { Length:64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool TextValid(string? s,int max) => s is null || s.Length<=max && !s.Any(char.IsControl);
  private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
  private static ClientConversionFields? Canonical(ClientConversionFields? f) =>
    f is not null && !string.IsNullOrWhiteSpace(f.LegalName) && TextValid(f.LegalName,200) &&
    TextValid(f.CommercialName,200) && TextValid(f.RegistrationNumber,100) && TextValid(f.Jurisdiction,100) && TextValid(f.RestrictedProfile,2000)
    ? new(f.LegalName.Trim(),Trim(f.CommercialName),Trim(f.RegistrationNumber),Trim(f.Jurisdiction),Trim(f.RestrictedProfile)) : null;
  private static string RequestHash(ActorContext a,Guid id,Guid requestId,ClientConversionFields f) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new {a.FirmId,a.UserId,a.SessionEpoch,id,requestId,Action="CLIENT_CONVERSION",Fields=f}));
  private static ClientConversionReceipt Receipt(ClientConversion row) => new(row.Id,row.ProposalId,row.ClientId,
    row.ActorId,row.RequestId,row.RequestHash,row.ReviewBasis,JsonSerializer.Deserialize<ClientConversionPreview>(row.PreviewJson)!,row.CreatedAt);

  public static async Task<CommandResult<ClientConversionState>> StateAsync(IAuditSphereDbContext db,ActorContext a,
    Guid id,CancellationToken ct=default)
  {
    if(!(await Authorize(db,a,ct)).Succeeded)return Unavailable<ClientConversionState>();
    var p=await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct);
    if(p is null)return Unavailable<ClientConversionState>();
    var o=await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==p.OpportunityId,ct);
    if(o is null)return Unavailable<ClientConversionState>();
    var lead=await db.Leads.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==o.LeadId,ct);
    if(lead is null)return Unavailable<ClientConversionState>();
    var basis=Hashing.Sha256Hex(JsonSerializer.Serialize(new {a.FirmId,a.UserId,a.SessionEpoch,id,Proposal=p,Opportunity=o,Lead=lead}));
    if(!(await Authorize(db,a,ct)).Succeeded)return Unavailable<ClientConversionState>();
    return CommandResult<ClientConversionState>.Ok(new(id,p.Revision.ToString(CultureInfo.InvariantCulture),p.Status,
      p.Status==CrmStates.ProposalAccepted && o.Stage==CrmStates.OpportunityWon && p.PracticeClientId is null,
      p.PracticeClientId,lead.Name,basis));
  }

  public static async Task<CommandResult<ClientConversionPreview>> PreviewAsync(IAuditSphereDbContext db,ActorContext a,
    Guid id,ClientConversionRequest? r,CancellationToken ct=default)
  {
    if (!(await Authorize(db,a,ct)).Succeeded) return Unavailable<ClientConversionPreview>();
    var fields=Canonical(r?.Fields);
    if (r is null || r.RequestId==Guid.Empty || fields is null)
      return CommandResult<ClientConversionPreview>.Fail("request.invalid","Enter bounded legal identity fields before review.");
    var p=await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct);
    if (p is null) return Unavailable<ClientConversionPreview>();
    if (p.PracticeClientId is not null || p.Status!=CrmStates.ProposalAccepted)
      return CommandResult<ClientConversionPreview>.Fail(ErrorCodes.GateBlocked,"Review an accepted proposal that is not already converted.");
    var o=await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==p.OpportunityId,ct);
    if(o is null || o.Stage!=CrmStates.OpportunityWon) return Unavailable<ClientConversionPreview>();
    var lead=await db.Leads.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==o.LeadId,ct);
    if(lead is null || !System.Net.Mail.MailAddress.TryCreate(lead.PrimaryContactEmail,out _))
      return CommandResult<ClientConversionPreview>.Fail(ErrorCodes.GateBlocked,"Review a valid primary contact email before conversion.");
    var name=fields.LegalName.ToUpperInvariant();var registration=fields.RegistrationNumber?.ToUpperInvariant();var jurisdiction=fields.Jurisdiction?.ToUpperInvariant();
    var matches=await db.PracticeClients.AsNoTracking().Where(x=>x.FirmId==a.FirmId &&
      (x.LegalName.ToUpper()==name || registration!=null && jurisdiction!=null && x.RegistrationNumber!=null && x.Jurisdiction!=null &&
        x.RegistrationNumber.ToUpper()==registration && x.Jurisdiction.ToUpper()==jurisdiction)).Take(2).ToListAsync(ct);
    if(matches.Count>1) return CommandResult<ClientConversionPreview>.Fail("crm.duplicate","Conflicting canonical clients require reviewed resolution.");
    var existing=matches.SingleOrDefault();
    if(existing is not null && (!string.Equals(existing.LegalName.Trim(),fields.LegalName,StringComparison.OrdinalIgnoreCase) ||
      fields.RegistrationNumber is not null && existing.RegistrationNumber is not null && !string.Equals(existing.RegistrationNumber,fields.RegistrationNumber,StringComparison.OrdinalIgnoreCase) ||
      fields.Jurisdiction is not null && existing.Jurisdiction is not null && !string.Equals(existing.Jurisdiction,fields.Jurisdiction,StringComparison.OrdinalIgnoreCase)))
      return CommandResult<ClientConversionPreview>.Fail("crm.duplicate","The supplied legal identity conflicts with the canonical client.");
    long? generation=null;
    if(existing is not null) {
      generation=await db.ClientSafetyStates.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.Id==existing.Id).Select(x=>(long?)x.InputGeneration).SingleOrDefaultAsync(ct);
      if(generation is null) return Unavailable<ClientConversionPreview>();
    }
    var basis=Hashing.Sha256Hex(JsonSerializer.Serialize(new {a.FirmId,a.UserId,a.SessionEpoch,id,Fields=fields,Proposal=p,Opportunity=o,Lead=lead,Existing=existing,Generation=generation}));
    if(!(await Authorize(db,a,ct)).Succeeded) return Unavailable<ClientConversionPreview>();
    return CommandResult<ClientConversionPreview>.Ok(new(id,r.RequestId,RequestHash(a,id,r.RequestId,fields),basis,fields,
      p.Revision.ToString(CultureInfo.InvariantCulture),o.ServiceRoute,existing?.Id,existing?.LegalName,existing?.Status,
      lead.PrimaryContactName??lead.PrimaryContactEmail!,lead.PrimaryContactEmail!,
      "Records primary contact and pending portal intent only. No invitation or portal access is granted.",
      "Creates a prospect or reuses the reviewed canonical client. Professional acceptance remains a separate Partner decision; no engagement is activated."));
  }

  public static async Task<CommandResult<ClientConversionReceipt>> ExecuteAsync(IAuditSphereDbContext db,ActorContext a,
    Guid id,ClientConversionRequest? r,CancellationToken ct=default)
  {
    if(!(await Authorize(db,a,ct)).Succeeded) return Unavailable<ClientConversionReceipt>();
    var fields=Canonical(r?.Fields);
    if(r is null || fields is null || !r.Reviewed || r.RequestId==Guid.Empty || !HashValid(r.RequestHash) || !HashValid(r.ReviewBasis) ||
      r.RequestHash!=RequestHash(a,id,r.RequestId,fields)) return CommandResult<ClientConversionReceipt>.Fail("request.invalid","Preview and confirm the exact prospect conversion.");
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return Unavailable<ClientConversionReceipt>();
    if(!(await Authorize(db,a,ct)).Succeeded) return Unavailable<ClientConversionReceipt>();
    var prior=await db.ClientConversions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.ActorId==a.UserId && x.RequestId==r.RequestId,ct);
    if(prior is not null) {
      if(prior.ProposalId!=id || prior.RequestHash!=r.RequestHash) return CommandResult<ClientConversionReceipt>.Fail(ErrorCodes.IdempotencyConflict,"Changed conversion cannot reuse the request.");
      if(await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={prior.ClientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null)
        return Unavailable<ClientConversionReceipt>();
      await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
      if(!(await Authorize(db,a,ct)).Succeeded)return Unavailable<ClientConversionReceipt>();
      await tx.CommitAsync(ct);return CommandResult<ClientConversionReceipt>.Ok(Receipt(prior));
    }
    var preview=await PreviewAsync(db,a,id,r,ct);
    if(!preview.Succeeded)return CommandResult<ClientConversionReceipt>.Fail(preview.ErrorCode!,preview.Message!);
    if(preview.Value!.ExistingClientId is Guid clientId) {
      if(await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={clientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null)
        return Unavailable<ClientConversionReceipt>();
      preview=await PreviewAsync(db,a,id,r,ct);
      if(!preview.Succeeded)return CommandResult<ClientConversionReceipt>.Fail(preview.ErrorCode!,preview.Message!);
    }
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    if(!(await Authorize(db,a,ct)).Succeeded)return Unavailable<ClientConversionReceipt>();
    if(preview.Value!.ReviewBasis!=r.ReviewBasis)return CommandResult<ClientConversionReceipt>.Fail(ErrorCodes.StaleRevision,"Conversion context changed. Refresh and review again.");
    var created=await PracticeCrmService.ConvertToClientDraftAsync(db,a,new(id,fields.LegalName,fields.CommercialName,
      fields.RegistrationNumber,fields.Jurisdiction,fields.RestrictedProfile),ct);
    if(!created.Succeeded)return CommandResult<ClientConversionReceipt>.Fail(created.ErrorCode!,created.Message!);
    var row=new ClientConversion {Id=Guid.CreateVersion7(),FirmId=a.FirmId,ProposalId=id,ClientId=created.Value,ActorId=a.UserId,
      ActorEpoch=a.SessionEpoch,RequestId=r.RequestId,RequestHash=r.RequestHash!,ReviewBasis=r.ReviewBasis!,PreviewJson=JsonSerializer.Serialize(preview.Value),CreatedAt=DateTimeOffset.UtcNow};
    db.ClientConversions.Add(row);await db.SaveChangesAsync(ct);
    if(!(await Authorize(db,a,ct)).Succeeded)return Unavailable<ClientConversionReceipt>();
    await tx.CommitAsync(ct);return CommandResult<ClientConversionReceipt>.Ok(Receipt(row));
  }

  public static async Task<CommandResult<ClientConversionLookup>> LookupAsync(IAuditSphereDbContext db,ActorContext a,
    Guid id,Guid requestId,string? hash,CancellationToken ct=default)
  {
    if(!(await Authorize(db,a,ct)).Succeeded || requestId==Guid.Empty || !HashValid(hash))return Unavailable<ClientConversionLookup>();
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return Unavailable<ClientConversionLookup>();
    if(!await db.Proposals.AsNoTracking().AnyAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct))return Unavailable<ClientConversionLookup>();
    var row=await db.ClientConversions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.ActorId==a.UserId && x.RequestId==requestId,ct);
    if(row is not null && (row.ProposalId!=id || row.RequestHash!=hash))return CommandResult<ClientConversionLookup>.Fail(ErrorCodes.IdempotencyConflict,"This reference belongs to another conversion.");
    if(!(await Authorize(db,a,ct)).Succeeded)return Unavailable<ClientConversionLookup>();
    await tx.CommitAsync(ct);return CommandResult<ClientConversionLookup>.Ok(new(row is not null,row is null?null:Receipt(row)));
  }
}
