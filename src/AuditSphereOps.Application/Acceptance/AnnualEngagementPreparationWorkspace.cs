using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record AnnualContactReference(Guid Id, string Name, string Role, bool Primary);
public sealed record AnnualMappingReference(Guid Id, string Version, string TaxonomyVersion, string PeriodStart, string PeriodEnd);
public sealed record AnnualTemplateReference(Guid TemplateId, Guid RevisionId, int Version, string Name, string ContentDigest);
public sealed record AnnualMilestoneSuggestion(string Kind, DateOnly SourceDate,
  int OffsetDaysFromSourcePeriodEnd, DateOnly? SuggestedDate, long SourcePlanRevision, string Notice);
public sealed record AnnualEngagementPreparationState(Guid ClientId, Guid SourceEngagementId,
  string SourcePeriodStart, string SourcePeriodEnd, EngagementCreationFields Fields,
  IReadOnlyList<string> CopiedReferences, IReadOnlyList<string> RequiresFreshWork,
  IReadOnlyList<AnnualContactReference> ContactSuggestions, string HistoricalMappingState,
  IReadOnlyList<AnnualMappingReference> HistoricalMappings, IReadOnlyList<AnnualTemplateReference> TaskTemplateSuggestions,
  IReadOnlyList<AnnualMilestoneSuggestion>? MilestoneSuggestions = null,
  string MilestoneSuggestionState = "MISSING");

