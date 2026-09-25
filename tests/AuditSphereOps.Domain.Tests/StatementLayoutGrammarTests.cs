using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>M24 statement layout grammar: bounded typed lines, acyclic references and no
/// double counting of a mapped taxonomy balance. Pure and deterministic.</summary>
public sealed class StatementLayoutGrammarTests
{
  private static StatementLineDefinition Mapped(string code, string taxonomy, int order) =>
    new(code, code, StatementLayoutLineKinds.MappedTaxonomyBalance, StatementLayoutSections.FinancialPosition,
      order, StatementLayoutDisplaySigns.Signed, taxonomy);

  [Fact(DisplayName = "A valid layout evaluates to canonical amounts without double counting")]
  public void ValidLayout_Evaluates()
  {
    var lines = new List<StatementLineDefinition>
    {
      Mapped("CASH", "CASH", 1),
      Mapped("AR", "AR", 2),
      new("CURRENT_ASSETS", "Current assets", StatementLayoutLineKinds.SumChildLines,
        StatementLayoutSections.FinancialPosition, 3, StatementLayoutDisplaySigns.Signed,
        ChildLineCodes: ["CASH", "AR"], IsSubtotal: true),
      Mapped("PAYABLES", "AP", 4),
      new("NET_ASSETS", "Net assets", StatementLayoutLineKinds.TotalReferencedLines,
        StatementLayoutSections.FinancialPosition, 5, StatementLayoutDisplaySigns.Signed,
        ReferencedLineCodes: ["CURRENT_ASSETS", "PAYABLES"],
        Operations: [StatementLayoutOperations.Add, StatementLayoutOperations.Subtract])
    };
    Assert.Empty(StatementLayoutEngine.Validate(lines));
    var amounts = StatementLayoutEngine.Evaluate(lines, new Dictionary<string, decimal>
    {
      ["CASH"] = 100m, ["AR"] = 50m, ["AP"] = -30m
    });
    Assert.Equal(100m, amounts["CASH"]);
    Assert.Equal(150m, amounts["CURRENT_ASSETS"]);
    Assert.Equal(180m, amounts["NET_ASSETS"]);
  }

  [Fact(DisplayName = "Display signs change presentation only, never the canonical amount")]
  public void DisplaySign_DoesNotChangeCanonicalAmount()
  {
    var lines = new List<StatementLineDefinition>
    {
      new("CAPITAL", "Share capital", StatementLayoutLineKinds.MappedTaxonomyBalance,
        StatementLayoutSections.FinancialPosition, 1, StatementLayoutDisplaySigns.Inverted, "CAPITAL")
    };
    var amounts = StatementLayoutEngine.Evaluate(lines, new Dictionary<string, decimal> { ["CAPITAL"] = -500m });
    Assert.Equal(-500m, amounts["CAPITAL"]);
    Assert.Equal(500m, StatementLayoutEngine.ToDisplayAmount(amounts["CAPITAL"], StatementLayoutDisplaySigns.Inverted));
    Assert.Equal(500m, StatementLayoutEngine.ToDisplayAmount(amounts["CAPITAL"], StatementLayoutDisplaySigns.Absolute));
    Assert.Equal(-500m, StatementLayoutEngine.ToDisplayAmount(amounts["CAPITAL"], StatementLayoutDisplaySigns.Signed));
  }

  [Fact(DisplayName = "Cyclic subtotals are rejected")]
  public void CyclicSubtotal_Rejected()
  {
    var lines = new List<StatementLineDefinition>
    {
      new("A", "A", StatementLayoutLineKinds.SumChildLines, StatementLayoutSections.FinancialPosition, 1,
        ChildLineCodes: ["B"]),
      new("B", "B", StatementLayoutLineKinds.SumChildLines, StatementLayoutSections.FinancialPosition, 2,
        ChildLineCodes: ["A"])
    };
    var errors = StatementLayoutEngine.Validate(lines);
    Assert.Contains(errors, x => x.Code == "line.cycle");
    Assert.Throws<ArgumentException>(() => StatementLayoutEngine.Evaluate(lines, new Dictionary<string, decimal>()));
  }

