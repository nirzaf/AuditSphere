using System.Text;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularOperationsRecoveryJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-OPERATIONS-RECOVERY-01")]
  public async Task FirmAdministratorCanCancelAndRearmBoundedOperations_StaffCannotReadThem()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-OPERATIONS-RECOVERY-01");
    var firm = host.Fixture;
    var administrator = PbcSeed.User(firm.FirmId, "Staff");
    const string retryKind = "SYN-PAR-002-RETRYABLE-OPERATION";
    const string cancelKind = "SYN-PAR-002-QUEUED-OPERATION";
    const string privatePayload = "SYN-PAR-002-PRIVATE-REQUEST-BYTES";
    var retryId = Guid.NewGuid();
    var cancelId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;

    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(administrator);
      db.RoleGrants.Add(PbcSeed.Grant(firm.FirmId, administrator, "Administrator"));
      db.DurableOperations.Add(new DurableOperation
      {
        Id = retryId,
        FirmId = firm.FirmId,
        OperationKind = retryKind,
        TargetId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        ExpectedRevision = 1,
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        RequestDigest = new string('b', 64),
        RequestBytes = Encoding.UTF8.GetBytes(privatePayload),
        Status = OperationState.DEAD_LETTER,
        AttemptCount = 4,
        ErrorCode = "provider.test-blocked",
        CreatedAt = now.AddSeconds(2),
        NextAttemptAt = now
      });
      db.DurableOperations.Add(new DurableOperation
      {
        Id = cancelId,
        FirmId = firm.FirmId,
        OperationKind = cancelKind,
        TargetId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        ExpectedRevision = 1,
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        RequestDigest = new string('c', 64),
        RequestBytes = [1],
        Status = OperationState.PENDING,
        CreatedAt = now.AddSeconds(1),
        NextAttemptAt = now
      });
      for (var index = 0; index < 24; index++)
      {
        db.DurableOperations.Add(new DurableOperation
        {
          Id = Guid.NewGuid(),
          FirmId = firm.FirmId,
          OperationKind = $"SYN-PAR-002-FILLER-{index:00}",
          TargetId = Guid.NewGuid(),
          CorrelationId = Guid.NewGuid(),
          ExpectedRevision = 1,
          IdempotencyKey = Guid.NewGuid().ToString("N"),
          RequestDigest = new string('d', 64),
          RequestBytes = [1],
          Status = OperationState.PENDING,
          AttemptCount = 0,
          CreatedAt = now.AddMinutes(-index - 1),
          NextAttemptAt = now
        });
      }
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var adminOrigin = await host.StartApiForIdentityAsync(administrator, settings);
    var staffOrigin = await host.StartApiForIdentityAsync(firm.Staff, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var adminContext = await browser.NewContextAsync();
    var page = await adminContext.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);

    await page.GotoAsync(adminOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Foperations");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Operations", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("#search-help")).ToBeVisibleAsync();
    var table = page.GetByRole(AriaRole.Table, new() { Name = "Latest 50 durable operations" });
    await Assertions.Expect(table).ToBeVisibleAsync();
    await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(10);
    await Assertions.Expect(page.GetByLabel("Rows per page", new() { Exact = true })).ToHaveValueAsync("10");
    await Assertions.Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Durable operation pages", Exact = true }))
      .ToContainTextAsync("Page 1 of 3 · 1–10 of 26");
    Assert.DoesNotContain(privatePayload, await page.Locator("body").InnerTextAsync());

    foreach (var width in new[] { 1141, 1024, 700, 390 })
    {
      await page.SetViewportSizeAsync(width, 844);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Operations page overflowed the {width}px viewport.");
      Assert.True(await page.EvaluateAsync<bool>("""
        () => [...document.querySelectorAll('.app-header, #search-help')].every(element => {
          const rect = element.getBoundingClientRect();
          return rect.left >= -1 && rect.right <= innerWidth + 1 && element.scrollWidth <= element.clientWidth + 1;
        })
        """), $"Operations header or search guidance was clipped at {width}px.");
    }
    await page.SetViewportSizeAsync(1440, 900);

    await page.GetByLabel("Cancellation disposition", new() { Exact = true })
      .FillAsync("Operator cancelled synthetic queued work before worker claim.");
    var cancelRow = table.Locator("tbody tr").Filter(new() { HasText = cancelKind });
    await cancelRow.GetByRole(AriaRole.Button, new() { Name = "Cancel queued work", Exact = true }).ClickAsync();
    await Assertions.Expect(cancelRow).ToContainTextAsync("cancelled with disposition");
    await Assertions.Expect(cancelRow).ToContainTextAsync("Operator cancelled synthetic queued work before worker claim.");

    var retryRow = table.Locator("tbody tr").Filter(new() { HasText = retryKind });
    var retryDispatches = 0;
    await page.RouteAsync($"**/api/ui/operations/{retryId:D}/retry", async route =>
    {
      Interlocked.Increment(ref retryDispatches);
      await using var accepted = await route.FetchAsync();
      Assert.Equal(200, accepted.Status);
      await route.AbortAsync("failed");
    });
    await retryRow.GetByRole(AriaRole.Button, new() { Name = "Re-arm for retry", Exact = true }).ClickAsync();
    await Assertions.Expect(retryRow).ToContainTextAsync("retry wait");
    await Assertions.Expect(retryRow.GetByRole(AriaRole.Button, new() { Name = "Re-arm for retry", Exact = true })).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByText("Re-arm confirmed from the persisted operation state.", new() { Exact = true })).ToBeVisibleAsync();
    Assert.Equal(1, retryDispatches);

    await using (var verify = host.CreateDbContext())
    {
      var cancelled = await verify.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == cancelId);
      Assert.Equal(OperationState.CANCELLED_WITH_DISPOSITION, cancelled.Status);
      Assert.Equal("Operator cancelled synthetic queued work before worker claim.", cancelled.CancellationDisposition);
      var retried = await verify.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == retryId);
      Assert.Equal(OperationState.RETRY_WAIT, retried.Status);
      Assert.Equal(0, retried.AttemptCount);
      Assert.Contains(await verify.OperationEvents.AsNoTracking().Where(x => x.OperationId == cancelId).ToListAsync(),
        x => x.Kind == "operation.cancelled.v1" && x.Executor == administrator.Id.ToString("D"));
      var retryEvents = await verify.OperationEvents.AsNoTracking()
        .Where(x => x.OperationId == retryId && x.Kind == "operation.recovery-retry.v1").ToListAsync();
      Assert.Equal(administrator.Id.ToString("D"), Assert.Single(retryEvents).Executor);
    }

    await using var staffContext = await browser.NewContextAsync();
    var staffPage = await staffContext.NewPageAsync();
    staffPage.PageError += (_, error) => pageErrors.Add(error);
    await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Foperations");
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading, new() { Name = "Operations", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Operations is limited to firm-wide AuditSphere Administrators. Your current account does not have that access;");
    var deniedStatus = await staffPage.EvaluateAsync<int>("async () => (await fetch('/api/ui/operations')).status");
    Assert.Equal(403, deniedStatus);
    var deniedBody = await staffPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(retryKind, deniedBody);
    Assert.DoesNotContain(cancelKind, deniedBody);
    Assert.DoesNotContain(privatePayload, deniedBody);
    Assert.Empty(pageErrors);
  }
}
