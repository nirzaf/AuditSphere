using System.Text;
using System.Text.Json;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularCrossFirmOperationsIsolationJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-OPERATIONS-CROSS-FIRM-01")]
  public async Task FirmAdministratorCannotListOrMutateAnotherFirmsOperationByGuessedId()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-OPERATIONS-CROSS-FIRM-01");
    var firm = host.Fixture;
    var foreignFirmId = Guid.NewGuid();
    var foreignOperationId = Guid.NewGuid();
    const string privateKind = "SYN-PAR-002-FOREIGN-FIRM-OPERATION";
    const string privatePayload = "SYN-PAR-002-FOREIGN-FIRM-PRIVATE-BYTES";
    var now = DateTimeOffset.UtcNow;

    await using (var db = host.CreateDbContext())
    {
      db.FirmSafetyStates.Add(new FirmSafetyState { Id = foreignFirmId });
      db.DurableOperations.Add(new DurableOperation
      {
        Id = foreignOperationId,
        FirmId = foreignFirmId,
        OperationKind = privateKind,
        TargetId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        ExpectedRevision = 1,
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        RequestDigest = new string('f', 64),
        RequestBytes = Encoding.UTF8.GetBytes(privatePayload),
        Status = OperationState.PROVIDER_BLOCKED,
        AttemptCount = 3,
        ErrorCode = "provider.synthetic-blocked",
        CreatedAt = now,
        NextAttemptAt = now
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(firm.Admin,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Foperations");
    var responseScript = """
      async () => {
        await fetch('/api/ui/session');
        const tokenCookie = document.cookie.split(';').map(value => value.trim())
          .find(value => value.startsWith('XSRF-TOKEN='));
        const token = tokenCookie ? decodeURIComponent(tokenCookie.slice('XSRF-TOKEN='.length)) : '';
        const headers = { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token };
        const list = await fetch('/api/ui/operations');
        const listBody = await list.text();
        const retry = await fetch('/api/ui/operations/__OPERATION_ID__/retry', {
          method: 'POST', headers, body: '{}'
        });
        const retryBody = await retry.text();
        const cancel = await fetch('/api/ui/operations/__OPERATION_ID__/cancel', {
          method: 'POST', headers, body: JSON.stringify({ disposition: 'Reviewed isolation test' })
        });
        const cancelBody = await cancel.text();
        return JSON.stringify({
          listStatus: list.status, listBody,
          retryStatus: retry.status, retryBody,
          cancelStatus: cancel.status, cancelBody
        });
      }
      """;
    var responseJson = await page.EvaluateAsync<string>(responseScript.Replace(
      "__OPERATION_ID__", foreignOperationId.ToString("D"), StringComparison.Ordinal));
    using var response = JsonDocument.Parse(responseJson);
    var root = response.RootElement;
    Assert.Equal(200, root.GetProperty("listStatus").GetInt32());
    Assert.Equal(403, root.GetProperty("retryStatus").GetInt32());
    Assert.Equal(403, root.GetProperty("cancelStatus").GetInt32());
    foreach (var property in new[] { "listBody", "retryBody", "cancelBody" })
    {
      var body = root.GetProperty(property).GetString() ?? string.Empty;
      Assert.DoesNotContain(privateKind, body, StringComparison.Ordinal);
      Assert.DoesNotContain(privatePayload, body, StringComparison.Ordinal);
    }
    var listBody = root.GetProperty("listBody").GetString() ?? string.Empty;
    var retryBody = root.GetProperty("retryBody").GetString() ?? string.Empty;
    var cancelBody = root.GetProperty("cancelBody").GetString() ?? string.Empty;
    Assert.DoesNotContain(foreignOperationId.ToString("D"), listBody, StringComparison.Ordinal);
    Assert.Contains("scope.denied", retryBody, StringComparison.Ordinal);
    Assert.Contains("scope.denied", cancelBody, StringComparison.Ordinal);
    Assert.DoesNotContain(privateKind, await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
    Assert.DoesNotContain(privatePayload, await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);

    await using (var verify = host.CreateDbContext())
    {
      var operation = await verify.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == foreignOperationId);
      Assert.Equal(foreignFirmId, operation.FirmId);
      Assert.Equal(OperationState.PROVIDER_BLOCKED, operation.Status);
      Assert.Equal(3, operation.AttemptCount);
      Assert.Equal(privatePayload, Encoding.UTF8.GetString(operation.RequestBytes));
      Assert.Empty(await verify.OperationEvents.AsNoTracking().Where(x => x.OperationId == foreignOperationId).ToListAsync());
    }

    Assert.Empty(pageErrors);
  }
}