  [Fact(DisplayName = "A subtotal and its own leaf cannot both count the same taxonomy balance")]
  public void ParentAndLeafDoubleCount_Detected()
  {
    var lines = new List<StatementLineDefinition>
    {
      Mapped("CASH", "CASH", 1),
      new("CURRENT_ASSETS", "Current assets", StatementLayoutLineKinds.SumChildLines,
        StatementLayoutSections.FinancialPosition, 2, ChildLineCodes: ["CASH"], IsSubtotal: true),
      new("TOTAL_ASSETS", "Total assets", StatementLayoutLineKinds.SumChildLines,
        StatementLayoutSections.FinancialPosition, 3, ChildLineCodes: ["CURRENT_ASSETS", "CASH"])
    };
    var errors = StatementLayoutEngine.Validate(lines);
    Assert.Contains(errors, x => x.Code == "line.double-count");
  }

  [Fact(DisplayName = "Unknown references, bad operations and bad sections are rejected")]
  public void InvalidGrammar_IsRejected()
  {
    var unknownReference = StatementLayoutEngine.Validate(new List<StatementLineDefinition>
    {
      new("TOTAL", "Total", StatementLayoutLineKinds.TotalReferencedLines, StatementLayoutSections.FinancialPosition, 1,
        ReferencedLineCodes: ["MISSING"], Operations: [StatementLayoutOperations.Add])
    });
    Assert.Contains(unknownReference, x => x.Code == "line.unknown-reference");

    var missingOperation = StatementLayoutEngine.Validate(new List<StatementLineDefinition>
    {
      Mapped("CASH", "CASH", 1),
      new("TOTAL", "Total", StatementLayoutLineKinds.TotalReferencedLines, StatementLayoutSections.FinancialPosition, 2,
        ReferencedLineCodes: ["CASH"], Operations: [StatementLayoutOperations.Add, StatementLayoutOperations.Subtract])
    });
    Assert.Contains(missingOperation, x => x.Code == "line.operation-count");

    var badSection = StatementLayoutEngine.Validate(new List<StatementLineDefinition>
    {
      new("X", "X", StatementLayoutLineKinds.Heading, "NOT_A_SECTION", 1)
    });
    Assert.Contains(badSection, x => x.Code == "line.section");

    var badKind = StatementLayoutEngine.Validate(new List<StatementLineDefinition>
    {
      new("X", "X", "FREE_FORM_EXPRESSION", StatementLayoutSections.FinancialPosition, 1)
    });
    Assert.Contains(badKind, x => x.Code == "line.kind");

    var duplicateOrder = StatementLayoutEngine.Validate(new List<StatementLineDefinition>
    {
      Mapped("A", "A", 1), Mapped("B", "B", 1)
    });
    Assert.Contains(duplicateOrder, x => x.Code == "layout.duplicate-order");

    Assert.Contains(StatementLayoutEngine.Validate([]), x => x.Code == "layout.empty");
  }

