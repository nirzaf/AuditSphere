using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Practice;

public sealed record CommercialProfileItem(string Version, string LegalName, string Address, string Email, string Phone,
  string Accent, string Closing, string History, string Credentials, string Methodology);
public sealed record CommercialRuleItem(Guid Id, string Version, string Kind, string? Threshold, string Role);
public sealed record CommercialSettingsWorkspace(bool CanEdit, CommercialProfileItem? Profile, string RulesRevision,
  string DefaultDiscountThreshold, string DefaultRole, IReadOnlyList<string> ApproverRoles, IReadOnlyList<CommercialRuleItem> Rules);

public static class CommercialSettingsQuery
{
  public static async Task<CommandResult<CommercialSettingsWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator", "Partner", "Manager", "RelationshipManager"],
      InternalOnly: true, RequireFirmWide: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<CommercialSettingsWorkspace>.Fail(ErrorCodes.ScopeDenied, "Settings unavailable.");
    var profile = await CommercialDocumentService.GetProfileAsync(db, actor, ct);
    var rules = await QuotationService.ListRulesAsync(db, actor, ct);
    if (!profile.Succeeded || !rules.Succeeded || rules.Value!.Count > 100)
      return CommandResult<CommercialSettingsWorkspace>.Fail(ErrorCodes.GateBlocked, "Settings require bounded review.");
    var editable = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, RequiredRoles: ["Administrator", "Partner"], InternalOnly: true, RequireFirmWide: true), ct)).Succeeded;
    var p = profile.Value;
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<CommercialSettingsWorkspace>.Fail(ErrorCodes.ScopeDenied, "Settings unavailable.");
    return CommandResult<CommercialSettingsWorkspace>.Ok(new(editable,
      p is null ? null : new(p.Version.ToString(CultureInfo.InvariantCulture), p.LegalName, p.Address, p.ContactEmail, p.ContactPhone,
        p.AccentColorHex, p.ClosingText, p.FirmHistoryAndRegistrations, p.IndustryCredentials, p.AuditMethodology),
      QuotationService.RulesRevision(rules.Value!), CommercialApprovalMatrix.DefaultDiscountThresholdPercent.ToString(CultureInfo.InvariantCulture),
      CommercialApprovalMatrix.DefaultRole, ["Partner", "Manager", "Administrator"],
      rules.Value!.Select(r => new CommercialRuleItem(r.Id, r.Version.ToString(CultureInfo.InvariantCulture), r.Kind,
        r.ThresholdPercent?.ToString(CultureInfo.InvariantCulture), r.RequiredRole)).ToArray()));
  }
}
