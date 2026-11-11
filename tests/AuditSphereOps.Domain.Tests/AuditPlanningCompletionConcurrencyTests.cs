using System.Data.Common;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static AuditSphereOps.Domain.Tests.AuditPlanningCompletionTests;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditPlanningCompletionConcurrencyTests
{
  [Fact]
  public async Task WaitingReviewCannotApproveAnAmendedPreparation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var request = Request(world.Primary.EngagementId);
    var created = await Prepare(pg, world.Primary.Preparer, request);
    Assert.True(created.Succeeded, created.Message); var id = created.Value!.VerificationId;
    var pause = new PausePreparation();
    var arrived = new ObserveReviewLock();
    await using var preparation = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var review = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(arrived).Options);
    var amendment = AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(preparation, world.Primary.Preparer,
      request with { ExpectedRevision = 1, ExpectedVerificationId = id, AgreedSignedTotal = 990.123456m });
    try
    {
      await pause.Updated.Task.WaitAsync(TimeSpan.FromSeconds(30));
      var approval = AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(review, world.Reviewer,
        new(world.Primary.EngagementId, id, 1, "Review of the previous preparation."));
      await arrived.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
      Assert.False(approval.IsCompleted);
      pause.Release.TrySetResult();
      Assert.True((await amendment).Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, (await approval).ErrorCode);
    }
    finally { pause.Release.TrySetResult(); await amendment; }
    var row = await Read(pg, world.Primary.Preparer, world.Primary.EngagementId, id);
    Assert.Equal(2, row.Revision); Assert.Equal(10m, row.DifferenceAmount);
    Assert.False(row.CurrentIndependentReview); Assert.Null(row.ReviewedByUserId); Assert.Null(row.ReviewedBasisRevision);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.False(verify.Database.HasPendingModelChanges());
  }

  [Fact]
  public async Task WaitingCommandsRecheckRevokedAuthorityAndCurrentSession()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var request = Request(world.Primary.EngagementId);
    var created = await Prepare(pg, world.Primary.Preparer, request);
    Assert.True(created.Succeeded, created.Message); var id = created.Value!.VerificationId;
    foreach (var reviewing in new[] { true, false })
    {
      var actor = reviewing ? world.Reviewer : world.Primary.Preparer;
      var pause = new PauseBeforeFirmLock();
      await using var commandDb = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
      async Task<string?> Execute()
      {
        if (reviewing) return (await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(commandDb, actor,
          new(world.Primary.EngagementId, id, 1, null))).ErrorCode;
        return (await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(commandDb, actor,
          request with { ExpectedRevision = 1, ExpectedVerificationId = id, AgreedSignedTotal = 900m })).ErrorCode;
      }
      var command = Execute();
      try
      {
        await pause.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await using var adminDb = new AuditSphereDbContext(pg.Options);
        var grantId = await adminDb.RoleGrants.AsNoTracking().Where(x => x.UserId == actor.UserId &&
          x.EngagementId == world.Primary.EngagementId && x.RevokedAt == null).Select(x => x.Id).SingleAsync();
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(adminDb, world.Administrator, new(grantId, Reason: "Synthetic R06 waiting-command revocation."));
        Assert.True(revoked.Succeeded, revoked.Message);
        pause.Release.TrySetResult(); Assert.Equal(ErrorCodes.GenerationStale, await command);
        var epoch = await adminDb.Users.AsNoTracking().Where(x => x.Id == actor.UserId).Select(x => x.SessionEpoch).SingleAsync();
        Assert.True(epoch > actor.SessionEpoch);
        if (reviewing)
          Assert.Equal(ErrorCodes.ScopeDenied, (await Review(pg, actor with { SessionEpoch = epoch }, new(world.Primary.EngagementId, id, 1, null))).ErrorCode);
        else
          Assert.Equal(ErrorCodes.ScopeDenied, (await Prepare(pg, actor with { SessionEpoch = epoch }, request with { ExpectedRevision = 1, ExpectedVerificationId = id })).ErrorCode);
      }
      finally { pause.Release.TrySetResult(); await command; }
    }
    var row = await Read(pg, world.Administrator, world.Primary.EngagementId, id);
    Assert.Equal(1, row.Revision); Assert.Equal(request.OpeningSignedTotal, row.OpeningSignedTotal);
    Assert.Equal(request.AgreedSignedTotal, row.AgreedSignedTotal); Assert.Equal(request.EvidenceReferences, row.EvidenceReferences);
    Assert.Null(row.ReviewedByUserId); Assert.Null(row.ReviewedBasisRevision); Assert.False(row.CurrentIndependentReview);
  }

  private sealed class PauseBeforeFirmLock : DbCommandInterceptor
  {
    public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
      InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.Ordinal) && command.CommandText.Contains("FOR SHARE", StringComparison.Ordinal))
      { Waiting.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct); }
      return result;
    }
  }

  private sealed class PausePreparation : DbCommandInterceptor
  {
    public TaskCompletionSource Updated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
    {
      if (command.CommandText.Contains("UPDATE opening_balance_verifications", StringComparison.Ordinal))
      { Updated.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct); }
      return result;
    }
  }
  private sealed class ObserveReviewLock : DbCommandInterceptor
  {
    public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
      InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
      if (command.CommandText.Contains("client_safety_states", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Waiting.TrySetResult();
      return ValueTask.FromResult(result);
    }
  }
}
