using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class BillingCommandScopeIsolationTests
{
  [Fact]
  public async Task ClientScopedFinanceCommandsCannotReadOrMutateSiblingClientBillingRecords()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var sibling = await SiblingClientSeed.SeedAsync(pg, own.FirmId, "BILLING-HIDDEN-CLIENT-B");
    try
    {
      var ownManager = PbcSeed.User(own.FirmId, "Staff");
      var ownReviewer = PbcSeed.User(own.FirmId, "Staff");
      var siblingManager = PbcSeed.User(own.FirmId, "Staff");
      var siblingReviewer = PbcSeed.User(own.FirmId, "Staff");
      Guid siblingAccountId;
      Guid siblingInvoiceId;
      Guid siblingReceiptId;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        db.Users.AddRange(ownManager, ownReviewer, siblingManager, siblingReviewer);
        db.RoleGrants.AddRange(
          PbcSeed.Grant(own.FirmId, ownManager, "FinanceManager", own.ClientId),
          PbcSeed.Grant(own.FirmId, ownReviewer, "FinanceReviewer", own.ClientId),
          PbcSeed.Grant(own.FirmId, siblingManager, "FinanceManager", sibling.Fixture.ClientId),
          PbcSeed.Grant(own.FirmId, siblingReviewer, "FinanceReviewer", sibling.Fixture.ClientId));
        await db.SaveChangesAsync();

        var siblingManagerActor = PbcSeed.Actor(siblingManager, "FinanceManager");
        var siblingReviewerActor = PbcSeed.Actor(siblingReviewer, "FinanceReviewer");
        var account = await BillingService.CreateBillingAccountAsync(db, siblingManagerActor,
          new(sibling.Fixture.ClientId, "QAR"));
        Assert.True(account.Succeeded, account.Message);
        siblingAccountId = account.Value;
        var invoice = await BillingService.CreateInvoiceDraftAsync(db, siblingManagerActor,
          new(siblingAccountId, "SYN-BILLING-B-001", [new("Synthetic approved service", 1m, 100m)]));
        Assert.True(invoice.Succeeded, invoice.Message);
        siblingInvoiceId = invoice.Value;
        Assert.True((await BillingService.SubmitInvoiceAsync(db, siblingManagerActor, siblingInvoiceId)).Succeeded);
        Assert.True((await BillingService.ApproveInvoiceAsync(db, siblingReviewerActor, siblingInvoiceId)).Succeeded);
        var receipt = await BillingService.RecordReceiptAsync(db, siblingManagerActor,
          new(siblingAccountId, 50m, "SYN-BILLING-B-RECEIPT"));
        Assert.True(receipt.Succeeded, receipt.Message);
        siblingReceiptId = receipt.Value;
      }

      var manager = PbcSeed.Actor(ownManager, "FinanceManager");
      var reviewer = PbcSeed.Actor(ownReviewer, "FinanceReviewer");
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.CreateBillingAccountAsync(db, manager,
          new(sibling.Fixture.ClientId, "QAR"))).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.CreateInvoiceDraftAsync(db, manager,
          new(siblingAccountId, "SYN-UNAUTHORIZED-BILLING-001", [new("Must not persist", 1m, 1m)]))).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.SubmitInvoiceAsync(db, manager, siblingInvoiceId)).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.ApproveInvoiceAsync(db, reviewer, siblingInvoiceId)).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.PostInvoiceAsync(db, manager, siblingInvoiceId)).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.SendInvoiceAsync(db, manager, siblingInvoiceId)).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.RecordReceiptAsync(db, manager,
          new(siblingAccountId, 1m, "SYN-UNAUTHORIZED-RECEIPT"))).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.AllocateReceiptAsync(db, manager,
          new(siblingReceiptId, siblingInvoiceId, 1m))).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.IssueCreditNoteAsync(db, manager,
          new(siblingInvoiceId, "SYN-UNAUTHORIZED-CREDIT", 1m, "Must not persist"))).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.GetInvoiceBalanceAsync(db, manager, siblingInvoiceId)).ErrorCode);
        Assert.Equal(ErrorCodes.ScopeDenied, (await BillingService.GetInvoiceDetailAsync(db, manager, siblingInvoiceId)).ErrorCode);
        Assert.False(await BillingService.CanOpenInvoiceAsync(db, manager, siblingInvoiceId));
      }

      await using (var verify = new AuditSphereDbContext(pg.Options))
      {
        Assert.Equal(1, await verify.BillingAccounts.CountAsync(x => x.FirmId == own.FirmId && x.PracticeClientId == sibling.Fixture.ClientId));
        Assert.Equal(1, await verify.Invoices.CountAsync(x => x.FirmId == own.FirmId && x.BillingAccountId == siblingAccountId));
        Assert.Equal(1, await verify.Receipts.CountAsync(x => x.FirmId == own.FirmId && x.BillingAccountId == siblingAccountId));
        Assert.Empty(await verify.ReceiptAllocations.Where(x => x.FirmId == own.FirmId && x.ReceiptId == siblingReceiptId).ToListAsync());
        Assert.Empty(await verify.CreditNotes.Where(x => x.FirmId == own.FirmId && x.InvoiceId == siblingInvoiceId).ToListAsync());
        Assert.Equal(BillingStates.InvoiceApproved,
          (await verify.Invoices.SingleAsync(x => x.Id == siblingInvoiceId && x.FirmId == own.FirmId)).Status);
      }
    }
    finally
    {
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }
  }
}
