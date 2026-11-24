using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

/// <summary>One fail-closed check shared by CRM and quotation commands for the current decision on an exact opportunity.</summary>
public static class OpportunityAcceptanceGate
{
  public static string BasisSha256(Opportunity opportunity, PracticeClient client) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
      opportunity.FirmId,
      opportunity.Id,
      opportunity.LeadId,
      opportunity.DiscoveryIdentitySha256,
      ClientId = client.Id,
      client.LegalName,
      client.RegistrationNumber,
      client.Jurisdiction,
      opportunity.ServiceRoute,
      opportunity.EntityScope,
      opportunity.PeriodStart,
      opportunity.PeriodEnd,
      opportunity.EntityScopeConfirmedByUserId,
      opportunity.EntityScopeConfirmedAt
    })))).ToLowerInvariant();

  public static async Task<bool> IsCurrentUnconditionalAsync(IAuditSphereDbContext db,
    Guid firmId, Guid opportunityId, CancellationToken ct = default)
  {
    var opportunity = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Id == opportunityId && x.AcceptanceClientId != null &&
      x.DiscoveryIdentitySha256 != null && x.EntityScopeConfirmedByUserId != null &&
      x.EntityScopeConfirmedAt != null, ct);
    if (opportunity is null || opportunity.AcceptanceClientId is not { } clientId) return false;
    var discoveryIdentitySha256 = opportunity.DiscoveryIdentitySha256;
    if (discoveryIdentitySha256 is not { Length: 64 } ||
        discoveryIdentitySha256.Any(c => !Uri.IsHexDigit(c))) return false;
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Id == clientId, ct);
    if (client is null) return false;
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.Id == clientId).Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if (generation is null) return false;
    var decision = await db.AcceptanceDecisions.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.PracticeClientId == clientId && x.OpportunityId == opportunity.Id &&
      x.EngagementId == null && x.ServiceRoute == opportunity.ServiceRoute &&
      x.Generation == generation.Value && x.Decision != "Pending")
      .OrderByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    return decision is { Decision: "Accepted", Conditions: null, DecidedByUserId: not null, DecidedAt: not null } &&
      string.Equals(decision.OpportunityBasisSha256, BasisSha256(opportunity, client), StringComparison.Ordinal);
  }
}
