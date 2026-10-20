using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class ClientManualSettlementsApiTests
{
  [Fact]
  public async Task PreviewRechecksSessionAfterReadingAndReturnsNoPreviewOrIntentHashAfterRevocation()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-MANUAL-SETTLEMENT-PREVIEW-EPOCH");
    var fixture = await PbcSeed.SeedAsync(pg);
    var (periodId, counterpartyId) = await SeedNativeSupplierBookkeeping(pg, fixture);
    var interceptor = new EpochAfterCounterpartyRead(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      await db.Users.Where(x => x.Id == fixture.Staff.Id)
        .ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    });
    using var factory = Factory(pg, fixture, interceptor);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    var path = $"/api/ui/accounting/clients/{fixture.ClientId}/manual-settlements/preview";

    using var allowed = await Preview(client, csrf, path, Request(periodId, counterpartyId, "BANK-API-ALLOWED"));
    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    Assert.True(allowed.Headers.Contains("X-AuditSphere-Intent-Hash"));
    using (var preview = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync()))
    {
      Assert.Equal("2100", preview.RootElement.GetProperty("controlAccountCode").GetString());
      Assert.Equal("1000", preview.RootElement.GetProperty("cashAccountCode").GetString());
    }

    interceptor.Arm();
    using var revoked = await Preview(client, csrf, path, Request(periodId, counterpartyId, "BANK-API-REVOKED"));
    Assert.True(interceptor.Fired);
    Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    Assert.False(revoked.Headers.Contains("X-AuditSphere-Intent-Hash"));
    var revokedBody = await revoked.Content.ReadAsStringAsync();
    Assert.DoesNotContain("controlAccountCode", revokedBody, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("cashAccountCode", revokedBody, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("2100", revokedBody, StringComparison.Ordinal);
    Assert.DoesNotContain("1000", revokedBody, StringComparison.Ordinal);
    using var error = JsonDocument.Parse(revokedBody);
    Assert.Equal("session.unavailable", error.RootElement.GetProperty("code").GetString());
  }

  private static async Task<(Guid PeriodId, Guid CounterpartyId)> SeedNativeSupplierBookkeeping(
    OwnedPostgresDatabase pg, PbcSeed.Fixture fixture)
  {
    var preparer = PbcSeed.Actor(fixture.Staff, "AccountingPreparer");
    var reviewer = PbcSeed.Actor(fixture.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.AddRange(
      PbcSeed.Grant(fixture.FirmId, fixture.Staff, "AccountingPreparer", clientId: fixture.ClientId),
      PbcSeed.Grant(fixture.FirmId, fixture.Reviewer, "AccountingReviewer", clientId: fixture.ClientId));
    db.AcceptanceDecisions.Add(new()
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
      ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
      Rationale = "Synthetic accepted bookkeeping fixture", EvaluationTemplateVersion = "TEST-1",
      EvaluationSnapshotDigest = new string('f', 64), DecidedByUserId = fixture.Reviewer.Id,
      DecidedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var profile = await ClientAccountingService.CreateProfileAsync(db, reviewer,
      new(fixture.ClientId, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE", ClientAccountingSourceModes.NativeBookkeeping));
    Assert.True(profile.Succeeded, profile.Message);
    var period = await ClientAccountingService.CreatePeriodAsync(db, preparer,
      new(fixture.ClientId, "2026", new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"));
    Assert.True(period.Succeeded, period.Message);
    var chart = await ClientAccountingService.CreateChartVersionAsync(db, preparer, fixture.ClientId,
      "MANUAL-SETTLEMENT-API", new(2026, 1, 1));
    Assert.True(chart.Succeeded, chart.Message);
    var accounts = await ClientAccountingService.AddAccountsAsync(db, preparer, chart.Value,
    [
      new("cash", "1000", "Client bank", "ASSET", "DEBIT", true),
      new("ap", "2100", "Supplier payables", "LIABILITY", "CREDIT", true)
    ]);
    Assert.True(accounts.Succeeded, accounts.Message);
    var published = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chart.Value);
    Assert.True(published.Succeeded, published.Message);
    var ap = await db.ClientAccounts.SingleAsync(x => x.ChartVersionId == chart.Value && x.AccountCode == "2100");
    var proposedRole = await ClientAccountRoleWorkspace.ProposeAsync(db, preparer, fixture.ClientId,
      new(chart.Value, ap.Id, "AP", new(2026, 1, 1), null, "Reviewed supplier control"));
    Assert.True(proposedRole.Succeeded, proposedRole.Message);
    var reviewedRole = await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, fixture.ClientId,
      proposedRole.Value, "APPROVE", "Independent AP control review");
    Assert.True(reviewedRole.Succeeded, reviewedRole.Message);
    var supplier = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, preparer, fixture.ClientId,
      new("Example supplier", "Example supplier", "SUPPLIER", "QA address", "QA", "", "", "", "", "TEST-SYSTEM", "SUPPLIER-1"));
    Assert.True(supplier.Succeeded, supplier.Message);
    return (period.Value, supplier.Value);
  }

  private static object Request(Guid periodId, Guid counterpartyId, string reference) => new
  {
    commandId = Guid.CreateVersion7(), periodId, counterpartyId, sourceKind = "SUPPLIER_PAYMENT",
    journalNumber = "PAY-API-001", description = "Record completed supplier payment", postingDate = "2026-01-25",
    cashAccountCode = "1000", amount = "40", reference, evidenceReference = "Synthetic completed-payment evidence",
    previewDigest = "", reviewed = false
  };

  private static Task<HttpResponseMessage> Preview(HttpClient client, string csrf, string path, object request)
  {
    var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request) };
    message.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(message);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, PbcSeed.Fixture fixture,
    DbCommandInterceptor interceptor) => new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:TenantId"] = fixture.Staff.TenantId,
      ["DevelopmentIdentity:Subject"] = fixture.Staff.Subject,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    }, services =>
    {
      services.RemoveAll<IDbContextFactory<AuditSphereDbContext>>();
      services.AddSingleton<IDbContextFactory<AuditSphereDbContext>>(
        new InterceptingContextFactory(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
          .AddInterceptors(interceptor).Options));
    });

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private sealed class EpochAfterCounterpartyRead(Func<Task> revoke) : DbCommandInterceptor
  {
    private int armed;
    private int fired;
    public bool Fired => Volatile.Read(ref fired) == 1;
    public void Arm() => Interlocked.Exchange(ref armed, 1);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (Volatile.Read(ref armed) == 1 &&
          command.CommandText.Contains("client_bookkeeping_counterparties", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref fired, 1, 0) == 0)
        await revoke();
      return result;
    }
  }

  private sealed class InterceptingContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }
}
