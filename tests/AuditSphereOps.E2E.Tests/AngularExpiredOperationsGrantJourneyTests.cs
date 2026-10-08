using System.Text;
using System.Text.Json;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularExpiredOperationsGrantJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-OPERATIONS-EXPIRED-ADMIN-01")]
  public async Task ExpiredFirmAdministratorGrantInvalidatesSessionBeforeOperationsAreReturned()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-OPERATIONS-EXPIRED-ADMIN-01");
    var firm = host.Fixture;
    var expiredAdministrator = PbcSeed.User(firm.FirmId, "Staff");
    const string privateKind = "SYN-PAR-002-EXPIRED-ADMIN-PRIVATE-OPERATION";
    const string privatePayload = "SYN-PAR-002-EXPIRED-ADMIN-PRIVATE-BYTES";
    var operationId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    var grant = PbcSeed.Grant(firm.FirmId, expiredAdministrator, "Administrator");
    grant.GrantedAt = now.AddMinutes(-2);
    grant.ExpiresAt = now.AddMinutes(-1);

    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(expiredAdministrator);
      db.RoleGrants.Add(grant);
      db.DurableOperations.Add(new DurableOperation
      {
        Id = operationId,
        FirmId = firm.FirmId,
        OperationKind = privateKind,
        TargetId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        ExpectedRevision = 1,
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        RequestDigest = new string('e', 64),
        RequestBytes = Encoding.UTF8.GetBytes(privatePayload),
        Status = OperationState.PENDING,
        CreatedAt = now,
        NextAttemptAt = now
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(expiredAdministrator,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Foperations");
    var resultJson = await page.EvaluateAsync<string>("""
      async () => {
        const response = await fetch('/api/ui/operations');
        return JSON.stringify({ status: response.status, body: await response.text() });
      }
      """);
    using var result = JsonDocument.Parse(resultJson);
    var status = result.RootElement.GetProperty("status").GetInt32();
    Assert.Contains(status, new[] { 401, 409 });
    var safeBody = result.RootElement.GetProperty("body").GetString() ?? string.Empty;
    Assert.DoesNotContain(privateKind, safeBody, StringComparison.Ordinal);
    Assert.DoesNotContain(privatePayload, safeBody, StringComparison.Ordinal);
    Assert.DoesNotContain(privateKind, await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
    Assert.DoesNotContain(privatePayload, await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);

    await using (var verify = host.CreateDbContext())
    {
      var persistedGrant = await verify.RoleGrants.AsNoTracking().SingleAsync(x => x.Id == grant.Id);
      Assert.NotNull(persistedGrant.RevokedAt);
      Assert.Equal(now.AddMinutes(-1), persistedGrant.ExpiresAt);
      var persistedUser = await verify.Users.AsNoTracking().SingleAsync(x => x.Id == expiredAdministrator.Id);
      Assert.Equal(expiredAdministrator.SessionEpoch + 1, persistedUser.SessionEpoch);
      var evidence = await verify.RoleGrantChangeEvidences.AsNoTracking()
        .Where(x => x.RoleGrantId == grant.Id).ToListAsync();
      var evidenceEvent = Assert.Single(evidence);
      Assert.Equal("REVOKED", evidenceEvent.Action);
      Assert.Equal("EXPIRY", evidenceEvent.Source);
      Assert.Equal(Guid.Empty, evidenceEvent.ActorUserId);
      var operation = await verify.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.PENDING, operation.Status);
    }

    Assert.Empty(pageErrors);
  }
}
