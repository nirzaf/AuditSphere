using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>T061-T072 area summary: one parameterized read over each substantive audit
/// area reporting workpapers, sampling, cut-off, matching, confirmations and differences.</summary>
[Trait("Profile", "Database")]
public sealed class AuditAreaSummaryTests
{
  [Fact(DisplayName = "Area summary reports each substantive area and rejects unsupported codes")]
  public async Task AreaSummary_ReportsCoveragePerArea()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var program = await AuditProgramService.PublishAsync(db, scope.Actor,
        new PublishAuditProgramRequest("2026.5", AuditProgramCatalog.SourceHash));
      Assert.True((await AuditProgramService.AdoptAsync(db, scope.Actor,
        new AdoptAuditProgramRequest(scope.EngagementId, program.Value!.ProgramVersionId))).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Every substantive area resolves to its source section and the right procedure set.
      var cash = await AuditAreaSummaryQuery.GetAreaSummaryAsync(db, scope.Actor, scope.EngagementId, AuditAreaCodes.CashBank);
      Assert.True(cash.Succeeded, cash.Message);
      Assert.Equal(2, cash.Value!.SectionNumber);
      Assert.Equal("Cash & Bank", cash.Value.SectionTitle);
      Assert.Equal(8, cash.Value.ProcedureCount);
      Assert.Equal(0, cash.Value.ApplicableProcedureCount); // decisions not yet taken
      Assert.Empty(cash.Value.Assessments);
      Assert.Equal(0, cash.Value.SelectionCount);
      Assert.Equal(0, cash.Value.CutOffRecordedCount);
      Assert.False(cash.Value.WorkpapersReviewed);
      Assert.All(cash.Value.Procedures, p => Assert.StartsWith("AWP-02-", p.SourceProcedureId));

      var receivables = await AuditAreaSummaryQuery.GetAreaSummaryAsync(db, scope.Actor, scope.EngagementId, AuditAreaCodes.Receivables);
      Assert.Equal(3, receivables.Value!.SectionNumber);
      Assert.Equal(9, receivables.Value.ProcedureCount);
      Assert.All(receivables.Value.Procedures, p => Assert.StartsWith("AWP-03-", p.SourceProcedureId));

      var tax = await AuditAreaSummaryQuery.GetAreaSummaryAsync(db, scope.Actor, scope.EngagementId, AuditAreaCodes.TaxStatutory);
      Assert.Equal(13, tax.Value!.SectionNumber);
      Assert.Equal("Tax & Statutory Liabilities", tax.Value.SectionTitle);

      // The mapping covers every declared area exactly once.
      Assert.Equal(AuditAreaCodes.All.Count, AuditAreaCodes.SectionByArea.Count);
      Assert.All(AuditAreaCodes.All, code => Assert.True(AuditAreaCodes.SectionByArea.ContainsKey(code)));

      // Unsupported codes and unknown engagements are refused before any read.
      var unsupported = await AuditAreaSummaryQuery.GetAreaSummaryAsync(db, scope.Actor, scope.EngagementId, "NOT_AN_AREA");
      Assert.False(unsupported.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, unsupported.ErrorCode);
      var unknown = await AuditAreaSummaryQuery.GetAreaSummaryAsync(db, scope.Actor, Guid.NewGuid(), AuditAreaCodes.CashBank);
      Assert.False(unknown.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, unknown.ErrorCode);

      // The actor's grant covers only the primary engagement, so the sibling is denied.
      var sibling = await AuditAreaSummaryQuery.GetAreaSummaryAsync(db, scope.Actor, fixture.Other.EngagementId, AuditAreaCodes.CashBank);
      Assert.False(sibling.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, sibling.ErrorCode);
    }
  }
}
