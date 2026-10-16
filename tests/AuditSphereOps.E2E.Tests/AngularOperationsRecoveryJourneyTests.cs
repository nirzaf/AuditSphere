using System.Text;
using System.Text.Json;
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

    Assert.Equal(1, await page.Locator("#search-help.sr-only").CountAsync());
    Assert.Equal(1, await page.Locator("input[aria-describedby~='search-help']").CountAsync());
    await Assertions.Expect(page.Locator("#search-help")).ToContainTextAsync("Press slash outside fields and dialogs to focus search.");
    await Assertions.Expect(page.Locator("#search-help")).ToContainTextAsync("Documents and emails are not searched.");
    foreach (var width in new[] { 1141, 1024, 700, 390 })
    {
      await page.SetViewportSizeAsync(width, 844);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Operations page overflowed the {width}px viewport.");
      Assert.True(await page.EvaluateAsync<bool>("""
        () => [...document.querySelectorAll('.app-header')].every(element => {
          const rect = element.getBoundingClientRect();
          return rect.left >= -1 && rect.right <= innerWidth + 1 && element.scrollWidth <= element.clientWidth + 1;
        })
        """), $"Operations header was clipped at {width}px.");
    }
    await page.SetViewportSizeAsync(1440, 900);

    await page.GetByLabel("Cancellation disposition", new() { Exact = true })
      .FillAsync("Operator cancelled synthetic queued work before worker claim.");
    var cancelRow = table.Locator("tbody tr").Filter(new() { HasText = cancelKind });
    await cancelRow.Locator("button[aria-label^='Cancel queued work']").ClickAsync();
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact operator command preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync("CANCEL");
    await Assertions.Expect(preview).ToContainTextAsync(cancelId.ToString("D"));
    await Assertions.Expect(preview).ToContainTextAsync("Operator cancelled synthetic queued work before worker claim.");
    await Assertions.Expect(preview.GetByRole(AriaRole.Heading, new() { Name = "Review operator command", Exact = true })).ToBeFocusedAsync();
    var confirm = preview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed operator command", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    await preview.GetByLabel("I reviewed this exact operator command, current state and effect.", new() { Exact = true }).CheckAsync();
    await confirm.ClickAsync();
    var receipt = page.GetByRole(AriaRole.Region, new() { Name = "Verified original operator receipt", Exact = true });
    await Assertions.Expect(receipt).ToContainTextAsync("CANCEL");
    await Assertions.Expect(receipt).ToContainTextAsync(cancelId.ToString("D"));
    await Assertions.Expect(receipt).ToContainTextAsync(administrator.Id.ToString("D"));
    await Assertions.Expect(cancelRow).ToContainTextAsync("cancelled with disposition");
    await Assertions.Expect(cancelRow).ToContainTextAsync("Operator cancelled synthetic queued work before worker claim.");

    var retryRow = table.Locator("tbody tr").Filter(new() { HasText = retryKind });
    var retryDispatches = 0;
    page.Request += (_, request) =>
    {
      if (request.Method != "POST" || !request.Url.EndsWith("/operations/commands/actions", StringComparison.Ordinal)) return;
      using var body = JsonDocument.Parse(request.PostData!);
      if (body.RootElement.GetProperty("input").GetProperty("action").GetString() == "RETRY")
        Interlocked.Increment(ref retryDispatches);
    };
    string? retryRequestId = null, retryRequestHash = null;
    const string actionRoute = "**/api/ui/operations/commands/actions";
    await page.RouteAsync(actionRoute, async route =>
    {
      using var request = JsonDocument.Parse(route.Request.PostData!);
      var input = request.RootElement.GetProperty("input");
      Assert.Equal("RETRY", input.GetProperty("action").GetString());
      Assert.Equal(retryId.ToString("D"), input.GetProperty("operationId").GetString());
      Assert.True(request.RootElement.GetProperty("reviewed").GetBoolean());
      retryRequestId = request.RootElement.GetProperty("requestId").GetString();
      retryRequestHash = request.RootElement.GetProperty("expectedRequestHash").GetString();
      await using var accepted = await route.FetchAsync();
      Assert.Equal(200, accepted.Status);
      await route.AbortAsync("failed");
    });
    await retryRow.Locator("button[aria-label^='Re-arm for retry']").ClickAsync();
    await Assertions.Expect(preview).ToContainTextAsync("RETRY");
    await Assertions.Expect(preview).ToContainTextAsync(retryId.ToString("D"));
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    Assert.Equal(0, retryDispatches);
    await preview.GetByLabel("I reviewed this exact operator command, current state and effect.", new() { Exact = true }).CheckAsync();
    await confirm.ClickAsync();
    var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Original operator request recovery", Exact = true });
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await page.UnrouteAsync(actionRoute);
    await page.ReloadAsync();
    await Assertions.Expect(recovery).ToContainTextAsync(retryRequestId!);
    await Assertions.Expect(recovery).ToContainTextAsync(retryRequestHash!);
    await Assertions.Expect(preview).ToHaveCountAsync(0);
    await Assertions.Expect(retryRow).ToContainTextAsync("retry wait");
    await Assertions.Expect(retryRow.Locator("button[aria-label^='Re-arm for retry']")).ToHaveCountAsync(0);
    // A queue refresh cannot acknowledge the original actor's command, even when its state matches.
    await Assertions.Expect(recovery).ToBeVisibleAsync();
    await recovery.GetByRole(AriaRole.Button, new() { Name = "Look up original operator receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(receipt).ToContainTextAsync("RETRY");
    await Assertions.Expect(receipt).ToContainTextAsync(retryRequestId!);
    await Assertions.Expect(receipt).ToContainTextAsync(retryRequestHash!);
    await Assertions.Expect(receipt).ToContainTextAsync(administrator.Id.ToString("D"));
    await Assertions.Expect(receipt).ToContainTextAsync("RETRY_WAIT");
    await Assertions.Expect(recovery).ToHaveCountAsync(0);
    Assert.DoesNotContain(privatePayload, await page.Locator("body").InnerTextAsync());
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
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Button, new() { Name = "Refresh operations", Exact = true })).ToHaveCountAsync(0);
    var deniedStatus = await staffPage.EvaluateAsync<int>("async () => (await fetch('/api/ui/operations')).status");
    Assert.Equal(403, deniedStatus);
    var deniedBody = await staffPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(retryKind, deniedBody);
    Assert.DoesNotContain(cancelKind, deniedBody);
    Assert.DoesNotContain(privatePayload, deniedBody);
    Assert.Empty(pageErrors);
  }
}
