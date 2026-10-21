using AuditSphereOps.Application.Completion;
using System.Data.Common;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class ClientPortalActivityQueryTests
{
  [Fact]
  public async Task AggregatePagesAreOrderedAndRequestEventsStayWithTheirParticipant()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var sibling = PbcSeed.User(f.FirmId, "Client");
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.Users.Add(sibling);
      seed.RoleGrants.Add(PbcSeed.Grant(f.FirmId, sibling, "ClientUser", f.ClientId, f.EngagementId));
      seed.ClientPortalFirstSignIns.Add(PbcSeed.FirstSignIn(sibling));
      await seed.SaveChangesAsync();
    }

    var ownRequestId = await SentRequestAsync(pg, f, f.Client.Id);
    var siblingRequestId = await SentRequestAsync(pg, f, sibling.Id);
    var (reviewId, sharedAt) = await SharedReviewAsync(pg, f);

    var communicationIds = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToArray();
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.PbcCommunications.AddRange(communicationIds.Select((id, index) => new PbcCommunication
      {
        Id = id, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PbcRequestId = ownRequestId, AuthorUserId = f.Client.Id, Kind = PbcCommunicationKinds.ClientMessage,
        Body = $"Synthetic client communication {index:D2}", CreatedAt = sharedAt
      }));
      await seed.SaveChangesAsync();
    }

    string[] expectedKeys;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var orderedCommunicationIds = await db.PbcCommunications.AsNoTracking()
        .Where(x => communicationIds.Contains(x.Id)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
      var requestCommunicationId = await db.PbcCommunications.AsNoTracking()
        .Where(x => x.PbcRequestId == ownRequestId && x.Kind == PbcCommunicationKinds.Request)
        .Select(x => x.Id).SingleAsync();
      expectedKeys = new[] { $"DOCUMENT_SHARED:{reviewId:D}" }
        .Concat(orderedCommunicationIds.Select(id => $"PBC_COMMUNICATION:{id:D}"))
        .Append($"PBC_COMMUNICATION:{requestCommunicationId:D}").ToArray();

      var actor = PbcSeed.Actor(f.Client, "ClientUser");
      var first = await ClientPortalActivityQuery.GetAsync(db, actor);
      Assert.True(first.Succeeded, first.Message);
      Assert.Equal(25, first.Value!.PageSize);
      Assert.Equal(25, first.Value.Items.Count);
      Assert.True(first.Value.HasMore);
      Assert.NotNull(first.Value.NextCursor);
      Assert.Equal(expectedKeys.Take(25), first.Value.Items.Select(x => x.Key));
      Assert.Equal("document-review", first.Value.Items[0].TargetKind);

      var second = await ClientPortalActivityQuery.GetAsync(db, actor, cursor: first.Value.NextCursor);
      Assert.True(second.Succeeded, second.Message);
      Assert.Equal(first.Value.SnapshotAtUtc, second.Value!.SnapshotAtUtc);
      Assert.False(second.Value.HasMore);
      Assert.Null(second.Value.NextCursor);
      Assert.Equal(expectedKeys.Skip(25), second.Value.Items.Select(x => x.Key));

      var pages = first.Value.Items.Concat(second.Value.Items).ToArray();
      Assert.Equal(expectedKeys, pages.Select(x => x.Key));
      Assert.Equal(expectedKeys.Length, pages.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count());
      Assert.DoesNotContain(pages, x => x.TargetKind == "pbc-request" && x.TargetId == siblingRequestId);
      Assert.Contains(pages, x => x.TargetKind == "pbc-request" && x.TargetId == ownRequestId);
    }
  }

  [Fact]
  public async Task ReceiptEventsRequireSameAuthorizedBillingAccountBeforeTheBoundedPage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var foreignClientId = Guid.NewGuid();
    var ownAccountId = Guid.NewGuid();
    var foreignAccountId = Guid.NewGuid();
    var leadId = Guid.NewGuid();
    var opportunityId = Guid.NewGuid();
    var proposalId = Guid.NewGuid();
    var agreementId = Guid.NewGuid();
    var invoiceId = Guid.NewGuid();
    var milestoneId = Guid.NewGuid();
    var ownReceiptId = Guid.NewGuid();
    var ownAllocationId = Guid.NewGuid();
    var baseTicks = DateTimeOffset.UtcNow.UtcTicks;
    var eventBase = new DateTimeOffset(baseTicks - baseTicks % 10, TimeSpan.Zero).AddMinutes(-10);
    var ownReceiptAt = eventBase.AddSeconds(-1);
    var foreignReceipts = Enumerable.Range(1, 26).Select(index => new Receipt
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = foreignAccountId, Amount = 10m, Currency = "QAR",
      Reference = $"SYN-UNAUTHORIZED-RECEIPT-{index:D2}", Status = BillingStates.ReceiptRecorded,
      ReceivedAt = eventBase.AddSeconds(index)
    }).ToArray();

    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.PracticeClients.Add(new PracticeClient
      {
        Id = foreignClientId, FirmId = f.FirmId, LegalName = "UNAUTHORIZED RECEIPT CLIENT SENTINEL",
        CreatedAt = eventBase.AddMinutes(-1)
      });
      seed.BillingAccounts.AddRange(
        new BillingAccount { Id = ownAccountId, FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "QAR", CreatedAt = eventBase.AddMinutes(-1) },
        new BillingAccount { Id = foreignAccountId, FirmId = f.FirmId, PracticeClientId = foreignClientId, Currency = "QAR", CreatedAt = eventBase.AddMinutes(-1) });
      seed.Leads.Add(new Lead { Id = leadId, FirmId = f.FirmId, Name = "Synthetic portal activity lead", Source = "TEST", CreatedAt = eventBase.AddMinutes(-1) });
      seed.Opportunities.Add(new Opportunity
      {
        Id = opportunityId, FirmId = f.FirmId, LeadId = leadId, PracticeClientId = f.ClientId,
        ServiceRoute = "FinancialStatementAudit", EntityScope = "Synthetic test scope", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", ExpectedFee = 1000m, Currency = "QAR", CreatedAt = eventBase.AddMinutes(-1)
      });
      seed.Proposals.Add(new Proposal
      {
        Id = proposalId, FirmId = f.FirmId, OpportunityId = opportunityId, PracticeClientId = f.ClientId,
        ServiceProfileId = "SYNTHETIC", Scope = "Synthetic fixture only", Exclusions = "None",
        Deliverables = "Synthetic fixture only", Dependencies = "None", Fee = 1000m, Currency = "QAR",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = eventBase.AddMinutes(-1)
      });
      seed.EngagementFeeAgreements.Add(new EngagementFeeAgreement
      {
        Id = agreementId, FirmId = f.FirmId, ProposalId = proposalId, QuotationVersionId = Guid.NewGuid(),
        PracticeClientId = f.ClientId, EngagementId = f.EngagementId, Currency = "QAR", AgreedFee = 1000m,
        CreatedByUserId = f.Admin.Id, CreatedAt = eventBase.AddMinutes(-1)
      });
      seed.Invoices.Add(new Invoice
      {
        Id = invoiceId, FirmId = f.FirmId, BillingAccountId = ownAccountId, InvoiceNumber = "SYN-PORTAL-ACTIVITY-001",
        Currency = "QAR", Subtotal = 1000m, Tax = 0m, Total = 1000m, Status = BillingStates.InvoicePosted,
        PostedAt = eventBase.AddSeconds(-2), CreatedAt = eventBase.AddMinutes(-1)
      });
      seed.FeeMilestones.Add(new FeeMilestone
      {
        Id = milestoneId, FirmId = f.FirmId, AgreementId = agreementId, Kind = FeeMilestoneKinds.Advance,
        Amount = 1000m, State = FeeMilestoneStates.Invoiced, InvoiceId = invoiceId, CreatedAt = eventBase.AddMinutes(-1)
      });
      seed.Receipts.Add(new Receipt
      {
        Id = ownReceiptId, FirmId = f.FirmId, BillingAccountId = ownAccountId, Amount = 10m, Currency = "QAR",
        Reference = "SYN-AUTHORIZED-RECEIPT", Status = BillingStates.ReceiptRecorded, ReceivedAt = ownReceiptAt
      });
      seed.Receipts.AddRange(foreignReceipts);
      seed.ReceiptAllocations.Add(new ReceiptAllocation
      {
        Id = ownAllocationId, FirmId = f.FirmId, ReceiptId = ownReceiptId, InvoiceId = invoiceId,
        Amount = 1m, CreatedAt = eventBase
      });
      // Synthetic malformed allocation: each same-firm FK is valid, but these receipts belong to an ungranted client's account.
      seed.ReceiptAllocations.AddRange(foreignReceipts.Select(receipt => new ReceiptAllocation
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ReceiptId = receipt.Id, InvoiceId = invoiceId,
        Amount = 1m, CreatedAt = eventBase
      }));
      await seed.SaveChangesAsync();
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await ClientPortalActivityQuery.GetAsync(db, PbcSeed.Actor(f.Client, "ClientUser"));

    Assert.True(result.Succeeded, result.Message);
    var ownReceipt = Assert.Single(result.Value!.Items, x => x.Key == $"PAYMENT_RECEIPT:{ownReceiptId:D}");
    Assert.Equal(ownReceiptAt.ToString("O"), ownReceipt.At);
    Assert.Equal("PBC TEST CLIENT", ownReceipt.Context.ClientLegalName);
    Assert.DoesNotContain(result.Value.Items, x => foreignReceipts.Any(receipt => x.Key == $"PAYMENT_RECEIPT:{receipt.Id:D}"));
    Assert.DoesNotContain(result.Value.Items, x => foreignReceipts.Any(receipt => x.At == receipt.ReceivedAt.ToString("O")));
    Assert.DoesNotContain(result.Value.Items, x => x.Context.ClientLegalName == "UNAUTHORIZED RECEIPT CLIENT SENTINEL");
    Assert.False(result.Value.HasMore);
    Assert.Null(result.Value.NextCursor);
  }

  [Fact]
  public async Task QueryRefusesCallerTransactionAndWithdrawsProjectionWhenClientGrantChangesAfterSnapshot()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var requestId = await SentRequestAsync(pg, f, f.Client.Id);
    var actor = PbcSeed.Actor(f.Client, "ClientUser");

    await using (var caller = new AuditSphereDbContext(pg.Options))
    await using (var transaction = await caller.Database.BeginTransactionAsync())
    {
      var refused = await ClientPortalActivityQuery.GetAsync(caller, actor);
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
      Assert.Null(refused.Value);
      Assert.Equal(1, await caller.Database.SqlQuery<int>($"SELECT 1 AS \"Value\"").SingleAsync());
    }

    var revocations = 0;
    var interceptor = new RevokeClientGrantAfterCommunicationRead(async ct =>
    {
      await using var revoke = new AuditSphereDbContext(pg.Options);
      revocations = await revoke.RoleGrants.Where(x => x.FirmId == f.FirmId && x.UserId == f.Client.Id &&
          x.Role == "ClientUser" && x.ClientId == f.ClientId && x.EngagementId == f.EngagementId && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var reader = new AuditSphereDbContext(options);
    var result = await ClientPortalActivityQuery.GetAsync(reader, actor);

    Assert.Equal(1, interceptor.Calls);
    Assert.Equal(1, revocations);
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    Assert.Null(result.Value);
    Assert.True(await reader.PbcCommunications.AnyAsync(x => x.PbcRequestId == requestId));
  }

  private static async Task<Guid> SentRequestAsync(PgTestSchema pg, PbcSeed.Fixture f, Guid clientOwner)
  {
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    Guid id;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(
        f.EngagementId, "Bank statements", "TEST ENTITY", "2026-01-01", "2026-12-31", "Cash", "PDF or CSV", "12 months",
        clientOwner, f.Staff.Id, f.Reviewer.Id, "2027-01-31", "Confidential", "Complete period."));
      Assert.True(created.Succeeded, created.Message);
      id = created.Value;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await PbcService.ChangeStateAsync(db, staff, new PbcStateChangeRequest(id, PbcStates.Sent, 1))).Succeeded);
    return id;
  }

  private static async Task<(Guid ReviewId, DateTimeOffset SharedAt)> SharedReviewAsync(PgTestSchema pg, PbcSeed.Fixture f)
  {
    var deliverableId = Guid.NewGuid();
    Guid reviewId;
    DateTimeOffset sharedAt;
    await using var db = new AuditSphereDbContext(pg.Options);
    var content = new byte[] { 1 };
    db.AuditDeliverables.Add(new AuditDeliverable
    {
      Id = deliverableId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      Kind = "INDEPENDENT_AUDITORS_REPORT", InputDigest = new string('a', 64), Content = content,
      ContentSha256 = Hashing.Sha256Hex(content), TemplateVersion = "SYNTHETIC", FileName = "synthetic.pdf",
      ContentType = "application/pdf", CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var shared = await AuditDeliverableService.ShareWithClientAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), deliverableId);
    Assert.True(shared.Succeeded, shared.Message);
    reviewId = shared.Value;
    var review = await db.ClientDeliverableReviews.AsNoTracking().SingleAsync(x => x.Id == reviewId);
    sharedAt = review.SharedAt;
    return (reviewId, sharedAt);
  }

  private sealed class RevokeClientGrantAfterCommunicationRead(Func<CancellationToken, Task> revoke) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => Volatile.Read(ref calls);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("pbc_communications", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref calls, 1, 0) == 0)
        await revoke(cancellationToken);
      return result;
    }
  }
}
