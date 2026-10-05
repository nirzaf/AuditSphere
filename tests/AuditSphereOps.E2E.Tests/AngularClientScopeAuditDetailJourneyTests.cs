using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularClientScopeAuditDetailJourneyTests
{
    [Fact]
    [Trait("CaseId", "AS-PAR-002-ANGULAR-AUDIT-DETAIL-01")]
    public async Task AuditDetailRoutesClearSiblingAndRevokedContentInTheSameDocument()
    {
        await using var host = await OwnedHost.StartAsync(startWorker: false,
          caseId: "AS-PAR-002-ANGULAR-AUDIT-DETAIL-01");
        const string siblingMarker = "ZQXSIBLING-AUDIT";
        const string ownMarker = "OWN-AUDIT";
        var sibling = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, "ZQXSIBLING");
        try
        {
            var own = await SeedDetailsAsync(host, host.Fixture, ownMarker);
            var other = await SeedDetailsAsync(host, sibling.Fixture, siblingMarker);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            var errors = new List<string>();
            page.PageError += (_, error) => errors.Add($"page-error: {error}");
            var origin = host.StaffUrl;

            var routes = new (string Template, Guid OwnId, Guid SiblingId, string[] OwnAssertions, string DeniedMessage)[]
            {
      ("/app/audit/workpapers/{0}", own.WorkpaperId, other.WorkpaperId,
        [own.WorkpaperTitleMarker, own.WorkpaperMarker, own.WorkpaperProcedureMarker, own.WorkpaperConclusionMarker],
        "The requested workpaper is unavailable in your current scope."),
      ("/app/records/archives/{0}", own.ArchiveId, other.ArchiveId,
        [own.ArchiveMarker, own.ArchiveEntryMarker, own.ArchiveDigestMarker],
        "The archive is not available in the current scope."),
      ("/app/findings/{0}", own.FindingId, other.FindingId,
        [own.FindingMarker, own.FindingResponseMarker],
        "The requested finding is unavailable in your current scope."),
      ("/app/audit/populations/{0}", own.PopulationId, other.PopulationId,
        [own.PopulationMarker, own.PopulationParametersMarker, own.ReceiptMarker],
        "The requested population is unavailable in your current scope."),
      ("/app/reviews/{0}", own.ReviewPointId, other.ReviewPointId, [own.ReviewMarker],
        "The requested review point was not found in the current firm scope."),
            };

            foreach (var route in routes)
            {
                string Path(Guid id) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                  route.Template, id.ToString("D"));

                await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(Path(route.OwnId))}");
                foreach (var assertion in route.OwnAssertions)
                    await page.GetByText(assertion, new() { Exact = false }).First.WaitForAsync(new() { Timeout = 15000 });
                var initial = await SettledMainTextAsync(page);
                foreach (var assertion in route.OwnAssertions)
                    Assert.Contains(assertion, initial, StringComparison.Ordinal);
                if (route.Template == "/app/audit/populations/{0}")
                    Assert.DoesNotContain(own.ReceiptFileMarker, initial, StringComparison.Ordinal);

                var documentToken = Guid.NewGuid().ToString("N");
                await page.EvaluateAsync("token => window.__clientScopeAuditDetailToken = token", documentToken);
                var siblingText = await NavigateInPlaceAsync(page, Path(route.SiblingId), route.DeniedMessage);
                foreach (var assertion in route.OwnAssertions)
                    Assert.DoesNotContain(assertion, siblingText, StringComparison.Ordinal);
                Assert.DoesNotContain(siblingMarker, siblingText, StringComparison.OrdinalIgnoreCase);
                if (route.Template == "/app/findings/{0}")
                    Assert.DoesNotContain("125.00", siblingText, StringComparison.Ordinal);
                if (route.Template == "/app/audit/populations/{0}")
                {
                    Assert.DoesNotContain(other.ReceiptFileMarker, siblingText, StringComparison.Ordinal);
                    Assert.DoesNotContain("98765.43", siblingText, StringComparison.Ordinal);
                }

                var randomId = Guid.NewGuid();
                var randomText = await NavigateInPlaceAsync(page, Path(randomId), route.DeniedMessage);
                Assert.Equal(NormalizeId(siblingText, route.SiblingId), NormalizeId(randomText, randomId));

                var restored = await NavigateInPlaceAsync(page, Path(route.OwnId), null, route.OwnAssertions[0]);
                foreach (var assertion in route.OwnAssertions)
                    Assert.Contains(assertion, restored, StringComparison.Ordinal);
                Assert.Equal(documentToken,
                  await page.EvaluateAsync<string>("() => window.__clientScopeAuditDetailToken"));

                if (route.Template == "/app/reviews/{0}")
                {
                    await page.GetByRole(AriaRole.Button, new() { Name = "Clear review point", Exact = true }).ClickAsync();
                    await page.GetByText("Disposition updated successfully.", new() { Exact = true })
                      .WaitForAsync(new() { Timeout = 10000 });
                    await using var verifyDisposition = host.CreateDbContext();
                    Assert.True(await verifyDisposition.ReviewPoints.AsNoTracking()
                      .Where(x => x.Id == own.ReviewPointId).Select(x => x.Cleared).SingleAsync());
                    await page.GetByRole(AriaRole.Button, new() { Name = "Reopen review point", Exact = true }).ClickAsync();
                    await page.GetByRole(AriaRole.Button, new() { Name = "Clear review point", Exact = true })
                      .WaitForAsync(new() { Timeout = 10000 });
                    await using var verifyReopened = host.CreateDbContext();
                    Assert.False(await verifyReopened.ReviewPoints.AsNoTracking()
                      .Where(x => x.Id == own.ReviewPointId).Select(x => x.Cleared).SingleAsync());
                }
            }

            var workpaperPage = await context.NewPageAsync();
            await workpaperPage.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/audit/workpapers/{own.WorkpaperId:D}")}");
            await workpaperPage.GetByText(own.WorkpaperMarker, new() { Exact = false })
              .WaitForAsync(new() { Timeout = 15000 });
            WorkpaperDraftResult initialDraft;
            await using (var db = host.CreateDbContext())
            {
                var workpaper = await AuditRecordQueries.WorkpaperAsync(db,
                  PbcSeed.Actor(host.Fixture.Staff, "Staff"), own.WorkpaperId);
                Assert.True(workpaper.Succeeded, workpaper.Message);
                initialDraft = workpaper.Value!.Draft;
            }
            await using (var db = host.CreateDbContext())
            {
                var grantId = await db.RoleGrants.AsNoTracking()
                  .Where(x => x.FirmId == host.Fixture.FirmId && x.UserId == host.Fixture.Staff.Id &&
                    x.Role == "Staff" && x.ClientId == host.Fixture.ClientId &&
                    x.EngagementId == host.Fixture.EngagementId && x.RevokedAt == null)
                  .Select(x => x.Id)
                  .SingleAsync();
                var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
                  PbcSeed.Actor(host.Fixture.Admin, "Administrator"),
                  new RevokeRoleGrantRequest(grantId, Reason: "Client scope test revocation"));
                Assert.True(revoked.Succeeded, revoked.Message);
            }

            await using (var db = host.CreateDbContext())
            {
                var actor = PbcSeed.Actor(host.Fixture.Staff, "Staff");
                var refusedReviewClear = await AuditPlanningService.SetReviewPointDispositionAsync(
                  db, actor, own.ReviewPointId, cleared: true);
                Assert.False(refusedReviewClear.Succeeded);
                var refusedWorkpaperSave = await AuditPlanningService.SaveWorkpaperDraftAsync(db, actor,
                  new SaveWorkpaperDraftRequest(own.WorkpaperId, initialDraft.DraftRevision,
                    initialDraft.BaseWorkpaperRevision, initialDraft.BaseInputGeneration,
                    initialDraft.BasePolicyGeneration, Guid.NewGuid(),
                    $"{own.Marker}-WORKPAPER-AFTER-REVOKE", "Revoked save must not persist."));
                Assert.False(refusedWorkpaperSave.Succeeded);
                Assert.False(await db.ReviewPoints.AsNoTracking().Where(x => x.Id == own.ReviewPointId)
                  .Select(x => x.Cleared).SingleAsync());
                Assert.False(await db.WorkpaperDrafts.AsNoTracking().AnyAsync(x => x.WorkpaperId == own.WorkpaperId));
            }

            await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })
              .WaitForAsync(new() { Timeout = 15000 });
            await workpaperPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })
              .WaitForAsync(new() { Timeout = 15000 });
            var revokedText = await page.Locator("main").InnerTextAsync();
            var revokedWorkpaperText = await workpaperPage.Locator("main").InnerTextAsync();
            Assert.DoesNotContain("BLOCKING", revokedText, StringComparison.Ordinal);
            Assert.DoesNotContain("Review context", revokedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Clear review point", revokedText, StringComparison.OrdinalIgnoreCase);
            foreach (var marker in own.AllMarkers)
            {
                Assert.DoesNotContain(marker, revokedText, StringComparison.Ordinal);
                Assert.DoesNotContain(marker, revokedWorkpaperText, StringComparison.Ordinal);
            }
            Assert.Empty(errors);

        }
        finally
        {
            PbcSeed.DeleteDirectory(sibling.StagingRoot);
        }
    }

    private static async Task<AuditDetailIds> SeedDetailsAsync(
      OwnedHost host, PbcSeed.Fixture scope, string marker)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = PbcSeed.Actor(scope.Staff, "Staff");
        await using var db = host.CreateDbContext();

        var findingResult = await AuditPlanningService.CreateFindingAsync(db, actor,
          new CreateFindingRequest(scope.EngagementId, $"{marker}-FINDING", $"{marker}-FINDING-IMPACT",
            false, 125m, null));
        Assert.True(findingResult.Succeeded, findingResult.Message);
        var findingId = findingResult.Value!.FindingId;
        var findingResponseMarker = $"{marker}-FINDING-RESPONSE";
        var response = await AuditPlanningService.RecordFindingResponseAsync(db, actor,
          new RecordFindingResponseRequest(findingId, findingResponseMarker, false));
        Assert.True(response.Succeeded, response.Message);

        var populationParametersMarker = $"{marker}-EXTRACTION-PARAMETERS";
        var populationResult = await AuditPlanningService.CreatePopulationVersionAsync(db, actor,
          new CreatePopulationRequest(scope.EngagementId, $"{marker}-POPULATION-PURPOSE", "Existence",
            "Synthetic source receipt", populationParametersMarker, 3, 98765.43m, "QAR", null));
        Assert.True(populationResult.Succeeded, populationResult.Message);
        var receiptId = Guid.NewGuid();
        var receiptMarker = $"{marker}-RECEIPT-TOKEN";
        var receiptFileMarker = $"{marker}-SOURCE.csv";
        db.SourceReceipts.Add(new SourceReceipt
        {
            Id = receiptId,
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = scope.EngagementId,
            SourceType = "CSV_IMPORT",
            ReceiptToken = receiptMarker,
            Sha256Digest = new string('a', 64),
            ByteCount = 3,
            OriginalFileName = receiptFileMarker,
            AcquiredAt = now,
            AcquiredByUserId = scope.Staff.Id
        });
        db.EvidenceLinks.Add(new EvidenceLink
        {
            Id = Guid.NewGuid(),
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = scope.EngagementId,
            SourceReceiptId = receiptId,
            Purpose = $"{marker}-EVIDENCE-PURPOSE",
            Assertion = "Existence",
            RelevanceReliabilityAssessment = "Synthetic client scope fixture",
            CreatedAt = now
        });

        var workpaperTitleMarker = $"{marker}-WORKPAPER-TITLE";
        var workpaperPerformedMarker = $"{marker}-SUBMITTED-WORK";
        var workpaperResult = await AuditPlanningService.CreateWorkpaperAsync(db, actor,
          new CreateWorkpaperRequest(scope.EngagementId, $"{marker}-WP-01", workpaperTitleMarker,
            $"{marker}-WORKPAPER-OBJECTIVE", "SYNTHETIC-TEMPLATE-v1", null, $"{marker}-WORKPAPER-PROCEDURE"));
        Assert.True(workpaperResult.Succeeded, workpaperResult.Message);
        var workpaperId = workpaperResult.Value!.WorkpaperId;
        var workpaperProcedureMarker = $"{marker}-WORKPAPER-PROCEDURE";
        var workpaperConclusionMarker = $"{marker}-SUBMISSION-CONCLUSION";
        db.WorkpaperSubmissions.Add(new WorkpaperSubmission
        {
            Id = Guid.NewGuid(),
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = scope.EngagementId,
            WorkpaperId = workpaperId,
            ActorId = scope.Staff.Id,
            Revision = 2,
            WorkPerformed = workpaperPerformedMarker,
            Conclusion = workpaperConclusionMarker,
            SubmittedAt = now
        });

        var reviewPointId = Guid.NewGuid();
        db.ReviewPoints.Add(new ReviewPoint
        {
            Id = reviewPointId,
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = scope.EngagementId,
            TargetId = workpaperId,
            TargetKind = "workpaper",
            TargetRevision = 1,
            Comment = $"{marker}-REVIEW-PRIVATE",
            RaisedByUserId = scope.Staff.Id,
            RaisedAt = now,
            Significant = true
        });

        var archiveId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();
        db.Archives.Add(new Archive
        {
            Id = archiveId,
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = scope.EngagementId,
            ProfileId = $"{marker}-ARCHIVE-PROFILE",
            ProfileVersion = 1,
            Status = ArchiveStates.Issued,
            CreatedAt = now
        });
        db.ArchiveManifests.Add(new ArchiveManifest
        {
            Id = manifestId,
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = scope.EngagementId,
            ArchiveId = archiveId,
            Version = 1,
            Status = "BUILT",
            ManifestDigest = new string('b', 64),
            EntryCount = 1,
            CompletenessStatus = "COMPLETE",
            BuiltAt = now
        });
        db.ArchiveManifestEntries.Add(new ArchiveManifestEntry
        {
            Id = Guid.NewGuid(),
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = scope.EngagementId,
            ArchiveManifestId = manifestId,
            Ordinal = 1,
            EntryKind = "WORKPAPER",
            SourceKind = "SYNTHETIC",
            RelativeName = $"{marker}-ARCHIVE-FILE",
            ContentHash = new string('c', 64),
            ByteCount = 32
        });
        await db.SaveChangesAsync();

        return new AuditDetailIds(
          reviewPointId, archiveId, findingId, populationResult.Value!.PopulationId, workpaperId,
          $"{marker}-REVIEW-PRIVATE", $"{marker}-ARCHIVE-PROFILE", $"{marker}-ARCHIVE-FILE",
          new string('b', 64), $"{marker}-FINDING-IMPACT", findingResponseMarker,
          $"{marker}-POPULATION-PURPOSE", populationParametersMarker, receiptMarker, receiptFileMarker,
          $"{marker}-WORKPAPER-OBJECTIVE", workpaperProcedureMarker, workpaperConclusionMarker,
          workpaperTitleMarker, workpaperPerformedMarker, marker);
    }

    private static async Task<string> NavigateInPlaceAsync(
      IPage page, string path, string? deniedMessage, string? expectedContent = null)
    {
        await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }", path);
        if (deniedMessage is not null)
            await page.GetByRole(AriaRole.Alert).GetByText(deniedMessage, new() { Exact = true })
              .WaitForAsync(new() { Timeout = 10000 });
        if (expectedContent is not null)
            await page.GetByText(expectedContent, new() { Exact = false }).WaitForAsync(new() { Timeout = 10000 });
        return await SettledMainTextAsync(page);
    }

    private static async Task<string> SettledMainTextAsync(IPage page)
    {
        await page.Locator("main h1").First.WaitForAsync(new() { Timeout = 15000 });
        var previous = string.Empty;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await page.WaitForTimeoutAsync(100);
            var text = System.Text.RegularExpressions.Regex.Replace(
              await page.Locator("main").InnerTextAsync(), @"\s+", " ").Trim();
            if (text.Length > 0 && text == previous) return text;
            previous = text;
        }
        return previous;
    }

    private static string NormalizeId(string text, Guid id) =>
      text.Replace(id.ToString("D"), "{id}", StringComparison.OrdinalIgnoreCase)
        .Replace(id.ToString("N"), "{id}", StringComparison.OrdinalIgnoreCase);

    private sealed record AuditDetailIds(
      Guid ReviewPointId, Guid ArchiveId, Guid FindingId, Guid PopulationId, Guid WorkpaperId,
      string ReviewMarker, string ArchiveMarker, string ArchiveEntryMarker, string ArchiveDigestMarker,
      string FindingMarker, string FindingResponseMarker, string PopulationMarker, string PopulationParametersMarker,
      string ReceiptMarker, string ReceiptFileMarker, string WorkpaperMarker, string WorkpaperProcedureMarker,
      string WorkpaperConclusionMarker, string WorkpaperTitleMarker, string WorkpaperPerformedMarker, string Marker)
    {
        public string[] AllMarkers =>
        [ReviewMarker, ArchiveMarker, ArchiveEntryMarker, FindingMarker, FindingResponseMarker,
      PopulationMarker, PopulationParametersMarker, ReceiptMarker, ReceiptFileMarker,
      WorkpaperMarker, WorkpaperProcedureMarker, WorkpaperConclusionMarker, WorkpaperTitleMarker,
      WorkpaperPerformedMarker, $"{Marker}-WORKPAPER-AFTER-REVOKE"];
    }
}
