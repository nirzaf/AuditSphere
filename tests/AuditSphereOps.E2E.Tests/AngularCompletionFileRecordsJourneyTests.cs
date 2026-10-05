using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditCompletion")]
public sealed class AngularCompletionFileRecordsJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true",
    ["AngularUi__CanonicalRoutes"] = "true"
  };

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-COMPLETION-FILE-RECORDS-01")]
  public async Task FrozenFileAmendmentAndDocumentLocksRequireIndependentAuthorityAndRefreeze()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-COMPLETION-FILE-RECORDS-01");
    var f = host.Fixture;
    var requester = PbcSeed.User(f.FirmId, "Staff");
    requester.DisplayName = "Synthetic amendment requester";
    var secondPartner = PbcSeed.User(f.FirmId, "Staff");
    secondPartner.DisplayName = "Synthetic independent Partner";
    var lockContender = PbcSeed.User(f.FirmId, "Staff");
    lockContender.DisplayName = "Synthetic lock contender";
    var now = DateTimeOffset.UtcNow;
    var reportSignedAt = now.AddDays(-61);
    var dueAt = reportSignedAt.AddDays(FileFreezeService.FreezeDays);
    var report = new AuditDeliverable
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      Kind = DeliverableKinds.IndependentAuditorsReport, Version = 1, TemplateVersion = "synthetic-e2e",
      InputDigest = new string('a', 64), InputSummaryJson = "{}", FileName = "synthetic-signed-report.pdf",
      ContentType = "application/pdf", Content = [1], ContentSha256 = new string('b', 64),
      CreatedByUserId = requester.Id, CreatedAt = reportSignedAt
    };
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(requester, secondPartner, lockContender);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, requester, "Partner", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, secondPartner, "Partner", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, lockContender, "Staff", f.ClientId, f.EngagementId));
      db.AuditDeliverables.Add(report);
      db.EngagementFileFreezes.Add(new EngagementFileFreeze
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ReportDeliverableId = report.Id, ReportSignedAt = reportSignedAt, DueAt = dueAt,
        State = FileFreezeStates.Frozen, Revision = 1, FrozenAt = dueAt,
        ExternalReadOnly = ExternalReadOnlyStates.BlockedExternal, UpdatedAt = now
      });
      await db.Engagements.Where(x => x.Id == f.EngagementId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProfessionalWorkBlocked, true));
      await db.SaveChangesAsync();
    }

    var requesterOrigin = await host.StartApiForIdentityAsync(requester, Angular);
    var partnerOrigin = await host.StartApiForIdentityAsync(secondPartner, Angular);
    var contenderOrigin = await host.StartApiForIdentityAsync(lockContender, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var errors = new List<string>();
    var route = $"/app/engagements/{f.EngagementId:D}/completion";

    async Task<IPage> OpenAsAsync(string origin)
    {
      var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      page.PageError += (_, error) => errors.Add(error);
      await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
      await Assertions.Expect(page.GetByRole(AriaRole.Heading,
        new() { Name = "Engagement completion checklist", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading,
        new() { Name = "File freeze and activity trail", Exact = true })).ToBeVisibleAsync();
      return page;
    }

    var requesterPage = await OpenAsAsync(requesterOrigin);
    await Assertions.Expect(requesterPage.Locator("audit-status .status-chip[data-status='FROZEN']")).ToBeVisibleAsync();
    await requesterPage.GetByRole(AriaRole.Textbox, new() { Name = "Reason for amendment" })
      .FillAsync("Synthetic evidence correction required before records retention.");
    await requesterPage.GetByRole(AriaRole.Button, new() { Name = "Request amendment", Exact = true }).ClickAsync();
    await Assertions.Expect(requesterPage.GetByRole(AriaRole.Region,
      new() { Name = "File freeze and activity trail", Exact = true }))
      .ToContainTextAsync("Synthetic evidence correction required before records retention.");
    var amendmentId = await ReadSingleAmendmentIdAsync(host, f.EngagementId);

    // A Partner cannot approve their own request. The UI reports the safe application message and the persisted
    // amendment remains pending until another Partner acts.
    await requesterPage.GetByRole(AriaRole.Button, new() { Name = "Approve (another Partner)", Exact = true }).ClickAsync();
    await Assertions.Expect(requesterPage.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Another Partner must approve an amendment you requested.");
    await using (var db = host.CreateDbContext())
    {
      var pending = await db.FileFreezeAmendments.AsNoTracking().SingleAsync(x => x.Id == amendmentId);
      Assert.Null(pending.OpenedAt);
      Assert.Null(pending.ApprovedByUserId);
    }

    var partnerPage = await OpenAsAsync(partnerOrigin);
    await partnerPage.GetByRole(AriaRole.Button, new() { Name = "Approve (another Partner)", Exact = true }).ClickAsync();
    await Assertions.Expect(partnerPage.Locator("p.command-result[role='status']"))
      .ToContainTextAsync("Amendment approved; the file is open for the documented change.");
    await Assertions.Expect(partnerPage.Locator("audit-status .status-chip[data-status='AMENDMENT_OPEN']")).ToBeVisibleAsync();

    await partnerPage.GetByRole(AriaRole.Textbox, new() { Name = "Document" }).FillAsync("SYN-AMENDMENT-WORKPAPER-01");
    await partnerPage.GetByRole(AriaRole.Button, new() { Name = "Lock for editing", Exact = true }).ClickAsync();
    await Assertions.Expect(partnerPage.GetByText("Document locked for you.", new() { Exact = true })).ToBeVisibleAsync();

    var contenderPage = await OpenAsAsync(contenderOrigin);
    await contenderPage.GetByRole(AriaRole.Textbox, new() { Name = "Document" }).FillAsync("SYN-AMENDMENT-WORKPAPER-01");
    await contenderPage.GetByRole(AriaRole.Button, new() { Name = "Lock for editing", Exact = true }).ClickAsync();
    await Assertions.Expect(contenderPage.Locator("p.command-result[role='alert']")).ToContainTextAsync("Another user holds the lock on this document.");
    await contenderPage.GetByRole(AriaRole.Button, new() { Name = "Release lock on SYN-AMENDMENT-WORKPAPER-01", Exact = true }).ClickAsync();
    await Assertions.Expect(contenderPage.Locator("p.command-result[role='alert']")).ToContainTextAsync("Access denied.");
    await using (var db = host.CreateDbContext())
    {
      var activeLock = await db.DocumentLocks.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId && x.ReleasedAt == null);
      Assert.Equal(secondPartner.Id, activeLock.LockedByUserId);
      Assert.Null(activeLock.ReleasedAt);
    }

    // The assigned Partner can break the lock, then close the amendment. Closing restores the application freeze.
    await requesterPage.ReloadAsync();
    await requesterPage.GetByRole(AriaRole.Button,
      new() { Name = "Release lock on SYN-AMENDMENT-WORKPAPER-01", Exact = true }).ClickAsync();
    await Assertions.Expect(requesterPage.Locator("p.command-result[role='status']")).ToContainTextAsync("Lock released.");
    await requesterPage.GetByRole(AriaRole.Button, new() { Name = "Close and re-freeze", Exact = true }).ClickAsync();
    await Assertions.Expect(requesterPage.Locator("p.command-result[role='status']")).ToContainTextAsync("Amendment closed; the file is frozen again.");
    await Assertions.Expect(requesterPage.Locator("audit-status .status-chip[data-status='FROZEN']")).ToBeVisibleAsync();
    await requesterPage.Locator("select[name='kind']").SelectOptionAsync("FREEZE");
    await Assertions.Expect(requesterPage.GetByRole(AriaRole.Table, new() { Name = "Activity trail", Exact = true }))
      .ToContainTextAsync("Amendment opened: Synthetic evidence correction required before records retention.");

    await using (var db = host.CreateDbContext())
    {
      var closed = await db.FileFreezeAmendments.AsNoTracking().SingleAsync(x => x.Id == amendmentId);
      var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId);
      var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == f.EngagementId);
      var lockRow = await db.DocumentLocks.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId);
      Assert.Equal(secondPartner.Id, closed.ApprovedByUserId);
      Assert.NotNull(closed.OpenedAt);
      Assert.NotNull(closed.ClosedAt);
      Assert.Equal(FileFreezeStates.Frozen, freeze.State);
      Assert.True(engagement.ProfessionalWorkBlocked);
      Assert.Equal(requester.Id, lockRow.ReleasedByUserId);
      Assert.NotNull(lockRow.ReleasedAt);
    }

    Assert.Empty(errors);
    foreach (var page in new[] { requesterPage, partnerPage, contenderPage })
      await page.Context.CloseAsync();
  }

  private static async Task<Guid> ReadSingleAmendmentIdAsync(OwnedHost host, Guid engagementId)
  {
    await using var db = host.CreateDbContext();
    return await db.FileFreezeAmendments.AsNoTracking().Where(x => x.EngagementId == engagementId)
      .Select(x => x.Id).SingleAsync();
  }
}
