using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Acceptance;
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
    var reviewer = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(database, admin, "AccountingReviewer");
    await using (var db = new AuditSphereDbContext(database.Options))
    {
      var currentGeneration = await db.ClientSafetyStates.AsNoTracking()
        .Where(x => x.Id == scope.ClientId && x.FirmId == scope.FirmId)
        .Select(x => x.InputGeneration).SingleAsync();
      var prior = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == scope.FirmId &&
          x.PracticeClientId == scope.ClientId && x.Decision == "Accepted")
        .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).FirstAsync();
      // This accounting journey's test precondition is the client-level native-bookkeeping mandate used by
      // the adjacent accounting E2Es. BOOKKEEPING is not a commercial opportunity route.
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientId,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = currentGeneration,
        Path = AcceptancePaths.Continuance, PriorDecisionId = prior.Id,
        Rationale = "Synthetic native bookkeeping cutover", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = partner.User.Id,
        DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
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
