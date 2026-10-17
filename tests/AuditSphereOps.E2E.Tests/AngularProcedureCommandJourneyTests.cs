using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuditFieldwork")]
public sealed class AngularProcedureCommandJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-AWP-PROCEDURE-COMMAND-RECOVERY-01")]
  public async Task CashAndBankProcedureRecoversExactSubmissionAndIndependentReviewerCanReturnIt()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-AWP-PROCEDURE-COMMAND-RECOVERY-01");
    var f = host.Fixture;
    var preparer = PbcSeed.Actor(f.Staff, "Partner");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Partner");
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Partner"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      await db.SaveChangesAsync();

      var publication = await AuditProgramTestHelpers.PublishAsync(db, preparer,
        new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(publication.Succeeded, publication.Message);
      await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId, preparer, reviewer);
    }

    var angular = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    };
    var origin = await host.StartApiForIdentityAsync(f.Staff, angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var preparerContext = await browser.NewContextAsync();
    var preparerPage = await preparerContext.NewPageAsync();
    var pageErrors = new List<string>();
    preparerPage.PageError += (_, error) => pageErrors.Add(error);
    var path = $"/app/engagements/{f.EngagementId:D}/audit-fieldwork";
    await preparerPage.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(preparerPage.GetByRole(AriaRole.Button,
      new() { Name = "Adopt published programme 2026.1", Exact = true })).ToBeVisibleAsync();
    await preparerPage.GetByRole(AriaRole.Button,
      new() { Name = "Adopt published programme 2026.1", Exact = true }).ClickAsync();
    await Assertions.Expect(preparerPage.GetByText("AUDIT-WORKING-PROCESS v2026.1", new() { Exact = true })).ToBeVisibleAsync();

    var section = preparerPage.Locator("select[name='section']");
    var cashBankSection = section.Locator("option").Filter(new() { HasText = "Cash & Bank" });
    await Assertions.Expect(cashBankSection).ToHaveCountAsync(1);
    await section.SelectOptionAsync(new SelectOptionValue { Label = await cashBankSection.InnerTextAsync() });
    var procedureRow = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "AWP-02-01" });
    await Assertions.Expect(procedureRow).ToContainTextAsync("Cash & Bank");
    await procedureRow.GetByRole(AriaRole.Button, new() { Name = "Applicable", Exact = true }).ClickAsync();
    await Assertions.Expect(procedureRow).ToContainTextAsync(AuditApplicabilityStatuses.Applicable);
    await procedureRow.GetByRole(AriaRole.Button, new() { Name = "Open procedure", Exact = true }).ClickAsync();

    var execution = preparerPage.GetByRole(AriaRole.Region, new() { Name = "Procedure execution and review", Exact = true });
    await Assertions.Expect(execution).ToContainTextAsync("AWP-02-01");
    await execution.GetByLabel("Work performed", new() { Exact = true })
      .FillAsync("Agreed the synthetic year-end bank reconciliation to the listed bank accounts.");
    await execution.GetByLabel("Conclusion", new() { Exact = true })
      .FillAsync("The synthetic reconciliation agrees to the supporting statement.");
    await execution.GetByLabel("Manual reference", new() { Exact = true }).FillAsync("BANK-REC-SYNTHETIC-01");
    await execution.GetByLabel("What was observed", new() { Exact = true })
      .FillAsync("The closing balance and reconciling items agree to the synthetic schedule.");
    await execution.GetByRole(AriaRole.Button, new() { Name = "Preview exact submission", Exact = true }).ClickAsync();
    var preview = execution.GetByRole(AriaRole.Region, new() { Name = "Exact command preview", Exact = true });
    await Assertions.Expect(preview).ToContainTextAsync("Submit this exact work and evidence snapshot.");

    Guid requestId = Guid.Empty;
    string? requestHash = null;
    var actionUrl = $"**/api/ui/procedures/*/commands/actions";
    await preparerPage.RouteAsync(actionUrl, async route =>
    {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      using var body = JsonDocument.Parse(route.Request.PostData!);
      requestId = body.RootElement.GetProperty("requestId").GetGuid();
      requestHash = body.RootElement.GetProperty("expectedRequestHash").GetString();
      var committed = await route.FetchAsync();
      Assert.Equal(200, committed.Status);
      await route.AbortAsync("failed");
    });
    await preview.GetByLabel("I reviewed this exact procedure, work, evidence and basis.", new() { Exact = true }).CheckAsync();
    await preview.GetByRole(AriaRole.Button, new() { Name = "Record exact outcome", Exact = true }).ClickAsync();
    var recover = execution.GetByRole(AriaRole.Button, new() { Name = "Check original outcome", Exact = true });
    await Assertions.Expect(recover).ToBeVisibleAsync();
    Assert.NotEqual(Guid.Empty, requestId);
    Assert.Matches("^[0-9a-f]{64}$", requestHash!);
    await recover.ClickAsync();
    await Assertions.Expect(execution.Locator("p[role='status']").Filter(new() { HasText = "Recorded SUBMIT_RESULT" }))
      .ToBeVisibleAsync();
    await preparerPage.UnrouteAsync(actionUrl);

    Guid procedureId;
    await using (var db = host.CreateDbContext())
    {
      procedureId = await db.AuditProcedures.AsNoTracking().Where(x => x.EngagementId == f.EngagementId &&
          x.SourceProcedureId == "AWP-02-01").Select(x => x.Id).SingleAsync();
    }

    var reviewerOrigin = await host.StartApiForIdentityAsync(f.Reviewer, angular);
    await using var reviewerContext = await browser.NewContextAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    reviewerPage.PageError += (_, error) => pageErrors.Add(error);
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    var reviewerRow = reviewerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "AWP-02-01" });
    await reviewerRow.GetByRole(AriaRole.Button, new() { Name = "Open procedure", Exact = true }).ClickAsync();
    var review = reviewerPage.GetByRole(AriaRole.Region, new() { Name = "Procedure execution and review", Exact = true });
    await Assertions.Expect(review).ToContainTextAsync("Frozen submitted result");
    await review.GetByLabel("Decision", new() { Exact = true }).SelectOptionAsync("CHANGES_REQUIRED");
    await review.GetByLabel("Review comment (required when returning)", new() { Exact = true })
      .FillAsync("Return the reconciliation conclusion with an explicit statement-level tie-out.");
    await review.GetByRole(AriaRole.Button, new() { Name = "Preview exact review", Exact = true }).ClickAsync();
    var reviewPreview = review.GetByRole(AriaRole.Region, new() { Name = "Exact command preview", Exact = true });
    await reviewPreview.GetByLabel("I reviewed this exact procedure, work, evidence and basis.", new() { Exact = true }).CheckAsync();
    await reviewPreview.GetByRole(AriaRole.Button, new() { Name = "Record exact outcome", Exact = true }).ClickAsync();
    await Assertions.Expect(review.Locator("p[role='status']").Filter(new() { HasText = "Recorded REVIEW_RESULT" }))
      .ToBeVisibleAsync();

    await preparerPage.GotoAsync(origin + path);
    var preparerRowAfterReturn = preparerPage.GetByRole(AriaRole.Row).Filter(new() { HasText = "AWP-02-01" });
    await preparerRowAfterReturn.GetByRole(AriaRole.Button, new() { Name = "Open procedure", Exact = true }).ClickAsync();
    var returned = preparerPage.GetByRole(AriaRole.Region, new() { Name = "Procedure execution and review", Exact = true });
    await Assertions.Expect(returned.GetByLabel("Work performed", new() { Exact = true })).ToBeVisibleAsync();
    await returned.GetByLabel("Work performed", new() { Exact = true })
      .FillAsync("Reperformed the synthetic bank reconciliation tie-out after independent review.");
    await returned.GetByLabel("Conclusion", new() { Exact = true })
      .FillAsync("The reconciliation is now explicitly tied to the statement balance.");
    await returned.GetByLabel("Manual reference", new() { Exact = true }).FillAsync("BANK-REC-SYNTHETIC-02");
    await returned.GetByLabel("What was observed", new() { Exact = true })
      .FillAsync("The revised schedule and closing statement balance agree.");
    await returned.GetByRole(AriaRole.Button, new() { Name = "Preview exact submission", Exact = true }).ClickAsync();
    var resubmission = returned.GetByRole(AriaRole.Region, new() { Name = "Exact command preview", Exact = true });
    await Assertions.Expect(resubmission.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);

    await using (var proof = host.CreateDbContext())
    {
      var result = await proof.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.AuditProcedureId == procedureId);
      Assert.Equal(AuditProcedureResultStatuses.ChangesRequired, result.Status);
      Assert.Equal(f.Staff.Id, result.PreparedByUserId);
      using var submitted = JsonDocument.Parse(result.StructuredResultJson);
      var evidence = submitted.RootElement.GetProperty(AuditProgramService.EvidenceSnapshotProperty)
        .GetProperty("selections").EnumerateArray().Single();
      Assert.Equal("BANK-REC-SYNTHETIC-01", evidence.GetProperty("manualReference").GetString());
      Assert.Equal("The closing balance and reconciling items agree to the synthetic schedule.",
        evidence.GetProperty("observation").GetString());
      var reviewRecord = await proof.AuditProcedureReviews.AsNoTracking().SingleAsync(x => x.AuditProcedureResultId == result.Id);
      Assert.Equal(AuditProcedureReviewDecisions.ChangesRequired, reviewRecord.Decision);
      Assert.Equal(f.Reviewer.Id, reviewRecord.ReviewerUserId);
      Assert.Equal(requestId, await proof.AuditProcedureCommandReceipts.AsNoTracking()
        .Where(x => x.AuditProcedureId == procedureId && x.Action == ProcedureCommandActions.SubmitResult)
        .Select(x => x.RequestId).SingleAsync());
      Assert.Equal(requestHash, await proof.AuditProcedureCommandReceipts.AsNoTracking()
        .Where(x => x.AuditProcedureId == procedureId && x.Action == ProcedureCommandActions.SubmitResult)
        .Select(x => x.RequestHash).SingleAsync());
      Assert.Equal(2, await proof.AuditProcedureCommandReceipts.CountAsync(x => x.AuditProcedureId == procedureId));
    }
    Assert.Empty(pageErrors);
  }
}
