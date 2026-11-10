using System.Data;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record CreateFirmFinanceProfileRequest(string FunctionalCurrency, string ProfileKind);
public sealed record FirmFinanceProfileDraft(Guid Id, string FunctionalCurrency, string ProfileKind,
  long Revision, string DraftBasisSha256, Guid CreatedByUserId, DateTimeOffset CreatedAt);
public sealed record ApproveFirmFinanceProfileRequest(Guid ProfileId, long ExpectedRevision, string ExpectedDraftBasisSha256);
public sealed record FirmFinanceProfileApproval(Guid ProfileId, string FunctionalCurrency, string ProfileKind,
  long ApprovedRevision, string ApprovedDraftBasisSha256, Guid ApprovedByUserId, DateTimeOffset ApprovedAt);
public sealed record FirmFinanceProfileView(Guid Id, string FunctionalCurrency, string ProfileKind,
  bool Approved, long? Revision, string? DraftBasisSha256, Guid? CreatedByUserId, DateTimeOffset CreatedAt,
  long? ApprovedRevision, string? ApprovedDraftBasisSha256, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt);

/// <summary>Controlled maker/checker setup for the firm's functional-currency billing profile.
/// It records the explicit profile inputs only; it does not select a production currency or
/// create/post financial transactions.</summary>
public static class FirmFinanceProfileService
{
  private static readonly string[] MakerRoles = ["FinanceManager"];
  private static readonly string[] CheckerRoles = ["FinanceReviewer"];
  private static readonly string[] ReaderRoles = ["FinanceManager", "FinanceReviewer"];

