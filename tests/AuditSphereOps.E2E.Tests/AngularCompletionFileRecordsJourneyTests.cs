using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
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
    ["AngularUi__CanonicalRoutes"] = "true",
    ["ReportSigning__Required"] = "false",
    ["ReportSigning__CertificatePath"] = "",
    ["ReportSigning__PrivateKeyPath"] = ""
  };

  [Fact]
  [Trait("CaseId", "STE-GAP-006-EARLY-LOCK-BROWSER")]
  public async Task PartnerReviewsReadinessLocksScheduledFileAndLifecycleKeepsProviderStateSeparate()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "STE-GAP-006-EARLY-LOCK-BROWSER");
    var f = host.Fixture;
    var partner = PbcSeed.User(f.FirmId, "Staff");
    var associate = PbcSeed.User(f.FirmId, "Staff");
    var senior = PbcSeed.User(f.FirmId, "Staff");
    var secondPartner = PbcSeed.User(f.FirmId, "Staff");
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var now = DateTimeOffset.UtcNow;
    var report = new AuditDeliverable
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      Kind = DeliverableKinds.IndependentAuditorsReport, Version = 1, TemplateVersion = "synthetic-early-lock-e2e",
      InputDigest = new string('c', 64), InputSummaryJson = "{}", FileName = "synthetic-report.pdf",
      ContentType = "application/pdf", Content = [1, 2, 3], ContentSha256 = new string('d', 64),
      CreatedByUserId = partner.Id, CreatedAt = now
    };
    var scope = new CompletionBundleScope(f.FirmId, f.ClientId, f.EngagementId, new()
    {
      ["partner"] = partner, ["associate"] = associate, ["senior"] = senior,
      ["partner2"] = secondPartner, ["manager"] = manager
    });
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(partner, associate, senior, secondPartner, manager);
      await db.SaveChangesAsync();
    }
    await CompletionBundleFixture.ReleasedFinancialPackageAsync(host.DbOptions, scope);

    await using (var db = host.CreateDbContext())
    {
      Assert.True(await db.RoleGrants.AsNoTracking().AnyAsync(x => x.FirmId == f.FirmId &&
        x.UserId == partner.Id && x.Role == "Partner" && x.ClientId == f.ClientId &&
        x.EngagementId == f.EngagementId && x.RevokedAt == null),
        "The released-package fixture must retain the assigned Engagement Partner grant.");
      db.AuditDeliverables.Add(report);
      db.EngagementFileFreezes.Add(new EngagementFileFreeze
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ReportDeliverableId = report.Id, ReportSignedAt = now, DueAt = now.AddDays(FileFreezeService.FreezeDays),
        State = FileFreezeStates.Scheduled, Revision = 1,
        ExternalReadOnly = ExternalReadOnlyStates.BlockedExternal, UpdatedAt = now
      });
      await db.SaveChangesAsync();
    }

    var preparer = PbcSeed.Actor(f.Staff, "Staff");
    GoingConcernAssessmentValue concern;
    SubsequentEventReviewValue subsequentEvent;
    OpeningBalanceVerificationValue openingBalance;
    await using (var db = host.CreateDbContext())
    {
      var preparedConcern = await AuditCompletionService.RecordGoingConcernAssessmentAsync(db, preparer,
        new(f.EngagementId, null, new(2027, 1, 20), new(2028, 1, 20), "Synthetic forecast observations",
          false, true, "QAR", "Synthetic forecast rationale", ["synthetic-forecast-evidence"]));
      Assert.True(preparedConcern.Succeeded, preparedConcern.Message);
      concern = preparedConcern.Value!;

      var preparedEvent = await AuditCompletionService.RecordSubsequentEventAsync(db, preparer,
        new(f.EngagementId, null, new(2026, 12, 31), new(2027, 1, 20), "Synthetic post-period event",
          SubsequentEventClassifications.NonAdjusting, false, true, "Note 27",
          "Synthetic conditions arose after the reporting date.", "QAR", 125.123456m, []));
      Assert.True(preparedEvent.Succeeded, preparedEvent.Message);
      subsequentEvent = preparedEvent.Value!;
    }
    await using (var db = host.CreateDbContext())
    {
      var preparedOpening = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, preparer,
        new(f.EngagementId, null, null, "Synthetic prior statement", new(2026, 1, 1), "QAR",
          1000.123456m, 1000.123456m, true, "Synthetic opening balance comparison",
          ["synthetic-prior-source"], 0));
      Assert.True(preparedOpening.Succeeded, preparedOpening.Message);
      openingBalance = preparedOpening.Value!;
    }

    var origin = await host.StartApiForIdentityAsync(partner, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);

    var engagementRoute = $"/app/engagements/{f.EngagementId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(engagementRoute));
    var workflow = page.GetByRole(AriaRole.Region,
      new() { Name = "Engagement workflow", Exact = true });
    await Assertions.Expect(workflow).ToContainTextAsync("Phase 6 of 7: Report and Delivery");
    await Assertions.Expect(workflow).ToContainTextAsync("Current step: Deliverable Release (stage 9 of 11)");
    await Assertions.Expect(workflow).ToContainTextAsync("Local archive state: SCHEDULED");
    await Assertions.Expect(workflow).ToContainTextAsync("External provider protection: BLOCKED_EXTERNAL");
    await Assertions.Expect(workflow).ToContainTextAsync("Compliance countdown:");
    await Assertions.Expect(workflow).ToContainTextAsync("60 days until the scheduled freeze.");
    await Assertions.Expect(workflow).ToContainTextAsync("final bundle available in the client portal");

    var completionRoute = engagementRoute + "/completion";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(completionRoute));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "File freeze and activity trail", Exact = true })).ToBeVisibleAsync();
    var earlyLock = page.GetByRole(AriaRole.Region,
      new() { Name = "Early compliance lock (STE 4.4.3)", Exact = true });
    await Assertions.Expect(earlyLock.GetByText("Reviewed readiness digest:", new() { Exact = false })).ToBeVisibleAsync();
    await earlyLock.GetByLabel("I confirm the file is complete and I am locking it now as the Engagement Partner.",
      new() { Exact = true }).CheckAsync();
    await earlyLock.GetByLabel("Reason for the early lock", new() { Exact = true })
      .FillAsync("Synthetic browser acceptance: reviewed final release and archive readiness.");
    await earlyLock.GetByRole(AriaRole.Button, new() { Name = "Lock audit file early", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText(
      "The audit file is locked early and is read-only in AuditSphere. Provider protection is recorded separately.",
      new() { Exact = true })).ToBeVisibleAsync();

    await using (var db = host.CreateDbContext())
    {
      var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId);
      Assert.Equal(FileFreezeStates.Frozen, freeze.State);
      Assert.Equal(ExternalReadOnlyStates.BlockedExternal, freeze.ExternalReadOnly);
      var evidence = await db.FileFreezeEarlyLocks.AsNoTracking().SingleAsync(x => x.FreezeId == freeze.Id);
      Assert.Equal(partner.Id, evidence.LockedByUserId);
      Assert.Contains("reviewed final release", evidence.Rationale, StringComparison.Ordinal);
    }

    var reviewer = PbcSeed.Actor(partner, "Partner");
    int priorProfessionalWorkRefusals;
    await using (var db = host.CreateDbContext())
    {
      priorProfessionalWorkRefusals = await db.FrozenAccessAttempts.CountAsync(x => x.FirmId == f.FirmId &&
        x.EngagementId == f.EngagementId && x.ActorUserId == partner.Id &&
        x.Action == "professional work on a frozen engagement file");
      var concernReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "GOING_CONCERN", concern.AssessmentId, concern.Revision);
      Assert.Equal(ErrorCodes.ProtectedState, concernReview.ErrorCode);
      var eventReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "SUBSEQUENT_EVENT", subsequentEvent.ReviewId, subsequentEvent.Revision);
      Assert.Equal(ErrorCodes.ProtectedState, eventReview.ErrorCode);
      var openingReview = await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(db, reviewer,
        new(f.EngagementId, openingBalance.VerificationId, openingBalance.Revision, null));
      Assert.Equal(ErrorCodes.ProtectedState, openingReview.ErrorCode);
    }
    await using (var db = host.CreateDbContext())
    {
      var retainedConcern = await db.GoingConcernAssessments.AsNoTracking().SingleAsync(x => x.Id == concern.AssessmentId);
      Assert.Equal((concern.Revision, (Guid?)null), (retainedConcern.Revision, retainedConcern.ReviewedByUserId));
      var retainedEvent = await db.SubsequentEventReviews.AsNoTracking().SingleAsync(x => x.Id == subsequentEvent.ReviewId);
      Assert.Equal((subsequentEvent.Revision, (Guid?)null), (retainedEvent.Revision, retainedEvent.ReviewedByUserId));
      var retainedOpening = await db.OpeningBalanceVerifications.AsNoTracking()
        .SingleAsync(x => x.Id == openingBalance.VerificationId);
      Assert.Equal((openingBalance.Revision, (Guid?)null), (retainedOpening.Revision, retainedOpening.ReviewedByUserId));
      Assert.Equal(priorProfessionalWorkRefusals + 3, await db.FrozenAccessAttempts.CountAsync(x => x.FirmId == f.FirmId &&
        x.EngagementId == f.EngagementId && x.ActorUserId == partner.Id &&
        x.Action == "professional work on a frozen engagement file"));
    }

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(engagementRoute));
    workflow = page.GetByRole(AriaRole.Region,
      new() { Name = "Engagement workflow", Exact = true });
    await Assertions.Expect(workflow).ToContainTextAsync("Current step: Archived (Read-Only) (stage 11 of 11)");
    await Assertions.Expect(workflow).ToContainTextAsync("Local archive state: FROZEN");
    await Assertions.Expect(workflow).ToContainTextAsync("External provider protection: BLOCKED_EXTERNAL");

    var archiveHandoffRoute = engagementRoute + "/archive";
    var lifecycleAction = workflow.GetByRole(AriaRole.Link, new() { Name = "Open: Archived (Read-Only)", Exact = true });
    await Assertions.Expect(lifecycleAction).ToHaveAttributeAsync("href", archiveHandoffRoute);
    await WaitForPlanningAsync(page);
    await lifecycleAction.ClickAsync();
    await Assertions.Expect(page).ToHaveURLAsync(origin + archiveHandoffRoute);
    await Assertions.Expect(page.GetByText(
      "The engagement must be issued and completed before an archive can be prepared.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Prepare ") })).ToHaveCountAsync(0);

    var archiveId = Guid.NewGuid();
    Assert.NotEqual(f.EngagementId, archiveId);
    const string profileCode = "SYNTH-LINKED-ARCHIVE";
    await using (var db = host.CreateDbContext())
    {
      Assert.Equal("Active", await db.Engagements.Where(x => x.Id == f.EngagementId).Select(x => x.Status).SingleAsync());
      Assert.False(await db.Archives.AnyAsync());
      Assert.False(await db.RecordsProfiles.AnyAsync());
      // An existing read fixture exercises the links without establishing archive creation eligibility.
      db.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ProfileId = profileCode, ProfileVersion = 1, Status = ArchiveStates.AssemblyInProgress, CreatedAt = now
      });
      await db.SaveChangesAsync();
    }

    var archiveRoute = $"/app/records/archives/{archiveId:D}";
    var requestedArchives = new List<string>();
    page.Request += (_, request) =>
    {
      if (request.Method == "GET" && new Uri(request.Url).AbsolutePath.StartsWith("/api/ui/records/archives/", StringComparison.Ordinal))
        requestedArchives.Add(new Uri(request.Url).AbsolutePath);
    };
    foreach (var action in new[] { "Open: Archived (Read-Only)", "Records and archive" })
    {
      var previousRequests = requestedArchives.Count;
      await page.GotoAsync(origin + engagementRoute);
      var link = workflow.GetByRole(AriaRole.Link, new() { Name = action, Exact = true });
      await Assertions.Expect(link).ToHaveAttributeAsync("href", archiveHandoffRoute);
      await WaitForPlanningAsync(page);
      await link.ClickAsync();
      await Assertions.Expect(page).ToHaveURLAsync(origin + archiveHandoffRoute);
      var archive = page.GetByRole(AriaRole.Link, new() { Name = $"Open archive {profileCode} version 1", Exact = true });
      await Assertions.Expect(archive).ToHaveAttributeAsync("href", archiveRoute);
      await archive.ClickAsync();
      await Assertions.Expect(page).ToHaveURLAsync(origin + archiveRoute);
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Archive manifest", Exact = true })).ToBeVisibleAsync();
      Assert.True(requestedArchives.Count > previousRequests, $"{action} must read the archive.");
    }
    var requestsBeforeBookmark = requestedArchives.Count;
    await page.GotoAsync(origin + archiveRoute);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Archive manifest", Exact = true })).ToBeVisibleAsync();
    Assert.True(requestedArchives.Count > requestsBeforeBookmark, "The bookmark must read the archive.");
    Assert.All(requestedArchives, path => Assert.Equal($"/api/ui/records/archives/{archiveId:D}", path));
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-COMPLETION-FILE-RECORDS-01")]
  public async Task FrozenFileSupplementRequestsDoNotReopenArchive()
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
      var signing = page.Locator("[data-report-signing]");
      await Assertions.Expect(signing).ToContainTextAsync("Report signing: signature image only.");
      await Assertions.Expect(signing).ToContainTextAsync("not tamper-evident.");
      return page;
    }

    var requesterPage = await OpenAsAsync(requesterOrigin);
    await Assertions.Expect(requesterPage.Locator("audit-status .status-chip[data-status='FROZEN']")).ToBeVisibleAsync();
    await requesterPage.GetByRole(AriaRole.Textbox, new() { Name = "Reason for supplementary record" })
      .FillAsync("Synthetic evidence correction required before records retention.");
    await requesterPage.GetByRole(AriaRole.Button, new() { Name = "Request supplementary record", Exact = true }).ClickAsync();
    await Assertions.Expect(requesterPage.GetByRole(AriaRole.Region,
      new() { Name = "File freeze and activity trail", Exact = true }))
      .ToContainTextAsync("Synthetic evidence correction required before records retention.");
    var amendmentId = await ReadSingleAmendmentIdAsync(host, f.EngagementId);

    // A Partner cannot approve their own request. The UI reports the safe application message and the persisted
    // amendment remains pending until another Partner acts.
    await requesterPage.GetByRole(AriaRole.Button, new() { Name = "Approve supplement request", Exact = true }).ClickAsync();
    await Assertions.Expect(requesterPage.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Another Partner must approve an amendment you requested.");
    await using (var db = host.CreateDbContext())
    {
      var pending = await db.FileFreezeAmendments.AsNoTracking().SingleAsync(x => x.Id == amendmentId);
      Assert.Null(pending.OpenedAt);
      Assert.Null(pending.ApprovedByUserId);
    }

    var partnerPage = await OpenAsAsync(partnerOrigin);
    await partnerPage.GetByRole(AriaRole.Button, new() { Name = "Approve supplement request", Exact = true }).ClickAsync();
    await Assertions.Expect(partnerPage.Locator("p.command-result[role='status']"))
      .ToContainTextAsync("Supplementary record request approved; the original archive remains read-only.");
    await Assertions.Expect(partnerPage.Locator("audit-status .status-chip[data-status='FROZEN']")).ToBeVisibleAsync();
    await Assertions.Expect(partnerPage.GetByRole(AriaRole.Button, new() { Name = "Lock for editing", Exact = true }))
      .ToBeDisabledAsync();

    var contenderPage = await OpenAsAsync(contenderOrigin);
    await Assertions.Expect(contenderPage.Locator("audit-status .status-chip[data-status='FROZEN']")).ToBeVisibleAsync();

    // Closing the supplementary-record request changes only its evidence state; it never changes the frozen archive.
    await requesterPage.ReloadAsync();
    await requesterPage.GetByRole(AriaRole.Button, new() { Name = "Close supplement request", Exact = true }).ClickAsync();
    await Assertions.Expect(requesterPage.Locator("p.command-result[role='status']"))
      .ToContainTextAsync("Supplementary record request closed; the original archive remains read-only.");
    await Assertions.Expect(requesterPage.Locator("audit-status .status-chip[data-status='FROZEN']")).ToBeVisibleAsync();
    await requesterPage.Locator("select[name='kind']").SelectOptionAsync("FREEZE");
    await Assertions.Expect(requesterPage.GetByRole(AriaRole.Table, new() { Name = "Activity trail", Exact = true }))
      .ToContainTextAsync("Amendment opened: Synthetic evidence correction required before records retention.");

    await using (var db = host.CreateDbContext())
    {
      var closed = await db.FileFreezeAmendments.AsNoTracking().SingleAsync(x => x.Id == amendmentId);
      var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId);
      var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == f.EngagementId);
      Assert.Equal(secondPartner.Id, closed.ApprovedByUserId);
      Assert.NotNull(closed.OpenedAt);
      Assert.NotNull(closed.ClosedAt);
      Assert.Equal(FileFreezeStates.Frozen, freeze.State);
      Assert.True(engagement.ProfessionalWorkBlocked);
      Assert.Empty(await db.DocumentLocks.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
    }

    Assert.Empty(errors);
    foreach (var page in new[] { requesterPage, partnerPage, contenderPage })
      await page.Context.CloseAsync();
  }

  private static Task WaitForPlanningAsync(IPage page) =>
    // The engagement navigation guard rejects attempts while the deferred planning read is loading.
    Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Engagement planning", Exact = true })
      .GetByRole(AriaRole.Heading, new() { Name = "Engagement team", Exact = true })).ToBeVisibleAsync();

  private static async Task<Guid> ReadSingleAmendmentIdAsync(OwnedHost host, Guid engagementId)
  {
    await using var db = host.CreateDbContext();
    return await db.FileFreezeAmendments.AsNoTracking().Where(x => x.EngagementId == engagementId)
      .Select(x => x.Id).SingleAsync();
  }
}
