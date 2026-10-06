using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  public async Task ClientSalesDraftsReplayReviseFreezePartiesAndNeverPost()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid periodId, customerId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1, Rationale = "Synthetic native invoice service",
        EvaluationTemplateVersion = "TEST-1", EvaluationSnapshotDigest = new string('f', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, reviewer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE", ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
      var period = await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"));
      Assert.True(period.Succeeded, period.Message); periodId = period.Value;
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "AUDITSPHERE", new DateOnly(2026, 1, 1));
      Assert.True(chart.Succeeded, chart.Message);
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, chart.Value, [new("revenue", "4000", "Revenue", "INCOME", "CREDIT", true)])).Succeeded);
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chart.Value)).Succeeded);
      var customer = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, preparer, scope.ClientA,
        new("Customer", "Customer", "CUSTOMER", "Original customer address", "QA", "", "", "", "", "", ""));
      Assert.True(customer.Succeeded, customer.Message); customerId = customer.Value;
    }
    var request = new ClientSalesInvoiceDraftRequest(Guid.CreateVersion7(), null, 0, "DRAFT-001", "SOURCE-001", periodId, customerId,
      new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 10), "QAR",
      new("QAR", 2, "AWAY_FROM_ZERO", "REJECT", 0, ""), [new("Consulting", "4000", 2m, 125.125m, .25m, "NONE", [])], "Unverified client reference");
    ClientSalesInvoiceDraftView original;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var saved = await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, request);
      Assert.True(saved.Succeeded, saved.Message); original = saved.Value!;
      Assert.Equal("CLIENT A", original.Snapshot.Seller.LegalName); Assert.Equal(scope.ClientA, original.Snapshot.Seller.ClientId);
      Assert.Equal("250.00", original.Snapshot.Gross); Assert.Equal("125.125000", original.Snapshot.Lines[0].UnitPrice);
      Assert.False(original.Snapshot.PossibleDuplicateSourceReference);
      Assert.Equal(original.Id, (await ClientSalesInvoiceDraftWorkspace.GetByCommandAsync(db, preparer, scope.ClientA, request.CommandId)).Value!.Id);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.GetByCommandAsync(db, reviewer, scope.ClientA, request.CommandId)).Succeeded);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.GetByCommandAsync(db, preparer, scope.ClientB, request.CommandId)).Succeeded);
      Assert.Equal("DRAFT", original.Status); Assert.False(original.Posted); Assert.False(original.Issued);
      Assert.Equal(original.Id, (await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, request)).Value!.Id);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, request with { SourceReference = "Changed" })).Succeeded);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientB, request)).Succeeded);
      var amendment = await ClientBookkeepingCounterpartyWorkspace.ProposeAmendmentAsync(db, preparer, scope.ClientA, customerId,
        new(1, "Customer", "Reviewed customer address", "", "", "", "Contact changed"));
      Assert.True(amendment.Succeeded, amendment.Message);
      Assert.True((await ClientBookkeepingCounterpartyWorkspace.ReviewAmendmentAsync(db, reviewer, scope.ClientA, customerId, amendment.Value, 2, "APPROVE", "Independent check")).Succeeded);
      Assert.Equal("Original customer address", (await ClientSalesInvoiceDraftWorkspace.GetAsync(db, preparer, scope.ClientA, original.InvoiceId)).Value!.Snapshot.Customer.Address);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, reviewer, scope.ClientA, request with { CommandId = Guid.CreateVersion7(), InvoiceId = original.InvoiceId, ExpectedRevision = 1 })).Succeeded);
    }
    var revised = request with { CommandId = Guid.CreateVersion7(), InvoiceId = original.InvoiceId, ExpectedRevision = 1, Lines = [new("Updated consulting", "4000", 1m, 99.99m, 0m, "NONE", [])] };
    async Task<bool> Revise(Guid command)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return (await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, revised with { CommandId = command })).Succeeded;
    }
    var competing = await Task.WhenAll(Revise(revised.CommandId), Revise(Guid.CreateVersion7()));
    Assert.Single(competing, x => x);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var latest = await ClientSalesInvoiceDraftWorkspace.GetAsync(db, reviewer, scope.ClientA, original.InvoiceId);
      Assert.True(latest.Succeeded, latest.Message); Assert.Equal("2", latest.Value!.Revision); Assert.Equal(original.Id, latest.Value.PreviousRevisionId);
      Assert.Equal("Reviewed customer address", latest.Value.Snapshot.Customer.Address); Assert.Equal("99.99", latest.Value.Snapshot.Gross);
      var historical = await ClientSalesInvoiceDraftWorkspace.GetAsync(db, preparer, scope.ClientA, original.InvoiceId, 1);
      Assert.Equal("Original customer address", historical.Value!.Snapshot.Customer.Address); Assert.Equal("250.00", historical.Value.Snapshot.Gross);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.GetAsync(db, preparer, scope.ClientB, original.InvoiceId)).Succeeded);
      Assert.False(latest.Value.Snapshot.PossibleDuplicateSourceReference);
      var duplicateSource = await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, request with { CommandId = Guid.CreateVersion7(), DraftReference = "DRAFT-DUPLICATE-SOURCE" });
      Assert.True(duplicateSource.Succeeded, duplicateSource.Message);
      Assert.True(duplicateSource.Value!.Snapshot.PossibleDuplicateSourceReference);
      Assert.Equal(3, await db.ClientSalesInvoiceDrafts.CountAsync());
      Assert.Equal(0, await db.FirmJournals.CountAsync()); Assert.Equal(0, await db.ClientOperationalJournals.CountAsync()); Assert.Equal(0, await db.ClientOperationalJournalLines.CountAsync());
      Assert.False((await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, request with { CommandId = Guid.CreateVersion7(), DraftReference = "DRAFT-002", Lines = [new("Tax", "4000", 1m, 10m, 0m, "EXCLUSIVE", [new("STANDARD", .05m)])] })).Succeeded);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, request with { CommandId = Guid.CreateVersion7(), DraftReference = "DRAFT-003", Lines = [new("Invalid account", "FOREIGN", 1m, 10m, 0m, "NONE", [])] })).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
        ServiceRoute = "BOOKKEEPING", Decision = "Declined", Generation = 2, Rationale = "Synthetic service withdrawal",
        EvaluationTemplateVersion = "TEST-1", EvaluationSnapshotDigest = new string('e', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.Equal(original.Id, (await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, request)).Value!.Id);
      Assert.Equal(original.Id, (await ClientSalesInvoiceDraftWorkspace.GetByCommandAsync(db, preparer, scope.ClientA, request.CommandId)).Value!.Id);
      Assert.True((await ClientSalesInvoiceDraftWorkspace.GetAsync(db, reviewer, scope.ClientA, original.InvoiceId)).Succeeded);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, request with { CommandId = Guid.CreateVersion7(), DraftReference = "WITHDRAWN-NEW" })).Succeeded);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, preparer, scope.ClientA, revised with { CommandId = Guid.CreateVersion7(), ExpectedRevision = 2 })).Succeeded);
      Assert.Equal(3, await db.ClientSalesInvoiceDrafts.CountAsync());
    }
    foreach (var sql in new[] { "UPDATE client_sales_invoice_drafts SET gross_amount=1 WHERE id={0}", "DELETE FROM client_sales_invoice_drafts WHERE id={0}" })
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, original.Id)); Assert.Equal("23514", error.SqlState);
    }
  }
}
