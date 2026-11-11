using System.Data.Common;
using System.IO.Compression;
using System.Text.Json;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FivePartBundle_CorrespondenceChangesCreateNewDigest_AndRowLimitFailsClosed(bool resolvedComments)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.A("partner", "Partner"); var manager = w.A("manager", "Manager");
    await CompletionBundleFixture.ReleasedSupportedFinancialPackageAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U));
    await CompletionBundleFixture.PostSupportedBalanceFeeAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U), automateBalance: true);
    await RefreshSupportedPortalRiskAsync(pg, w);
    await ReviewSupportedBundleApplicabilityAsync(pg, w);
    await SignedReportAsync(pg, w);
    await using var scope = new AuditSphereDbContext(pg.Options);
    var missing = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.Equal(ErrorCodes.GateBlocked, missing.ErrorCode); Assert.Contains("management letter", missing.Message);
    Assert.Empty(await scope.CommercialDeliverableBundles.ToListAsync());
    var management = await AuditDeliverableService.GenerateReportAsync(scope, manager, w.EngagementId, DeliverableKinds.ManagementLetter);
    Assert.True(management.Succeeded, management.Message);
    var review = await AuditDeliverableService.ShareWithClientAsync(scope, manager, management.Value!.DeliverableId!.Value);
    Assert.True(review.Succeeded, review.Message);
    Assert.Null(await scope.ClientDeliverableReviews.Where(x => x.Id == review.Value).Select(x => x.AcknowledgedAt).SingleAsync());
    var first = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.True(first.Succeeded, first.Message);
    var original = await scope.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == first.Value);
    Guid? firstDraftRequestId = null;
    async Task AddRowsAsync(AuditSphereDbContext db, int start, int count)
    {
      for (var number = start; number < start + count; number++)
      {
        if (!resolvedComments)
        {
          // A draft request contributes exactly one retained REQUEST communication. No delivery runs.
          var created = await PbcService.CreateRequestAsync(db, manager,
            new(w.EngagementId, $"Synthetic reviewed correspondence {number}", "TEST", "2026-01-01", "2026-12-31",
              "Cash", "PDF", "", w.U["client"].Id, manager.UserId, partner.UserId,
              "2027-01-31", "Internal", "Reviewed synthetic source"));
          Assert.True(created.Succeeded, created.Message);
          firstDraftRequestId ??= created.Value;
        }
        else
        {
          // Preserve the original client-authored query and independent staff resolution.
          var comment = await AuditDeliverableService.CommentAsync(db, w.A("client", "ClientUser"), review.Value, Guid.NewGuid(),
            $"Synthetic reviewed comment {number}");
          Assert.True(comment.Succeeded, comment.Message);
          var resolved = await AuditDeliverableService.ResolveCommentAsync(db, manager, comment.Value,
            "Reviewed and resolved in synthetic fixture");
          Assert.True(resolved.Succeeded, resolved.Message);
        }
        db.ChangeTracker.Clear();
      }
    }
    await AddRowsAsync(scope, 1, 1);
    var changed = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.True(changed.Succeeded, changed.Message); Assert.NotEqual(first.Value, changed.Value);
    var current = await scope.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == changed.Value);
    Assert.NotEqual(original.SourceDigest, current.SourceDigest);
    async Task<int> CorrespondenceCountAsync(byte[] bytes)
    {
      using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
      using var stream = zip.GetEntry("04_Management_correspondence/Audit_trail.json")!.Open();
      using var trail = await JsonDocument.ParseAsync(stream);
      Assert.True(trail.RootElement.TryGetProperty("ClearedQueries", out _));
      return trail.RootElement.GetProperty(resolvedComments ? "ClearedQueries" : "Communications").GetArrayLength();
    }
    Assert.Equal(0, await CorrespondenceCountAsync(original.Content));
    Assert.Equal(1, await CorrespondenceCountAsync(current.Content));
    var retries = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => {
      await using var retryDb = new AuditSphereDbContext(pg.Options);
      return await AuditDeliverableService.AssembleBundleAsync(retryDb, partner, w.EngagementId, true);
    }));
    Assert.All(retries, x => { Assert.True(x.Succeeded, x.Message); Assert.Equal(changed.Value, x.Value); });
    // The inclusive boundary is accepted, while the first row beyond it never creates a bundle.
    await AddRowsAsync(scope, 2, 9999);
    var atLimit = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.True(atLimit.Succeeded, atLimit.Message);
    var bounded = await scope.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == atLimit.Value);
    Assert.Equal(10000, await CorrespondenceCountAsync(bounded.Content));
    // A separate real transaction appends after the count statement has already executed.
    // The materialized read must still refuse overflow; no sleeps or timing assumptions.
    var hook = new AppendAfterCorrespondenceCount(resolvedComments ? "client_deliverable_comments" : "pbc_communications", async () => {
      await using var concurrent = new AuditSphereDbContext(pg.Options);
      if (resolvedComments) await AddRowsAsync(concurrent, 10001, 1);
      else
      {
        // The supported send command commits one queued EMAIL locally. No mail worker/provider is run.
        var sent = await PbcService.SendRequestAsync(concurrent, manager, firstDraftRequestId!.Value, 1,
          "https://portal.example.test", createWorkflowTask: false);
        Assert.True(sent.Succeeded, sent.Message);
        var email = await concurrent.PbcCommunications.AsNoTracking().SingleAsync(x =>
          x.PbcRequestId == firstDraftRequestId.Value && x.Kind == PbcCommunicationKinds.Email);
        Assert.Equal("QUEUED", email.DeliveryState);
        Assert.Null(email.DeliveredAt);
        Assert.Null(email.ProviderCorrelationId);
        var request = await concurrent.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == firstDraftRequestId.Value);
        Assert.Equal((PbcStates.Sent, 2L), (request.State, request.Revision));
      }
    });
    await using (var raceDb = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hook).Options))
    {
      var raced = await AuditDeliverableService.AssembleBundleAsync(raceDb, partner, w.EngagementId, true);
      Assert.True(hook.Fired); Assert.Equal(ErrorCodes.GateBlocked, raced.ErrorCode);
      Assert.Contains("bounded assembly limit", raced.Message);
    }
    Assert.Equal(10001, resolvedComments
      ? await scope.ClientDeliverableComments.CountAsync(x => x.ReviewId == review.Value)
      : await scope.PbcCommunications.CountAsync(x => x.EngagementId == w.EngagementId));
    var overflow = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.Equal(ErrorCodes.GateBlocked, overflow.ErrorCode); Assert.Contains("bounded assembly limit", overflow.Message);
    Assert.Equal(3, await scope.CommercialDeliverableBundles.CountAsync());
    var preserved = await scope.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == original.Id);
    Assert.Equal(original.ContentSha256, preserved.ContentSha256); Assert.Equal(original.Content, preserved.Content);
    var finding = await AuditPlanningService.CreateFindingAsync(scope, manager,
      new(w.EngagementId, "Additional evidence", "New evidence requires renewed completion review", false, null, null));
    Assert.True(finding.Succeeded, finding.Message);
    var stale = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.Equal(ErrorCodes.GateBlocked, stale.ErrorCode); Assert.Contains("current auditor's report", stale.Message);
    Assert.DoesNotContain("bounded assembly limit", stale.Message);
    Assert.Equal(3, await scope.CommercialDeliverableBundles.CountAsync());
  }
  private sealed class AppendAfterCorrespondenceCount(string table, Func<Task> append) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (!Fired && command.CommandText.Contains("count(*)", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains(table, StringComparison.Ordinal))
      { Fired = true; await append(); }
      return result;
    }
  }
}
