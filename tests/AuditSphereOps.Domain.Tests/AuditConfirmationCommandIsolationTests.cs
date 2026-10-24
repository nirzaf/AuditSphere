using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditConfirmationCommandIsolationTests
{
  [Fact]
  public async Task DirectConfirmationCreationRefusesAuthorityRevokedBeforeItsTransaction()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var actor = fixture.Primary.Actor;
    var administrator = PbcSeed.User(actor.FirmId, "Staff");
    Guid grantId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.Add(administrator);
      setup.RoleGrants.Add(PbcSeed.Grant(actor.FirmId, administrator, "Administrator"));
      await setup.SaveChangesAsync(timeout.Token);
      grantId = await setup.RoleGrants.Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId &&
        x.RevokedAt == null).Select(x => x.Id).SingleAsync(timeout.Token);
      var allowed = await AuditFieldworkService.CreateConfirmationAsync(setup, actor,
        NewConfirmation(fixture.Primary.EngagementId) with { SourceRecordId = "authorized-before-revocation" }, timeout.Token);
      Assert.True(allowed.Succeeded, allowed.Message);
    }

    var interceptor = new RevokeOnConfirmationTransaction(async ct =>
    {
      await using var revokeDb = new AuditSphereDbContext(pg.Options);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(revokeDb,
        PbcSeed.Actor(administrator, "Administrator"), new(grantId, Reason: "Synthetic confirmation authority race"), ct);
      Assert.True(revoked.Succeeded, revoked.Message);
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using (var db = new AuditSphereDbContext(options))
    {
      var result = await AuditFieldworkService.CreateConfirmationAsync(db, actor,
        NewConfirmation(fixture.Primary.EngagementId) with { SourceRecordId = "denied-after-revocation" }, timeout.Token);
      await using var verify = new AuditSphereDbContext(pg.Options);
      Assert.True(interceptor.Invoked);
      Assert.NotNull((await verify.RoleGrants.SingleAsync(x => x.Id == grantId, timeout.Token)).RevokedAt);
      Assert.True(await verify.Users.Where(x => x.Id == actor.UserId).Select(x => x.SessionEpoch)
        .SingleAsync(timeout.Token) > actor.SessionEpoch);
      var unauthorizedRows = await verify.AuditConfirmationCases.CountAsync(x => x.EngagementId == fixture.Primary.EngagementId &&
        x.SourceRecordId == "denied-after-revocation", timeout.Token);
      Assert.True(!result.Succeeded && unauthorizedRows == 0,
        $"Official role revocation committed before the direct write transaction continued; " +
        $"creation succeeded={result.Succeeded}, unauthorized persisted cases={unauthorizedRows}.");
      Assert.Single(await verify.AuditConfirmationCases.Where(x => x.EngagementId == fixture.Primary.EngagementId)
        .ToListAsync(timeout.Token));
    }
  }

  [Theory]
  [InlineData("APPROVE")]
  [InlineData("DISPATCH")]
  [InlineData("RESPONSE")]
  [InlineData("REVIEW_RESPONSE")]
  [InlineData("ALTERNATIVE")]
  [InlineData("REVIEW_ALTERNATIVE")]
  [InlineData("CLOSE")]
  public async Task DirectConfirmationMutationsRefuseAuthorityRevokedBeforeTheirTransaction(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var ct = timeout.Token;
    var scope = (await PlanningSeed.CreateAsync(pg, role: "Partner")).Primary;
    var actor = new ActorContext(Guid.NewGuid(), scope.FirmId, 1, ["Partner"]);
    var administrator = PbcSeed.User(scope.FirmId, "Staff");
    Guid grantId;
    Guid caseId;
    Guid responseId = Guid.Empty;
    Guid alternativeId = Guid.Empty;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      AddScopedStaff(setup, scope.Actor.UserId, scope, actor.UserId, "Partner", "revoked-confirmation-partner");
      setup.Users.Add(administrator);
      setup.RoleGrants.Add(PbcSeed.Grant(scope.FirmId, administrator, "Administrator"));
      await setup.SaveChangesAsync(ct);
      grantId = await setup.RoleGrants.Where(x => x.UserId == actor.UserId && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync(ct);
      var created = await AuditFieldworkService.CreateConfirmationAsync(setup, scope.Actor,
        NewConfirmation(scope.EngagementId), ct);
      Assert.True(created.Succeeded, created.Message);
      caseId = created.Value!.ConfirmationCaseId;
      if (action != "APPROVE")
        Assert.True((await AuditFieldworkService.ApproveConfirmationAsync(setup, actor, caseId, ct)).Succeeded);
      if (action is not ("APPROVE" or "DISPATCH"))
        Assert.True((await AuditFieldworkService.RecordDispatchEvidenceAsync(setup, scope.Actor,
          new(caseId, "Synthetic observed dispatch reference"), ct)).Succeeded);
      if (action is "REVIEW_RESPONSE" or "ALTERNATIVE" or "REVIEW_ALTERNATIVE" or "CLOSE")
      {
        var response = await AuditFieldworkService.RecordConfirmationResponseAsync(setup, scope.Actor,
          new(caseId, "DIRECT", "PORTAL", "Synthetic retained response", 50m,
            "Synthetic authenticity assessment", action == "CLOSE" ? AuditConfirmationDecisions.Agreed : AuditConfirmationDecisions.AlternativeRequired), ct);
        Assert.True(response.Succeeded, response.Message);
        responseId = await setup.AuditConfirmationResponses.Where(x => x.ConfirmationCaseId == caseId)
          .Select(x => x.Id).SingleAsync(ct);
      }
      if (action == "REVIEW_ALTERNATIVE")
      {
        var alternative = await AuditFieldworkService.RecordAlternativeProcedureAsync(setup, scope.Actor,
          new(caseId, "Inspect subsequent clearing", ["Synthetic clearing evidence"], "The balance is supported."), ct);
        Assert.True(alternative.Succeeded, alternative.Message);
        alternativeId = await setup.AuditAlternativeProcedures.Where(x => x.ConfirmationCaseId == caseId)
          .Select(x => x.Id).SingleAsync(ct);
      }
      if (action == "CLOSE")
        Assert.True((await AuditFieldworkService.ReviewConfirmationResponseAsync(setup, actor, new(responseId), ct)).Succeeded);
    }

    async Task<(bool Succeeded, string? ErrorCode, string? Message)> Execute(AuditSphereDbContext db)
    {
      if (action is "REVIEW_RESPONSE" or "REVIEW_ALTERNATIVE")
      {
        var review = action == "REVIEW_RESPONSE"
          ? await AuditFieldworkService.ReviewConfirmationResponseAsync(db, actor, new(responseId), ct)
          : await AuditFieldworkService.ReviewAlternativeProcedureAsync(db, actor, new(alternativeId, "Independently reviewed clearing evidence."), ct);
        return (review.Succeeded, review.ErrorCode, review.Message);
      }
      var result = action switch
      {
        "APPROVE" => await AuditFieldworkService.ApproveConfirmationAsync(db, actor, caseId, ct),
        "DISPATCH" => await AuditFieldworkService.RecordDispatchEvidenceAsync(db, actor, new(caseId, "Synthetic target dispatch reference"), ct),
        "RESPONSE" => await AuditFieldworkService.RecordConfirmationResponseAsync(db, actor,
          new(caseId, "DIRECT", "PORTAL", "Synthetic target response", 50m, "Independently assessed provenance", AuditConfirmationDecisions.Agreed), ct),
        "ALTERNATIVE" => await AuditFieldworkService.RecordAlternativeProcedureAsync(db, actor,
          new(caseId, "Inspect subsequent clearing", ["Synthetic clearing evidence"], "The balance is supported."), ct),
        "CLOSE" => await AuditFieldworkService.CloseConfirmationAsync(db, actor, new(caseId, "Independently evaluated returned response."), ct),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
      };
      return (result.Succeeded, result.ErrorCode, result.Message);
    }

    async Task<string> Snapshot(AuditSphereDbContext db) => JsonSerializer.Serialize(new
    {
      Cases = await db.AuditConfirmationCases.AsNoTracking().Where(x => x.Id == caseId).ToListAsync(ct),
      Responses = await db.AuditConfirmationResponses.AsNoTracking().Where(x => x.ConfirmationCaseId == caseId).OrderBy(x => x.Id).ToListAsync(ct),
      Alternatives = await db.AuditAlternativeProcedures.AsNoTracking().Where(x => x.ConfirmationCaseId == caseId).OrderBy(x => x.Id).ToListAsync(ct),
      Closures = await db.AuditConfirmationClosures.AsNoTracking().Where(x => x.ConfirmationCaseId == caseId).OrderBy(x => x.Id).ToListAsync(ct)
    });

    string before;
    await using (var positive = new AuditSphereDbContext(pg.Options))
    {
      before = await Snapshot(positive);
      await using var callerTx = await positive.Database.BeginTransactionAsync(ct);
      var allowed = await Execute(positive);
      Assert.True(allowed.Succeeded, $"Authorized {action} positive failed: {allowed.ErrorCode}: {allowed.Message}");
      Assert.NotEqual(before, await Snapshot(positive));
      await callerTx.RollbackAsync(ct);
    }
    await using (var verifyRollback = new AuditSphereDbContext(pg.Options))
      Assert.Equal(before, await Snapshot(verifyRollback));

    var interceptor = new RevokeOnConfirmationTransaction(async token =>
    {
      await using var revokeDb = new AuditSphereDbContext(pg.Options);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(revokeDb,
        PbcSeed.Actor(administrator, "Administrator"), new(grantId, Reason: "Synthetic direct confirmation mutation race"), token);
      Assert.True(revoked.Succeeded, revoked.Message);
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var target = new AuditSphereDbContext(options);
    var denied = await Execute(target);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.True(interceptor.Invoked);
    Assert.NotNull((await verify.RoleGrants.SingleAsync(x => x.Id == grantId, ct)).RevokedAt);
    Assert.True(await verify.Users.Where(x => x.Id == actor.UserId).Select(x => x.SessionEpoch).SingleAsync(ct) > actor.SessionEpoch);
    var after = await Snapshot(verify);
    Assert.True(!denied.Succeeded && before == after,
      $"Official revocation committed before {action}'s own transaction continued; succeeded={denied.Succeeded}, persistedMutation={before != after}.");
    Assert.Equal(ErrorCodes.GenerationStale, denied.ErrorCode);
    Assert.Equal(before, after);
  }

  [Fact]
  public async Task AlternativeProcedureRefusesCaseClosedBeforeItsTransaction()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var ct = timeout.Token;
    var scope = (await PlanningSeed.CreateAsync(pg, role: "Partner")).Primary;
    var reviewer = new ActorContext(Guid.NewGuid(), scope.FirmId, 1, ["Partner"]);
    Guid caseId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      AddScopedStaff(setup, scope.Actor.UserId, scope, reviewer.UserId, "Partner", "alternative-closure-reviewer");
      await setup.SaveChangesAsync(ct);
      var created = await AuditFieldworkService.CreateConfirmationAsync(setup, scope.Actor,
        NewConfirmation(scope.EngagementId), ct);
      Assert.True(created.Succeeded, created.Message);
      caseId = created.Value!.ConfirmationCaseId;
      Assert.True((await AuditFieldworkService.ApproveConfirmationAsync(setup, reviewer, caseId, ct)).Succeeded);
      Assert.True((await AuditFieldworkService.RecordDispatchEvidenceAsync(setup, scope.Actor,
        new(caseId, "Synthetic observed dispatch reference"), ct)).Succeeded);
      Assert.True((await AuditFieldworkService.RecordConfirmationResponseAsync(setup, scope.Actor,
        new(caseId, "DIRECT", "PORTAL", "Synthetic retained non-response", null,
          "No response received by the reviewed follow-up date.", AuditConfirmationDecisions.NoResponse), ct)).Succeeded);
      var responseId = await setup.AuditConfirmationResponses.Where(x => x.ConfirmationCaseId == caseId)
        .Select(x => x.Id).SingleAsync(ct);
      Assert.True((await AuditFieldworkService.ReviewConfirmationResponseAsync(setup, reviewer, new(responseId), ct)).Succeeded);
      Assert.True((await AuditFieldworkService.RecordAlternativeProcedureAsync(setup, scope.Actor,
        new(caseId, "Inspect subsequent clearing", ["Synthetic clearing evidence"], "The balance is supported."), ct)).Succeeded);
      var alternativeId = await setup.AuditAlternativeProcedures.Where(x => x.ConfirmationCaseId == caseId)
        .Select(x => x.Id).SingleAsync(ct);
      Assert.True((await AuditFieldworkService.ReviewAlternativeProcedureAsync(setup, reviewer,
        new(alternativeId, "Independently reviewed clearing evidence."), ct)).Succeeded);
    }

    async Task<string> Snapshot(AuditSphereDbContext db) => JsonSerializer.Serialize(new
    {
      Case = await db.AuditConfirmationCases.AsNoTracking().SingleAsync(x => x.Id == caseId, ct),
      Responses = await db.AuditConfirmationResponses.AsNoTracking().Where(x => x.ConfirmationCaseId == caseId).OrderBy(x => x.Id).ToListAsync(ct),
      Alternatives = await db.AuditAlternativeProcedures.AsNoTracking().Where(x => x.ConfirmationCaseId == caseId).OrderBy(x => x.Id).ToListAsync(ct),
      Closures = await db.AuditConfirmationClosures.AsNoTracking().Where(x => x.ConfirmationCaseId == caseId).OrderBy(x => x.Id).ToListAsync(ct)
    });

    var request = new RecordAlternativeProcedureRequest(caseId, "Inspect additional subsequent clearing",
      ["Synthetic additional clearing evidence"], "Additional clearing supports the recorded balance.");
    string before;
    await using (var positive = new AuditSphereDbContext(pg.Options))
    {
      before = await Snapshot(positive);
      await using var callerTx = await positive.Database.BeginTransactionAsync(ct);
      var allowed = await AuditFieldworkService.RecordAlternativeProcedureAsync(positive, scope.Actor, request, ct);
      Assert.True(allowed.Succeeded, allowed.Message);
      Assert.Equal(2, await positive.AuditAlternativeProcedures.CountAsync(x => x.ConfirmationCaseId == caseId, ct));
      await callerTx.RollbackAsync(ct);
    }
    await using (var verifyRollback = new AuditSphereDbContext(pg.Options))
      Assert.Equal(before, await Snapshot(verifyRollback));

    string? closedSnapshot = null;
    var interceptor = new RevokeOnConfirmationTransaction(async token =>
    {
      await using var closeDb = new AuditSphereDbContext(pg.Options);
      var closed = await AuditFieldworkService.CloseConfirmationAsync(closeDb, reviewer,
        new(caseId, "Independently reviewed alternative work is sufficient for closure."), token);
      Assert.True(closed.Succeeded, closed.Message);
      Assert.Equal(AuditConfirmationStatuses.Closed, closed.Value!.Status);
      Assert.Single(await closeDb.AuditConfirmationClosures.AsNoTracking().Where(x => x.ConfirmationCaseId == caseId).ToListAsync(token));
      closedSnapshot = await Snapshot(closeDb);
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var target = new AuditSphereDbContext(options);
    var result = await AuditFieldworkService.RecordAlternativeProcedureAsync(target, scope.Actor, request, ct);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.True(interceptor.Invoked);
    Assert.NotNull(closedSnapshot);
    Assert.Equal(AuditConfirmationStatuses.Closed,
      await verify.AuditConfirmationCases.Where(x => x.Id == caseId).Select(x => x.Status).SingleAsync(ct));
    var after = await Snapshot(verify);
    Assert.True(!result.Succeeded && after == closedSnapshot,
      $"Official closure committed before the alternative transaction continued; succeeded={result.Succeeded}, postCloseMutation={after != closedSnapshot}.");
    Assert.Equal(ErrorCodes.GateBlocked, result.ErrorCode);
    Assert.Equal(closedSnapshot, after);
    Assert.Single(await verify.AuditAlternativeProcedures.Where(x => x.ConfirmationCaseId == caseId).ToListAsync(ct));
  }

  private sealed class RevokeOnConfirmationTransaction(Func<CancellationToken, Task> revoke) : DbTransactionInterceptor
  {
    private int invoked;
    public bool Invoked => Volatile.Read(ref invoked) != 0;

    public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
      TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
      if (Interlocked.Exchange(ref invoked, 1) == 0) await revoke(cancellationToken);
      return result;
    }
  }

  [Fact(DisplayName = "Legal is a confirmation category, not an audit-program section, and unsupported categories fail closed")]
  public async Task ConfirmationTaxonomy_KeepsLegalSeparateAndRejectsUnknownCategories()
  {
    Assert.Equal(18, AuditAreaCodes.All.Count);
    Assert.DoesNotContain(AuditConfirmationAreaCodes.Legal, AuditAreaCodes.All);
    Assert.True(AuditConfirmationAreaCodes.IsSupported(" legal "));
    Assert.False(AuditConfirmationAreaCodes.IsSupported("LEGAL_SERVICES"));

    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    var legal = await AuditFieldworkService.CreateConfirmationAsync(db, fixture.Primary.Actor,
      new CreateConfirmationRequest(fixture.Primary.EngagementId, null, AuditConfirmationAreaCodes.Legal,
        "legal-confirmation-001", 0m, "QAR", new DateOnly(2026, 10, 9), "Synthetic legal contact",
        "Synthetic approved contact register"));
    Assert.True(legal.Succeeded, legal.Message);

    var unsupported = await AuditFieldworkService.CreateConfirmationAsync(db, fixture.Primary.Actor,
      new CreateConfirmationRequest(fixture.Primary.EngagementId, null, "LEGAL_SERVICES",
        "unsupported-confirmation-001", 0m, "QAR", new DateOnly(2026, 10, 9), "Synthetic legal contact",
        "Synthetic approved contact register"));
    Assert.False(unsupported.Succeeded);
    Assert.Equal(ErrorCodes.AuditPlanning.Invalid, unsupported.ErrorCode);
    Assert.Equal(1, await db.AuditConfirmationCases.CountAsync(x => x.EngagementId == fixture.Primary.EngagementId));
  }

  [Fact(DisplayName = "Client-scoped confirmation commands cannot create or mutate sibling client records by identifier")]
  public async Task SiblingClientIds_AreDeniedAcrossConfirmationLifecycle()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var clientBPartnerId = Guid.NewGuid();
    var clientBReviewerId = Guid.NewGuid();
    var clientBPartner = new ActorContext(clientBPartnerId, clientB.FirmId, 1, ["Partner"]);
    var clientBReviewer = new ActorContext(clientBReviewerId, clientB.FirmId, 1, ["Reviewer"]);

    Guid confirmationId;
    Guid responseId;
    Guid alternativeId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingEngagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      siblingEngagement.ProfessionalWorkBlocked = false;
      AddScopedStaff(db, clientA.Actor.UserId, clientB, clientBPartnerId, "Partner", "confirmation-client-b-partner");
      AddScopedStaff(db, clientA.Actor.UserId, clientB, clientBReviewerId, "Reviewer", "confirmation-client-b-reviewer");
      await db.SaveChangesAsync();

      var created = await AuditFieldworkService.CreateConfirmationAsync(db, clientBPartner,
        NewConfirmation(clientB.EngagementId));
      Assert.True(created.Succeeded, created.Message);
      confirmationId = created.Value!.ConfirmationCaseId;

      var wrongClientCreate = await AuditFieldworkService.CreateConfirmationAsync(db, clientA.Actor,
        NewConfirmation(clientB.EngagementId));
      Assert.Equal(ErrorCodes.ScopeDenied, wrongClientCreate.ErrorCode);

      var unauthorizedApproval = await AuditFieldworkService.ApproveConfirmationAsync(db, clientA.Actor, confirmationId);
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedApproval.ErrorCode);
      Assert.Equal(AuditConfirmationStatuses.Draft,
        await db.AuditConfirmationCases.Where(x => x.Id == confirmationId).Select(x => x.Status).SingleAsync());

      Assert.True((await AuditFieldworkService.ApproveConfirmationAsync(db, clientBReviewer, confirmationId)).Succeeded);
      var unauthorizedDispatch = await AuditFieldworkService.RecordDispatchEvidenceAsync(db, clientA.Actor,
        new RecordConfirmationDispatchRequest(confirmationId, "attempted-sibling-dispatch"));
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedDispatch.ErrorCode);

      Assert.True((await AuditFieldworkService.RecordDispatchEvidenceAsync(db, clientBPartner,
        new RecordConfirmationDispatchRequest(confirmationId, "provider-dispatch-client-b"))).Succeeded);
      var responseRequest = new RecordConfirmationResponseRequest(confirmationId, "DIRECT", "PORTAL",
        "response-client-b", null, "Authenticated portal response reviewed for authenticity.",
        AuditConfirmationDecisions.AlternativeRequired);
      var unauthorizedResponse = await AuditFieldworkService.RecordConfirmationResponseAsync(db, clientA.Actor, responseRequest);
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedResponse.ErrorCode);

      Assert.True((await AuditFieldworkService.RecordConfirmationResponseAsync(db, clientBPartner, responseRequest)).Succeeded);
      responseId = await db.AuditConfirmationResponses.Where(x => x.ConfirmationCaseId == confirmationId)
        .Select(x => x.Id).SingleAsync();

      var unauthorizedResponseReview = await AuditFieldworkService.ReviewConfirmationResponseAsync(db, clientA.Actor,
        new ReviewConfirmationResponseRequest(responseId));
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedResponseReview.ErrorCode);

      var alternativeRequest = new RecordAlternativeProcedureRequest(confirmationId,
        "Inspect subsequent cleared transactions.", ["bank-statement-subsequent-client-b"],
        "Subsequent clearing supports the recorded balance.");
      var unauthorizedAlternative = await AuditFieldworkService.RecordAlternativeProcedureAsync(db, clientA.Actor,
        alternativeRequest);
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedAlternative.ErrorCode);

      Assert.True((await AuditFieldworkService.RecordAlternativeProcedureAsync(db, clientBPartner,
        alternativeRequest)).Succeeded);
      alternativeId = await db.AuditAlternativeProcedures.Where(x => x.ConfirmationCaseId == confirmationId)
        .Select(x => x.Id).SingleAsync();

      var unauthorizedAlternativeReview = await AuditFieldworkService.ReviewAlternativeProcedureAsync(db, clientA.Actor,
        new ReviewAlternativeProcedureRequest(alternativeId, "Attempted sibling review."));
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedAlternativeReview.ErrorCode);

      var unauthorizedClose = await AuditFieldworkService.CloseConfirmationAsync(db, clientA.Actor,
        new CloseConfirmationRequest(confirmationId, "Attempted sibling closure."));
      Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedClose.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var confirmation = await db.AuditConfirmationCases.AsNoTracking().SingleAsync(x => x.Id == confirmationId);
      var response = await db.AuditConfirmationResponses.AsNoTracking().SingleAsync(x => x.Id == responseId);
      var alternative = await db.AuditAlternativeProcedures.AsNoTracking().SingleAsync(x => x.Id == alternativeId);
      Assert.Equal(clientB.ClientId, confirmation.ClientId);
      Assert.Equal(clientB.EngagementId, confirmation.EngagementId);
      Assert.Equal(AuditConfirmationStatuses.AlternativeRequired, confirmation.Status);
      Assert.Equal("provider-dispatch-client-b", confirmation.DispatchReference);
      Assert.Equal(clientBPartnerId, response.CreatedByUserId);
      Assert.Null(response.ReviewedByUserId);
      Assert.Equal(AuditAlternativeStatuses.Submitted, alternative.Status);
      Assert.Equal(clientBPartnerId, alternative.CreatedByUserId);
      Assert.Equal(0, await db.AuditConfirmationClosures.CountAsync(x => x.ConfirmationCaseId == confirmationId));
      Assert.Equal(1, await db.AuditConfirmationCases.CountAsync(x => x.EngagementId == clientB.EngagementId));
      Assert.Equal(1, await db.AuditConfirmationResponses.CountAsync(x => x.ConfirmationCaseId == confirmationId));
      Assert.Equal(1, await db.AuditAlternativeProcedures.CountAsync(x => x.ConfirmationCaseId == confirmationId));
    }
  }

  private static CreateConfirmationRequest NewConfirmation(Guid engagementId) => new(
    engagementId, null, AuditAreaCodes.CashBank, "bank-client-b", 50m, "QAR",
    new DateOnly(2026, 9, 19), "Bank relationship manager", "Client master contact list reviewed by partner.");

  private static void AddScopedStaff(
    AuditSphereDbContext db, Guid grantedBy, PlanningScope scope, Guid userId, string role, string subject)
  {
    db.Users.Add(new AppUser
    {
      Id = userId,
      FirmId = scope.FirmId,
      Subject = subject + "-" + userId.ToString("N"),
      TenantId = "tenant-planning",
      Email = subject + "@example.test",
      DisplayName = subject,
      UserKind = "Staff",
      SessionEpoch = 1,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(),
      FirmId = scope.FirmId,
      UserId = userId,
      Role = role,
      ClientId = scope.ClientId,
      EngagementId = scope.EngagementId,
      GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = grantedBy
    });
  }
}
