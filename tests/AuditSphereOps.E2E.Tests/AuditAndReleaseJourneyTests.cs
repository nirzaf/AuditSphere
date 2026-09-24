using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditWorkflow")]
public sealed class AuditAndReleaseJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-COMPLETION-STALE-ROUTE-01")]
  public async Task CompletionClearsPriorEngagementWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-COMPLETION-STALE-ROUTE-01");
    var (packageId, _) = await FinancialArtifactJourneyTests.CreatePackageAsync(host);
    var unauthorizedEngagementId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new Engagement
      {
        Id = unauthorizedEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/completion"));
    await page.GetByText(packageId.ToString(), new() { Exact = true }).WaitForAsync();
    await connected;
    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{unauthorizedEngagementId:D}/completion");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Engagement unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(packageId.ToString(), body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-AUDIT-FIELDWORK-STALE-ROUTE-01")]
  public async Task AuditFieldworkClearsPriorEngagementWhenRouteChangesInPlace()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-AUDIT-FIELDWORK-STALE-ROUTE-01");
    var unauthorizedEngagementId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.Engagements.Add(new Engagement
      {
        Id = unauthorizedEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-fieldwork"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Controlled Audit Fieldwork" }).WaitForAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Publish and adopt 2026.1" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await page.GetByRole(AriaRole.Button, new() { Name = "Publish and adopt 2026.1" }).ClickAsync();
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1")).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("165", new() { Exact = true })).ToBeVisibleAsync();

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{unauthorizedEngagementId:D}/audit-fieldwork");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access blocked" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("AUDIT-WORKING-PROCESS", body);
    Assert.DoesNotContain("165", body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-fieldwork");
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1")).ToBeVisibleAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-009-AGGREGATE-DIFFERENCES-UI-01")]
  public async Task AuditFieldworkRecordsHumanAggregateConclusionBoundToDifferenceSchedule()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-009-AGGREGATE-DIFFERENCES-UI-01");
    var assessmentId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.MaterialityAssessments.Add(new MaterialityAssessment
      {
        Id = assessmentId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id,
        BenchmarkSource = "Total assets", BenchmarkVersion = "AFS-v1", Rationale = "Synthetic E2E fixture",
        BenchmarkAmount = 1_000_000m, RateApplied = 0.05m, OverallMateriality = 50_000m,
        PerformanceMateriality = 37_500m, ClearlyTrivialThreshold = 2_500m, Status = MaterialityStatuses.Draft,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.MaterialityApprovals.Add(new MaterialityApproval
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, MaterialityAssessmentId = assessmentId,
        ApprovedByUserId = host.Fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow
      });
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, AccountArea = "Revenue", DifferenceType = "KNOWN",
        Description = "Synthetic unadjusted cut-off difference", Amount = 12_000m, Currency = "QAR",
        MaterialityReference = "AFS-v1", QualitativeConcerns = "Synthetic only", Status = AuditDifferenceStatuses.Evaluated,
        CreatedByUserId = host.Fixture.Staff.Id, EvaluatedByUserId = host.Fixture.Reviewer.Id,
        Evaluation = "Synthetic individual evaluation", CreatedAt = DateTimeOffset.UtcNow, EvaluatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-fieldwork"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Controlled Audit Fieldwork" }).WaitForAsync();
    await connected;
    await page.GetByRole(AriaRole.Heading, new() { Name = "Aggregate differences and reporting assessment" }).WaitForAsync();
    await page.GetByLabel("Required: professional aggregate conclusion and reporting impact")
      .FillAsync("Human assessment: evaluate unadjusted QAR amounts and qualitative factors; reporting impact remains subject to partner judgment.");
    await page.GetByRole(AriaRole.Button, new() { Name = "Record aggregate conclusion" }).ClickAsync();
    await Assertions.Expect(page.GetByText("Human assessment: evaluate unadjusted QAR amounts and qualitative factors; reporting impact remains subject to partner judgment.", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("SUBMITTED", new() { Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-04")]
  public async Task AuditProgramAndReviewedFieldworkSurviveReconnectAndFreezeWorkpaper()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-04");
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/audit-fieldwork"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Controlled Audit Fieldwork" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await page.GetByRole(AriaRole.Button, new() { Name = "Publish and adopt 2026.1" }).ClickAsync();
    await Assertions.Expect(page.GetByText("AUDIT-WORKING-PROCESS v2026.1")).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("165", new() { Exact = true })).ToBeVisibleAsync();

    Guid procedureId;
    string sourceProcedureId;
    await using (var db = host.CreateDbContext())
    {
      var procedure = await db.AuditProcedures.AsNoTracking()
        .Where(x => x.EngagementId == host.Fixture.EngagementId)
        .OrderBy(x => x.SourceSectionNumber).ThenBy(x => x.SourceProcedureId).FirstAsync();
      procedureId = procedure.Id;
      sourceProcedureId = procedure.SourceProcedureId;
    }
    var procedureRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = sourceProcedureId });
    await procedureRow.GetByRole(AriaRole.Button, new() { Name = "Applicable" }).ClickAsync();
    await Assertions.Expect(procedureRow).ToContainTextAsync("APPLICABLE");

    Guid scheduleId;
    Guid selectionId;
    Guid itemTestId;
    Guid findingId;
    Guid workpaperId;
    var staff = PbcSeed.Actor(host.Fixture.Staff, "Staff");
    var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "Reviewer");
    await using (var db = host.CreateDbContext())
    {
      var sourceHash = Hashing.Sha256Hex("synthetic-audit-source-schedule");
      var schedule = await AuditFieldworkService.CreateScheduleAsync(db, staff, new CreateScheduleRequest(
        host.Fixture.EngagementId, "SYNTHETIC_GL", "TEST-ENTITY", "E2E-SOURCE-001", null,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "DEBIT_MINUS_CREDIT", sourceHash, 125m,
        [new ScheduleRowInput("ROW-001", 1, "1000", "Synthetic cash item", 125m, "QAR",
          new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 30), null, null, "{}")]));
      Assert.True(schedule.Succeeded, schedule.Message);
      scheduleId = schedule.Value!.ScheduleId;
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
        new ReviewScheduleRequest(scheduleId, "Complete synthetic source schedule", true))).Succeeded);

      var selection = await AuditFieldworkService.CreateSelectionAsync(db, staff, new CreateSelectionRequest(
        host.Fixture.EngagementId, procedureId, scheduleId, null, "TARGETED", "Synthetic high-value selection",
        [new SelectionItemInput("ROW-001", 125m, "QAR", "Synthetic test item")]));
      Assert.True(selection.Succeeded, selection.Message);
      selectionId = selection.Value!.SelectionId;
      Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
        new ReviewSelectionRequest(selectionId, AuditSelectionStatuses.Reviewed, "Independent selection review."))).Succeeded);

      var selectedItemId = await db.AuditSelectionItems.AsNoTracking()
        .Where(x => x.SelectionId == selectionId).Select(x => x.Id).SingleAsync();
      var itemTest = await AuditFieldworkService.RecordItemTestAsync(db, staff, new RecordItemTestRequest(
        selectedItemId, "Agreed synthetic row to source evidence.", ["synthetic-evidence-001"],
        AuditItemTestResults.Pass, null, null, null));
      Assert.True(itemTest.Succeeded, itemTest.Message);
      itemTestId = itemTest.Value!.AuditItemTestId;
      Assert.True((await AuditFieldworkService.ReviewItemTestAsync(db, reviewer,
        new ReviewItemTestRequest(itemTestId, AuditItemTestReviewDecisions.Reviewed, "Independent result review."))).Succeeded);

      var finding = await AuditPlanningService.CreateFindingAsync(db, staff, new CreateFindingRequest(
        host.Fixture.EngagementId, "Synthetic cut-off exception", "Synthetic test exception retained for review.",
        false, 125m, null));
      Assert.True(finding.Succeeded, finding.Message);
      findingId = finding.Value!.FindingId;
      var workpaper = await AuditPlanningService.CreateWorkpaperAsync(db, staff, new CreateWorkpaperRequest(
        host.Fixture.EngagementId, "E2E-04", "Synthetic source test", "Test scoped source evidence",
        "E2E-TEMPLATE-v1", procedureId, "Inspect selected source row and resolve exception."));
      Assert.True(workpaper.Succeeded, workpaper.Message);
      workpaperId = workpaper.Value!.WorkpaperId;
      var draft = await AuditPlanningService.SaveWorkpaperDraftAsync(db, staff,
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(),
          "Agreed ROW-001 to the synthetic source schedule.",
          "The selected item agrees; the separately recorded finding remains open."));
      Assert.True(draft.Succeeded, draft.Message);
    }

    var workpaperConnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/audit/workpapers/{workpaperId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper: Synthetic source test" }).WaitForAsync();
    await workpaperConnected;
    await Assertions.Expect(page.Locator(".workpaper-shell p.notice-text[role='status']")).ToContainTextAsync("Saved at");
    Assert.Equal("Agreed ROW-001 to the synthetic source schedule.", await page.GetByLabel("Work performed").InputValueAsync());
    Assert.Equal("The selected item agrees; the separately recorded finding remains open.",
      await page.GetByLabel("Conclusion").InputValueAsync());
    await using (var verifyDraft = host.CreateDbContext())
    {
      var saved = await verifyDraft.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == workpaperId);
      Assert.Equal("Agreed ROW-001 to the synthetic source schedule.", saved.WorkPerformed);
      Assert.Equal("The selected item agrees; the separately recorded finding remains open.", saved.Conclusion);
    }

    var reconnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.EvaluateAsync("localStorage.clear()");
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper: Synthetic source test" }).WaitForAsync();
    await reconnected;
    Assert.Equal("Agreed ROW-001 to the synthetic source schedule.", await page.GetByLabel("Work performed").InputValueAsync());
    Assert.Equal("The selected item agrees; the separately recorded finding remains open.",
      await page.GetByLabel("Conclusion").InputValueAsync());
    await page.GetByRole(AriaRole.Button, new() { Name = "Submit for review" }).ClickAsync();
    await Assertions.Expect(page.GetByText("The submission was recorded.")).ToBeVisibleAsync();

    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/findings/{findingId:D}"));
    await Assertions.Expect(page.GetByText("Synthetic cut-off exception")).ToBeVisibleAsync();
    await using var verify = host.CreateDbContext();
    Assert.Equal(AuditScheduleStatuses.Approved,
      await verify.AuditSchedules.Where(x => x.Id == scheduleId).Select(x => x.Status).SingleAsync());
    Assert.Equal(AuditSelectionStatuses.Reviewed,
      await verify.AuditSelections.Where(x => x.Id == selectionId).Select(x => x.Status).SingleAsync());
    Assert.Single(await verify.AuditItemTestReviews.Where(x => x.AuditItemTestId == itemTestId).ToListAsync());
    Assert.Single(await verify.WorkpaperSubmissions.Where(x => x.WorkpaperId == workpaperId).ToListAsync());
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("Category", "ReleaseAndRecords")]
  [Trait("CaseId", "PROP-E2E-09")]
  public async Task ExpiredProtectionBlocksReleaseAndIsNeverPresentedAsVerified()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-09",
      requireProtectionAttestation: true);
    var artifact = "synthetic-release-artifact"u8.ToArray();
    var digest = Hashing.Sha256Hex(artifact);
    var workpaperId = Guid.NewGuid();
    var siblingEngagementId = Guid.NewGuid();
    var siblingStaff = PbcSeed.User(host.Fixture.FirmId, "Staff");
    Guid candidateId;
    var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "Reviewer");
    var partner = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
    await using (var db = host.CreateDbContext())
    {
      db.Workpapers.Add(new Workpaper
      {
        Id = workpaperId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ActorId = host.Fixture.Staff.Id, Index = "R-E2E-09",
        Title = "Synthetic release candidate", Objective = "Exercise fail-closed release gates",
        TemplateVersion = "SYNTHETIC-v1", Procedure = "Synthetic procedure", Status = WorkpaperStatuses.Working,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(siblingStaff);
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, siblingStaff, "Staff",
        host.Fixture.ClientId, siblingEngagementId));
      await db.SaveChangesAsync();
      var approval = await ApprovalService.CreateAsync(db, reviewer,
        new CreateApprovalRequest("WORKPAPER", workpaperId, 1, 1, 1, digest));
      Assert.True(approval.Succeeded, approval.Message);
      var candidate = await ReleaseService.CreateCandidateAsync(db, partner,
        new CreateReleaseCandidateRequest(approval.Value!, "WORKPAPER", workpaperId, 1, 1, 1, digest));
      Assert.True(candidate.Succeeded, candidate.Message);
      candidateId = candidate.Value;
      var checkpointStore = new LocalAppendOnlyCheckpointStore(Path.Combine(host.RunRoot, "checkpoints"));
      var checkpoint = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, checkpointStore, partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "synthetic-local-release-checkpoint", digest, artifact));
      Assert.True(checkpoint.Succeeded, checkpoint.Message);
      db.ProtectionAttestations.Add(new ProtectionAttestation
      {
        Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ArtifactId = workpaperId, ArtifactHash = digest,
        Binding = "synthetic://audit-sphere-test/records", ProfileId = "AUDITSPHERE-SYNTHETIC-RECORD",
        ProfileVersion = 1, ObservedState = "PROTECTED", VerificationTime = DateTimeOffset.UtcNow.AddDays(-2),
        Verifier = "synthetic-fixture", ExpiryTime = DateTimeOffset.UtcNow.AddMinutes(-1),
        RecheckRule = "TEST_ONLY", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
      });
      await db.SaveChangesAsync();
    }

    var expired = await ReleaseService.IssueAsync(host.CreateDbContext(), partner,
      new IssueReleaseRequest(candidateId, 1, digest, "synthetic-expired-release"),
      new ReleaseSafetyOptions { RequireExternalCheckpointBeforeDelivery = true, RequireProtectionAttestation = true });
    Assert.False(expired.Succeeded);
    Assert.Contains("Protection attestation has expired", expired.Message);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    var siblingStaffUrl = await host.StartWebForIdentityAsync(siblingStaff);
    await using (var siblingContext = await browser.NewContextAsync())
    {
      var siblingPage = await siblingContext.NewPageAsync();
      var siblingConnected = WaitForCircuitConnectionAsync(siblingPage, []);
      await siblingPage.GotoAsync(SignInUrl(siblingStaffUrl, $"/app/releases/{candidateId:D}"));
      await siblingPage.GetByRole(AriaRole.Heading, new() { Name = "Candidate unavailable" }).WaitForAsync();
      await siblingConnected;
      var siblingBody = await siblingPage.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(candidateId.ToString("D"), siblingBody, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain(digest, siblingBody, StringComparison.Ordinal);
      Assert.DoesNotContain("AUDITSPHERE-SYNTHETIC-RECORD", siblingBody, StringComparison.Ordinal);
      Assert.DoesNotContain("synthetic-local-release-checkpoint", siblingBody, StringComparison.Ordinal);
    }

    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var connected = WaitForCircuitConnectionAsync(page, []);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/releases/{candidateId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Release candidate" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains("expired — Profile AUDITSPHERE-SYNTHETIC-RECORD", body);
    Assert.Contains("No live Microsoft records action is observed", body);
    Assert.DoesNotContain("verified — Profile AUDITSPHERE-SYNTHETIC-RECORD", body);
    Assert.False(await page.GetByRole(AriaRole.Button, new() { Name = "Issue release" }).IsEnabledAsync());
    await using var verify = host.CreateDbContext();
    Assert.Empty(await verify.Releases.AsNoTracking().Where(x => x.ReleaseCandidateId == candidateId).ToListAsync());
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";

  private static Task WaitForCircuitConnectionAsync(IPage page, List<string> diagnostics)
  {
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    page.Console += (_, message) =>
    {
      diagnostics.Add($"console/{message.Type}: {message.Text}");
      if (message.Text.Contains("WebSocket connected to ws://", StringComparison.Ordinal))
        connected.TrySetResult();
    };
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    return connected.Task.WaitAsync(TimeSpan.FromSeconds(15));
  }
}
