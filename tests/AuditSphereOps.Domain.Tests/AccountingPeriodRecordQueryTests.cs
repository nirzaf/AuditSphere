using System.Data.Common;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class AccountingPeriodRecordQueryTests
{
  [Fact]
  public async Task RevocationDuringHandoffReadsRefusesTheEntirePeriodProjection()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var periodId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", f.ClientId));
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "SYNTHETIC-2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Draft,
        CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    var interceptor = new RevokeAfterBookRead(async () =>
    {
      await using var revoke = new AuditSphereDbContext(pg.Options);
      await revoke.RoleGrants.Where(x => x.UserId == f.Staff.Id && x.Role == "AccountingPreparer")
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var reader = new AuditSphereDbContext(options);
    var result = await AccountingPeriodRecordQuery.GetAsync(reader, PbcSeed.Actor(f.Staff, "AccountingPreparer"), periodId);
    Assert.Equal(1, interceptor.Calls);
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    Assert.Null(result.Value);
  }

  private sealed class RevokeAfterBookRead(Func<Task> revoke) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => calls;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("client_reporting_books", StringComparison.Ordinal) &&
          Interlocked.CompareExchange(ref calls, 1, 0) == 0)
        await revoke();
      return result;
    }
  }
}
