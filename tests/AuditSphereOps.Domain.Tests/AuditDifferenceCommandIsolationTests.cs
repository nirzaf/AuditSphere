using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Text.Json;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditDifferenceCommandIsolationTests
{
  [Theory]
  [InlineData("RECORD")]
  [InlineData("EVALUATE")]
  public async Task DirectDifferenceWritesRefuseAuthorityRevokedBeforeTheirTransaction(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var ct = timeout.Token;
    var scope = (await PlanningSeed.CreateAsync(pg, role: "Partner")).Primary;
    var writer = PbcSeed.User(scope.FirmId, "Staff");
    var administrator = PbcSeed.User(scope.FirmId, "Staff");
    var actor = PbcSeed.Actor(writer, "Partner");
    Guid grantId, targetId;
    async Task<CommandResult<DifferenceValue>> Execute(AuditSphereDbContext db, string description) =>
      action == "RECORD"
        ? await AuditFieldworkService.RecordDifferenceAsync(db, actor, NewDifference(scope.EngagementId, description), ct)
        : await AuditFieldworkService.EvaluateDifferenceAsync(db, actor,
          new(targetId, false, description, "Synthetic retained human response", null), ct);

    async Task<string> Snapshot(AuditSphereDbContext db) => JsonSerializer.Serialize(
      await db.AuditDifferences.AsNoTracking().Where(x => x.EngagementId == scope.EngagementId)
        .OrderBy(x => x.Id).ToListAsync(ct));

    string before;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.AddRange(writer, administrator);
      var grant = PbcSeed.Grant(scope.FirmId, writer, "Partner", scope.ClientId, scope.EngagementId);
      grantId = grant.Id;
      setup.RoleGrants.AddRange(grant, PbcSeed.Grant(scope.FirmId, administrator, "Administrator"));
      await setup.SaveChangesAsync(ct);
      var target = await AuditFieldworkService.RecordDifferenceAsync(setup, scope.Actor,
        NewDifference(scope.EngagementId, "Retained original source difference"), ct);
      Assert.True(target.Succeeded, target.Message);
      targetId = target.Value!.AuditDifferenceId;
      // A distinct authorized actor can perform the same operation before revocation.
      var originalTargetId = targetId;
      if (action == "EVALUATE")
      {
        var positiveTarget = await AuditFieldworkService.RecordDifferenceAsync(setup, scope.Actor,
          NewDifference(scope.EngagementId, "Separate source for authorized evaluation"), ct);
        Assert.True(positiveTarget.Succeeded, positiveTarget.Message);
        targetId = positiveTarget.Value!.AuditDifferenceId;
      }
      var allowed = await Execute(setup, "Authorized difference action before revocation");
      Assert.True(allowed.Succeeded, allowed.Message);
      targetId = originalTargetId;
      before = await Snapshot(setup);
    }

    var interceptor = new RevokeOnDifferenceTransaction(async token =>
    {
      await using var revokeDb = new AuditSphereDbContext(pg.Options);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(revokeDb,
        PbcSeed.Actor(administrator, "Administrator"), new(grantId, Reason: "Synthetic difference authority interleaving"), token);
      Assert.True(revoked.Succeeded, revoked.Message);
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var db = new AuditSphereDbContext(options);
    var result = await Execute(db, "Denied difference action after committed revocation");
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.True(interceptor.Invoked);
    Assert.NotNull((await verify.RoleGrants.SingleAsync(x => x.Id == grantId, ct)).RevokedAt);
    Assert.True(await verify.Users.Where(x => x.Id == writer.Id).Select(x => x.SessionEpoch).SingleAsync(ct) > actor.SessionEpoch);
    var after = await Snapshot(verify);
    Assert.True(!result.Succeeded && before == after,
      $"Official revocation committed before {action} continued; succeeded={result.Succeeded}, retained differences changed={before != after}.");
  }

  private sealed class RevokeOnDifferenceTransaction(Func<CancellationToken, Task> revoke) : DbTransactionInterceptor
  {
    private int invoked;
    public bool Invoked => Volatile.Read(ref invoked) != 0;
    public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
      TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
      if (Interlocked.Exchange(ref invoked, 1) == 0) await revoke(cancellationToken);
      return result;
    }
  }

  [Fact(DisplayName = "Client-scoped difference commands cannot create or mutate sibling-client differences by identifier")]
  public async Task SiblingClientIds_AreDeniedForDifferenceQueriesAndCommands()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var clientBUserId = Guid.NewGuid();
    var clientBActor = new ActorContext(clientBUserId, clientB.FirmId, 1, ["Partner"]);

    Guid differenceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingEngagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      siblingEngagement.ProfessionalWorkBlocked = false;
      db.Users.Add(new AppUser
      {
        Id = clientBUserId,
        FirmId = clientB.FirmId,
        Subject = "audit-difference-client-b-" + clientBUserId.ToString("N"),
        TenantId = "tenant-planning",
        Email = "difference-client-b@example.test",
        DisplayName = "Client B Partner",
        UserKind = "Staff",
        SessionEpoch = 1,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(),
        FirmId = clientB.FirmId,
        UserId = clientBUserId,
        Role = "Partner",
        ClientId = clientB.ClientId,
        EngagementId = clientB.EngagementId,
        GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = clientA.Actor.UserId
      });
      await db.SaveChangesAsync();

      var created = await AuditFieldworkService.RecordDifferenceAsync(db, clientBActor,
        NewDifference(clientB.EngagementId, "Client B's timing difference."));
      Assert.True(created.Succeeded, created.Message);
      differenceId = created.Value!.AuditDifferenceId;

      var siblingCreate = await AuditFieldworkService.RecordDifferenceAsync(db, clientA.Actor,
        NewDifference(clientB.EngagementId, "Unauthorized sibling difference."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingCreate.ErrorCode);

      var siblingSummary = await AuditFieldworkService.GetDifferenceSummariesAsync(db, clientA.Actor,
        clientB.EngagementId);
      Assert.Equal(ErrorCodes.ScopeDenied, siblingSummary.ErrorCode);

      var siblingEvaluation = await AuditFieldworkService.EvaluateDifferenceAsync(db, clientA.Actor,
        new EvaluateDifferenceRequest(differenceId, false, "Unauthorized evaluation.", "Unauthorized response.", null));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingEvaluation.ErrorCode);

      var siblingCorrection = await AuditFieldworkService.SetDifferenceCorrectionStateAsync(db, clientA.Actor,
        new SetDifferenceCorrectionStateRequest(differenceId, AuditDifferenceCorrectionStates.Rejected,
          "Unauthorized correction decision."));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingCorrection.ErrorCode);

      var siblingJournalLink = await AuditFieldworkService.LinkDifferenceToJournalAsync(db, clientA.Actor,
        new LinkDifferenceToJournalRequest(differenceId, Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid()));
      Assert.Equal(ErrorCodes.ScopeDenied, siblingJournalLink.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var difference = await db.AuditDifferences.AsNoTracking().SingleAsync(x => x.Id == differenceId);
      Assert.Equal(clientB.ClientId, difference.ClientId);
      Assert.Equal(clientB.EngagementId, difference.EngagementId);
      Assert.Equal("Client B's timing difference.", difference.Description);
      Assert.Equal(AuditDifferenceStatuses.Open, difference.Status);
      Assert.False(difference.Corrected);
      Assert.Null(difference.Evaluation);
      Assert.Null(difference.ManagementResponse);
      Assert.Null(difference.CorrectionState);
      Assert.Null(difference.ProposedJournalId);
      Assert.Equal(clientBUserId, difference.CreatedByUserId);
      Assert.Equal(1, await db.AuditDifferences.CountAsync(x => x.EngagementId == clientB.EngagementId));
    }
  }

  private static RecordDifferenceRequest NewDifference(Guid engagementId, string description) => new(
    engagementId, null, "Cash", "KNOWN", description, -25m, "QAR");
}
