using System.Data.Common;
using System.IO.Compression;
using System.Text.Json;
using AuditSphereOps.Application.Completion;
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
    var requestId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.PbcRequests.Add(new PbcRequest { Id = requestId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
        Objective = "Synthetic correspondence boundary", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Area = "Cash", RequestedFormat = "PDF", DueDate = "2027-01-31", Confidentiality = "Internal", AcceptanceCriteria = "Reviewed synthetic source",
        ClientOwnerUserId = w.U["client"].Id, FirmOwnerUserId = manager.UserId, ReviewerUserId = manager.UserId,
        State = PbcStates.Accepted, AcceptedByUserId = manager.UserId, AcceptedAt = now, CreatedByUserId = manager.UserId, CreatedAt = now, UpdatedAt = now });
      await db.SaveChangesAsync();
    }
    await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U));
    await CompletionBundleFixture.SeedPostedFeeAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U), automateBalance: true);
    await SignedReportAsync(pg, w);
    await using var scope = new AuditSphereDbContext(pg.Options);
    var missing = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.Equal(ErrorCodes.GateBlocked, missing.ErrorCode); Assert.Contains("management letter", missing.Message);
    Assert.Empty(await scope.CommercialDeliverableBundles.ToListAsync());
    Assert.True((await AuditDeliverableService.GenerateReportAsync(scope, manager, w.EngagementId, DeliverableKinds.ManagementLetter)).Succeeded);
    var first = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.True(first.Succeeded, first.Message);
    var original = await scope.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == first.Value);
    PbcCommunication Message(int number) => new() { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
      PbcRequestId = requestId, AuthorUserId = manager.UserId, Kind = PbcCommunicationKinds.StaffMessage,
      Body = $"Synthetic reviewed correspondence {number}", CreatedAt = now.AddTicks(number) };
    var reviewId = await scope.ClientDeliverableReviews.Where(x => x.EngagementId == w.EngagementId).Select(x => x.Id).SingleAsync();
    void AddRows(AuditSphereDbContext db, int start, int count)
    {
      if (!resolvedComments) db.PbcCommunications.AddRange(Enumerable.Range(start, count).Select(Message));
      else db.ClientDeliverableComments.AddRange(Enumerable.Range(start, count).Select(number => new ClientDeliverableComment {
        Id = Guid.NewGuid(), FirmId = w.FirmId, ReviewId = reviewId, AuthorUserId = w.U["client"].Id, FromClient = true,
        Body = $"Synthetic reviewed comment {number}", CreatedAt = now.AddTicks(number), ResolvedAt = now.AddMinutes(1),
        ResolvedByUserId = manager.UserId, Resolution = "Reviewed and resolved in synthetic fixture"
      }));
    }
    AddRows(scope, 1, 1); await scope.SaveChangesAsync();
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
    AddRows(scope, 2, 9999); await scope.SaveChangesAsync();
    var atLimit = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.True(atLimit.Succeeded, atLimit.Message);
    var bounded = await scope.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == atLimit.Value);
    Assert.Equal(10000, await CorrespondenceCountAsync(bounded.Content));
    // A separate real transaction appends after the count statement has already executed.
    // The materialized read must still refuse overflow; no sleeps or timing assumptions.
    var hook = new AppendAfterCorrespondenceCount(resolvedComments ? "client_deliverable_comments" : "pbc_communications", async () => {
      await using var concurrent = new AuditSphereDbContext(pg.Options);
      AddRows(concurrent, 10001, 1); await concurrent.SaveChangesAsync();
    });
    await using (var raceDb = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hook).Options))
    {
      var raced = await AuditDeliverableService.AssembleBundleAsync(raceDb, partner, w.EngagementId, true);
      Assert.True(hook.Fired); Assert.Equal(ErrorCodes.GateBlocked, raced.ErrorCode);
      Assert.Contains("bounded assembly limit", raced.Message);
    }
    var overflow = await AuditDeliverableService.AssembleBundleAsync(scope, partner, w.EngagementId, true);
    Assert.Equal(ErrorCodes.GateBlocked, overflow.ErrorCode); Assert.Contains("bounded assembly limit", overflow.Message);
    Assert.Equal(3, await scope.CommercialDeliverableBundles.CountAsync());
    var preserved = await scope.CommercialDeliverableBundles.AsNoTracking().SingleAsync(x => x.Id == original.Id);
    Assert.Equal(original.ContentSha256, preserved.ContentSha256); Assert.Equal(original.Content, preserved.Content);
    scope.Findings.Add(new AuditSphereOps.Domain.Audit.Finding { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId,
      EngagementId = w.EngagementId, ActorId = manager.UserId, FindingType = "Additional evidence",
      ImpactDescription = "New evidence requires renewed completion review", CreatedAt = DateTimeOffset.UtcNow });
    await scope.SaveChangesAsync();
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
