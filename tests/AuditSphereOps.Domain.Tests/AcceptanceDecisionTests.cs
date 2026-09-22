using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AcceptanceDecisionTests
{
  [Fact]
  public async Task AcceptedDecision_IsImmutableAndCreatesOneWaitingWorkspace()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg, "Partner", withResponse: true);
    var request = new RecordAcceptanceDecisionRequest(scope.ClientId, null, "FinancialStatementAudit",
      "ACCEPTED", "Independence and continuance evidence reviewed.", null, 1);

    Guid decisionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await AcceptanceDecisionService.RecordAsync(db, scope.Actor, request);
      Assert.True(result.Succeeded);
      decisionId = result.Value;

      var repeat = await AcceptanceDecisionService.RecordAsync(db, scope.Actor, request);
      Assert.True(repeat.Succeeded);
      Assert.Equal(decisionId, repeat.Value);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var decision = await db.AcceptanceDecisions.SingleAsync(x => x.Id == decisionId);
      Assert.Equal("Accepted", decision.Decision);
      Assert.Equal("FinancialStatementAudit", decision.ServiceRoute);
      Assert.NotEqual(string.Empty, decision.EvaluationSnapshotDigest);
      Assert.Equal("ACCEPTED", (await db.PracticeClients.SingleAsync(x => x.Id == scope.ClientId)).Status);
      var workspace = await db.ClientWorkspaces.SingleAsync(x => x.PracticeClientId == scope.ClientId);
      Assert.Equal(decisionId, workspace.AcceptanceDecisionId);
      Assert.Equal(ClientWorkspaceStates.WaitingForIntegration, workspace.State);
      Assert.Equal($"client-workspace/{scope.FirmId:D}/{scope.ClientId:D}", workspace.LogicalKey);
      Assert.Equal(1, await db.ClientWorkspaces.CountAsync(x => x.FirmId == scope.FirmId && x.PracticeClientId == scope.ClientId));

      decision.Rationale = "tamper";
      var exception = await Record.ExceptionAsync(() => db.SaveChangesAsync());
      Assert.NotNull(exception);
      Assert.Contains("append-only", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }
  }

  [Fact]
  public async Task AcceptanceRequiresCurrentEvaluationAndPartnerScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var incomplete = await SeedAsync(pg, "Partner", withResponse: false);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var blocked = await AcceptanceDecisionService.RecordAsync(db, incomplete.Actor,
        new(incomplete.ClientId, null, "AccountingOnly", "ACCEPTED", "Reviewed.", null, 1));
      Assert.False(blocked.Succeeded);
      Assert.Equal("gate.blocked", blocked.ErrorCode);
      Assert.Empty(await db.AcceptanceDecisions.Where(x => x.FirmId == incomplete.FirmId && x.Decision != "Pending").ToListAsync());
      Assert.Empty(await db.ClientWorkspaces.Where(x => x.FirmId == incomplete.FirmId).ToListAsync());
    }

    var manager = await SeedAsync(pg, "Manager", withResponse: true);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var denied = await AcceptanceDecisionService.RecordAsync(db, manager.Actor,
        new(manager.ClientId, null, "AccountingOnly", "DECLINED", "Not accepted.", null, 1));
      Assert.False(denied.Succeeded);
      Assert.Equal("scope.denied", denied.ErrorCode);
    }
  }

  private static async Task<Scope> SeedAsync(PgTestSchema pg, string role, bool withResponse)
  {
    var (firmId, clientId, _) = await pg.SeedScopeAsync();
    var user = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "acceptance-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = $"{role.ToLowerInvariant()}@example.test", DisplayName = role,
      CreatedAt = DateTimeOffset.UtcNow
    };
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(user);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
    });
    var template = new QuestionnaireTemplate
    {
      Id = Guid.NewGuid(), Bank = "CE", Version = "1.0-" + firmId.ToString("N")[..8], Name = "Acceptance test", IsActive = true,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.QuestionnaireTemplates.Add(template);
    db.QuestionDefinitions.Add(new QuestionDefinition
    {
      Id = Guid.NewGuid(), TemplateId = template.Id, QuestionCode = "CE-01", Section = "A",
      Category = "General", PromptText = "Test question", AnswerType = "BOOLEAN", SortOrder = 1
    });
    if (withResponse)
      db.EvaluationResponses.Add(new EvaluationResponse
      {
        Id = Guid.NewGuid(), FirmId = firmId, PracticeClientId = clientId, Bank = "CE",
        QuestionId = "CE-01", Answer = "Yes", Revision = 1, AnsweredByUserId = user.Id,
        AnsweredAt = DateTimeOffset.UtcNow
      });
    await db.SaveChangesAsync();
    return new(firmId, clientId, new(user.Id, firmId, user.SessionEpoch, [role]));
  }

  private sealed record Scope(Guid FirmId, Guid ClientId, ActorContext Actor);
}
