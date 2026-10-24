using System.Data.Common;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Theory]
  [InlineData(false, false)]
  [InlineData(true, true)]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorCatalogWithholdsMetadataAfterLateAuthorityChange(bool approval, bool revokeScope)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeSuccessorFixtureAsync(pg);
    var maker = Actor(f.Native.Scope.Preparer, "AccountingPreparer");
    var actor = approval ? Actor(f.Native.Scope.Reviewer, "AccountingReviewer") : maker;
    await using var setup = new AuditSphereDbContext(pg.Options);
    if (approval)
    {
      var preview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(setup, maker, f.Request);
      Assert.True(preview.Succeeded, preview.Message);
      var created = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(setup, maker,
        f.Request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
      Assert.True(created.Succeeded, created.Message);
    }
    var before = await ClientOperationalOpeningBalanceWorkspace.SuccessorCatalogAsync(setup, actor,
      f.Native.Scope.ClientA, f.TargetPeriodId);
    Assert.True(before.Succeeded, before.Message);
    Assert.True(approval ? before.Value!.CanApprove : before.Value!.CanPrepare);
    Assert.Equal(f.SourceHash, Assert.Single(before.Value!.Sources).SourceIdentityHash);
    // The final prepare readiness has one freeze read; independent approval has a second.
    // Change real committed authority after its user/grant checks, before readiness finishes.
    var change = new ChangeSuccessorCatalogAuthority(approval ? 2 : 1, async ct =>
    {
      await using var writer = new AuditSphereDbContext(pg.Options);
      if (revokeScope)
        await writer.RoleGrants.Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
      else
        await writer.Users.Where(x => x.Id == actor.UserId && x.FirmId == actor.FirmId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), ct);
    });
    await using var read = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(change).Options);
    var result = await ClientOperationalOpeningBalanceWorkspace.SuccessorCatalogAsync(read, actor,
      f.Native.Scope.ClientA, f.TargetPeriodId);
    Assert.True(change.Invoked, "The late change must execute at the selected final readiness boundary.");
    Assert.False(result.Succeeded, "No source hashes, period metadata or retained proof IDs may be returned after authority changes.");
    Assert.Equal(revokeScope ? ErrorCodes.ScopeDenied : ErrorCodes.GenerationStale, result.ErrorCode);
    Assert.Null(result.Value);
  }

  private sealed class ChangeSuccessorCatalogAuthority(int atRead, Func<CancellationToken, Task> change) : DbCommandInterceptor
  {
    private int reads;
    public bool Invoked { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("engagement_file_freezes", StringComparison.Ordinal) && ++reads == atRead)
      {
        await change(cancellationToken);
        Invoked = true;
      }
      return result;
    }
  }
}
