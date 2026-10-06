using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Search;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// UX-029 bounded navigation search: every hit must be openable by the same actor through the route it links to,
/// and records outside the actor's current grants never appear, are never counted and never shape a snippet.
/// </summary>
[Trait("Profile", "Database")]
public sealed class GlobalSearchQueryTests
{
  private const string Marker = "ZQXSEARCH";

  private sealed record World(PgTestSchema Pg, PbcSeed.Fixture Own, SiblingClientSeed.Sibling Sibling) : IAsyncDisposable
  {
    public AuditSphereDbContext Db() => new(Pg.Options);
    public async ValueTask DisposeAsync()
    {
      PbcSeed.DeleteDirectory(Sibling.StagingRoot);
      await Pg.DisposeAsync();
    }
  }

  private static async Task<World> SeedAsync()
  {
    var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, own, PbcSeed.Actor(own.Staff, "Staff"), PbcSeed.Actor(own.Client, "ClientUser"));
    var sibling = await SiblingClientSeed.SeedAsync(pg, own.FirmId, Marker);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Leads.Add(new Lead { Id = Guid.NewGuid(), FirmId = own.FirmId, Name = $"{Marker} Prospect Lead", Source = "Referral", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    return new(pg, own, sibling);
  }

  private static async Task<(AppUser User, ActorContext Actor)> UserAsync(World w, params (string Role, Guid? ClientId, Guid? EngagementId)[] grants)
  {
    var user = PbcSeed.User(w.Own.FirmId, "Staff");
    await using var db = w.Db();
    db.Users.Add(user);
    foreach (var (role, client, engagement) in grants)
      db.RoleGrants.Add(PbcSeed.Grant(w.Own.FirmId, user, role, client, engagement));
    await db.SaveChangesAsync();
    return (user, new ActorContext(user.Id, user.FirmId, user.SessionEpoch, grants.Select(g => g.Role).Distinct().ToArray()));
  }

  private static async Task<GlobalSearchResult> SearchAsync(World w, ActorContext actor, string term)
  {
    await using var db = w.Db();
    var result = await GlobalSearchQuery.SearchAsync(db, actor, term);
    Assert.True(result.Succeeded, result.Message);
    return result.Value!;
  }

  [Fact]
  public async Task ClientScopedUser_FindsOwnClientEngagementAndRequest_NeverTheSibling()
  {
    await using var w = await SeedAsync();
    var (_, actor) = await UserAsync(w, ("Staff", w.Own.ClientId, null), ("Partner", w.Own.ClientId, null));

    var own = await SearchAsync(w, actor, "pbc test");
    Assert.Contains(own.Hits, x => x.Kind == GlobalSearchQuery.Kinds.Client && x.Href == $"/app/clients/{w.Own.ClientId:D}");
    Assert.Contains(own.Hits, x => x.Kind == GlobalSearchQuery.Kinds.Engagement && x.Href == $"/app/engagements/{w.Own.EngagementId:D}");
    Assert.Contains(own.Hits, x => x.Kind == GlobalSearchQuery.Kinds.PbcRequest && x.Href == $"/app/engagements/{w.Own.EngagementId:D}/pbc");

    var sibling = await SearchAsync(w, actor, Marker);
    Assert.Empty(sibling.Hits);
    Assert.False(sibling.Truncated);
    Assert.DoesNotContain(own.Hits, x => x.Title.Contains(Marker, StringComparison.OrdinalIgnoreCase) || x.Detail.Contains(Marker, StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task EngagementOnlyUser_GetsNoClientLevelHitAndNoSiblingEngagement()
  {
    await using var w = await SeedAsync();
    var otherEngagement = Guid.NewGuid();
    await using (var db = w.Db())
    {
      db.Engagements.Add(new Domain.Engagements.Engagement { Id = otherEngagement, FirmId = w.Own.FirmId, PracticeClientId = w.Own.ClientId,
        ServiceRoute = "SiblingEngagementRoute", Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
    }
    var (_, actor) = await UserAsync(w, ("Staff", w.Own.ClientId, w.Own.EngagementId));

    var result = await SearchAsync(w, actor, "pbc test");
    Assert.DoesNotContain(result.Hits, x => x.Kind == GlobalSearchQuery.Kinds.Client);
    Assert.Contains(result.Hits, x => x.Href == $"/app/engagements/{w.Own.EngagementId:D}");
    Assert.DoesNotContain(result.Hits, x => x.Href.Contains(otherEngagement.ToString("D"), StringComparison.Ordinal));
    Assert.DoesNotContain((await SearchAsync(w, actor, "SiblingEngagementRoute")).Hits, x => x.Kind != GlobalSearchQuery.Kinds.Page);
  }

  [Fact]
  public async Task FirmWideUser_FindsBothClients_AndCommercialRoleGatesLeads()
  {
    await using var w = await SeedAsync();
    var (_, partner) = await UserAsync(w, ("Partner", null, null));
    var both = await SearchAsync(w, partner, "holdings");
    Assert.Contains(both.Hits, x => x.Href == $"/app/clients/{w.Sibling.Fixture.ClientId:D}");
    Assert.Contains((await SearchAsync(w, partner, Marker)).Hits, x => x.Kind == GlobalSearchQuery.Kinds.Lead);

    // A client-scoped Partner is not a firm-wide commercial identity, so leads stay hidden.
    var (_, scoped) = await UserAsync(w, ("Partner", w.Own.ClientId, null));
    Assert.DoesNotContain((await SearchAsync(w, scoped, Marker)).Hits, x => x.Kind == GlobalSearchQuery.Kinds.Lead);
  }

  [Fact]
  public async Task FinanceSearch_ReturnsOnlyInvoicesInsideTheActorFirmAndClientScope()
  {
    await using var w = await SeedAsync();
    var now = DateTimeOffset.UtcNow;
    var ownInvoiceId = Guid.NewGuid();
    var siblingInvoiceId = Guid.NewGuid();
    var foreignFirmId = Guid.NewGuid();
    var foreignClientId = Guid.NewGuid();
    var foreignAccountId = Guid.NewGuid();
    var foreignInvoiceId = Guid.NewGuid();
    await using (var db = w.Db())
    {
      var ownAccountId = Guid.NewGuid();
      var siblingAccountId = Guid.NewGuid();
      db.BillingAccounts.AddRange(
        new BillingAccount { Id = ownAccountId, FirmId = w.Own.FirmId, PracticeClientId = w.Own.ClientId, Currency = "QAR", CreatedAt = now },
        new BillingAccount { Id = siblingAccountId, FirmId = w.Own.FirmId, PracticeClientId = w.Sibling.Fixture.ClientId, Currency = "QAR", CreatedAt = now },
        new BillingAccount { Id = foreignAccountId, FirmId = foreignFirmId, PracticeClientId = foreignClientId, Currency = "QAR", CreatedAt = now });
      db.FirmSafetyStates.Add(new FirmSafetyState { Id = foreignFirmId });
      db.PracticeClients.Add(new PracticeClient
      {
        Id = foreignClientId, FirmId = foreignFirmId, LegalName = $"{Marker} Foreign Billing Client", CreatedAt = now
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = foreignClientId, FirmId = foreignFirmId });
      db.Invoices.AddRange(
        new Invoice { Id = ownInvoiceId, FirmId = w.Own.FirmId, BillingAccountId = ownAccountId, InvoiceNumber = $"{Marker}-OWN-INV", Currency = "QAR", Subtotal = 100, Total = 100, Status = BillingStates.InvoiceDraft, CreatedAt = now },
        new Invoice { Id = siblingInvoiceId, FirmId = w.Own.FirmId, BillingAccountId = siblingAccountId, InvoiceNumber = $"{Marker}-SIBLING-INV", Currency = "QAR", Subtotal = 200, Total = 200, Status = BillingStates.InvoiceDraft, CreatedAt = now },
        new Invoice { Id = foreignInvoiceId, FirmId = foreignFirmId, BillingAccountId = foreignAccountId, InvoiceNumber = $"{Marker}-FOREIGN-INV", Currency = "QAR", Subtotal = 300, Total = 300, Status = BillingStates.InvoiceDraft, CreatedAt = now });
      await db.SaveChangesAsync();
    }

    var (_, scopedFinance) = await UserAsync(w, ("FinanceManager", w.Own.ClientId, null));
    var scoped = await SearchAsync(w, scopedFinance, Marker);
    Assert.Equal([$"/app/practice/invoices/{ownInvoiceId:D}"], scoped.Hits
      .Where(x => x.Kind == GlobalSearchQuery.Kinds.Invoice).Select(x => x.Href));
    Assert.DoesNotContain(scoped.Hits, x => x.Title.Contains("SIBLING", StringComparison.OrdinalIgnoreCase) ||
      x.Title.Contains("FOREIGN", StringComparison.OrdinalIgnoreCase));

    var (_, staff) = await UserAsync(w, ("Staff", w.Own.ClientId, null));
    Assert.DoesNotContain((await SearchAsync(w, staff, Marker)).Hits, x => x.Kind == GlobalSearchQuery.Kinds.Invoice);

    var (_, firmFinance) = await UserAsync(w, ("FinanceReviewer", null, null));
    var firmWide = await SearchAsync(w, firmFinance, Marker);
    Assert.Contains(firmWide.Hits, x => x.Href == $"/app/practice/invoices/{ownInvoiceId:D}");
    Assert.Contains(firmWide.Hits, x => x.Href == $"/app/practice/invoices/{siblingInvoiceId:D}");
    Assert.DoesNotContain(firmWide.Hits, x => x.Href == $"/app/practice/invoices/{foreignInvoiceId:D}");
  }

  [Fact]
  public async Task SearchResultKindsFollowCapabilitySpecificRolesAndScopes()
  {
    await using var w = await SeedAsync();
    await using (var db = w.Db())
    {
      var accountId = Guid.NewGuid();
      db.BillingAccounts.Add(new BillingAccount
      {
        Id = accountId, FirmId = w.Own.FirmId, PracticeClientId = w.Own.ClientId,
        Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Invoices.Add(new Invoice
      {
        Id = Guid.NewGuid(), FirmId = w.Own.FirmId, BillingAccountId = accountId,
        InvoiceNumber = "ROLE-MATRIX-INVOICE", Currency = "QAR", Subtotal = 100, Total = 100,
        Status = BillingStates.InvoiceDraft, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var (_, commercialManager) = await UserAsync(w, ("CommercialManager", w.Own.ClientId, null));
    var (_, engagementLeader) = await UserAsync(w, ("EngagementLeader", w.Own.ClientId, null));
    var (_, auditor) = await UserAsync(w, ("Auditor", w.Own.ClientId, w.Own.EngagementId));
    var (_, accountant) = await UserAsync(w, ("Accountant", w.Own.ClientId, w.Own.EngagementId));
    var (_, financeManager) = await UserAsync(w, ("FinanceManager", w.Own.ClientId, null));
    var (_, relationshipManager) = await UserAsync(w, ("RelationshipManager", null, null));

    static string[] RecordKinds(GlobalSearchResult result) => result.Hits
      .Where(hit => hit.Kind != GlobalSearchQuery.Kinds.Page)
      .Select(hit => hit.Kind).Distinct(StringComparer.Ordinal).ToArray();

    Assert.Equal([GlobalSearchQuery.Kinds.Client], RecordKinds(await SearchAsync(w, commercialManager, "PBC TEST CLIENT")));
    Assert.Equal([GlobalSearchQuery.Kinds.Client, GlobalSearchQuery.Kinds.Engagement],
      RecordKinds(await SearchAsync(w, engagementLeader, "PBC TEST CLIENT")));
    Assert.Equal([GlobalSearchQuery.Kinds.Engagement, GlobalSearchQuery.Kinds.PbcRequest],
      RecordKinds(await SearchAsync(w, auditor, "PBC TEST CLIENT")));
    Assert.Equal([GlobalSearchQuery.Kinds.PbcRequest],
      RecordKinds(await SearchAsync(w, accountant, "PBC TEST CLIENT")));
    Assert.Equal([GlobalSearchQuery.Kinds.Invoice],
      RecordKinds(await SearchAsync(w, financeManager, "PBC TEST CLIENT")));
    Assert.Equal([GlobalSearchQuery.Kinds.Lead],
      RecordKinds(await SearchAsync(w, relationshipManager, Marker)));
  }

  [Fact]
  public async Task SearchCapsEachKindAndSignalsThatMoreMatchesExist()
  {
    await using var w = await SeedAsync();
    const string term = "CAPBOUNDARY";
    await using (var db = w.Db())
    {
      db.Leads.AddRange(Enumerable.Range(0, 7).Select(index => new Lead
      {
        Id = Guid.NewGuid(), FirmId = w.Own.FirmId, Name = $"{term}-Prospect-{index:D2}",
        Source = "Referral", CreatedAt = DateTimeOffset.UtcNow.AddSeconds(index)
      }));
      await db.SaveChangesAsync();
    }
    var (_, partner) = await UserAsync(w, ("Partner", null, null));

    var result = await SearchAsync(w, partner, term);
    var leads = result.Hits.Where(x => x.Kind == GlobalSearchQuery.Kinds.Lead).ToArray();
    Assert.Equal(6, leads.Length);
    Assert.All(leads, hit => Assert.Contains(term, hit.Title, StringComparison.OrdinalIgnoreCase));
    Assert.True(result.Truncated);
  }

  [Fact]
  public async Task ClientEngagementPbcAndInvoiceCapsSignalWhenMoreThanSixMatchesExist()
  {
    await using var w = await SeedAsync();
    const string term = "MULTIKINDCAP";
    var now = DateTimeOffset.UtcNow;
    await using (var db = w.Db())
    {
      var clients = Enumerable.Range(0, 7).Select(index => new PracticeClient
      {
        Id = Guid.NewGuid(), FirmId = w.Own.FirmId, LegalName = $"{term} Client {index:D2}", CreatedAt = now
      }).ToArray();
      db.PracticeClients.AddRange(clients);
      db.ClientSafetyStates.AddRange(clients.Select(client => new ClientSafetyState
      {
        Id = client.Id, FirmId = w.Own.FirmId
      }));
      db.Engagements.AddRange(Enumerable.Range(0, 7).Select(index => new Engagement
      {
        Id = Guid.NewGuid(), FirmId = w.Own.FirmId, PracticeClientId = w.Own.ClientId,
        ServiceRoute = $"{term} Engagement {index:D2}", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
      }));
      db.PbcRequests.AddRange(Enumerable.Range(0, 7).Select(index => new PbcRequest
      {
        Id = Guid.NewGuid(), FirmId = w.Own.FirmId, ClientId = w.Own.ClientId, EngagementId = w.Own.EngagementId,
        Objective = $"{term} PBC request {index:D2}", EntityScope = "TEST ENTITY", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", Area = "Cash", RequestedFormat = "PDF", ControlTotals = "12 months",
        ClientOwnerUserId = w.Own.Client.Id, FirmOwnerUserId = w.Own.Staff.Id, ReviewerUserId = w.Own.Reviewer.Id,
        DueDate = "2027-01-31", Confidentiality = "Confidential", AcceptanceCriteria = "Complete period.",
        State = PbcStates.Sent, CreatedAt = now, CreatedByUserId = w.Own.Staff.Id, UpdatedAt = now
      }));

      var billingAccountId = Guid.NewGuid();
      db.BillingAccounts.Add(new BillingAccount
      {
        Id = billingAccountId, FirmId = w.Own.FirmId, PracticeClientId = w.Own.ClientId,
        Currency = "QAR", CreatedAt = now
      });
      db.Invoices.AddRange(Enumerable.Range(0, 7).Select(index => new Invoice
      {
        Id = Guid.NewGuid(), FirmId = w.Own.FirmId, BillingAccountId = billingAccountId,
        InvoiceNumber = $"{term}-INV-{index:D2}", Currency = "QAR", Subtotal = 100, Total = 100,
        Status = BillingStates.InvoiceDraft, CreatedAt = now.AddSeconds(index)
      }));
      await db.SaveChangesAsync();
    }

    var (_, actor) = await UserAsync(w, ("Partner", null, null), ("FinanceReviewer", null, null));
    var result = await SearchAsync(w, actor, term);
    foreach (var kind in new[]
      {
        GlobalSearchQuery.Kinds.Client, GlobalSearchQuery.Kinds.Engagement,
        GlobalSearchQuery.Kinds.PbcRequest, GlobalSearchQuery.Kinds.Invoice
      })
    {
      var hits = result.Hits.Where(hit => hit.Kind == kind).ToArray();
      Assert.Equal(6, hits.Length);
      Assert.All(hits, hit => Assert.Contains(term, hit.Title, StringComparison.OrdinalIgnoreCase));
    }
    Assert.True(result.Truncated);
  }

  [Fact]
  public async Task LibraryAndPageCapsSignalWhenMoreThanSixResultsExist()
  {
    await using var w = await SeedAsync();
    var (_, manager) = await UserAsync(w, ("Manager", null, null));
    var (_, partner) = await UserAsync(w, ("Partner", null, null));
    await using var db = w.Db();

    for (var index = 0; index < 7; index++)
    {
      var created = await TechnicalLibraryService.CreateAsync(db, manager,
        $"ZQX-LIB-CAP-{index:D2}", $"Library cap item {index:D2}", TechnicalLibraryCategories.Isa,
        TechnicalLibraryAudiences.AllStaff, $"CAPLIBRARYTERM published policy content {index:D2}",
        "Approved test source", new DateOnly(2026, 1, 1));
      Assert.True(created.Succeeded, created.Message);
      var versionId = await db.TechnicalLibraryVersions.Where(x => x.DocumentId == created.Value)
        .Select(x => x.Id).SingleAsync();
      var published = await TechnicalLibraryService.PublishAsync(db, partner, versionId);
      Assert.True(published.Succeeded, published.Message);
    }

    var pages = await SearchAsync(w, manager, "re");
    var pageHits = pages.Hits.Where(x => x.Kind == GlobalSearchQuery.Kinds.Page).ToArray();
    Assert.Equal(6, pageHits.Length);
    Assert.True(pages.Truncated);

    var library = await SearchAsync(w, manager, "CAPLIBRARYTERM");
    var libraryHits = library.Hits.Where(x => x.Kind == GlobalSearchQuery.Kinds.Library).ToArray();
    Assert.Equal(6, libraryHits.Length);
    Assert.True(library.Truncated);
  }

  [Fact]
  public async Task TechnicalLibraryHitsRespectAudiencePublicationAndFirmBoundary()
  {
    await using var w = await SeedAsync();
    var (manager, managerActor) = await UserAsync(w, ("Manager", null, null));
    var (partner, partnerActor) = await UserAsync(w, ("Partner", null, null));
    var (_, staffActor) = await UserAsync(w, ("Staff", w.Own.ClientId, null));
    const string term = "ZQXLIBAUDIENCE";
    await using var db = w.Db();

    async Task<(Guid DocumentId, Guid VersionId)> CreateEntryAsync(string code, string audience, string body)
    {
      var created = await TechnicalLibraryService.CreateAsync(db, managerActor, code, $"{term} {code}",
        "ISA", audience, body, "Approved test source", new DateOnly(2026, 1, 1));
      Assert.True(created.Succeeded, created.Message);
      var versionId = await db.TechnicalLibraryVersions.Where(x => x.DocumentId == created.Value)
        .Select(x => x.Id).SingleAsync();
      return (created.Value, versionId);
    }

    var allStaff = await CreateEntryAsync("ZQX-ALL-STAFF", TechnicalLibraryAudiences.AllStaff,
      $"{term} guidance available to all staff.");
    var leadership = await CreateEntryAsync("ZQX-LEADERSHIP", TechnicalLibraryAudiences.PartnersAndManagers,
      $"{term} guidance limited to partners and managers.");
    var draft = await CreateEntryAsync("ZQX-DRAFT", TechnicalLibraryAudiences.AllStaff,
      $"{term} unpublished draft.");
    Assert.True((await TechnicalLibraryService.PublishAsync(db, partnerActor, allStaff.VersionId)).Succeeded);
    Assert.True((await TechnicalLibraryService.PublishAsync(db, partnerActor, leadership.VersionId)).Succeeded);

    // A matching published row in another firm must not cross the search boundary.
    var foreignFirmId = Guid.NewGuid();
    var foreignDocumentId = Guid.NewGuid();
    var foreignBody = $"{term} foreign firm policy.";
    db.TechnicalLibraryDocuments.Add(new TechnicalLibraryDocument
    {
      Id = foreignDocumentId, FirmId = foreignFirmId, Code = "ZQX-FOREIGN", Title = $"{term} foreign",
      Category = TechnicalLibraryCategories.Isa, Audience = TechnicalLibraryAudiences.AllStaff,
      CreatedByUserId = manager.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    db.TechnicalLibraryVersions.Add(new TechnicalLibraryVersion
    {
      Id = Guid.NewGuid(), FirmId = foreignFirmId, DocumentId = foreignDocumentId, Version = 1,
      Body = foreignBody, SourceReference = "Approved foreign test source", EffectiveFrom = new DateOnly(2026, 1, 1),
      Status = TechnicalLibraryStates.Published, ContentSha256 = Hashing.Sha256Hex(foreignBody),
      PreparedByUserId = manager.Id, PreparedAt = DateTimeOffset.UtcNow, ApprovedByUserId = partner.Id,
      PublishedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var staffHits = (await SearchAsync(w, staffActor, term)).Hits
      .Where(x => x.Kind == GlobalSearchQuery.Kinds.Library).ToArray();
    var managerHits = (await SearchAsync(w, managerActor, term)).Hits
      .Where(x => x.Kind == GlobalSearchQuery.Kinds.Library).ToArray();

    var staffHit = Assert.Single(staffHits);
    Assert.Equal($"/app/library/{allStaff.DocumentId:D}", staffHit.Href);
    Assert.DoesNotContain(staffHits, x => x.Href == $"/app/library/{leadership.DocumentId:D}");
    Assert.DoesNotContain(staffHits, x => x.Href == $"/app/library/{draft.DocumentId:D}");
    Assert.DoesNotContain(staffHits, x => x.Href == $"/app/library/{foreignDocumentId:D}");
    Assert.Contains(managerHits, x => x.Href == $"/app/library/{allStaff.DocumentId:D}");
    Assert.Contains(managerHits, x => x.Href == $"/app/library/{leadership.DocumentId:D}");
    Assert.DoesNotContain(managerHits, x => x.Href == $"/app/library/{draft.DocumentId:D}");
    Assert.DoesNotContain(managerHits, x => x.Href == $"/app/library/{foreignDocumentId:D}");
  }

  [Fact]
  public async Task ClientIdentitiesRevokedGrantsAndStaleSessionsGetNothing()
  {
    await using var w = await SeedAsync();
    await using (var db = w.Db())
    {
      var denied = await GlobalSearchQuery.SearchAsync(db, PbcSeed.Actor(w.Own.Client, "ClientUser"), "pbc test");
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }

    var (user, actor) = await UserAsync(w, ("Staff", w.Own.ClientId, null));
    Assert.Contains((await SearchAsync(w, actor, "pbc test")).Hits, x => x.Kind != GlobalSearchQuery.Kinds.Page);
    await using (var db = w.Db())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == user.Id);
      grant.RevokedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync();
    }
    Assert.DoesNotContain((await SearchAsync(w, actor, "pbc test")).Hits, x => x.Kind != GlobalSearchQuery.Kinds.Page);

    await using (var db = w.Db())
    {
      var stale = await GlobalSearchQuery.SearchAsync(db, actor with { SessionEpoch = actor.SessionEpoch + 1 }, "pbc test");
      Assert.Equal(ErrorCodes.ScopeDenied, stale.ErrorCode);
    }
  }

  [Theory]
  [InlineData("%")]
  [InlineData("%%")]
  [InlineData("__")]
  [InlineData("a")]
  [InlineData("   ")]
  public async Task WildcardsAndShortTermsNeverMatchEverything(string term)
  {
    await using var w = await SeedAsync();
    var (_, partner) = await UserAsync(w, ("Partner", null, null));
    Assert.DoesNotContain((await SearchAsync(w, partner, term)).Hits, x => x.Kind != GlobalSearchQuery.Kinds.Page);
  }

  [Fact]
  public async Task PagesAreNavigationOnlyAndLongTermsAreBounded()
  {
    await using var w = await SeedAsync();
    var (_, actor) = await UserAsync(w, ("Staff", w.Own.ClientId, null));
    var pages = await SearchAsync(w, actor, "consolidation");
    Assert.Contains(pages.Hits, x => x.Kind == GlobalSearchQuery.Kinds.Page && x.Href == "/app/consolidation");
    var longTerm = await SearchAsync(w, actor, new string('x', 500));
    Assert.Equal(GlobalSearchQuery.MaximumTermLength, longTerm.Term.Length);
  }
}
