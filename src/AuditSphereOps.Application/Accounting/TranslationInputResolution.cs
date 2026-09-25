using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// One approved statement section per destination code. A code with more than one approved
/// section is reported as ambiguous instead of being silently narrowed.
/// </summary>
public sealed record ApprovedStatementSections(
  IReadOnlyDictionary<string, string> SectionByCode,
  IReadOnlyList<string> AmbiguousCodes);

/// <summary>
/// Shared resolution of translation inputs. Translation, approval revalidation and group
/// calculation all build their lines and their rate purposes here, so one line always carries
/// one statement section and one rate purpose on every path. A code with no approved
/// classification is an explicit unmapped issue and blocks the operation; it is never assumed
/// to be an asset.
/// </summary>
public static class TranslationInputResolution
{
  /// <summary>The approved rate available for each rate purpose under one policy and rate set.</summary>
  public static IReadOnlyDictionary<string, decimal> BuildRatesByPurpose(
    TranslationPolicyVersion policy, IReadOnlyList<ExchangeRate> directRates,
    string headerRateType, decimal headerRate)
  {
    ArgumentNullException.ThrowIfNull(policy);
    ArgumentNullException.ThrowIfNull(directRates);
    var ratesByPurpose = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    foreach (var r in directRates)
    {
      if (string.Equals(r.RateType, policy.ClosingRateRule, StringComparison.OrdinalIgnoreCase))
        ratesByPurpose[TranslationRatePurposes.Closing] = r.Rate;
      if (string.Equals(r.RateType, policy.AverageRateRule, StringComparison.OrdinalIgnoreCase))
        ratesByPurpose[TranslationRatePurposes.Average] = r.Rate;
      if (string.Equals(r.RateType, policy.HistoricalRateRule, StringComparison.OrdinalIgnoreCase))
        ratesByPurpose[TranslationRatePurposes.Historical] = r.Rate;
    }
    if (!ratesByPurpose.ContainsKey(TranslationRatePurposes.Closing) && headerRate > 0m &&
        string.Equals(headerRateType, policy.ClosingRateRule, StringComparison.OrdinalIgnoreCase))
      ratesByPurpose[TranslationRatePurposes.Closing] = headerRate;
    return ratesByPurpose;
  }

