using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>Assessment states. UNKNOWN means the viewer cannot read, or the system cannot establish, the evidence; it is never zero or clear.</summary>
public static class ClientOffboardingStates
{
  public const string ActionRequired = "ACTION_REQUIRED";
  public const string Unknown = "UNKNOWN";
  public const string NoFindings = "NO_FINDINGS";
}

/// <summary>One assessed obligation. <see cref="Count"/> is null whenever <see cref="State"/> is UNKNOWN.</summary>
public sealed record ClientOffboardingItem(string Key, string Label, string State, long? Count, string? Detail);

/// <summary>A section is read from its owning module under that module's own read authority; <see cref="OwnerRoute"/> is set only when the viewer holds it.</summary>
public sealed record ClientOffboardingSection(
  string Key, string Title, string State, string? OwnerRoute, IReadOnlyList<ClientOffboardingItem> Items);

/// <summary>
/// Read-only closeout assessment. It records nothing and changes no engagement, money, grant or provider state, and
/// <see cref="OverallState"/> never asserts that offboarding is complete; NO_FINDINGS covers only the listed items
/// (see <see cref="CoverageNotes"/>).
/// </summary>
public sealed record ClientOffboardingReadiness(
  Guid ClientId, bool IsArchived, string ArchiveRevision, DateTimeOffset AssessedAt, string OverallState,
  IReadOnlyList<ClientOffboardingSection> Sections, IReadOnlyList<string> CoverageNotes);

public static partial class PracticeCrmService
{
  private static readonly string[] OffboardingWorkRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff"];
  private static readonly string[] OffboardingFinanceRoles = ["FinanceManager", "FinanceReviewer"];
  private static readonly string[] OffboardingRecordsRoles = ["Administrator", "Partner", "RecordsCustodian"];
  private static readonly string[] OffboardingUnpostedInvoiceStates =
    [BillingStates.InvoiceDraft, BillingStates.InvoiceReviewRequired, BillingStates.InvoiceApproved];

