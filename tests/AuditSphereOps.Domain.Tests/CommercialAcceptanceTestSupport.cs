using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal static class CommercialAcceptanceTestSupport
{
  public static CreateOpportunityRequest DiscoveryRequest(Guid leadId, string serviceRoute, string legalName,
    string periodStart, string periodEnd, string? entityScope = null, decimal? probability = null,
    Guid? ownerUserId = null, Guid? requestId = null, string? nextAction = null) =>
    new(leadId, serviceRoute, entityScope ?? EngagementLetterTemplates.DefaultEntityScope(serviceRoute, legalName),
      periodStart, periodEnd, null, null, probability, ownerUserId, nextAction, requestId,
      legalName, EntityScopeConfirmed: true);

  public static async Task<CommandResult<Guid>> AcceptOpportunityAsync(AuditSphereDbContext db,
    ActorContext evaluator, ActorContext partner, Guid firmId, Guid opportunityId, string rationale = "Reviewed client acceptance evidence.",
    string decision = "Accepted")
  {
    foreach (var (actor, role) in new[] { (evaluator, "Manager"), (partner, "Partner") })
    {
      if (!await db.RoleGrants.AnyAsync(x => x.FirmId == firmId && x.UserId == actor.UserId &&
          x.Role == role && x.RevokedAt == null))
        db.RoleGrants.Add(PbcSeed.Grant(firmId,
          await db.Users.SingleAsync(x => x.FirmId == firmId && x.Id == actor.UserId), role));
    }
    await db.SaveChangesAsync();

    var opportunity = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Id == opportunityId && x.AcceptanceClientId != null &&
      x.EntityScopeConfirmedByUserId != null && x.EntityScopeConfirmedAt != null);
    if (opportunity?.AcceptanceClientId is not { } clientId)
      return CommandResult<Guid>.Fail("test.acceptance.unavailable", "A confirmed canonical opportunity prospect is required.");

    if (!await db.QuestionnaireTemplates.AnyAsync(x => x.Id == QuestionnaireSeed.CeTemplateId))
      await QuestionnaireSeed.SeedTemplatesAndDefinitionsAsync(db);

    var checklistResult = await AcceptanceChecklistService.GetAsync(db, evaluator, clientId,
      serviceRoute: opportunity.ServiceRoute, opportunityId: opportunity.Id);
    if (!checklistResult.Succeeded)
      return CommandResult<Guid>.Fail(checklistResult.ErrorCode!, checklistResult.Message!);
    var checklist = checklistResult.Value!;
    if (decision == "Accepted" && checklist.CurrentDecision is { Decision: "Accepted", Conditions: null } current &&
        await OpportunityAcceptanceGate.IsCurrentUnconditionalAsync(db, firmId, opportunity.Id))
      return CommandResult<Guid>.Ok(current.Id);
    if (checklist.CurrentDecision is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The opportunity already has a recorded decision for this evaluation.");
    if (checklist.Path == AcceptancePaths.Continuance)
    {
      var started = await AcceptanceChecklistService.StartContinuanceAsync(db, evaluator, clientId,
        expectedGeneration: checklist.Generation, opportunityId: opportunity.Id, serviceRoute: opportunity.ServiceRoute);
      if (!started.Succeeded)
        return CommandResult<Guid>.Fail(started.ErrorCode!, started.Message!);
      checklistResult = await AcceptanceChecklistService.GetAsync(db, evaluator, clientId,
        serviceRoute: opportunity.ServiceRoute, opportunityId: opportunity.Id);
      if (!checklistResult.Succeeded)
        return CommandResult<Guid>.Fail(checklistResult.ErrorCode!, checklistResult.Message!);
      checklist = checklistResult.Value!;
    }
    foreach (var item in checklist.Items)
    {
      var answer = item.Question.AdverseAnswer switch
      {
        "NO" => "Yes",
        "YES" => "No",
        _ => "Yes"
      };
      var evidence = item.Question.RequiresEvidence ? $"TEST-EVIDENCE-{item.Question.Code}" : null;
      var saved = await AcceptanceChecklistService.RecordAnswerAsync(db, evaluator, clientId,
        item.Question.Code, answer, evidence, expectedGeneration: checklist.Generation,
        expectedRevision: item.Answer?.Revision ?? 0, serviceRoute: opportunity.ServiceRoute,
        opportunityId: opportunity.Id);
      if (!saved.Succeeded)
        return CommandResult<Guid>.Fail(saved.ErrorCode!, $"{item.Question.Code}: {saved.Message}");
    }

    return await AcceptanceDecisionService.RecordAsync(db, partner,
      new(clientId, null, opportunity.ServiceRoute, decision, rationale,
        decision == "AcceptedWithConditions" ? "Reassessment condition for test" : null,
        checklist.Generation, opportunity.Id));
  }
}
