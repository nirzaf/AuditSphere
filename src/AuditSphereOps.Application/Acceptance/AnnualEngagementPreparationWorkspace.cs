using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record AnnualContactReference(Guid Id, string Name, string Role, bool Primary);
public sealed record AnnualMappingReference(Guid Id, string Version, string TaxonomyVersion, string PeriodStart, string PeriodEnd);
public sealed record AnnualEngagementPreparationState(Guid ClientId, Guid SourceEngagementId,
  string SourcePeriodStart, string SourcePeriodEnd, EngagementCreationFields Fields,
  IReadOnlyList<string> CopiedReferences, IReadOnlyList<string> RequiresFreshWork,
  IReadOnlyList<AnnualContactReference> ContactSuggestions, string HistoricalMappingState,
  IReadOnlyList<AnnualMappingReference> HistoricalMappings);

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
    var references = new List<string> { "Client identity", "Service route", "Service profile", "Prior period calendar reference" };
    // These are discovery references only. The current plan command must independently review and pin a new plan.
    var templates = await (from receipt in db.WorkflowTaskPlanReceipts.AsNoTracking()
      join template in db.WorkflowTemplates.AsNoTracking() on receipt.TemplateId equals template.Id
      join revision in db.WorkflowTemplateRevisions.AsNoTracking() on template.Id equals revision.TemplateId
      where receipt.FirmId == actor.FirmId && receipt.ClientId == clientId && receipt.EngagementId == sourceId &&
        template.FirmId == actor.FirmId && template.ArchivedAt == null && revision.FirmId == actor.FirmId &&
        revision.Status == WorkflowTemplateStates.Published && revision.ServiceRoute == source.ServiceRoute
      select new { revision.Id, revision.Version }).Distinct().OrderBy(x => x.Id).Take(3).ToListAsync(ct);
    foreach (var template in templates)
      references.Add($"Published task template reference: revision {template.Id}, version {template.Version}; review again before use");
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
      contacts, canReadMappings ? "AVAILABLE" : "RESTRICTED", mappings));
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
    return Suggest(source) == fields ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.GenerationStale,
      "The annual reference or suggested fields changed. Refresh the source and review again.");
  }
}
