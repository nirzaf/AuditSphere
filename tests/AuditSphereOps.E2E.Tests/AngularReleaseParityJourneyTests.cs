using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "ReleaseAndRecords")]
public sealed class AngularReleaseParityJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-RELEASE-01")]
  public async Task ExpiredProtectionBlocksIssueAndReleaseDetailsClearOnScopeChangeAndRevocation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-RELEASE-01", requireProtectionAttestation: true);
    var f = host.Fixture;
    var artifact = "synthetic-angular-release-artifact"u8.ToArray();
    var digest = Hashing.Sha256Hex(artifact);
    var workpaperId = Guid.NewGuid();
    var siblingEngagementId = Guid.NewGuid();
    var siblingStaff = PbcSeed.User(f.FirmId, "Staff");
    Guid candidateId;
    var reviewer = PbcSeed.Actor(f.Reviewer, "Reviewer");
    var partner = PbcSeed.Actor(f.Admin, "Administrator");

    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId));
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(siblingStaff);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, siblingStaff, "Staff", f.ClientId, siblingEngagementId));
      db.Workpapers.Add(new Workpaper
      {
        Id = workpaperId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ActorId = f.Staff.Id, Index = "R-ANGULAR-RELEASE", Title = "Synthetic Angular release candidate",
        Objective = "Exercise Angular release evidence gates", TemplateVersion = "SYNTHETIC-v1",
        Procedure = "Synthetic procedure", Status = WorkpaperStatuses.Working, CreatedAt = DateTimeOffset.UtcNow
      });
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
        new RecordReleaseCheckpointRequest(candidateId, 1, "synthetic-angular-release-checkpoint", digest, artifact));
      Assert.True(checkpoint.Succeeded, checkpoint.Message);
      db.ProtectionAttestations.Add(new ProtectionAttestation
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ArtifactId = workpaperId, ArtifactHash = digest,
        Binding = "synthetic://audit-sphere-test/records", ProfileId = "AUDITSPHERE-SYNTHETIC-RECORD",
        ProfileVersion = 1, ObservedState = "PROTECTED", VerificationTime = DateTimeOffset.UtcNow.AddDays(-2),
        Verifier = "synthetic-fixture", ExpiryTime = DateTimeOffset.UtcNow.AddMinutes(-1),
        RecheckRule = "TEST_ONLY", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
      });
      await db.SaveChangesAsync();
    }

    await using (var db = host.CreateDbContext())
    {
      var blocked = await ReleaseService.IssueAsync(db, partner,
        new IssueReleaseRequest(candidateId, 1, digest, "synthetic-angular-expired-release"),
        new ReleaseSafetyOptions { RequireExternalCheckpointBeforeDelivery = true, RequireProtectionAttestation = true });
      Assert.False(blocked.Succeeded);
      Assert.Contains("Protection attestation has expired", blocked.Message, StringComparison.Ordinal);
    }
    await using (var db = host.CreateDbContext())
      Assert.Empty(await db.Releases.AsNoTracking().Where(x => x.ReleaseCandidateId == candidateId).ToListAsync());

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var siblingOrigin = await host.StartApiForIdentityAsync(siblingStaff);
    await using (var siblingContext = await browser.NewContextAsync())
    {
      var siblingPage = await siblingContext.NewPageAsync();
      var path = $"/app/releases/{candidateId:D}";
      await siblingPage.GotoAsync(siblingOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
      await Assertions.Expect(siblingPage.GetByRole(AriaRole.Alert))
        .ToContainTextAsync("The candidate is not available in the current firm scope.");
      var siblingBody = await siblingPage.Locator("main").InnerTextAsync();
      Assert.DoesNotContain(digest, siblingBody, StringComparison.Ordinal);
      Assert.DoesNotContain(candidateId.ToString("D"), siblingBody, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("AUDITSPHERE-SYNTHETIC-RECORD", siblingBody, StringComparison.Ordinal);
      Assert.DoesNotContain("synthetic-angular-release-checkpoint", siblingBody, StringComparison.Ordinal);
    }

    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    var route = $"/app/releases/{candidateId:D}";
    await page.GotoAsync(host.StaffUrl + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Release candidate", Exact = true }))
      .ToBeVisibleAsync();
    await page.GetByText(digest, new() { Exact = true }).WaitForAsync(new() { Timeout = 15000 });
    var body = await page.Locator("main").InnerTextAsync();
    Assert.Contains("expired — Profile AUDITSPHERE-SYNTHETIC-RECORD", body, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("No external records-provider acceptance is claimed", body, StringComparison.Ordinal);
    Assert.DoesNotContain("verified — Profile AUDITSPHERE-SYNTHETIC-RECORD", body, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("Required preflight evidence is incomplete.", body, StringComparison.Ordinal);
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Issue release", Exact = true }))
      .ToBeDisabledAsync();

    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync("expected => document.documentElement.clientWidth === expected", width);
      var documentWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
      Assert.True(documentWidth <= width + 1, $"Release page is {documentWidth}px wide at {width}px.");
    }
    var releaseKey = page.GetByLabel("Authorized release key", new() { Exact = true });
    var portfolioLink = page.GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb", Exact = true })
      .GetByRole(AriaRole.Link, new() { Name = "Portfolio", Exact = true });
    await portfolioLink.FocusAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(releaseKey).ToBeFocusedAsync();
    await Assertions.Expect(releaseKey).ToHaveCSSAsync("outline-style", "solid");

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__releaseParityToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/releases/{Guid.NewGuid():D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("The candidate is not available in the current firm scope.");
    body = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(digest, body, StringComparison.Ordinal);
    Assert.DoesNotContain("AUDITSPHERE-SYNTHETIC-RECORD", body, StringComparison.Ordinal);
    Assert.DoesNotContain("synthetic-angular-release-checkpoint", body, StringComparison.Ordinal);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("() => window.__releaseParityToken"));

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }", route);
    await page.GetByText(digest, new() { Exact = true }).WaitForAsync(new() { Timeout = 15000 });
    await using (var db = host.CreateDbContext())
    {
      var grants = await db.RoleGrants.Where(x => x.FirmId == f.FirmId && x.UserId == f.Staff.Id &&
        x.RevokedAt == null && (x.Role == "Partner" || x.Role == "Staff")).ToListAsync();
      Assert.NotEmpty(grants);
      foreach (var grant in grants)
      {
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, partner,
          new RevokeRoleGrantRequest(grant.Id));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
    }
    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync(new() { Timeout = 15000 });
    body = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(digest, body, StringComparison.Ordinal);
    Assert.DoesNotContain("AUDITSPHERE-SYNTHETIC-RECORD", body, StringComparison.Ordinal);
    Assert.DoesNotContain("synthetic-angular-release-checkpoint", body, StringComparison.Ordinal);
    Assert.DoesNotContain(pageErrors, x => x.Contains("unhandled", StringComparison.OrdinalIgnoreCase));
    await using (var db = host.CreateDbContext())
      Assert.Empty(await db.Releases.AsNoTracking().Where(x => x.ReleaseCandidateId == candidateId).ToListAsync());
  }
}
