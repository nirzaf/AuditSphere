using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.E2E.Tests;

internal sealed record SupportedNativeOpeningScope(Guid FirmId, Guid ClientId, Guid EngagementId,
  AppUser Admin, AppUser Reviewer, ActorContext MakerActor, ActorContext ReviewerActor)
{
  internal static async Task<SupportedNativeOpeningScope> CreateAsync(ITestPostgresDatabase database)
  {
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(database, includeSibling: false);
    var scope = world.Primary;
    var admin = (scope.FirmId, world.Administrator);
    var partner = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(database, admin, "Partner");
    var preparer = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(database, admin, "Staff");
    var reviewer = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(database, admin, "AccountingReviewer");
    // Retain the audit acceptance unchanged. A new, genuinely reviewed continuance supplies the native mandate.
    await using (var db = new AuditSphereDbContext(database.Options))
    {
      var current = await AcceptanceChecklistService.GetAsync(db, partner.Actor, scope.ClientId);
      Assert.True(current.Succeeded, current.Message);
      var continuance = await AcceptanceChecklistService.StartContinuanceAsync(db, partner.Actor, scope.ClientId,
        expectedGeneration: current.Value!.Generation);
      Assert.True(continuance.Succeeded, continuance.Message);
    }
    await SupportedAuditCompletionWorldBuilder.RecordUnconditionalAcceptanceAsync(database, scope.ClientId,
      "BOOKKEEPING", preparer.Actor, partner.Actor);
    await using var retained = new AuditSphereDbContext(database.Options);
    var generation = await retained.ClientSafetyStates.AsNoTracking()
      .Where(x => x.Id == scope.ClientId && x.FirmId == scope.FirmId).Select(x => x.InputGeneration).SingleAsync();
    var decision = await retained.AcceptanceDecisions.AsNoTracking().SingleAsync(x =>
      x.FirmId == scope.FirmId && x.PracticeClientId == scope.ClientId && x.EngagementId == null &&
      x.ServiceRoute == "BOOKKEEPING" && x.Generation == generation && x.Decision == "Accepted");
    Assert.Equal("CONTINUANCE", decision.Path);
    Assert.NotNull(decision.PriorDecisionId);
    var administrator = await retained.Users.AsNoTracking().SingleAsync(x => x.Id == world.Administrator.UserId);
    return new(scope.FirmId, scope.ClientId, scope.EngagementId, administrator, reviewer.User,
      world.Administrator, reviewer.Actor);
  }

  internal sealed class ContextFactory(DbContextOptions<AuditSphereDbContext> options) : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
    public Task<AuditSphereDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
      => Task.FromResult(new AuditSphereDbContext(options));
  }
}
