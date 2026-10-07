using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalOpeningBalanceLineInput(string AccountCode, decimal Debit, decimal Credit);
public sealed record ClientOperationalOpeningBalanceItemInput(string Role, Guid CounterpartyId, string Reference,
  DateOnly? DueDate, string AccountCode, decimal Amount);
public sealed record ClientOperationalOpeningBalanceRequest(Guid ClientId, Guid PeriodId, long ExpectedPeriodRevision,
  DateOnly AsOfDate, string Currency, string EvidenceReference, string EvidenceSha256,
  IReadOnlyList<ClientOperationalOpeningBalanceLineInput> Lines,
  IReadOnlyList<ClientOperationalOpeningBalanceItemInput>? OpenItems = null);
public sealed record ClientOperationalOpeningBalanceLine(string AccountCode, string AccountName, string Debit, string Credit);
public sealed record ClientOperationalOpeningBalanceItem(Guid Id, string Role, Guid CounterpartyId, string CounterpartyName,
  string Reference, DateOnly? DueDate, Guid AccountId, string AccountCode, string Amount);
public sealed record ClientOperationalOpeningBalanceView(Guid Id, Guid ClientId, Guid PeriodId, Guid ChartVersionId, string PeriodRevision,
  string AsOfDate, string Currency, string EvidenceReference, string EvidenceSha256, string ManifestSha256,
  Guid CreatedByUserId, string CreatedAt, Guid? ApprovedByUserId, string? ApprovedAt,
  IReadOnlyList<ClientOperationalOpeningBalanceLine> Lines, IReadOnlyList<ClientOperationalOpeningBalanceItem> OpenItems);

