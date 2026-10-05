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
        const string siblingEngagementMarker = "SAMECLIENTSIBLING-AUDIT";
        const string ownMarker = "OWN-AUDIT";
        var sibling = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, "ZQXSIBLING");
        try
        {
            var own = await SeedDetailsAsync(host, host.Fixture, ownMarker);
            var other = await SeedDetailsAsync(host, sibling.Fixture, siblingMarker);
            var siblingEngagementId = Guid.NewGuid();
            var siblingEngagementStaff = PbcSeed.User(host.Fixture.FirmId, "Staff");
            await using (var db = host.CreateDbContext())
            {
                db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
                {
                    Id = siblingEngagementId, FirmId = host.Fixture.FirmId,
                    PracticeClientId = host.Fixture.ClientId, Status = "Active",
                    ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
                });
                db.Users.Add(siblingEngagementStaff);
                db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, siblingEngagementStaff, "Staff",
                  host.Fixture.ClientId, siblingEngagementId));
                await db.SaveChangesAsync();
            }
            var siblingEngagementScope = host.Fixture with
            {
                EngagementId = siblingEngagementId,
                Staff = siblingEngagementStaff
            };
            var sameClientOther = await SeedDetailsAsync(host, siblingEngagementScope, siblingEngagementMarker);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            var errors = new List<string>();
            page.PageError += (_, error) => errors.Add($"page-error: {error}");
            var origin = host.StaffUrl;

            var routes = new (string Template, Guid OwnId, Guid SiblingId, Guid? SameClientSiblingId,
              string[] OwnAssertions, string DeniedMessage)[]
            {
      ("/app/audit/workpapers/{0}", own.WorkpaperId, other.WorkpaperId, sameClientOther.WorkpaperId,
        [own.WorkpaperTitleMarker, own.WorkpaperMarker, own.WorkpaperProcedureMarker, own.WorkpaperConclusionMarker],
        "The requested workpaper is unavailable in your current scope."),
      ("/app/records/archives/{0}", own.ArchiveId, other.ArchiveId, null,
        [own.ArchiveMarker, own.ArchiveEntryMarker, own.ArchiveDigestMarker],
        "The archive is not available in the current scope."),
      ("/app/findings/{0}", own.FindingId, other.FindingId, sameClientOther.FindingId,
        [own.FindingMarker, own.FindingResponseMarker],
        "The requested finding is unavailable in your current scope."),
      ("/app/audit/populations/{0}", own.PopulationId, other.PopulationId, sameClientOther.PopulationId,
        [own.PopulationMarker, own.PopulationParametersMarker, own.ReceiptMarker, own.PopulationRationaleMarker],
        "The requested population is unavailable in your current scope."),
      ("/app/reviews/{0}", own.ReviewPointId, other.ReviewPointId, null, [own.ReviewMarker],
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
                if (route.Template == "/app/records/archives/{0}")
                {
                    Assert.Contains("Archive manifest", initial, StringComparison.Ordinal);
                    Assert.Contains("1 manifest entries", initial, StringComparison.Ordinal);
                }
                if (route.Template == "/app/audit/workpapers/{0}")
                    Assert.Contains("1 frozen submissions", initial, StringComparison.Ordinal);
                if (route.Template == "/app/findings/{0}")
                    Assert.Contains("management response recorded", initial, StringComparison.OrdinalIgnoreCase);
                if (route.Template == "/app/reviews/{0}")
                    Assert.Contains("This significant review point remains open and blocks the engagement completion gate.",
                      initial, StringComparison.Ordinal);
                if (route.Template == "/app/audit/populations/{0}")
                {
                    Assert.Contains("12000 population rows", initial, StringComparison.Ordinal);
                    Assert.Contains("1 reviewed item tests", initial, StringComparison.Ordinal);
                    Assert.Contains("Population details (§20.1)", initial, StringComparison.Ordinal);
                    Assert.DoesNotContain(own.ReceiptFileMarker, initial, StringComparison.Ordinal);
                }

                foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
                {
                    await page.SetViewportSizeAsync(width, 900);
                    await page.WaitForFunctionAsync(
                      "expected => document.documentElement.clientWidth === expected", width);
                    var documentWidth = await page.EvaluateAsync<int>(
                      "() => document.documentElement.scrollWidth");
                    Assert.True(documentWidth <= width + 1,
                      $"Route {route.Template} is {documentWidth}px wide at {width}px.");
                }

                await page.SetViewportSizeAsync(1440, 900);
                if (route.Template == "/app/audit/workpapers/{0}")
                {
                    var planLink = page.GetByRole(AriaRole.Link, new() { Name = "Audit plan", Exact = true }).Last;
                    await planLink.FocusAsync();
                    await page.Keyboard.PressAsync("Tab");
                    var fieldworkLink = page.GetByRole(AriaRole.Link,
                      new() { Name = "Fieldwork control center", Exact = true }).Last;
                    await Assertions.Expect(fieldworkLink).ToBeFocusedAsync();
                    await Assertions.Expect(fieldworkLink).ToHaveCSSAsync("outline-style", "solid");
                }
                else if (route.Template == "/app/findings/{0}")
                {
                    var planLink = page.GetByRole(AriaRole.Link, new() { Name = "Audit plan", Exact = true });
                    await planLink.FocusAsync();
                    await page.Keyboard.PressAsync("Tab");
                    var engagementLink = page.GetByRole(AriaRole.Link,
                      new() { Name = "Engagement", Exact = true });
                    await Assertions.Expect(engagementLink).ToBeFocusedAsync();
                    await Assertions.Expect(engagementLink).ToHaveCSSAsync("outline-style", "solid");
                }
                else if (route.Template == "/app/audit/populations/{0}")
                {
                    var fieldworkLink = page.GetByRole(AriaRole.Link,
                      new() { Name = "Fieldwork control center", Exact = true }).Last;
                    await fieldworkLink.FocusAsync();
                    await page.Keyboard.PressAsync("Tab");
                    var planLink = page.GetByRole(AriaRole.Link, new() { Name = "Audit plan", Exact = true });
                    await Assertions.Expect(planLink).ToBeFocusedAsync();
                    await Assertions.Expect(planLink).ToHaveCSSAsync("outline-style", "solid");
                }
                else
                {
                    var portfolioLink = page.GetByRole(AriaRole.Navigation,
                      new() { Name = "Breadcrumb", Exact = true }).GetByRole(AriaRole.Link,
                      new() { Name = "Portfolio", Exact = true });
                    await portfolioLink.FocusAsync();
                    await page.Keyboard.PressAsync("Tab");
                    var engagementLink = page.GetByRole(AriaRole.Link,
                      new() { Name = "Engagement", Exact = true }).First;
                    await Assertions.Expect(engagementLink).ToBeFocusedAsync();
                    await Assertions.Expect(engagementLink).ToHaveCSSAsync("outline-style", "solid");
                }

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
                    Assert.DoesNotContain("4,800,000", siblingText, StringComparison.Ordinal);
                }

                var randomId = Guid.NewGuid();
                var randomText = await NavigateInPlaceAsync(page, Path(randomId), route.DeniedMessage);
                Assert.Equal(NormalizeId(siblingText, route.SiblingId), NormalizeId(randomText, randomId));

                if (route.SameClientSiblingId is Guid sameClientSiblingId)
                {
                    var sameClientSiblingText = await NavigateInPlaceAsync(page,
                      Path(sameClientSiblingId), route.DeniedMessage);
                    foreach (var assertion in route.OwnAssertions)
                        Assert.DoesNotContain(assertion, sameClientSiblingText, StringComparison.Ordinal);
                    foreach (var marker in sameClientOther.AllMarkers)
                        Assert.DoesNotContain(marker, sameClientSiblingText, StringComparison.Ordinal);
                    if (route.Template == "/app/audit/populations/{0}")
                    {
                        Assert.DoesNotContain("12000 population rows", sameClientSiblingText, StringComparison.Ordinal);
                        Assert.DoesNotContain("4,800,000", sameClientSiblingText, StringComparison.Ordinal);
                    }
                    await NavigateInPlaceAsync(page, Path(route.OwnId), null, route.OwnAssertions[0]);
                }

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
        var populationRationaleMarker = $"{marker}-SYNTHETIC-SAMPLE-RATIONALE";
        var populationResult = await AuditPlanningService.CreatePopulationVersionAsync(db, actor,
          new CreatePopulationRequest(scope.EngagementId, $"{marker}-POPULATION-PURPOSE", "Existence",
            "Synthetic source receipt", populationParametersMarker, 12_000, 4_800_000m, "QAR", null));
        Assert.True(populationResult.Succeeded, populationResult.Message);
        var procedureId = Guid.NewGuid();
        var selectionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var itemTestId = Guid.NewGuid();
        db.AuditProcedures.Add(new AuditProcedure
        {
            Id = procedureId, FirmId = scope.FirmId, ClientId = scope.ClientId,
            EngagementId = scope.EngagementId, Title = $"{marker}-POPULATION-PROCEDURE",
            Status = AuditProcedureStatuses.Planned, CreatedAt = now
        });
        db.AuditSelections.Add(new AuditSelection
        {
            Id = selectionId, FirmId = scope.FirmId, ClientId = scope.ClientId,
            EngagementId = scope.EngagementId, PopulationVersionId = populationResult.Value!.PopulationId,
            ProcedureId = procedureId, Method = "Manual", Rationale = populationRationaleMarker,
            SelectedCount = 1, SelectedSignedTotal = 400m, Status = AuditSelectionStatuses.Submitted,
            CreatedByUserId = scope.Staff.Id, CreatedAt = now
        });
        db.AuditSelectionItems.Add(new AuditSelectionItem
        {
            Id = itemId, FirmId = scope.FirmId, ClientId = scope.ClientId,
            EngagementId = scope.EngagementId, SelectionId = selectionId,
            StableRowId = $"{marker}-SAMPLE-ROW-1", SignedAmount = 400m, Currency = "QAR",
            InclusionReason = "Synthetic scope parity fixture", CreatedAt = now
        });
        db.AuditItemTests.Add(new AuditItemTest
        {
            Id = itemTestId, FirmId = scope.FirmId, ClientId = scope.ClientId,
            EngagementId = scope.EngagementId, SelectionId = selectionId,
            SelectionItemId = itemId, ProcedureId = procedureId,
            WorkPerformed = $"{marker}-SAMPLE-TEST-WORK", Result = AuditItemTestResults.Pass,
            TestedByUserId = scope.Staff.Id, TestedAt = now
        });
        db.AuditItemTestReviews.Add(new AuditItemTestReview
        {
            Id = Guid.NewGuid(), FirmId = scope.FirmId, ClientId = scope.ClientId,
            EngagementId = scope.EngagementId, SelectionItemId = itemId,
            AuditItemTestId = itemTestId, TestRevision = 1,
            Decision = AuditItemTestReviewDecisions.Reviewed,
            ReviewerUserId = scope.Reviewer.Id, CreatedAt = now
        });
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
          $"{marker}-POPULATION-PURPOSE", populationParametersMarker, populationRationaleMarker,
          receiptMarker, receiptFileMarker,
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
      string PopulationRationaleMarker, string ReceiptMarker, string ReceiptFileMarker,
      string WorkpaperMarker, string WorkpaperProcedureMarker,
      string WorkpaperConclusionMarker, string WorkpaperTitleMarker, string WorkpaperPerformedMarker, string Marker)
    {
        public string[] AllMarkers =>
        [ReviewMarker, ArchiveMarker, ArchiveEntryMarker, FindingMarker, FindingResponseMarker,
      PopulationMarker, PopulationParametersMarker, ReceiptMarker, ReceiptFileMarker,
      PopulationRationaleMarker,
      WorkpaperMarker, WorkpaperProcedureMarker, WorkpaperConclusionMarker, WorkpaperTitleMarker,
      WorkpaperPerformedMarker, $"{Marker}-WORKPAPER-AFTER-REVOKE"];
    }
}