  [Fact(DisplayName = "Ratios handle a zero denominator explicitly instead of silently")]
  public void Ratio_ZeroDenominatorBehaviourIsExplicit()
  {
    var guarded = new List<StatementLineDefinition>
    {
      Mapped("PROFIT", "PROFIT", 1),
      Mapped("REVENUE", "REVENUE", 2),
      new("MARGIN", "Margin", StatementLayoutLineKinds.Ratio, StatementLayoutSections.ProfitOrLoss, 3,
        ReferencedLineCodes: ["PROFIT"], DenominatorLineCode: "REVENUE", ZeroDenominatorYieldsZero: true)
    };
    var amounts = StatementLayoutEngine.Evaluate(guarded, new Dictionary<string, decimal>
    {
      ["PROFIT"] = 25m, ["REVENUE"] = 0m
    });
    Assert.Equal(0m, amounts["MARGIN"]);

    var unguarded = guarded.Select(x => x.LineCode == "MARGIN" ? x with { ZeroDenominatorYieldsZero = false } : x).ToList();
    Assert.Throws<InvalidOperationException>(() => StatementLayoutEngine.Evaluate(unguarded,
      new Dictionary<string, decimal> { ["PROFIT"] = 25m, ["REVENUE"] = 0m }));

    var missingDenominator = StatementLayoutEngine.Validate(new List<StatementLineDefinition>
    {
      Mapped("PROFIT", "PROFIT", 1),
      new("MARGIN", "Margin", StatementLayoutLineKinds.Ratio, StatementLayoutSections.ProfitOrLoss, 2,
        ReferencedLineCodes: ["PROFIT"])
    });
    Assert.Contains(missingDenominator, x => x.Code == "line.missing-denominator");
  }

  [Fact(DisplayName = "Headings are presentational and unused taxonomy balances do not appear")]
  public void HeadingsCarryNoValueAndMissingBalancesAreZero()
  {
    var lines = new List<StatementLineDefinition>
    {
      new("H1", "Current assets", StatementLayoutLineKinds.Heading, StatementLayoutSections.FinancialPosition, 1),
      Mapped("CASH", "CASH", 2)
    };
    Assert.Empty(StatementLayoutEngine.Validate(lines));
    var amounts = StatementLayoutEngine.Evaluate(lines, new Dictionary<string, decimal>());
    Assert.Equal(0m, amounts["H1"]);
    Assert.Equal(0m, amounts["CASH"]);
  }
}

