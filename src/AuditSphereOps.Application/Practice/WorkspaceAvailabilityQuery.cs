using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>Presentation hints only; destination reads and commands retain their own authorization.</summary>
public sealed record WorkspaceAvailabilityView(Guid UserId, Guid FirmId, string Generation, bool Staff,
  IReadOnlyList<string> Destinations, string Tasks, string Reviews, string Templates, int ValidForMs);

public static class WorkspaceAvailabilityQuery
{
  public static async Task<CommandResult<WorkspaceAvailabilityView>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, bool workflowEnabled, CancellationToken ct = default)
  {
    var initial = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, InternalOnly: true), ct);
    if (!initial.Succeeded) return CommandResult<WorkspaceAvailabilityView>.Fail(initial.ErrorCode!, initial.Message!);
    var staff = await db.Users.AsNoTracking().AnyAsync(u => u.Id == actor.UserId && u.FirmId == actor.FirmId &&
      u.SessionEpoch == actor.SessionEpoch && !u.Disabled && u.UserKind == "Staff", ct);
    if (!staff || !actor.Roles.Any() || actor.Roles.Contains("ClientUser"))
      return CommandResult<WorkspaceAvailabilityView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    // An expiry during construction must not release a hint evaluated before that expiry.
    var observedAt = DateTimeOffset.UtcNow;
    var earliestExpiry = await db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId &&
      g.UserId == actor.UserId && g.RevokedAt == null && g.ExpiresAt > observedAt)
      .Select(g => g.ExpiresAt).MinAsync(ct);
    var destinations = new List<string>();
    CommandResult? unexpected = null;
    async Task<bool> Allowed(Task<CommandResult> operation)
    {
      var result = await operation;
      if (result.Succeeded) return true;
      if (result.ErrorCode != ErrorCodes.ScopeDenied && result.ErrorCode != "scope.limit") unexpected ??= result;
      // A scope-window limit is an unavailable data window, not an authorization denial.
      return result.ErrorCode == "scope.limit";
    }
    void Add(bool allowed, params string[] ids) { if (allowed) destinations.AddRange(ids); }

    var tasks = workflowEnabled && await Allowed(WorkflowTaskQueueQuery.AuthorizeWorkspaceReadAsync(db, actor, ct));
    var reviews = workflowEnabled && await Allowed(ProcedureReviewInboxQuery.AuthorizeReadAsync(db, actor, ct));
    var templates = workflowEnabled && await Allowed(WorkflowTemplateService.AuthorizeWorkspaceReadAsync(db, actor, ct));
    var packageReviews = await Allowed(FinancialPackageReviewService.AuthorizeStaffQueueAsync(db, actor, ct));
    Add(tasks || reviews || packageReviews, "my-work");
    Add(await Allowed(PortfolioQuery.AuthorizeWorkspaceReadAsync(db, actor, ct)), "portfolio");
    Add(await Allowed(PracticeLeadQuery.AuthorizeReadAsync(db, actor, ct)), "leads");
    Add(await Allowed(CommercialPipelineQuery.AuthorizeReadAsync(db, actor, ct)), "pipeline");
    Add(await Allowed(ResourcePlanningService.AuthorizeFirmWideAsync(db, actor, ct)), "resources");
    Add(await Allowed(AccountingWorkspaceQuery.AuthorizeWorkspaceReadAsync(db, actor, ct)), "accounting");
    Add(await Allowed(AccountingRecordsQuery.AuthorizeReadAsync(db, actor, ct)), "mappings", "adjustments", "differences");
    Add(await Allowed(AccountingEvidenceQueueQuery.AuthorizeReadAsync(db, actor, ct)), "evidence");
    Add(await Allowed(CurrencyConfigurationWorkspace.AuthorizeReadAsync(db, actor, ct)), "fx-policies");
    Add(await Allowed(CurrencyRemeasurementWorkspaceQuery.AuthorizeReadAsync(db, actor, ct)), "fx-remeasurement");
    // A group grant, creation permission or non-observing oversight eligibility is required for navigation.
    Add(await ConsolidationOverviewQuery.CanReadWorkspaceAsync(db, actor, ct), "consolidation");
    Add(packageReviews, "reviews");
    Add(await Allowed(PeriodMaintenanceQuery.AuthorizeWorkspaceReadAsync(db, actor, ct)), "roll-forward", "restatements");
    Add(await Allowed(PracticeTimeWorkspaceQuery.AuthorizeWorkspaceReadAsync(db, actor, ct)), "time");
    var oversight = await Allowed(AdministratorOversightReadAuthorization.CheckEligibilityAsync(db, actor, ct));
    var finance = await Allowed(FirmFinanceQuery.AuthorizeReadAsync(db, actor, ct));
    Add(finance || oversight, "firm-ledger");
    Add(await FirmBooksWorkspaceQuery.CanReadWorkspaceAsync(db, actor, ct), "firm-books");
    Add(await Allowed(BillingWorkbenchQuery.AuthorizeReadAsync(db, actor, ct)) || oversight, "firm-invoices");
    Add(await Allowed(FeeSettlementWorkspaceQuery.AuthorizeWorkspaceReadAsync(db, actor, ct)), "fee-settlements");
    Add(await Allowed(PracticeAnalyticsQuery.AuthorizeReadAsync(db, actor, ct)), "analytics");
    Add(await Allowed(AuditProgramLibraryQuery.AuthorizeReadAsync(db, actor, ct)), "program-library");
    Add(await Allowed(TechnicalLibraryService.AuthorizeReadAsync(db, actor, ct)), "technical-library");
    Add(await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct), "administration", "users-access", "microsoft365");
    Add(await Allowed(OperationRecoveryService.AuthorizeAsync(db, actor, ct)), "operations");

    if (unexpected is not null) return CommandResult<WorkspaceAvailabilityView>.Fail(unexpected.ErrorCode!, unexpected.Message!);
    var current = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, InternalOnly: true), ct);
    if (!current.Succeeded) return CommandResult<WorkspaceAvailabilityView>.Fail(current.ErrorCode!, current.Message!);
    if (earliestExpiry is { } expiry && expiry <= DateTimeOffset.UtcNow)
      return CommandResult<WorkspaceAvailabilityView>.Fail("scope.changed", "Access changed; refresh the workspace.");
    var validUntil = earliestExpiry is { } nextExpiry && nextExpiry < DateTimeOffset.UtcNow.AddSeconds(30)
      ? nextExpiry : DateTimeOffset.UtcNow.AddSeconds(30);
    var validForMs = (int)Math.Floor((validUntil - DateTimeOffset.UtcNow).TotalMilliseconds);
    if (validForMs <= 0) return CommandResult<WorkspaceAvailabilityView>.Fail("scope.changed", "Access changed; refresh the workspace.");
    string State(bool allowed) => !workflowEnabled ? "not_enabled" : allowed ? "available" : "not_authorized";
    return CommandResult<WorkspaceAvailabilityView>.Ok(new(actor.UserId, actor.FirmId,
      actor.SessionEpoch.ToString(CultureInfo.InvariantCulture), true, destinations,
      State(tasks), State(reviews), State(templates), Math.Min(validForMs, 30000)));
  }
}
