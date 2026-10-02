using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Application.Accounting;

public sealed record CurrencyConfigurationCatalogue(string Revision, IReadOnlyList<ExchangeRateSetVersion> RateSets, IReadOnlyList<TranslationPolicyVersion> Policies, bool CanWrite);
public sealed record CurrencyRateSetReview(ExchangeRateSetVersion Set, IReadOnlyList<ExchangeRate> Rates, string Revision, bool CanWrite, bool CanApprove);
public sealed record CurrencyPolicyReview(TranslationPolicyVersion Policy, string Revision, bool CanApprove);
/// <summary>Reviewed, bounded firm configuration. Uses existing currency authorities, calculations and maker/checker services.</summary>
public static class CurrencyConfigurationWorkspace
{
  private static readonly string[] Roles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static Task<CommandResult> Auth(IClientAccountingDbContext db, ActorContext a, bool write, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, a, new(a.FirmId, RequiredRoles: Roles, InternalOnly: true, RequireFirmWide: true), ct);
  private static string Revision(ActorContext a, object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, value }))));
  public static async Task<CommandResult<CurrencyConfigurationCatalogue>> GetAsync(IClientAccountingDbContext db, ActorContext a, CancellationToken ct = default)
  {
    var auth = await Auth(db, a, false, ct); if (!auth.Succeeded) return CommandResult<CurrencyConfigurationCatalogue>.Fail(auth.ErrorCode!, auth.Message!);
    var sets = await db.ExchangeRateSetVersions.AsNoTracking().Where(x => x.FirmId == a.FirmId).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(1001).ToListAsync(ct);
    var policies = await db.TranslationPolicyVersions.AsNoTracking().Where(x => x.FirmId == a.FirmId).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(1001).ToListAsync(ct);
    if (sets.Count > 1000 || policies.Count > 1000) return CommandResult<CurrencyConfigurationCatalogue>.Fail("window.exceeded", "The currency configuration exceeds the interactive catalogue window.");
    auth = await Auth(db, a, false, ct); if (!auth.Succeeded) return CommandResult<CurrencyConfigurationCatalogue>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<CurrencyConfigurationCatalogue>.Ok(new(Revision(a, new { sets, policies }), sets, policies, (await Auth(db, a, true, ct)).Succeeded));
  }
  public static async Task<CommandResult<CurrencyRateSetReview>> RateSetAsync(IClientAccountingDbContext db, ActorContext a, Guid id, CancellationToken ct = default)
  {
    var auth = await Auth(db, a, false, ct); if (!auth.Succeeded) return CommandResult<CurrencyRateSetReview>.Fail(auth.ErrorCode!, auth.Message!);
    var set = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (set is null) return CommandResult<CurrencyRateSetReview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var rates = await db.ExchangeRates.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.RateSetVersionId == id).OrderBy(x => x.RateDate).ThenBy(x => x.FromCurrency).ThenBy(x => x.ToCurrency).ThenBy(x => x.RateType).ThenBy(x => x.Id).Take(1001).ToListAsync(ct);
    if (rates.Count > 1000) return CommandResult<CurrencyRateSetReview>.Fail("window.exceeded", "This rate set exceeds the interactive observation window.");
    auth = await Auth(db, a, false, ct); if (!auth.Succeeded) return CommandResult<CurrencyRateSetReview>.Fail(auth.ErrorCode!, auth.Message!);
    var write = (await Auth(db, a, true, ct)).Succeeded && set.Status == AccountingWorkflowStates.Draft;
    return CommandResult<CurrencyRateSetReview>.Ok(new(set, rates, Revision(a, new { set, rates }), write, write && set.CreatedByUserId != a.UserId && ValidObservations(set, rates)));
  }
  private static bool ValidObservations(ExchangeRateSetVersion set, IReadOnlyList<ExchangeRate> rates) =>
    set.Version >= 1 && !string.IsNullOrWhiteSpace(set.Source) && rates.Count > 0 &&
    set.EffectiveFrom is { } from && set.EffectiveTo is { } to && from <= to &&
    rates.All(x => x.Rate > 0 && x.Direction == ExchangeRateDirections.Direct && x.RateDate >= from && x.RateDate <= to &&
      x.FromCurrency.Length == 3 && x.ToCurrency.Length == 3 && x.FromCurrency != x.ToCurrency &&
      x.RateType is "CLOSING" or "AVERAGE" or "HISTORICAL") &&
    rates.Select(x => (x.FromCurrency, x.ToCurrency, x.RateDate, x.RateType)).Distinct().Count() == rates.Count;
  public static async Task<CommandResult<CurrencyPolicyReview>> PolicyAsync(IClientAccountingDbContext db, ActorContext a, Guid id, CancellationToken ct = default)
  {
    var auth = await Auth(db, a, false, ct); if (!auth.Succeeded) return CommandResult<CurrencyPolicyReview>.Fail(auth.ErrorCode!, auth.Message!);
    var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId && x.Id == id, ct);
    if (policy is null) return CommandResult<CurrencyPolicyReview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await Auth(db, a, false, ct); if (!auth.Succeeded) return CommandResult<CurrencyPolicyReview>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<CurrencyPolicyReview>.Ok(new(policy, Revision(a, policy), policy.Status == AccountingWorkflowStates.Draft && policy.CreatedByUserId != a.UserId && (await Auth(db, a, true, ct)).Succeeded));
  }
  private static async Task<CommandResult<T>> Guard<T>(IClientAccountingDbContext db, ActorContext a, string expected, bool reviewed,
    Func<Task<CommandResult<string>>> current, Func<Task<CommandResult<T>>> command, CancellationToken ct)
  {
    var auth = await Auth(db, a, true, ct); if (!auth.Succeeded) return CommandResult<T>.Fail(auth.ErrorCode!, auth.Message!);
    if (!reviewed) return CommandResult<T>.Fail(ErrorCodes.StaleRevision, "Review the exact current currency configuration before submitting.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var firm = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {a.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (firm is null) return CommandResult<T>.Fail(ErrorCodes.GateBlocked, "The firm safety guard is unavailable.");
    auth = await Auth(db, a, true, ct); if (!auth.Succeeded) return CommandResult<T>.Fail(auth.ErrorCode!, auth.Message!);
    var revision = await current(); if (!revision.Succeeded) return CommandResult<T>.Fail(revision.ErrorCode!, revision.Message!);
    if (revision.Value != expected) return CommandResult<T>.Fail(ErrorCodes.StaleRevision, "Currency inputs changed. Refresh and review the current revision.");
    var result = await command();
    if (!result.Succeeded) return result;
    auth = await Auth(db, a, true, ct);
    if (!auth.Succeeded) return CommandResult<T>.Fail(auth.ErrorCode!, auth.Message!);
    await tx.CommitAsync(ct); return result;
  }
  private static async Task<CommandResult<string>> CatalogueRevision(IClientAccountingDbContext db, ActorContext a, CancellationToken ct) { var r = await GetAsync(db, a, ct); return r.Succeeded ? CommandResult<string>.Ok(r.Value!.Revision) : CommandResult<string>.Fail(r.ErrorCode!, r.Message!); }
  private static async Task<CommandResult<string>> SetRevision(IClientAccountingDbContext db, ActorContext a, Guid id, CancellationToken ct) { var r = await RateSetAsync(db, a, id, ct); return r.Succeeded ? CommandResult<string>.Ok(r.Value!.Revision) : CommandResult<string>.Fail(r.ErrorCode!, r.Message!); }
  private static async Task<CommandResult<string>> PolicyRevision(IClientAccountingDbContext db, ActorContext a, Guid id, CancellationToken ct) { var r = await PolicyAsync(db, a, id, ct); return r.Succeeded ? CommandResult<string>.Ok(r.Value!.Revision) : CommandResult<string>.Fail(r.ErrorCode!, r.Message!); }
  public static Task<CommandResult<Guid>> CreateSetAsync(IClientAccountingDbContext db, ActorContext a, ExchangeRateSetRequest request, string revision, bool reviewed, CancellationToken ct = default) =>
    Guard(db, a, revision, reviewed, () => CatalogueRevision(db, a, ct), () => CurrencyTranslationService.CreateRateSetAsync(db, a, request, ct), ct);
  public static Task<CommandResult<Guid>> CreatePolicyAsync(IClientAccountingDbContext db, ActorContext a, TranslationPolicyRequest request, string revision, bool reviewed, CancellationToken ct = default) =>
    Guard(db, a, revision, reviewed, () => CatalogueRevision(db, a, ct), () => CurrencyTranslationService.CreatePolicyAsync(db, a, request, ct), ct);
  public static Task<CommandResult<bool>> AddRateAsync(IClientAccountingDbContext db, ActorContext a, Guid id, ExchangeRateInput request, string revision, bool reviewed, CancellationToken ct = default) =>
    Guard(db, a, revision, reviewed, () => SetRevision(db, a, id, ct), async () => AsValue(await CurrencyTranslationService.AddRateAsync(db, a, id, request, ct)), ct);
  public static Task<CommandResult<bool>> ApproveSetAsync(IClientAccountingDbContext db, ActorContext a, Guid id, string revision, bool reviewed, CancellationToken ct = default) =>
    Guard(db, a, revision, reviewed, () => SetRevision(db, a, id, ct), async () => {
      var current = await RateSetAsync(db, a, id, ct);
      if (!current.Succeeded) return CommandResult<bool>.Fail(current.ErrorCode!, current.Message!);
      if (!current.Value!.CanApprove) return CommandResult<bool>.Fail(ErrorCodes.GateBlocked, "Independent approval requires a populated current draft with valid DIRECT observations, declared purposes and effective dates.");
      return AsValue(await CurrencyTranslationService.ApproveRateSetAsync(db, a, id, ct));
    }, ct);
  public static Task<CommandResult<bool>> ApprovePolicyAsync(IClientAccountingDbContext db, ActorContext a, Guid id, string revision, bool reviewed, CancellationToken ct = default) =>
    Guard(db, a, revision, reviewed, () => PolicyRevision(db, a, id, ct), async () => AsValue(await CurrencyTranslationService.ApprovePolicyAsync(db, a, id, ct)), ct);
  private static CommandResult<bool> AsValue(CommandResult result) => result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
}