/// <summary>M24 layout authoring: draft validation, independent publication and the
/// immutability of a published version.</summary>
[Trait("Profile", "Database")]
public sealed class StatementLayoutServiceTests
{
  [Fact(DisplayName = "Layout drafts validate, publish independently, and published versions are immutable")]
  public async Task LayoutDraft_PublishesIndependentlyAndIsImmutable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    Guid reviewerId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      reviewerId = Guid.NewGuid();
      db.Users.Add(new AuditSphereOps.Domain.Security.AppUser
      {
        Id = reviewerId, FirmId = scope.FirmId, Subject = "layout-reviewer-" + reviewerId.ToString("N"),
        TenantId = "tenant-planning", Email = "layout-reviewer@example.test", DisplayName = "Layout Reviewer",
        UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
      });
      // Layouts are client-level, so both actors need client-scoped grants (the seeded
      // grants are engagement-scoped and deliberately do not widen to the client).
      db.RoleGrants.AddRange(
        new AuditSphereOps.Domain.Security.RoleGrant
        {
          Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "AccountingReviewer",
          ClientId = scope.ClientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId
        },
        new AuditSphereOps.Domain.Security.RoleGrant
        {
          Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = scope.Actor.UserId, Role = "AccountingPreparer",
          ClientId = scope.ClientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId
        });
      await db.SaveChangesAsync();
    }
    var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["AccountingReviewer", "Partner"]);

    Guid layoutId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // An unsupported line kind can never be stored.
      var unsupported = await StatementLayoutService.SaveLayoutDraftAsync(db, scope.Actor,
        new StatementLayoutDraftRequest(scope.ClientId, "IFRS-2026", "Primary statements",
          [new("FREE", "Free", "FREE_FORM", StatementLayoutSections.FinancialPosition, 1)]));
      Assert.False(unsupported.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, unsupported.ErrorCode);

      // A structurally invalid grammar is stored as an invalid draft and cannot publish.
      var invalid = await StatementLayoutService.SaveLayoutDraftAsync(db, scope.Actor,
        new StatementLayoutDraftRequest(scope.ClientId, "IFRS-2026", "Secondary statements",
        [
          new("TOTAL", "Total", StatementLayoutLineKinds.TotalReferencedLines,
            StatementLayoutSections.FinancialPosition, 1,
            ReferencedLineCodes: ["MISSING"], Operations: [StatementLayoutOperations.Add])
        ]));
      Assert.True(invalid.Succeeded, invalid.Message);
      Assert.Equal(StatementLayoutStatuses.Draft, invalid.Value!.Status);
      var invalidPublish = await StatementLayoutService.PublishLayoutAsync(db, reviewer, invalid.Value.LayoutVersionId, "Attempt");
      Assert.False(invalidPublish.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingIncomplete, invalidPublish.ErrorCode);

      // A valid draft becomes VALIDATED on save.
      var valid = await StatementLayoutService.SaveLayoutDraftAsync(db, scope.Actor,
        new StatementLayoutDraftRequest(scope.ClientId, "IFRS-2026", "Primary statements",
        [
          new("CASH", "Cash", StatementLayoutLineKinds.MappedTaxonomyBalance, StatementLayoutSections.FinancialPosition,
            1, StatementLayoutDisplaySigns.Signed, "CASH"),
          new("AR", "Receivables", StatementLayoutLineKinds.MappedTaxonomyBalance, StatementLayoutSections.FinancialPosition,
            2, StatementLayoutDisplaySigns.Signed, "AR"),
          new("CURRENT_ASSETS", "Current assets", StatementLayoutLineKinds.SumChildLines,
            StatementLayoutSections.FinancialPosition, 3, StatementLayoutDisplaySigns.Signed,
            ChildLineCodes: ["CASH", "AR"], IsSubtotal: true)
        ]));
      Assert.True(valid.Succeeded, valid.Message);
      Assert.Equal(StatementLayoutStatuses.Validated, valid.Value!.Status);
      Assert.Equal(3, valid.Value.LineCount);
      Assert.Matches("^[0-9a-f]{64}$", valid.Value.LayoutHash);
      layoutId = valid.Value.LayoutVersionId;

      // The author cannot publish their own layout.
      var selfPublish = await StatementLayoutService.PublishLayoutAsync(db, scope.Actor, layoutId, "Self publish");
      Assert.False(selfPublish.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, selfPublish.ErrorCode);

      var published = await StatementLayoutService.PublishLayoutAsync(db, reviewer, layoutId,
        "Approved by the reporting partner for the 2026 IFRS presentation.");
      Assert.True(published.Succeeded, published.Message);
      Assert.Equal(StatementLayoutStatuses.Published, published.Value!.Status);

      // A published version is immutable: re-publishing is refused.
      var republish = await StatementLayoutService.PublishLayoutAsync(db, reviewer, layoutId, "Again");
      Assert.False(republish.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, republish.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var lines = await StatementLayoutService.GetLayoutLinesAsync(db, scope.Actor, layoutId);
      Assert.True(lines.Succeeded, lines.Message);
      Assert.Equal(3, lines.Value!.Count);
      Assert.Equal("CASH", lines.Value[0].LineCode);
      Assert.Equal(1, lines.Value[0].LineOrder);
      Assert.Equal("[\"CASH\",\"AR\"]", lines.Value[2].ChildLineCodesJson);

      var evaluated = await StatementLayoutService.EvaluateLayoutAsync(db, scope.Actor, layoutId,
        new Dictionary<string, decimal> { ["CASH"] = 100m, ["AR"] = 50m });
      Assert.True(evaluated.Succeeded, evaluated.Message);
      Assert.Equal(150m, evaluated.Value!["CURRENT_ASSETS"]);

      var revalidated = await StatementLayoutService.ValidateLayoutAsync(db, scope.Actor, layoutId);
      Assert.True(revalidated.Succeeded);
      Assert.True(revalidated.Value!.IsValid);
      Assert.Empty(revalidated.Value.Errors);
    }
  }
}
