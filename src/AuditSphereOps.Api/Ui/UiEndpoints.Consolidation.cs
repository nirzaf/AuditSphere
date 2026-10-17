using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;
using System.Text.Json.Serialization;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record CreateConsolidationGroupInput(string? Code, string? Name);
  public sealed record AddConsolidationGroupMemberInput(
    Guid ClientId, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, string? ControlMethod,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? OwnershipPercent,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? EconomicInterestPercent,
    string? EvidenceReference);

  public sealed record AdvancedScheduleInput(string SourceManifestJson, string InputSnapshotJson);

  public sealed record CreateConsolidationScopeInput(
    Guid GroupId,
    Guid PeriodId,
    string ReportingCurrency,
    string Method,
    string OpeningBasis,
    Guid? ExchangeRateSetVersionId,
    Guid? TranslationPolicyVersionId,
    DateOnly? TranslationRateDate,
    string? TranslationRateType,
    Guid? PriorScopeVersionId,
    long ExpectedGroupRevision);

  public sealed record SubmitComponentInput(
    Guid ClientId,
    Guid EngagementId,
    Guid PackageId,
    decimal OwnershipPercent,
    string ControlMethod,
    string PeriodBasis,
    string TaxonomyVersion,
    string MappingVersion);

  public sealed record CreateConsolidationJournalInput(
    string JournalNumber,
    string JournalType,
    string Currency,
    string EvidenceReference,
    IReadOnlyList<ConsolidationJournalLineInput> Lines);

  public sealed record ReturnConsolidationJournalInput(string Reason);

  private static void MapConsolidationEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/consolidation", http => ReadAsync(http, (db, actor, ct) => ConsolidationOverviewQuery.GetAsync(db, actor, ct)));
    group.MapPost("/consolidation/groups", (CreateConsolidationGroupInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.CreateGroupAsync(db, actor,
        new ClientGroupRequest(input.Code ?? string.Empty, input.Name ?? string.Empty), ct)));
    group.MapGet("/consolidation/groups/{groupId:guid}/members",
      (Guid groupId, HttpContext http, int page = 0, int pageSize = 25, Guid? clientId = null, DateOnly? effectiveFrom = null) =>
        ReadAsync(http, (db, actor, ct) => ConsolidationGroupWorkspaceQuery.GetAsync(
          db, actor, groupId, page, pageSize, clientId, effectiveFrom, ct)));
    group.MapGet("/consolidation/groups/{groupId:guid}/scope-options", (Guid groupId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ConsolidationScopeCreationQuery.GetAsync(db, actor, groupId, ct)));
    group.MapPost("/consolidation/groups/{groupId:guid}/members",
      (Guid groupId, AddConsolidationGroupMemberInput input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) =>
        {
          if (input.EffectiveFrom is not { } effectiveFrom || input.OwnershipPercent is not { } ownershipPercent ||
              input.EconomicInterestPercent is not { } economicInterestPercent)
            return Task.FromResult(CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
              "Effective date and both percentage-point values are required."));
          return ConsolidationService.AddMembershipAsync(db, actor,
            new GroupMembershipRequest(groupId, input.ClientId, effectiveFrom, input.EffectiveTo,
              input.ControlMethod ?? string.Empty, ownershipPercent, economicInterestPercent,
              input.EvidenceReference ?? string.Empty), ct);
        }));

    // Scope workspace detail & lifecycle
    group.MapGet("/consolidation/scopes/{scopeId:guid}", (Guid scopeId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ConsolidationScopeWorkspaceQuery.GetScopeWorkspaceAsync(db, actor, scopeId, ct)));
    group.MapPost("/consolidation/scopes", (CreateConsolidationScopeInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.CreateScopeAsync(db, actor,
        new ConsolidationScopeRequest(i.GroupId, i.PeriodId, i.ReportingCurrency, i.Method, i.OpeningBasis,
          i.ExchangeRateSetVersionId, i.TranslationPolicyVersionId, i.TranslationRateDate, i.TranslationRateType ?? "",
          i.PriorScopeVersionId, i.ExpectedGroupRevision), ct)));
    group.MapPost("/consolidation/scopes/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveScopeAsync(db, actor, id, ct)));

    // Component commands
    group.MapPost("/consolidation/scopes/{scopeId:guid}/components", (Guid scopeId, SubmitComponentInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.SubmitComponentAsync(db, actor,
        new ConsolidationComponentRequest(scopeId, i.ClientId, i.EngagementId, i.PackageId, i.OwnershipPercent,
          i.ControlMethod, i.PeriodBasis, i.TaxonomyVersion, i.MappingVersion), ct)));
    group.MapPost("/consolidation/components/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveComponentAsync(db, actor, id, ct)));

    // Elimination journals
    group.MapPost("/consolidation/scopes/{scopeId:guid}/journals", (Guid scopeId, CreateConsolidationJournalInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.CreateConsolidationJournalAsync(db, actor,
        new ConsolidationJournalRequest(scopeId, i.JournalNumber, i.JournalType, i.Currency, i.EvidenceReference, i.Lines ?? []), ct)));
    group.MapPost("/consolidation/journals/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveConsolidationJournalAsync(db, actor, id, ct)));
    group.MapPost("/consolidation/journals/{id:guid}/return", (Guid id, ReturnConsolidationJournalInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ReturnConsolidationJournalAsync(db, actor, id, i.Reason ?? "", ct)));
    group.MapPost("/consolidation/journals/{id:guid}/resubmit", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ResubmitConsolidationJournalAsync(db, actor, id, ct)));

    // Consolidation calculation & run approval
    group.MapPost("/consolidation/scopes/{scopeId:guid}/run", (Guid scopeId, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.RunAsync(db, actor, scopeId, ct)));
    group.MapPost("/consolidation/runs/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveRunAsync(db, actor, id, ct)));

    // Advanced schedules and executions
    group.MapGet("/consolidation/advanced/{scopeId:guid}", (Guid scopeId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdvancedConsolidationWorkspaceQuery.GetAsync(db, actor, scopeId, ct)));
    group.MapPost("/consolidation/advanced/{scopeId:guid}/schedules", (Guid scopeId, AdvancedScheduleInput i, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        var workspace = await AdvancedConsolidationWorkspaceQuery.GetAsync(db, actor, scopeId, ct);
        if (!workspace.Succeeded) return Domain.Shared.CommandResult<Guid>.Fail(workspace.ErrorCode!, workspace.Message!);
        return await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, actor, new AdvancedConsolidationMethodScheduleRequest(scopeId, workspace.Value!.Scope.Method,
          "IFRS", i.SourceManifestJson ?? "", i.InputSnapshotJson ?? ""), ct);
      }));
    group.MapPost("/consolidation/schedules/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, actor, id, ct)));
    group.MapPost("/consolidation/advanced/{scopeId:guid}/executions", (Guid scopeId, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AdvancedConsolidationWorkspaceQuery.RunExecutionAsync(db, actor, scopeId, ct)));
    group.MapPost("/consolidation/executions/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveAdvancedExecutionAsync(db, actor, id, ct)));
  }
}