  /// <summary>
  /// Requires the same client-level commercial authority as archiving; each section additionally requires its owning
  /// module's read authority, so the summary never widens commercial staff's finance, work, access or records visibility.
  /// </summary>
  public static async Task<CommandResult<ClientOffboardingReadiness>> GetClientOffboardingReadinessAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    if (clientId == Guid.Empty)
      return CommandResult<ClientOffboardingReadiness>.Fail("crm.invalid", "A client is required.");
    var gate = new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: CommercialRoles, InternalOnly: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, gate, ct);
    if (!auth.Succeeded) return CommandResult<ClientOffboardingReadiness>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == clientId, ct))
      return CommandResult<ClientOffboardingReadiness>.Fail(ErrorCodes.ScopeDenied, "Client unavailable.");

    var latest = await db.ClientLifecycleEvents.AsNoTracking().Where(e => e.FirmId == actor.FirmId && e.ClientId == clientId)
      .OrderByDescending(e => e.Revision).Select(e => new { e.Revision, e.EventKind }).FirstOrDefaultAsync(ct);

    // The receivables report owns balance arithmetic and opens its own snapshot, so it runs before any other read here.
    var finance = await FinanceSectionAsync(db, actor, clientId, ct);
    var work = await WorkSectionAsync(db, actor, clientId, ct);
    var access = await AccessSectionAsync(db, actor, clientId, ct);
    var retention = await RetentionSectionAsync(db, actor, clientId, ct);
    var sections = new[] { work, finance, access, retention };

    // Revoked authority after the sections were read must not leak their contents.
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, gate, ct)).Succeeded)
      return CommandResult<ClientOffboardingReadiness>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    return CommandResult<ClientOffboardingReadiness>.Ok(new ClientOffboardingReadiness(
      clientId, latest?.EventKind == ClientLifecycleEventKinds.Archived,
      (latest?.Revision ?? 0).ToString(CultureInfo.InvariantCulture), DateTimeOffset.UtcNow,
      Aggregate(sections.Select(s => s.State)), sections,
      [
        "Findings cover only the listed items. NO_FINDINGS is not evidence that offboarding is complete.",
        "Unallocated cash, unbilled approved time, fee-agreement milestones and Microsoft 365 / SharePoint / Entra access are not assessed here.",
        "This assessment changes nothing; archive remains a separate profile marker."
      ]));
  }

  private static string Aggregate(IEnumerable<string> states)
  {
    var all = states.ToArray();
    return all.Contains(ClientOffboardingStates.ActionRequired) ? ClientOffboardingStates.ActionRequired
      : all.Contains(ClientOffboardingStates.Unknown) ? ClientOffboardingStates.Unknown
      : ClientOffboardingStates.NoFindings;
  }

  private static ClientOffboardingItem Counted(string key, string label, long count, string? detail = null) =>
    new(key, label, count > 0 ? ClientOffboardingStates.ActionRequired : ClientOffboardingStates.NoFindings, count, detail);

  private static ClientOffboardingItem Unavailable(string key, string label, string reason) =>
    new(key, label, ClientOffboardingStates.Unknown, null, reason);

  private static ClientOffboardingSection Section(string key, string title, string? route, params ClientOffboardingItem[] items) =>
    new(key, title, Aggregate(items.Select(i => i.State)), route, items);

  private static async Task<bool> CanReadAsync(IAuditSphereDbContext db, ActorContext actor, AuthorizationRequest request, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded;

  private static async Task<ClientOffboardingSection> WorkSectionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct)
  {
    const string reason = "Your access does not include engagement work for this client.";
    if (!await CanReadAsync(db, actor,
          new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: OffboardingWorkRoles, InternalOnly: true), ct))
      return Section("WORK", "Open work", null,
        Unavailable("OPEN_ENGAGEMENTS", "Engagements not closed", reason),
        Unavailable("OPEN_TASKS", "Open or in-progress tasks", reason),
        Unavailable("OPEN_PBC_REQUESTS", "Unresolved client document requests", reason));

    var engagements = await db.Engagements.AsNoTracking()
      .Where(e => e.FirmId == actor.FirmId && e.PracticeClientId == clientId).Select(e => new { e.Id, e.Status }).ToListAsync(ct);
    var open = engagements.Where(e => e.Status != "Closed").ToArray();
    var engagementIds = engagements.Select(e => e.Id).ToArray();
    var detail = open.Length == 0 ? null : string.Join(" · ", open.GroupBy(e => e.Status).OrderBy(g => g.Key)
      .Select(g => $"{g.Key} {g.Count().ToString(CultureInfo.InvariantCulture)}"));
    string[] activeTask = [PracticeTimeStates.TaskOpen, PracticeTimeStates.TaskInProgress];
    var tasks = await db.WorkTasks.AsNoTracking().CountAsync(t => t.FirmId == actor.FirmId && activeTask.Contains(t.Status) &&
      (t.ClientId == clientId || (t.EngagementId != null && engagementIds.Contains(t.EngagementId.Value))), ct);
    string[] resolvedPbc = [PbcStates.Accepted, PbcStates.Closed];
    var pbc = await db.PbcRequests.AsNoTracking().CountAsync(r => r.FirmId == actor.FirmId && r.ClientId == clientId &&
      !resolvedPbc.Contains(r.State), ct);
    return Section("WORK", "Open work", "/app/work",
      Counted("OPEN_ENGAGEMENTS", "Engagements not closed", open.Length, detail),
      Counted("OPEN_TASKS", "Open or in-progress tasks", tasks),
      Counted("OPEN_PBC_REQUESTS", "Unresolved client document requests", pbc));
  }

  private static async Task<ClientOffboardingSection> FinanceSectionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct)
  {
    const string reason = "Your access does not include firm finance for this client.";
    var aging = await FirmReceivablesAgingQuery.GetAsync(db, actor, DateOnly.FromDateTime(DateTime.UtcNow), ct);
    if (!aging.Succeeded || aging.Value is null)
      return Section("FINANCE", "Money", null,
        Unavailable("OUTSTANDING_INVOICES", "Posted invoices with an outstanding balance",
          aging.ErrorCode == ErrorCodes.ScopeDenied ? reason : "The receivables balance could not be established."),
        Unavailable("UNPOSTED_INVOICES", "Invoices not yet posted", reason));

    var owed = aging.Value.Rows.Where(r => r.ClientId == clientId && r.Outstanding > 0m).ToArray();
    var detail = owed.Length == 0 ? null : string.Join("; ", owed.GroupBy(r => r.Currency).OrderBy(g => g.Key)
      .Select(g => $"{g.Key} {g.Sum(r => r.Outstanding).ToString("0.00", CultureInfo.InvariantCulture)}"));
    var unposted = await (from invoice in db.Invoices.AsNoTracking()
      join account in db.BillingAccounts.AsNoTracking()
        on new { invoice.FirmId, invoice.BillingAccountId } equals new { account.FirmId, BillingAccountId = account.Id }
      where invoice.FirmId == actor.FirmId && account.PracticeClientId == clientId &&
        OffboardingUnpostedInvoiceStates.Contains(invoice.Status)
      select invoice.Id).CountAsync(ct);
    return Section("FINANCE", "Money", "/app/finance/receivables-aging",
      Counted("OUTSTANDING_INVOICES", "Posted invoices with an outstanding balance", owed.Length, detail),
      Counted("UNPOSTED_INVOICES", "Invoices not yet posted", unposted));
  }

  private static async Task<ClientOffboardingSection> AccessSectionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct)
  {
    const string externalReason = "Microsoft 365, SharePoint and Entra access is not verified by this assessment.";
    var external = Unavailable("EXTERNAL_ACCESS", "External (Microsoft 365) access", externalReason);
    if (!await CanReadAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
          InternalOnly: true, RequireFirmWide: true), ct))
      return Section("ACCESS", "Access", null,
        Unavailable("ACTIVE_GRANTS", "Active grants scoped to this client", "Your access does not include user administration."), external);

    var now = DateTimeOffset.UtcNow;
    var engagementIds = await db.Engagements.AsNoTracking()
      .Where(e => e.FirmId == actor.FirmId && e.PracticeClientId == clientId).Select(e => e.Id).ToListAsync(ct);
    var users = await db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId && g.RevokedAt == null &&
        (g.ExpiresAt == null || g.ExpiresAt > now) &&
        (g.ClientId == clientId || (g.EngagementId != null && engagementIds.Contains(g.EngagementId.Value))))
      .Select(g => g.UserId).Distinct().CountAsync(ct);
    return Section("ACCESS", "Access", "/app/administration/users",
      Counted("ACTIVE_GRANTS", "Active grants scoped to this client", users, "Distinct users holding a current client or engagement grant."),
      external);
  }

  private static async Task<ClientOffboardingSection> RetentionSectionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct)
  {
    if (!await CanReadAsync(db, actor,
          new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: OffboardingRecordsRoles, InternalOnly: true), ct))
    {
      const string reason = "Your access does not include records custody for this client.";
      return Section("RETENTION", "Retention", null,
        Unavailable("UNVERIFIED_ARCHIVES", "Engagement archives not yet verified", reason),
        Unavailable("UNRELEASED_LEGAL_HOLDS", "Legal holds not released", reason));
    }
    var unverified = await db.Archives.AsNoTracking().CountAsync(a => a.FirmId == actor.FirmId && a.ClientId == clientId &&
      a.Status != ArchiveStates.ArchiveVerified, ct);
    var holds = await db.LegalHolds.AsNoTracking().CountAsync(h => h.FirmId == actor.FirmId && h.ClientId == clientId &&
      h.State != "RELEASED", ct);
    return Section("RETENTION", "Retention", null,
      Counted("UNVERIFIED_ARCHIVES", "Engagement archives not yet verified", unverified),
      Counted("UNRELEASED_LEGAL_HOLDS", "Legal holds not released", holds));
  }
}