  public static async Task<CommandResult<FirmFinanceProfileView>> ReadAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId,
      RequiredRoles: ReaderRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded)
      return CommandResult<FirmFinanceProfileView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var profile = await db.FirmFinanceProfiles.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId, ct);
    return profile is null
      ? CommandResult<FirmFinanceProfileView>.Fail("finance.profile-not-found", "No finance profile is configured for this firm.")
      : CommandResult<FirmFinanceProfileView>.Ok(ToView(profile));
  }

  public static async Task<CommandResult<FirmFinanceProfileDraft>> CreateDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateFirmFinanceProfileRequest request,
    CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<FirmFinanceProfileDraft>.Fail("finance.profile-transaction-unsupported",
        "Run finance-profile setup outside a caller-owned transaction so current authorization is verified.");
    var currency = (request.FunctionalCurrency ?? string.Empty).Trim().ToUpperInvariant();
    var kind = (request.ProfileKind ?? string.Empty).Trim().ToUpperInvariant();
    if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        kind is not (BillingStates.TestProfile or BillingStates.ProductionProfile))
      return CommandResult<FirmFinanceProfileDraft>.Fail("finance.profile-invalid",
        "Choose a three-letter functional currency and an explicit TEST or PRODUCTION profile kind.");

    var auth = await AuthorizeMakerAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<FirmFinanceProfileDraft>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<FirmFinanceProfileDraft>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizeMakerAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<FirmFinanceProfileDraft>.Fail(auth.ErrorCode!, auth.Message!);

    // The established schema permits one profile per firm, including legacy rows.
    // Never reinterpret or replace that row through the new setup workflow.
    if (await db.FirmFinanceProfiles.AnyAsync(x => x.FirmId == actor.FirmId, ct))
      return CommandResult<FirmFinanceProfileDraft>.Fail("finance.profile-conflict",
        "A finance profile already exists for this firm; existing setup is not overwritten.");

    var now = UtcPostgresInstant();
    var profile = new FirmFinanceProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, FunctionalCurrency = currency,
      ProfileKind = kind, Approved = false, CreatedAt = now, CreatedByUserId = actor.UserId,
      CreatedByEpoch = actor.SessionEpoch, DraftRevision = 1
    };
    profile.DraftBasisSha256 = DraftBasis(profile);
    db.FirmFinanceProfiles.Add(profile);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<FirmFinanceProfileDraft>.Ok(ToDraft(profile));
  }

  public static async Task<CommandResult<FirmFinanceProfileApproval>> ApproveDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, ApproveFirmFinanceProfileRequest request,
    CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<FirmFinanceProfileApproval>.Fail("finance.profile-transaction-unsupported",
        "Run finance-profile approval outside a caller-owned transaction so current authorization is verified.");
    if (request.ProfileId == Guid.Empty || request.ExpectedRevision < 1 || !IsSha256(request.ExpectedDraftBasisSha256))
      return CommandResult<FirmFinanceProfileApproval>.Fail("finance.profile-review-invalid",
        "Reload the exact finance-profile draft before approval.");
    var auth = await AuthorizeCheckerAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<FirmFinanceProfileApproval>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<FirmFinanceProfileApproval>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    auth = await AuthorizeCheckerAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<FirmFinanceProfileApproval>.Fail(auth.ErrorCode!, auth.Message!);

    var profile = await db.FirmFinanceProfiles.FromSqlInterpolated(
      $"SELECT * FROM firm_finance_profiles WHERE firm_id={actor.FirmId} AND id={request.ProfileId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (profile is null)
      return CommandResult<FirmFinanceProfileApproval>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (profile.Approved)
      return profile.ApprovedByUserId == actor.UserId &&
        profile.ApprovedDraftRevision == request.ExpectedRevision &&
        string.Equals(profile.ApprovedDraftBasisSha256, request.ExpectedDraftBasisSha256, StringComparison.Ordinal)
          ? CommandResult<FirmFinanceProfileApproval>.Ok(ToApproval(profile))
          : CommandResult<FirmFinanceProfileApproval>.Fail("finance.profile-already-approved",
            "This finance-profile draft was approved by another reviewer; it cannot be approved again.");
    if (profile.CreatedByUserId is null || profile.CreatedByEpoch is null || profile.DraftRevision is null ||
        profile.DraftBasisSha256 is null || profile.DraftRevision != request.ExpectedRevision ||
        !IsSha256(profile.DraftBasisSha256) ||
        !string.Equals(profile.DraftBasisSha256, DraftBasis(profile), StringComparison.Ordinal) ||
        !string.Equals(profile.DraftBasisSha256, request.ExpectedDraftBasisSha256, StringComparison.Ordinal))
      return CommandResult<FirmFinanceProfileApproval>.Fail(ErrorCodes.GenerationStale,
        "The finance-profile draft changed; reload its current revision and basis before approval.");
    if (profile.CreatedByUserId == actor.UserId)
      return CommandResult<FirmFinanceProfileApproval>.Fail(ErrorCodes.ScopeDenied,
        "The finance-profile maker cannot approve their own draft.");

    var approvedAt = UtcPostgresInstant();
    var changed = await db.FirmFinanceProfiles.Where(x => x.FirmId == actor.FirmId && x.Id == request.ProfileId &&
        !x.Approved && x.DraftRevision == request.ExpectedRevision &&
        x.DraftBasisSha256 == request.ExpectedDraftBasisSha256 && x.CreatedByUserId == profile.CreatedByUserId)
      .ExecuteUpdateAsync(update => update
        .SetProperty(x => x.Approved, true)
        .SetProperty(x => x.ApprovedByUserId, actor.UserId)
        .SetProperty(x => x.ApprovedAt, approvedAt)
        .SetProperty(x => x.ApprovedDraftRevision, request.ExpectedRevision)
        .SetProperty(x => x.ApprovedDraftBasisSha256, request.ExpectedDraftBasisSha256), ct);
    if (changed != 1)
      return CommandResult<FirmFinanceProfileApproval>.Fail(ErrorCodes.GenerationStale,
        "The finance-profile draft changed before approval was recorded.");
    await tx.CommitAsync(ct);
    return CommandResult<FirmFinanceProfileApproval>.Ok(new(profile.Id, profile.FunctionalCurrency,
      profile.ProfileKind, request.ExpectedRevision, request.ExpectedDraftBasisSha256, actor.UserId, approvedAt));
  }

  public static string DraftBasis(FirmFinanceProfile profile) => Hashing.Sha256Hex(JsonSerializer.Serialize(new
  {
    profile.FirmId, profile.Id, profile.FunctionalCurrency, profile.ProfileKind,
    Maker = profile.CreatedByUserId, MakerEpoch = profile.CreatedByEpoch,
    profile.CreatedAt, Revision = profile.DraftRevision
  }));

  private static FirmFinanceProfileDraft ToDraft(FirmFinanceProfile profile) => new(profile.Id,
    profile.FunctionalCurrency, profile.ProfileKind, profile.DraftRevision!.Value,
    profile.DraftBasisSha256!, profile.CreatedByUserId!.Value, profile.CreatedAt);

  private static FirmFinanceProfileView ToView(FirmFinanceProfile profile) => new(profile.Id,
    profile.FunctionalCurrency, profile.ProfileKind, profile.Approved, profile.DraftRevision,
    profile.DraftBasisSha256, profile.CreatedByUserId, profile.CreatedAt,
    profile.ApprovedDraftRevision, profile.ApprovedDraftBasisSha256, profile.ApprovedByUserId, profile.ApprovedAt);

  private static FirmFinanceProfileApproval ToApproval(FirmFinanceProfile profile) => new(profile.Id,
    profile.FunctionalCurrency, profile.ProfileKind, profile.ApprovedDraftRevision!.Value,
    profile.ApprovedDraftBasisSha256!, profile.ApprovedByUserId!.Value, profile.ApprovedAt!.Value);

  private static bool IsSha256(string? value) => value is { Length: 64 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static DateTimeOffset UtcPostgresInstant()
  {
    var now = DateTimeOffset.UtcNow;
    return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
  }

  private static Task<CommandResult> AuthorizeMakerAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, RequiredRoles: MakerRoles,
      InternalOnly: true, RequireFirmWide: true), ct);

  private static Task<CommandResult> AuthorizeCheckerAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, RequiredRoles: CheckerRoles,
      InternalOnly: true, RequireFirmWide: true), ct);

  private static async Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
}