/// <summary>Creates and independently approves immutable native-book opening-balance snapshots.</summary>
public static class ClientOperationalOpeningBalanceWorkspace
{
  private static readonly string[] Preparers = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] Reviewers = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  private sealed record ManifestLine(string AccountCode, string AccountName, string Debit, string Credit);
  private sealed record ManifestOpenItem(Guid Id, string Role, Guid CounterpartyId, string CounterpartyName,
    string Reference, DateOnly? DueDate, Guid AccountId, string AccountCode, string Amount);
  private sealed record Manifest(string Version, Guid FirmId, Guid ClientId, Guid PeriodId, long PeriodRevision,
    Guid ChartVersionId, DateOnly AsOfDate, string Currency, string EvidenceReference, string EvidenceSha256,
    IReadOnlyList<ManifestLine> Lines, IReadOnlyList<ManifestOpenItem>? OpenItems = null);
  private static readonly JsonSerializerOptions ManifestJsonOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

  public static async Task<CommandResult<Guid>> CreateAsync(IClientAccountingDbContext db, ActorContext actor,
    ClientOperationalOpeningBalanceRequest request, CancellationToken ct = default)
  {
    var evidenceHash = (request.EvidenceSha256 ?? string.Empty).Trim().ToLowerInvariant();
    var evidenceReference = (request.EvidenceReference ?? string.Empty).Trim();
    if (request.ClientId == Guid.Empty || request.PeriodId == Guid.Empty || request.ExpectedPeriodRevision < 1 ||
        evidenceReference.Length is 0 or > 1000 || !IsSha256(evidenceHash) || request.Lines is null ||
        request.Lines.Count is < 2 or > 5000 || request.OpenItems is { Count: > 5000 })
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An opening balance needs its exact period revision, source evidence reference and SHA-256, and 2 to 5,000 account rows.");

    var auth = await Authorize(db, actor, request.ClientId, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == request.ClientId && x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct);
    if (profile is null || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, request.ClientId, ct: ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Current accepted native bookkeeping authority is required.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var period = await db.ClientReportingPeriods.FromSqlInterpolated(
      $"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={request.ClientId} AND id={request.PeriodId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (period is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (period.Status == AccountingWorkflowStates.Closed || period.Revision != request.ExpectedPeriodRevision ||
        request.AsOfDate != period.StartDate || !string.Equals(request.Currency, period.Currency, StringComparison.Ordinal) ||
        !string.Equals(request.Currency, profile.FunctionalCurrency, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale,
        "Refresh the open period; the opening date must be its first day and currency must match the accepted client books.");
    if (await db.ClientOperationalOpeningBalances.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.PeriodId == request.PeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This period already has an opening-balance snapshot.");

    var charts = await db.ClientChartVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == request.ClientId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= request.AsOfDate &&
      (x.EffectiveTo == null || x.EffectiveTo >= request.AsOfDate)).Take(2).ToListAsync(ct);
    if (charts.Count != 1) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Exactly one approved chart must cover the opening date.");
    var chart = charts[0];
    var inputs = request.Lines.Select(x => new { Code = (x.AccountCode ?? string.Empty).Trim(), x.Debit, x.Credit })
      .OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
    if (inputs.Any(x => x.Code.Length == 0 || x.Debit < 0m || x.Credit < 0m || (x.Debit > 0m && x.Credit > 0m) ||
        (x.Debit == 0m && x.Credit == 0m) || x.Debit != MoneyPolicy.Normalize(x.Debit) || x.Credit != MoneyPolicy.Normalize(x.Credit)) ||
        inputs.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != inputs.Length ||
        inputs.Sum(x => x.Debit) != inputs.Sum(x => x.Credit))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "Opening account balances must be unique, non-zero, within precision, and trial-balance to equal debits and credits.");

    var codes = inputs.Select(x => x.Code).ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.ChartVersionId == chart.Id && codes.Contains(x.AccountCode) && x.IsPosting && x.Status == AccountingWorkflowStates.Active)
      .Select(x => new { x.Id, x.AccountCode, x.AccountName, x.AccountType }).ToListAsync(ct);
    if (accounts.Count != codes.Length || accounts.Any(x => x.AccountType is not ("ASSET" or "LIABILITY" or "EQUITY")))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Opening balances may use only active balance-sheet posting accounts in the approved chart.");

    var openingItems = new List<ManifestOpenItem>();
    if (request.OpenItems is { Count: > 0 })
    {
      var rawItems = request.OpenItems.Select(x => new { x.Role, x.CounterpartyId, Reference = (x.Reference ?? string.Empty).Trim(),
        x.DueDate, AccountCode = (x.AccountCode ?? string.Empty).Trim(), x.Amount }).ToArray();
      if (rawItems.Any(x => x.Role is not ("AR" or "AP") || x.CounterpartyId == Guid.Empty || x.Reference.Length is 0 or > 200 ||
          x.AccountCode.Length == 0 || x.Amount <= 0m || x.Amount != MoneyPolicy.Normalize(x.Amount)) ||
          rawItems.Select(x => (x.Role, x.CounterpartyId, Reference: x.Reference.ToUpperInvariant())).Distinct().Count() != rawItems.Length)
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
          "Opening AR/AP detail requires unique customer or supplier references and positive amounts within currency precision.");

      var partyIds = rawItems.Select(x => x.CounterpartyId).Distinct().ToArray();
      var parties = await db.ClientBookkeepingCounterparties.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == request.ClientId && partyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
      var accountByCode = accounts.ToDictionary(x => x.AccountCode, StringComparer.Ordinal);
      var approvedRoles = await (from role in db.ClientAccountRoleConfigurations.AsNoTracking()
        join decision in db.ClientAccountRoleDecisions.AsNoTracking() on new { role.FirmId, role.ClientId, ConfigurationId = role.Id }
          equals new { decision.FirmId, decision.ClientId, decision.ConfigurationId }
        join account in db.ClientAccounts.AsNoTracking() on new { role.FirmId, role.ClientId, AccountId = role.AccountId }
          equals new { account.FirmId, account.ClientId, AccountId = account.Id }
        where role.FirmId == actor.FirmId && role.ClientId == request.ClientId &&
          (role.Role == "AR" || role.Role == "AP") && account.ChartVersionId == chart.Id && decision.Decision == "APPROVE" &&
          role.EffectiveFrom <= request.AsOfDate && (role.EffectiveTo == null || role.EffectiveTo >= request.AsOfDate)
        select new { role.Role, role.AccountId, account.AccountCode }).ToListAsync(ct);
      foreach (var item in rawItems)
      {
        var partyRole = item.Role == "AR" ? "CUSTOMER" : "SUPPLIER";
        if (!parties.TryGetValue(item.CounterpartyId, out var party) || (party.Role != partyRole && party.Role != "BOTH") ||
            !accountByCode.TryGetValue(item.AccountCode, out var account) ||
            approvedRoles.Count(x => x.Role == item.Role && x.AccountCode == item.AccountCode && x.AccountId == account.Id) != 1)
          return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
            "Each opening item must use a client customer/supplier and its effective independently approved AR/AP control account.");
        openingItems.Add(new(Guid.CreateVersion7(), item.Role, party.Id, party.DisplayName, item.Reference, item.DueDate,
          account.Id, item.AccountCode, item.Amount.ToString("F6", CultureInfo.InvariantCulture)));
      }

      var lineByCode = inputs.ToDictionary(x => x.Code, StringComparer.Ordinal);
      foreach (var control in rawItems.Select(x => (x.Role, x.AccountCode)).Distinct())
      {
        var entry = lineByCode[control.AccountCode];
        var controlBalance = control.Role == "AR" ? entry.Debit - entry.Credit : entry.Credit - entry.Debit;
        var detailTotal = rawItems.Where(x => x.Role == control.Role && x.AccountCode == control.AccountCode).Sum(x => x.Amount);
        if (controlBalance <= 0m || detailTotal != controlBalance)
          return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
            "Opening invoice detail must equal the positive balance of each selected approved AR/AP control account; aggregate or opposite-side balances remain explicitly unresolved.");
      }
      var selectedControls = rawItems.Select(x => (x.Role, x.AccountCode)).ToHashSet();
      foreach (var control in approvedRoles.Select(x => (x.Role, x.AccountCode)).Distinct())
      {
        var entry = lineByCode.GetValueOrDefault(control.AccountCode);
        if (entry is null) continue;
        var controlBalance = control.Role == "AR" ? entry.Debit - entry.Credit : entry.Credit - entry.Debit;
        if (controlBalance != 0m && !selectedControls.Contains(control))
          return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
            "Provide invoice-level detail for every non-zero approved AR/AP opening control balance, or keep the opening explicitly aggregate-only.");
      }
    }

    var manifest = new Manifest("native-opening-v1", actor.FirmId, request.ClientId, request.PeriodId, period.Revision, chart.Id,
      request.AsOfDate, request.Currency, evidenceReference, evidenceHash,
      inputs.Select(x => { var account = accounts.Single(a => a.AccountCode == x.Code); return new ManifestLine(
        account.AccountCode, account.AccountName, x.Debit.ToString("F6", CultureInfo.InvariantCulture),
        x.Credit.ToString("F6", CultureInfo.InvariantCulture)); }).ToArray(), openingItems.Count == 0 ? null : openingItems);
    var manifestJson = JsonSerializer.Serialize(manifest);
    var origin = new ClientOperationalOpeningBalance
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, PeriodId = period.Id,
      PeriodRevision = period.Revision, AsOfDate = request.AsOfDate, Currency = request.Currency,
      EvidenceReference = evidenceReference, EvidenceSha256 = evidenceHash, ManifestJson = manifestJson,
      ManifestSha256 = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions))),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientOperationalOpeningBalances.Add(origin);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(origin.Id);
  }

  public static async Task<CommandResult> ApproveAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid openingId, long expectedPeriodRevision, string expectedManifestSha256, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var origin = await db.ClientOperationalOpeningBalances.FromSqlInterpolated(
      $"SELECT * FROM client_operational_opening_balances WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={openingId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (origin is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await Authorize(db, actor, clientId, Reviewers, ct);
    if (!auth.Succeeded) return auth;
    if (origin.ApprovedByUserId.HasValue || origin.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Opening balances require one independent reviewer decision.");
    if (origin.PeriodRevision != expectedPeriodRevision || origin.ManifestSha256 != expectedManifestSha256)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The opening source or period revision changed; refresh before review.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.Id == origin.PeriodId, ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || period.Revision != origin.PeriodRevision ||
        origin.AsOfDate != period.StartDate || period.Currency != origin.Currency)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The opening snapshot no longer matches its open reporting period.");
    if (!ValidManifest(origin)) return CommandResult.Fail(ErrorCodes.ProtectedState, "The retained opening source manifest failed integrity validation.");
    origin.ApprovedByUserId = actor.UserId;
    origin.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<ClientOperationalOpeningBalanceView?>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid periodId, CancellationToken ct = default)
  {
    var auth = await Authorize(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<ClientOperationalOpeningBalanceView?>.Fail(auth.ErrorCode!, "Access denied.");
    var origin = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.PeriodId == periodId, ct);
    if (origin is null) return CommandResult<ClientOperationalOpeningBalanceView?>.Ok(null);
    if (!ValidManifest(origin)) return CommandResult<ClientOperationalOpeningBalanceView?>.Fail(ErrorCodes.ProtectedState, "The retained opening source manifest failed integrity validation.");
    using var manifestDocument = JsonDocument.Parse(origin.ManifestJson);
    var lines = manifestDocument.RootElement.GetProperty("Lines").EnumerateArray().Select(x =>
      new ClientOperationalOpeningBalanceLine(x.GetProperty("AccountCode").GetString()!, x.GetProperty("AccountName").GetString()!,
        x.GetProperty("Debit").GetString()!, x.GetProperty("Credit").GetString()!)).ToArray();
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOperationalOpeningBalanceView?>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var chartVersionId = manifestDocument.RootElement.GetProperty("ChartVersionId").GetGuid();
    var items = manifestDocument.RootElement.TryGetProperty("OpenItems", out var itemRows) && itemRows.ValueKind == JsonValueKind.Array
      ? itemRows.EnumerateArray().Select(x => new ClientOperationalOpeningBalanceItem(x.GetProperty("Id").GetGuid(),
          x.GetProperty("Role").GetString()!, x.GetProperty("CounterpartyId").GetGuid(), x.GetProperty("CounterpartyName").GetString()!,
          x.GetProperty("Reference").GetString()!, x.GetProperty("DueDate").ValueKind == JsonValueKind.Null ? null : DateOnly.FromDateTime(x.GetProperty("DueDate").GetDateTime()),
          x.GetProperty("AccountId").GetGuid(), x.GetProperty("AccountCode").GetString()!, x.GetProperty("Amount").GetString()!)).ToArray()
      : [];
    return CommandResult<ClientOperationalOpeningBalanceView?>.Ok(new(origin.Id, clientId, periodId, chartVersionId,
      origin.PeriodRevision.ToString(CultureInfo.InvariantCulture), origin.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      origin.Currency, origin.EvidenceReference, origin.EvidenceSha256, origin.ManifestSha256, origin.CreatedByUserId,
      origin.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), origin.ApprovedByUserId,
      origin.ApprovedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), lines, items));
  }

  internal static bool ValidManifest(ClientOperationalOpeningBalance origin)
  {
    try
    {
      using var document = JsonDocument.Parse(origin.ManifestJson);
      var manifest = document.RootElement.Deserialize<Manifest>();
      return manifest is not null && manifest.FirmId == origin.FirmId && manifest.ClientId == origin.ClientId &&
        manifest.PeriodId == origin.PeriodId && manifest.PeriodRevision == origin.PeriodRevision &&
        manifest.ChartVersionId != Guid.Empty &&
        manifest.AsOfDate == origin.AsOfDate && manifest.Currency == origin.Currency &&
        manifest.EvidenceReference == origin.EvidenceReference && manifest.EvidenceSha256 == origin.EvidenceSha256 &&
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions))) == origin.ManifestSha256;
    }
    catch (JsonException) { return false; }
  }

  private static bool IsSha256(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    string[] roles, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);
}