  /// <summary>Approved statement sections for a set of destination codes of one client, taken
  /// from approved mapping allocations rather than guessed from code spelling.</summary>
  public static async Task<ApprovedStatementSections> LoadApprovedSectionsAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, IEnumerable<string> codes,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(codes);
    var wanted = codes.Select(c => (c ?? string.Empty).Trim())
      .Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    if (wanted.Length == 0)
      return new ApprovedStatementSections(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);
    var rows = await (from allocation in db.MappingAllocations.AsNoTracking()
      join version in db.MappingVersions.AsNoTracking() on allocation.MappingVersionId equals version.Id
      where allocation.FirmId == firmId && allocation.ClientId == clientId &&
        wanted.Contains(allocation.DestinationCode) &&
        version.FirmId == firmId && version.ClientId == clientId &&
        version.Status == AccountingPackageStates.MappingApproved
      select new { allocation.DestinationCode, allocation.StatementSection }).ToListAsync(ct);
    var sectionByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var ambiguous = new List<string>();
    foreach (var group in rows.Where(x => !string.IsNullOrWhiteSpace(x.StatementSection))
      .GroupBy(x => x.DestinationCode.Trim(), StringComparer.OrdinalIgnoreCase))
    {
      var sections = group.Select(x => x.StatementSection.Trim().ToUpperInvariant())
        .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
      if (sections.Length == 1)
        sectionByCode[group.Key] = sections[0];
      else
        ambiguous.Add(group.Key);
    }
    return new ApprovedStatementSections(sectionByCode, ambiguous);
  }

  /// <summary>Declared section first, then an approved mapping allocation, then documented
  /// taxonomy prefix rules. Nothing else may classify a line.</summary>
  public static string? ResolveSection(
    string? declaredSection, string taxonomyCodeOrAccount, ApprovedStatementSections approved, out string? error)
  {
    ArgumentNullException.ThrowIfNull(approved);
    var declared = (declaredSection ?? string.Empty).Trim();
    // Only a recognized statement section may classify a line directly. A presentation label
    // that carries no rate classification falls through to approved mapping evidence and code
    // rules, and is never silently treated as a known section.
    if (declared.Length > 0 && LineTranslationCalculator.IsKnownSection(declared))
    {
      error = null;
      return declared;
    }
    var code = (taxonomyCodeOrAccount ?? string.Empty).Trim();
    if (approved.AmbiguousCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
    {
      error = $"more than one approved mapping section exists for '{code}'; narrow the approved mapping before translation.";
      return null;
    }
    if (approved.SectionByCode.TryGetValue(code, out var mapped))
    {
      error = null;
      return mapped;
    }
    if (LineTranslationCalculator.TryInferSection(code) is { } inferred)
    {
      error = null;
      return inferred;
    }
    error = $"taxonomy code '{code}' has no approved statement section; approve a mapping for it before translation.";
    return null;
  }

  /// <summary>Builds the exact translation input lines of one component from its immutable
  /// package or external-pack lines, each with its resolved statement section.</summary>
  public static CommandResult<IReadOnlyList<TranslationLineInput>> BuildInputLines(
    IReadOnlyList<FinancialPackageLine> packageLines,
    IReadOnlyList<ExternalComponentPackLine> externalLines,
    ApprovedStatementSections approved)
  {
    ArgumentNullException.ThrowIfNull(packageLines);
    ArgumentNullException.ThrowIfNull(externalLines);
    ArgumentNullException.ThrowIfNull(approved);
    var lines = new List<TranslationLineInput>(packageLines.Count + externalLines.Count);
    foreach (var line in packageLines)
    {
      var section = ResolveSection(line.StatementSection, line.DestinationCode, approved, out var error);
      if (section is null)
        return CommandResult<IReadOnlyList<TranslationLineInput>>.Fail(ErrorCodes.Accounting.MappingInvalid,
          $"Package line {line.Id:D} ({line.DestinationCode}) cannot be translated: {error}");
      lines.Add(new TranslationLineInput(line.Id.ToString("D"), line.DestinationCode, section, line.Amount, line.Currency));
    }
    foreach (var line in externalLines)
    {
      var section = ResolveSection(null, line.TaxonomyCode, approved, out var error);
      if (section is null)
        return CommandResult<IReadOnlyList<TranslationLineInput>>.Fail(ErrorCodes.Accounting.MappingInvalid,
          $"Component line {line.Id:D} ({line.TaxonomyCode}) cannot be translated: {error}");
      lines.Add(new TranslationLineInput(line.Id.ToString("D"), line.TaxonomyCode, section, line.Amount, line.Currency));
    }
    return CommandResult<IReadOnlyList<TranslationLineInput>>.Ok(lines);
  }
}

/// <summary>
/// Stable identities derived from immutable snapshot identities. Recalculation and currentness
/// replay never generate identities, so the same inputs always produce the same manifest.
/// </summary>
public static class TranslationIdentities
{
  /// <summary>
  /// The identity of the cumulative-translation-reserve line of one immutable translation
  /// snapshot. Owned by that snapshot: every replay of the same translation yields the same
  /// consolidation line identity and therefore the same run hash.
  /// </summary>
  public static Guid CumulativeTranslationReserveLineId(Guid translationResultId)
  {
    var digest = System.Security.Cryptography.SHA256.HashData(
      System.Text.Encoding.UTF8.GetBytes("auditsphere.cta-line.v1|" + translationResultId.ToString("D")));
    var bytes = new byte[16];
    Array.Copy(digest, bytes, 16);
    return new Guid(bytes);
  }
}
