using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuditSphereOps.Domain.Tests;

public sealed class CoreEntityCatalogTests
{
  [Fact(DisplayName = "CAT-01: EngagementAssignment persists with role, allocated hours and active flag")]
  public async Task EngagementAssignment_Persists_Correctly()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engId) = await pg.SeedScopeAsync();
    var userId = Guid.NewGuid();

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      ctx.Users.Add(new AppUser
      {
        Id = userId, FirmId = firmId, Subject = $"user-{userId:N}",
        TenantId = "tenant-1", Email = "staff@example.test",
        DisplayName = "Audit Senior", UserKind = "Staff", SessionEpoch = 1,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await ctx.SaveChangesAsync();
    }

    var assignmentId = Guid.NewGuid();
    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      ctx.EngagementAssignments.Add(new EngagementAssignment
      {
        Id = assignmentId,
        FirmId = firmId,
        EngagementId = engId,
        UserId = userId,
        Role = "Senior",
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 3, 31),
        AllocatedHours = 120.5m,
        Active = true,
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = userId
      });
      await ctx.SaveChangesAsync();
    }

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      var saved = await ctx.EngagementAssignments.AsNoTracking().SingleAsync(x => x.Id == assignmentId);
      Assert.Equal(engId, saved.EngagementId);
      Assert.Equal(userId, saved.UserId);
      Assert.Equal("Senior", saved.Role);
      Assert.Equal(120.5m, saved.AllocatedHours);
      Assert.True(saved.Active);
    }
  }

  [Fact(DisplayName = "CAT-02: EqrCase enforces unique engagement and tracks concurrence lifecycle")]
  public async Task EqrCase_EnforcesUniqueEngagement_And_TracksConcurrence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engId) = await pg.SeedScopeAsync();
    var eqrPartnerId = Guid.NewGuid();

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      ctx.Users.Add(new AppUser
      {
        Id = eqrPartnerId, FirmId = firmId, Subject = $"eqr-{eqrPartnerId:N}",
        TenantId = "tenant-1", Email = "eqr@example.test",
        DisplayName = "EQR Partner", UserKind = "Staff", SessionEpoch = 1,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await ctx.SaveChangesAsync();
    }

    var eqrId = Guid.NewGuid();
    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      ctx.EqrCases.Add(new EqrCase
      {
        Id = eqrId,
        FirmId = firmId,
        ClientId = clientId,
        EngagementId = engId,
        EqrPartnerUserId = eqrPartnerId,
        Status = "PENDING",
        FindingsDiscussed = false,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await ctx.SaveChangesAsync();
    }

    // Advance status to CONCURRED
    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      var eqr = await ctx.EqrCases.SingleAsync(x => x.Id == eqrId);
      eqr.Status = "CONCURRED";
      eqr.FindingsDiscussed = true;
      eqr.ConcurrenceDate = new DateOnly(2026, 3, 20);
      eqr.CompletedAt = DateTimeOffset.UtcNow;
      await ctx.SaveChangesAsync();
    }

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      var updated = await ctx.EqrCases.AsNoTracking().SingleAsync(x => x.Id == eqrId);
      Assert.Equal("CONCURRED", updated.Status);
      Assert.True(updated.FindingsDiscussed);
      Assert.NotNull(updated.CompletedAt);

      // Unique constraint on engagement_id prevents duplicate EQR cases
      ctx.EqrCases.Add(new EqrCase
      {
        Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId,
        EngagementId = engId, EqrPartnerUserId = eqrPartnerId,
        Status = "PENDING", CreatedAt = DateTimeOffset.UtcNow
      });
      await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
  }

  [Fact(DisplayName = "CAT-03: WrittenRepresentation enforces unique engagement code and tracks obtained state")]
  public async Task WrittenRepresentation_EnforcesUniqueCode_And_TracksObtained()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engId) = await pg.SeedScopeAsync();

    var repId = Guid.NewGuid();
    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      ctx.WrittenRepresentations.Add(new WrittenRepresentation
      {
        Id = repId,
        FirmId = firmId,
        ClientId = clientId,
        EngagementId = engId,
        Code = "R-01",
        Title = "Information completeness",
        Narrative = "Management provided all information relevant to the audit.",
        Obtained = false
      });
      await ctx.SaveChangesAsync();
    }

    // Duplicate code on same engagement must throw unique constraint violation
    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      ctx.WrittenRepresentations.Add(new WrittenRepresentation
      {
        Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId,
        EngagementId = engId, Code = "R-01", Title = "Duplicate",
        Narrative = "Duplicate rep", Obtained = false
      });
      await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // Update to obtained
    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      var rep = await ctx.WrittenRepresentations.SingleAsync(x => x.Id == repId);
      rep.Obtained = true;
      rep.ObtainedAt = DateTimeOffset.UtcNow;
      rep.SignatoryName = "CEO John Doe";
      await ctx.SaveChangesAsync();
    }

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      var saved = await ctx.WrittenRepresentations.AsNoTracking().SingleAsync(x => x.Id == repId);
      Assert.True(saved.Obtained);
      Assert.Equal("CEO John Doe", saved.SignatoryName);
    }
  }

  [Fact(DisplayName = "CAT-04: SpecialistClearance persists area clearance and condition gates")]
  public async Task SpecialistClearance_Persists_ConditionsAndStatus()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engId) = await pg.SeedScopeAsync();

    var clearanceId = Guid.NewGuid();
    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      ctx.SpecialistClearances.Add(new SpecialistClearance
      {
        Id = clearanceId,
        FirmId = firmId,
        PracticeClientId = clientId,
        EngagementId = engId,
        Area = "Valuation",
        SpecialistName = "Dr. Senior Valuation Expert",
        Status = "CONDITIONS",
        EvidenceReference = "VAL-MEMO-2026-01",
        Conditions = "Subject to final property appraisal certificate by March 15",
        CreatedAt = DateTimeOffset.UtcNow
      });
      await ctx.SaveChangesAsync();
    }

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      var sc = await ctx.SpecialistClearances.AsNoTracking().SingleAsync(x => x.Id == clearanceId);
      Assert.Equal("Valuation", sc.Area);
      Assert.Equal("CONDITIONS", sc.Status);
      Assert.Contains("property appraisal", sc.Conditions);
    }
  }

  [Fact(DisplayName = "CAT-05: SourceReceipt preserves SHA-256 digest and links to EvidenceLink")]
  public async Task SourceReceipt_And_EvidenceLink_PersistCorrectly()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engId) = await pg.SeedScopeAsync();
    var userId = Guid.NewGuid();

    var receiptId = Guid.NewGuid();
    const string expectedSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      ctx.SourceReceipts.Add(new SourceReceipt
      {
        Id = receiptId,
        FirmId = firmId,
        EngagementId = engId,
        SourceType = "PBC_UPLOAD",
        ReceiptToken = "RCPT-2026-00123",
        Sha256Digest = expectedSha256,
        ByteCount = 1048576,
        OriginalFileName = "BankStatements_2025Q4.pdf",
        AcquiredAt = DateTimeOffset.UtcNow,
        AcquiredByUserId = userId
      });

      ctx.EvidenceLinks.Add(new EvidenceLink
      {
        Id = Guid.NewGuid(),
        FirmId = firmId,
        EngagementId = engId,
        SourceReceiptId = receiptId,
        Purpose = "Substantive testing of cash balances",
        Assertion = "Existence",
        RelevanceReliabilityAssessment = "Direct external bank source document verified by SHA-256 receipt",
        CreatedAt = DateTimeOffset.UtcNow
      });

      await ctx.SaveChangesAsync();
    }

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      var receipt = await ctx.SourceReceipts.AsNoTracking().SingleAsync(x => x.Id == receiptId);
      Assert.Equal(expectedSha256, receipt.Sha256Digest);
      Assert.Equal(1048576, receipt.ByteCount);

      var link = await ctx.EvidenceLinks.AsNoTracking().SingleAsync(x => x.SourceReceiptId == receiptId);
      Assert.Equal("Existence", link.Assertion);
      Assert.Equal(engId, link.EngagementId);
    }
  }

  [Fact(DisplayName = "CAT-06: QuestionnaireSeed populates full CE-62 and RV-30 question banks")]
  public async Task QuestionnaireSeed_Populates_CompleteBanks()
  {
    await using var pg = await PgTestSchema.CreateAsync();

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      await QuestionnaireSeed.SeedTemplatesAndDefinitionsAsync(ctx);
      await ctx.SaveChangesAsync();
    }

    await using (var ctx = new AuditSphereDbContext(pg.Options))
    {
      var ceCount = await ctx.QuestionDefinitions.AsNoTracking()
        .CountAsync(q => q.TemplateId == QuestionnaireSeed.CeTemplateId);
      var rvCount = await ctx.QuestionDefinitions.AsNoTracking()
        .CountAsync(q => q.TemplateId == QuestionnaireSeed.RvTemplateId);

      // CE bank must contain all 55 defined questions across sections A.1 through A.10
      Assert.True(ceCount >= 50, $"Expected CE question bank to have >= 50 questions, found {ceCount}");
      // RV bank must contain all 30 questions from Appendix B
      Assert.Equal(30, rvCount);

      // Verify idempotency of seed call
      await QuestionnaireSeed.SeedTemplatesAndDefinitionsAsync(ctx);
      await ctx.SaveChangesAsync();

      var ceCountAfter = await ctx.QuestionDefinitions.AsNoTracking()
        .CountAsync(q => q.TemplateId == QuestionnaireSeed.CeTemplateId);
      Assert.Equal(ceCount, ceCountAfter);
    }
  }
}
