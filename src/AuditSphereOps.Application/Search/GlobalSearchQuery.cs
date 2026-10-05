using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Search;

/// <summary>One search hit: a record or page the actor can open right now, with a real application route.</summary>
public sealed record GlobalSearchHit(string Kind, string Title, string Detail, string Href);

public sealed record GlobalSearchResult(string Term, IReadOnlyList<GlobalSearchHit> Hits, bool Truncated);

/// <summary>
/// Bounded staff navigation search (UX-029). It covers exactly: clients, engagements, PBC requests, leads,
/// invoices and staff pages. Every record hit is re-authorized with the same decision its detail route applies,
/// so a hit never discloses a record the link would refuse. It is not a document, email or evidence search.
/// </summary>
public static class GlobalSearchQuery
{
  public const int MinimumTermLength = 2;
  public const int MaximumTermLength = 100;
  private const int PerKind = 6;
  private const int Candidates = 25;

  public const string Coverage =
    "Searches clients, engagements, PBC requests, leads, invoices, the technical library and staff pages you can open. Client documents, evidence and emails are not searched.";

  public static class Kinds
  {
    public const string Client = "Client";
    public const string Engagement = "Engagement";
    public const string PbcRequest = "PBC request";
    public const string Lead = "Lead";
    public const string Invoice = "Invoice";
    public const string Library = "Technical library";
    public const string Page = "Page";
  }

