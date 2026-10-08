using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>
/// STE v2.1 §3.5 / §4.5.1 default QAR charge-out schedule (STE-GAP-009). The four rates are initialised as ordinary
/// versioned rate cards, so the existing approval rule applies: a preparer cannot approve their own card, and the approved
/// version is what time capture and budgets read. Role aliases are explicit; any other role name inherits no rate.
/// </summary>
public static class SteChargeOutRateBaseline
{
  public const string PolicyVersion = "STE-QAR-CHARGEOUT-2026.1";
  public const string Activity = "General";
  public const string Currency = "QAR";

  public static readonly IReadOnlyList<(string Role, decimal RatePerHour)> Rates =
  [
    ("Engagement Partner", 1000m),
    ("Audit Manager", 750m),
    ("Audit Supervisor", 500m),
    ("Audit Associate", 200m)
  ];

  private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
  {
    ["Supervisor"] = "Audit Supervisor",
    ["Senior"] = "Audit Supervisor",
    ["Associate"] = "Audit Associate",
    ["Junior"] = "Audit Associate"
  };

  /// <summary>The canonical baseline role for a name, or null when the name is not part of the STE schedule.</summary>
  public static string? CanonicalRole(string role)
  {
    var name = role.Trim();
    if (Rates.Any(x => string.Equals(x.Role, name, StringComparison.Ordinal))) return name;
    return Aliases.TryGetValue(name, out var canonical) ? canonical : null;
  }

  /// <summary>
  /// Creates a draft for every baseline rate that does not already have an approved card at the same rate, or a draft
  /// awaiting approval. Initialisation never approves: a separate authorised approver must approve each card.
  /// </summary>
  public static async Task<CommandResult<IReadOnlyList<Guid>>> InitializeDraftsAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var created = new List<Guid>();
    foreach (var (role, rate) in Rates)
    {
      var settled = await db.RateCardVersions.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Role == role &&
        x.Activity == Activity && x.Currency == Currency && x.RatePerHour == rate &&
        (x.Status == PracticeTimeStates.RateApproved || x.Status == PracticeTimeStates.RateDraft), ct);
      if (settled) continue;
      var draft = await PracticeTimeService.ReviseRateCardAsync(db, actor, new RateCardDraftRequest(role, Activity, Currency, rate), ct);
      if (!draft.Succeeded) return CommandResult<IReadOnlyList<Guid>>.Fail(draft.ErrorCode!, draft.Message!);
      created.Add(draft.Value);
    }
    return CommandResult<IReadOnlyList<Guid>>.Ok(created);
  }

  /// <summary>
  /// Baseline state per STE role: APPROVED when an approved card carries the STE rate, DRAFT when one awaits approval,
  /// and MISSING when neither exists. Approved cards are preferred over drafts.
  /// </summary>
  public static async Task<IReadOnlyList<SteBaselineLine>> StatusAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct = default)
  {
    var lines = new List<SteBaselineLine>();
    foreach (var (role, rate) in Rates)
    {
      var card = await db.RateCardVersions.AsNoTracking()
        .Where(x => x.FirmId == firmId && x.Role == role && x.Activity == Activity && x.Currency == Currency && x.RatePerHour == rate &&
          (x.Status == PracticeTimeStates.RateApproved || x.Status == PracticeTimeStates.RateDraft))
        .OrderBy(x => x.Status == PracticeTimeStates.RateApproved ? 0 : 1).ThenByDescending(x => x.Version)
        .FirstOrDefaultAsync(ct);
      lines.Add(card is null
        ? new SteBaselineLine(role, rate, "MISSING", null, null)
        : new SteBaselineLine(role, rate, card.Status == PracticeTimeStates.RateApproved ? "APPROVED" : "DRAFT", card.Id, card.ApprovedByUserId));
    }
    return lines;
  }
}

/// <summary>One STE baseline role: its rate, state (APPROVED, DRAFT or MISSING), and the card that carries it when one exists.</summary>
public sealed record SteBaselineLine(string Role, decimal RatePerHour, string State, Guid? CardId, Guid? ApprovedByUserId);
