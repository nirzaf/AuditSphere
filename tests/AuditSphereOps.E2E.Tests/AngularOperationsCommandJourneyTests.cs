using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularOperationsCommandJourneyTests
{
  public static TheoryData<string, bool> Actions => new()
  {
    { "RETRY", false }, { "RETRY", true }, { "CANCEL", false }, { "CANCEL", true },
    { "LIFT_QUARANTINE", false }, { "LIFT_QUARANTINE", true },
    { "APPROVE_RECOVERY_RESTART", false }, { "APPROVE_RECOVERY_RESTART", true }
  };
  [Theory]
  [MemberData(nameof(Actions))]
  [Trait("CaseId", "ANGULAR-OPERATIONS-COMMAND")]
  public async Task ReviewedLocalCommand_RecoversOriginalReceiptWithoutResend(string action, bool dropResponse)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-OPERATIONS-COMMAND");
    var firm = host.Fixture; var administrator = PbcSeed.User(firm.FirmId, "Staff"); var operationId = Guid.NewGuid();
    const string privatePayload = "SYNTHETIC-PRIVATE-OPERATOR-PAYLOAD";
    const string disposition = "Operator deliberately cancelled this synthetic queued work.";
    const string findings = "Operator reconciled the exact synthetic restored recovery session.";
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(administrator); db.RoleGrants.Add(PbcSeed.Grant(firm.FirmId, administrator, "Administrator"));
      db.DurableOperations.Add(new DurableOperation { Id = operationId, FirmId = firm.FirmId, OperationKind = "SYNTHETIC-REVIEWED-OPERATION",
        TargetId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), ExpectedRevision = 9007199254740993L, AttemptToken = 9007199254740993L,
        AttemptCount = 4, IdempotencyKey = Guid.NewGuid().ToString("N"), RequestDigest = new string('b', 64), RequestBytes = Encoding.UTF8.GetBytes(privatePayload),
        Status = action == "CANCEL" ? OperationState.PENDING : OperationState.DEAD_LETTER, CreatedAt = DateTimeOffset.UtcNow, NextAttemptAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      if (action == "LIFT_QUARANTINE")
      {
        var safety = await db.FirmSafetyStates.SingleAsync(x => x.Id == firm.FirmId); safety.OperatingMode = "RECOVERY_QUARANTINE"; await db.SaveChangesAsync();
      }
      if (action == "APPROVE_RECOVERY_RESTART")
      {
        var started = await OperationRecoveryService.BeginRecoverySessionAsync(db, PbcSeed.Actor(administrator, "Administrator"), new("Synthetic restore", 1, "Synthetic operations", "Pending operator reconciliation"));
        Assert.True(started.Succeeded, started.Message);
      }
    }
    var origin = await host.StartApiForIdentityAsync(administrator, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    await page.SetViewportSizeAsync(390, 844);
    var writes = 0; string? requestId = null; string? requestHash = null;
    page.Request += (_, request) => {
      if (request.Method != "POST" || !request.Url.EndsWith("/operations/commands/actions", StringComparison.Ordinal)) return;
      Interlocked.Increment(ref writes); using var json = JsonDocument.Parse(request.PostData!);
      requestId = json.RootElement.GetProperty("requestId").GetString(); requestHash = json.RootElement.GetProperty("expectedRequestHash").GetString();
    };
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Foperations");
    await Assertions.Expect(page.GetByRole(AriaRole.Table, new() { Name = "Latest 50 durable operations", Exact = true })).ToBeVisibleAsync();
    if (action == "RETRY" && !dropResponse) await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/app/operations", "operations-command");
    switch (action)
    {
      case "RETRY": await page.GetByRole(AriaRole.Button, new() { Name = $"Re-arm for retry for SYNTHETIC-REVIEWED-OPERATION, operation {operationId}", Exact = true }).ClickAsync(); break;
      case "CANCEL":
        await page.GetByLabel("Cancellation disposition", new() { Exact = true }).FillAsync(disposition);
        await page.GetByRole(AriaRole.Button, new() { Name = $"Cancel queued work for SYNTHETIC-REVIEWED-OPERATION, operation {operationId}", Exact = true }).ClickAsync(); break;
      case "LIFT_QUARANTINE":
        await page.GetByRole(AriaRole.Button, new() { Name = "Lift recovery quarantine", Exact = true }).ClickAsync(); break;
      default:
        await page.GetByLabel("Reconciliation findings", new() { Exact = true }).FillAsync(findings);
        await page.GetByRole(AriaRole.Button, new() { Name = "Preview recovery restart approval", Exact = true }).ClickAsync(); break;
    }
    var preview = page.GetByRole(AriaRole.Region, new() { Name = "Exact operator command preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync(action); Assert.Equal(0, writes);
    await Assertions.Expect(preview.GetByRole(AriaRole.Heading, new() { Name = "Review operator command", Exact = true })).ToBeFocusedAsync();
    if (action is "RETRY" or "CANCEL") await Assertions.Expect(preview).ToContainTextAsync("9007199254740993");
    Assert.DoesNotContain(privatePayload, await page.Locator("body").InnerTextAsync());
    var confirm = preview.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed operator command", Exact = true }); await Assertions.Expect(confirm).ToBeDisabledAsync();
    await preview.GetByLabel("I reviewed this exact operator command, current state and effect.", new() { Exact = true }).CheckAsync();
    if (dropResponse) await page.RouteAsync("**/operations/commands/actions", async route => { var result = await route.FetchAsync(); Assert.Equal(200, result.Status); await route.AbortAsync(); });
    await confirm.ClickAsync();
    if (dropResponse)
    {
      var recovery = page.GetByRole(AriaRole.Region, new() { Name = "Original operator request recovery", Exact = true });
      await Assertions.Expect(recovery).ToBeVisibleAsync(); await page.UnrouteAsync("**/operations/commands/actions");
      if (action == "RETRY")
      {
        // Advance only the owned synthetic projection to prove lookup does not require the command's old queue head.
        await using var db = host.CreateDbContext(); var operation = await db.DurableOperations.SingleAsync(x => x.Id == operationId);
        operation.Status = OperationState.COMPLETED; operation.AttemptToken++; operation.CompletedAt = DateTimeOffset.UtcNow;
        operation.ResultIdentity = "SYNTHETIC-WORKER-RESULT"; operation.ResultDigest = new string('c', 64); await db.SaveChangesAsync();
      }
      await page.ReloadAsync(); await Assertions.Expect(recovery).ToContainTextAsync(requestId!); await Assertions.Expect(recovery).ToContainTextAsync(requestHash!);
      await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact operator command preview", Exact = true })).ToHaveCountAsync(0);
      var storage = await page.EvaluateAsync<string>("() => JSON.stringify(Object.keys(sessionStorage).filter(k => k.includes('operations-command/')).map(k => JSON.parse(sessionStorage.getItem(k)).value))");
      Assert.Contains(requestId!, storage); Assert.DoesNotContain(disposition, storage); Assert.DoesNotContain(findings, storage); Assert.DoesNotContain(privatePayload, storage);
      await recovery.GetByRole(AriaRole.Button, new() { Name = "Look up original operator receipt", Exact = true }).ClickAsync();
    }
    var receipt = page.GetByRole(AriaRole.Region, new() { Name = "Verified original operator receipt", Exact = true });
    await Assertions.Expect(receipt).ToContainTextAsync(action); await Assertions.Expect(receipt).ToContainTextAsync(requestId!); await Assertions.Expect(receipt).ToContainTextAsync(administrator.Id.ToString("D"));
    if (action == "RETRY") await Assertions.Expect(receipt).ToContainTextAsync("RETRY_WAIT");
    if (action == "CANCEL") await Assertions.Expect(receipt).ToContainTextAsync(disposition);
    if (action == "APPROVE_RECOVERY_RESTART") await Assertions.Expect(receipt).ToContainTextAsync(findings);
    Assert.Equal(1, writes); Assert.Empty(errors); Assert.DoesNotContain(privatePayload, await page.Locator("body").InnerTextAsync());
  }
}