  // The same role sets the destination routes authorize with.
  private static readonly string[] ClientRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "CommercialManager", "EngagementLeader"];
  private static readonly string[] EngagementRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"];
  private static readonly string[] PbcRoles = ["Administrator", "Partner", "Manager", "Reviewer", "Senior", "Staff", "Auditor", "Accountant", "AccountingPreparer", "AccountingReviewer"];
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];
  private static readonly string[] FinanceRoles = ["FinanceManager", "FinanceReviewer"];

  private static readonly (string Title, string Href, string Keywords)[] Pages =
  [
    ("Portfolio", "/app", "home overview dashboard clients"),
    ("Practice leads", "/app/practice/leads", "crm opportunities proposals"),
    ("Commercial settings", "/app/practice/commercial-settings", "letterhead approval matrix discount quotation"),
    ("Practice time & tasks", "/app/practice/time", "timesheet tasks budget"),
    ("Firm finance", "/app/finance", "ledger invoices receipts billing"),
    ("Client accounting workspace", "/app/accounting", "periods books chart"),
    ("Accounting evidence queue", "/app/accounting/evidence", "reconciliation evidence"),
    ("COA and mappings", "/app/accounting/mappings", "chart of accounts taxonomy"),
    ("Adjustment journals", "/app/accounting/journals", "adjustments aje"),
    ("Audit differences", "/app/accounting/differences", "misstatements"),
    ("Financial package reviews", "/app/accounting/reviews", "packages statements"),
    ("Period roll-forward", "/app/accounting/rollforward", "opening balances"),
    ("Period restatements", "/app/accounting/restatements", "ias 8"),
    ("Currency remeasurement", "/app/accounting/remeasurement", "fx foreign exchange"),
    ("Group consolidation", "/app/consolidation", "group eliminations fx translation"),
    ("Audit program library", "/app/audit/library", "procedures program"),
    ("Operations", "/app/operations", "durable operations worker"),
    ("Firm administration", "/app/administration", "users roles access microsoft 365"),
    ("Project task progress", "/app/administration/project-progress", "progress tasks"),
  ];

  public static async Task<CommandResult<GlobalSearchResult>> SearchAsync(
    IAuditSphereDbContext db, ActorContext actor, string? term, CancellationToken ct = default)
  {
    var text = (term ?? string.Empty).Trim();
    if (text.Length < MinimumTermLength)
      return CommandResult<GlobalSearchResult>.Ok(new(text, [], false));
    if (text.Length > MaximumTermLength) text = text[..MaximumTermLength];

    // Staff-only surface: client identities and stale or disabled sessions get no results at all.
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId, ct);
    if (user is null || user.Disabled || user.SessionEpoch != actor.SessionEpoch ||
        user.UserKind.Equals("Client", StringComparison.OrdinalIgnoreCase) ||
        actor.Roles.Contains("ClientUser", StringComparer.OrdinalIgnoreCase))
      return CommandResult<GlobalSearchResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var now = DateTimeOffset.UtcNow;
    var grants = await db.RoleGrants.AsNoTracking()
      .Where(g => g.UserId == actor.UserId && g.FirmId == actor.FirmId && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now))
      .Select(g => new { g.Role, g.ClientId, g.EngagementId })
      .ToListAsync(ct);
    if (grants.Count == 0)
      return CommandResult<GlobalSearchResult>.Ok(new(text, [], false));
    // Candidate scope per result kind comes only from grants whose role opens that kind's route, so an unrelated
    // firm-wide grant cannot crowd authorized records out of the bounded candidate set.
    (bool FirmWide, List<Guid> ClientIds, List<Guid> EngagementIds) ScopeFor(string[] roles)
    {
      var relevant = grants.Where(g => roles.Contains(g.Role, StringComparer.OrdinalIgnoreCase)).ToList();
      return (relevant.Any(g => g.ClientId is null && g.EngagementId is null),
        relevant.Where(g => g.ClientId.HasValue && g.EngagementId is null).Select(g => g.ClientId!.Value).Distinct().ToList(),
        relevant.Where(g => g.EngagementId.HasValue).Select(g => g.EngagementId!.Value).Distinct().ToList());
    }
    var lowered = text.ToLowerInvariant();
    var hits = new List<GlobalSearchHit>();
    var truncated = false;

    // Clients: candidate set limited to the actor's firm-wide or client grants, then re-authorized per hit.
    var (firmWide, clientIds, _) = ScopeFor(ClientRoles);
    var clients = await db.PracticeClients.AsNoTracking()
      .Where(c => c.FirmId == actor.FirmId && (firmWide || clientIds.Contains(c.Id)) &&
        (c.LegalName.ToLower().Contains(lowered) || (c.CommercialName != null && c.CommercialName.ToLower().Contains(lowered)) ||
         (c.RegistrationNumber != null && c.RegistrationNumber.ToLower().Contains(lowered))))
      .OrderBy(c => c.LegalName).Take(Candidates)
      .Select(c => new { c.Id, c.LegalName, c.CommercialName, c.Status })
      .ToListAsync(ct);
    truncated |= await AddAuthorizedAsync(hits, clients, async c => (await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, c.Id, RequiredRoles: ClientRoles, InternalOnly: true), ct)).Succeeded,
      c => new(Kinds.Client, c.LegalName, string.IsNullOrWhiteSpace(c.CommercialName) ? $"Client · {c.Status}" : $"Trading as {c.CommercialName} · {c.Status}",
        $"/app/clients/{c.Id:D}"));

    // Engagements match on their own service route or their client's name.
    (firmWide, clientIds, var engagementIds) = ScopeFor(EngagementRoles);
    var engagements = await (
        from e in db.Engagements.AsNoTracking()
        join c in db.PracticeClients.AsNoTracking() on new { e.FirmId, Id = e.PracticeClientId } equals new { c.FirmId, c.Id }
        where e.FirmId == actor.FirmId && (firmWide || engagementIds.Contains(e.Id) || clientIds.Contains(e.PracticeClientId)) &&
          (c.LegalName.ToLower().Contains(lowered) || e.ServiceRoute.ToLower().Contains(lowered))
        orderby c.LegalName, e.PeriodEnd descending
        select new { e.Id, e.ServiceRoute, e.PeriodStart, e.PeriodEnd, e.Status, ClientName = c.LegalName })
      .Take(Candidates).ToListAsync(ct);
    truncated |= await AddAuthorizedAsync(hits, engagements, async e => (await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, EngagementId: e.Id, RequiredRoles: EngagementRoles, InternalOnly: true), ct)).Succeeded,
      e => new(Kinds.Engagement, $"{e.ClientName} — {Blank(e.ServiceRoute, "Engagement")}",
        $"{Period(e.PeriodStart, e.PeriodEnd)} · {e.Status}", $"/app/engagements/{e.Id:D}"));

    (firmWide, clientIds, engagementIds) = ScopeFor(PbcRoles);
    var requests = await (
        from r in db.PbcRequests.AsNoTracking()
        join c in db.PracticeClients.AsNoTracking() on new { r.FirmId, Id = r.ClientId } equals new { c.FirmId, c.Id }
        where r.FirmId == actor.FirmId && (firmWide || engagementIds.Contains(r.EngagementId) || clientIds.Contains(r.ClientId)) &&
          (r.Objective.ToLower().Contains(lowered) || r.Area.ToLower().Contains(lowered) || c.LegalName.ToLower().Contains(lowered))
        orderby r.UpdatedAt descending
        select new { r.Id, r.ClientId, r.EngagementId, r.Objective, r.Area, r.State, ClientName = c.LegalName })
      .Take(Candidates).ToListAsync(ct);
    truncated |= await AddAuthorizedAsync(hits, requests, async r => (await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, r.ClientId, r.EngagementId, RequiredRoles: PbcRoles, InternalOnly: true), ct)).Succeeded,
      r => new(Kinds.PbcRequest, Trim(r.Objective, 90), $"{r.ClientName} · {Blank(r.Area, "PBC")} · {r.State}",
        $"/app/engagements/{r.EngagementId:D}/pbc"));

    // Leads are firm-level commercial records: the leads route requires a firm-wide commercial grant.
    if (ScopeFor(CommercialRoles).FirmWide && (await AuthorizationDecision.AuthorizeAsync(db, actor,
          new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct)).Succeeded)
    {
      var leads = await db.Leads.AsNoTracking()
        .Where(l => l.FirmId == actor.FirmId && (l.Name.ToLower().Contains(lowered) ||
          (l.PrimaryContactName != null && l.PrimaryContactName.ToLower().Contains(lowered))))
        .OrderBy(l => l.Name).Take(PerKind + 1)
        .Select(l => new { l.Id, l.Name, l.Status, l.Source })
        .ToListAsync(ct);
      truncated |= leads.Count > PerKind;
      hits.AddRange(leads.Take(PerKind).Select(l => new GlobalSearchHit(Kinds.Lead, l.Name, $"Lead · {l.Status} · {l.Source}", "/app/practice/leads")));
    }

    (firmWide, clientIds, _) = ScopeFor(FinanceRoles);
    var invoices = await (
        from i in db.Invoices.AsNoTracking()
        join a in db.BillingAccounts.AsNoTracking() on new { i.FirmId, Id = i.BillingAccountId } equals new { a.FirmId, a.Id }
        join c in db.PracticeClients.AsNoTracking() on new { a.FirmId, Id = a.PracticeClientId } equals new { c.FirmId, c.Id }
        where i.FirmId == actor.FirmId && (firmWide || clientIds.Contains(a.PracticeClientId)) &&
          (i.InvoiceNumber.ToLower().Contains(lowered) || c.LegalName.ToLower().Contains(lowered))
        orderby i.CreatedAt descending
        select new { i.Id, i.InvoiceNumber, i.Status, i.Total, i.Currency, ClientName = c.LegalName })
      .Take(Candidates).ToListAsync(ct);
    truncated |= await AddAuthorizedAsync(hits, invoices, i => BillingService.CanOpenInvoiceAsync(db, actor, i.Id, ct),
      i => new(Kinds.Invoice, i.InvoiceNumber, $"{i.ClientName} · {i.Status} · {i.Total:N2} {i.Currency}".TrimEnd(), $"/app/practice/invoices/{i.Id:D}"));

    // Technical library: published versions the actor's audience allows; each hit names its version.
    var library = await TechnicalLibraryService.SearchAsync(db, actor, text, ct);
    truncated |= library.Count > PerKind;
    hits.AddRange(library.Take(PerKind).Select(h => new GlobalSearchHit(Kinds.Library, $"{h.Code} — {h.Title}", $"{h.Category} · v{h.Version} · {Trim(h.Snippet, 90)}", $"/app/library/{h.DocumentId:D}")));

    // Pages are navigation, not data: each route still authorizes its own content when opened.
    var pages = Pages.Where(p => p.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        p.Keywords.Contains(text, StringComparison.OrdinalIgnoreCase))
      .Take(PerKind + 1).ToArray();
    truncated |= pages.Length > PerKind;
    hits.AddRange(pages.Take(PerKind).Select(p => new GlobalSearchHit(Kinds.Page, p.Title, "Page", p.Href)));

    return CommandResult<GlobalSearchResult>.Ok(new(text, hits, truncated));
  }

  private static async Task<bool> AddAuthorizedAsync<T>(List<GlobalSearchHit> hits, IReadOnlyList<T> candidates,
    Func<T, Task<bool>> authorized, Func<T, GlobalSearchHit> project)
  {
    var added = 0;
    foreach (var candidate in candidates)
    {
      if (!await authorized(candidate)) continue;
      if (added == PerKind) return true;
      hits.Add(project(candidate));
      added++;
    }
    return false;
  }

  private static string Blank(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
  private static string Period(string start, string end) =>
    string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end) ? "Period not set" : $"{start} to {end}";
  private static string Trim(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}