/// <summary>Harmless next-period preparation over reviewed blocked-shell creation; no approval or audit evidence is copied.</summary>
public static class AnnualEngagementPreparationWorkspace
{
  public static Task<CommandResult<EngagementCreationPreview>> PreviewAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, EngagementCreationRequest request, CancellationToken ct = default) =>
    request.Fields?.AnnualSourceEngagementId is not null ? EngagementCreationWorkspace.PreviewAsync(db, actor, clientId, request, ct)
      : Task.FromResult(CommandResult<EngagementCreationPreview>.Fail("annual.source-required", "Choose an annual source before review."));

  public static Task<CommandResult<EngagementCreationReceipt>> ExecuteAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, EngagementCreationRequest request, CancellationToken ct = default) =>
    request.Fields?.AnnualSourceEngagementId is not null ? EngagementCreationWorkspace.ExecuteAsync(db, actor, clientId, request, ct)
      : Task.FromResult(CommandResult<EngagementCreationReceipt>.Fail("annual.source-required", "Choose an annual source before review."));

  internal static EngagementCreationFields? Suggest(Engagement source)
  {
    if (!DateOnly.TryParseExact(source.PeriodStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
      !DateOnly.TryParseExact(source.PeriodEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
      end.DayNumber - start.DayNumber + 1 is < 365 or > 366 || string.IsNullOrWhiteSpace(source.ServiceRoute) ||
      string.IsNullOrWhiteSpace(source.ServiceProfileId)) return null;
    try
    {
      var next = end.AddDays(1);
      return new(source.ServiceRoute.Trim(), source.ServiceProfileId.Trim(), next.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        next.AddYears(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), source.Id);
    }
    catch (ArgumentOutOfRangeException) { return null; }
  }

  public static async Task<CommandResult<AnnualEngagementPreparationState>> StateAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid clientId, Guid sourceId, CancellationToken ct = default)
  {
    var authorized = await EngagementCreationWorkspace.StateAsync(db, actor, clientId, ct);
    if (!authorized.Succeeded) return CommandResult<AnnualEngagementPreparationState>.Fail(authorized.ErrorCode!, authorized.Message!);
    var source = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.PracticeClientId == clientId && x.Id == sourceId, ct);
    if (source is null) return CommandResult<AnnualEngagementPreparationState>.Fail(ErrorCodes.ScopeDenied, "Annual preparation unavailable.");
    var fields = Suggest(source);
    if (fields is null) return CommandResult<AnnualEngagementPreparationState>.Fail("annual.period-unsupported",
      "This source is not a supported 365- or 366-day engagement with a service profile. Prepare and review the period manually.");
    var milestoneResult = await StatutoryMilestoneService.GetMilestonesAsync(db, actor, sourceId, ct);
    string milestoneSuggestionState;
    IReadOnlyList<AnnualMilestoneSuggestion> milestoneSuggestions = [];
    if (!milestoneResult.Succeeded)
    {
      milestoneSuggestionState = milestoneResult.ErrorCode == ErrorCodes.ScopeDenied ? "RESTRICTED" : "UNAVAILABLE";
    }
    else if (milestoneResult.Value is not { } milestonePlan)
    {
      milestoneSuggestionState = "MISSING";
    }
    else if (DateOnly.TryParseExact(source.PeriodEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var sourcePeriodEnd) &&
      DateOnly.TryParseExact(fields.PeriodEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var nextPeriodEnd))
    {
      milestoneSuggestionState = "AVAILABLE";
      milestoneSuggestions = ProjectMilestones(milestonePlan, sourcePeriodEnd, nextPeriodEnd);
    }
    else
    {
      milestoneSuggestionState = "UNAVAILABLE";
    }
    var references = new List<string> { "Client identity", "Service route", "Service profile", "Prior period calendar reference" };
    // These are discovery references only. The current plan command must independently review and pin a new plan.
    var templates = await CurrentTemplates(db, actor.FirmId, clientId, sourceId, source.ServiceRoute)
      .OrderBy(x => x.Id).Take(3).Select(x => new AnnualTemplateReference(x.TemplateId, x.Id,
        x.Version, x.Name, x.ContentDigest)).ToListAsync(ct);
    if (templates.Count == 1) fields = fields with { AnnualTemplateRevisionId = templates[0].RevisionId };
    foreach (var template in templates)
      references.Add($"Published task template reference: revision {template.RevisionId}, version {template.Version}; review again before use");
    var schedules = await db.WorkflowTaskPlanReceipts.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.EngagementId == sourceId && x.ScheduleBasisDate != null)
      .Select(x => x.ScheduleBasisDate).Distinct().OrderByDescending(x => x).Take(3).ToListAsync(ct);
    foreach (var schedule in schedules)
      references.Add($"Prior task-plan schedule basis: {schedule:yyyy-MM-dd}; do not reuse as a current deadline");
    var nextStart = DateTimeOffset.ParseExact(fields.PeriodStart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
      DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    var contacts = await db.ClientContacts.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.PracticeClientId == clientId && x.IsActive && (x.ValidFrom == null || x.ValidFrom <= nextStart) &&
      (x.ValidTo == null || x.ValidTo >= nextStart)).OrderByDescending(x => x.Primary).ThenBy(x => x.FullName).ThenBy(x => x.Id)
      .Select(x => new AnnualContactReference(x.Id, x.FullName, x.Role, x.Primary)).Take(10).ToListAsync(ct);
    var mappingAuthority = new AuthorizationRequest(actor.FirmId, clientId, sourceId,
      MappingWorkbenchQuery.Roles, InternalOnly: true);
    var canReadMappings = (await AuthorizationDecision.AuthorizeAsync(db, actor, mappingAuthority, ct)).Succeeded;
    IReadOnlyList<AnnualMappingReference> mappings = [];
    if (canReadMappings)
    {
      var priorMappings = await db.MappingVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.EngagementId == sourceId && x.Status == AccountingPackageStates.MappingApproved &&
        x.PeriodStart == source.PeriodStart && x.PeriodEnd == source.PeriodEnd)
        .OrderByDescending(x => x.Version).ThenByDescending(x => x.Id).Take(5)
        .Select(x => new { x.Id, x.Version, x.TaxonomyVersion, x.PeriodStart, x.PeriodEnd }).ToListAsync(ct);
      mappings = priorMappings.Select(x => new AnnualMappingReference(x.Id,
        x.Version.ToString(CultureInfo.InvariantCulture), x.TaxonomyVersion, x.PeriodStart, x.PeriodEnd)).ToArray();
      canReadMappings = (await AuthorizationDecision.AuthorizeAsync(db, actor, mappingAuthority, ct)).Succeeded;
      if (!canReadMappings) mappings = [];
    }
    authorized = await EngagementCreationWorkspace.StateAsync(db, actor, clientId, ct);
    if (!authorized.Succeeded) return CommandResult<AnnualEngagementPreparationState>.Fail(authorized.ErrorCode!, authorized.Message!);
    return CommandResult<AnnualEngagementPreparationState>.Ok(new(clientId, sourceId, source.PeriodStart, source.PeriodEnd, fields,
      references,
      ["Reviewed task template and current-period schedule", "Current-period mapping review", "Client commercial consent", "Partner continuance", "Materiality", "Fieldwork", "Independent review", "Opinion", "Signature", "Fee agreement and advance"],
      contacts, canReadMappings ? "AVAILABLE" : "RESTRICTED", mappings, templates, milestoneSuggestions,
      milestoneSuggestionState));
  }

  internal static IReadOnlyList<AnnualMilestoneSuggestion> ProjectMilestones(MilestonePlanView plan,
    DateOnly sourcePeriodEnd, DateOnly nextPeriodEnd)
  {
    var chronologyValid = plan.FieldworkStartDate <= plan.DraftReportDate &&
      plan.DraftReportDate <= plan.FinalReportDate;
    return
    [
      ProjectInternalMilestone("FIELDWORK_START", plan.FieldworkStartDate, plan.Revision,
        sourcePeriodEnd, nextPeriodEnd, chronologyValid),
      ProjectInternalMilestone("DRAFT_REPORT", plan.DraftReportDate, plan.Revision,
        sourcePeriodEnd, nextPeriodEnd, chronologyValid),
      ProjectInternalMilestone("FINAL_REPORT", plan.FinalReportDate, plan.Revision,
        sourcePeriodEnd, nextPeriodEnd, chronologyValid),
      new("STATUTORY_CUTOFF", plan.StatutoryFilingCutoff,
        plan.StatutoryFilingCutoff.DayNumber - sourcePeriodEnd.DayNumber, null, plan.Revision,
        "Enter a separately verified current-year statutory cutoff; the previous deadline is not reused.")
    ];
  }

  private static AnnualMilestoneSuggestion ProjectInternalMilestone(string kind, DateOnly sourceDate,
    long sourcePlanRevision, DateOnly sourcePeriodEnd, DateOnly nextPeriodEnd, bool chronologyValid)
  {
    var offset = sourceDate.DayNumber - sourcePeriodEnd.DayNumber;
    if (!chronologyValid || offset is < 0 or > 366)
      return new(kind, sourceDate, offset, null, sourcePlanRevision,
        "Historical dates need review; no automatic date was proposed.");
    try
    {
      return new(kind, sourceDate, offset, nextPeriodEnd.AddDays(offset), sourcePlanRevision,
        "Suggested internal target only. Confirm current-year commitments in the planning workspace.");
    }
    catch (ArgumentOutOfRangeException)
    {
      return new(kind, sourceDate, offset, null, sourcePlanRevision,
        "Historical dates need review; no automatic date was proposed.");
    }
  }

  internal static async Task<CommandResult> ValidateAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId,
    EngagementCreationFields fields, bool lockSource, CancellationToken ct)
  {
    if (fields.AnnualSourceEngagementId is not { } sourceId) return CommandResult.Ok();
    var query = lockSource ? db.Engagements.FromSqlInterpolated(
      $"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND practice_client_id={clientId} AND id={sourceId} FOR SHARE").AsNoTracking()
      : db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId && x.Id == sourceId);
    var source = await query.SingleOrDefaultAsync(ct);
    if (source is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Annual preparation unavailable.");
    if (Suggest(source) != (fields with { AnnualTemplateRevisionId = null })) return CommandResult.Fail(ErrorCodes.GenerationStale,
      "The annual reference or suggested fields changed. Refresh the source and review again.");
    if (fields.AnnualTemplateRevisionId is { } revisionId)
    {
      // Publication and archive commands lock the template root; keep the reviewed choice stable through creation.
      if (lockSource && await db.WorkflowTemplates.FromSqlInterpolated(
        $"SELECT * FROM workflow_templates WHERE firm_id={actor.FirmId} AND id=(SELECT template_id FROM workflow_template_revisions WHERE firm_id={actor.FirmId} AND id={revisionId}) FOR SHARE")
        .AsNoTracking().SingleOrDefaultAsync(ct) is null)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The selected annual template changed. Refresh and review again.");
      if (!await CurrentTemplates(db, actor.FirmId, clientId, sourceId, source.ServiceRoute).AnyAsync(x => x.Id == revisionId, ct))
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The selected annual template is no longer a current published reference. Refresh and review again.");
    }
    return CommandResult.Ok();
  }

  private static IQueryable<WorkflowTemplateRevision> CurrentTemplates(IAuditSphereDbContext db, Guid firmId,
    Guid clientId, Guid sourceId, string route) => (from receipt in db.WorkflowTaskPlanReceipts.AsNoTracking()
      join template in db.WorkflowTemplates.AsNoTracking() on receipt.TemplateId equals template.Id
      join revision in db.WorkflowTemplateRevisions.AsNoTracking() on template.Id equals revision.TemplateId
      where receipt.FirmId == firmId && receipt.ClientId == clientId && receipt.EngagementId == sourceId &&
        template.FirmId == firmId && template.ArchivedAt == null && revision.FirmId == firmId &&
        revision.Status == WorkflowTemplateStates.Published && revision.ServiceRoute == route
      select revision).Distinct();
}
