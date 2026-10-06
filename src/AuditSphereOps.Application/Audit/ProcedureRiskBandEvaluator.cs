using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

internal sealed record EffectiveProcedureRiskBand(
  string Band,
  string QualitativeBand,
  Guid RiskId,
  Guid? RiskAssessmentId,
  Guid? MaterialityAssessmentId,
  Guid? MaterialityCalculationId,
  Guid? MappingVersionId,
  long? MappingVersionNumber,
  Guid? DatasetId,
  string? DatasetDigest,
  string? DestinationCode,
  string? StatementSection,
  decimal? Balance,
  string? Currency,
  decimal? TolerableError,
  decimal? PlanningMateriality,
  string RuleVersion,
  string Explanation);

internal sealed record ProcedureRiskBandEvaluation(EffectiveProcedureRiskBand? Value, string? Blocker);

/// <summary>
/// Combines a linked risk's current qualitative assessment with the strongest matching mapped FSLI exposure.
/// The result is accepted only when materiality is current and independently Partner-approved.
/// </summary>
internal static class ProcedureRiskBandEvaluator
{
  internal const string RiskBasisProperty = "_auditSphereRiskBasis";
  internal const int MaximumStructuredResultBytes = 100_000;

  private sealed record CapturedRiskBasis(
    string Band,
    string QualitativeBand,
    Guid RiskId,
    Guid? RiskAssessmentId,
    Guid? MaterialityAssessmentId,
    Guid? MaterialityCalculationId,
    Guid? MappingVersionId,
    long? MappingVersionNumber,
    Guid? DatasetId,
    string? DatasetDigest,
    string? DestinationCode,
    string? StatementSection,
    decimal? Balance,
    string? Currency,
    decimal? TolerableError,
    decimal? PlanningMateriality,
    string RuleVersion,
    string Explanation);

  public static async Task<ProcedureRiskBandEvaluation> EvaluateAsync(
    IAuditSphereDbContext db,
    Guid firmId,
    Guid engagementId,
    Guid? riskId,
    CancellationToken ct)
  {
    if (riskId is null)
      return new(null, null);
    var result = await EvaluateManyAsync(db, firmId, engagementId, [riskId.Value], ct);
    return result.GetValueOrDefault(riskId.Value,
      new(null, "The linked risk is no longer available in this engagement."));
  }

  public static async Task<IReadOnlyDictionary<Guid, ProcedureRiskBandEvaluation>> EvaluateManyAsync(
    IAuditSphereDbContext db,
    Guid firmId,
    Guid engagementId,
    IReadOnlyCollection<Guid> requestedRiskIds,
    CancellationToken ct)
  {
    var riskIds = requestedRiskIds.Distinct().Take(1000).ToArray();
    if (riskIds.Length == 0)
      return new Dictionary<Guid, ProcedureRiskBandEvaluation>();

    var risks = await db.AuditRisks.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.EngagementId == engagementId && riskIds.Contains(x.Id)).ToListAsync(ct);
    var risksById = risks.ToDictionary(x => x.Id);
    var assessments = await db.RiskBandAssessments.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.EngagementId == engagementId && riskIds.Contains(x.RiskId))
      .OrderByDescending(x => x.AssessedAt).ThenByDescending(x => x.Id).ToListAsync(ct);
    var latestByRisk = assessments.GroupBy(x => x.RiskId).ToDictionary(x => x.Key, x => x.First());

    var source = await MappedTrialBalanceSource.LoadAsync(db, firmId, engagementId, ct);
    var calculation = await MaterialityEngineService.GetLatestAsync(db, firmId, engagementId, ct);
    var latestAssessmentId = await db.MaterialityAssessments.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);

    string? materialityBlocker = null;
    if (source is null && calculation is not null)
      materialityBlocker = "The mapped trial-balance source is unavailable; refresh materiality before procedure work can continue.";
    else if (source is not null && (calculation is null || calculation.AssessmentId != latestAssessmentId ||
        calculation.State != MaterialityCalculationStates.Approved ||
        calculation.Calculation.Currency != source.Dataset.Currency))
      materialityBlocker = calculation?.Route is { Length: > 0 } route && calculation.State != MaterialityCalculationStates.Approved
        ? route
        : "Approve a current, source-bound materiality calculation in the trial-balance currency before executing or reviewing mapped FSLI procedures.";

    var evaluations = new Dictionary<Guid, ProcedureRiskBandEvaluation>();
    foreach (var id in riskIds)
    {
      if (!risksById.TryGetValue(id, out var risk))
      {
        evaluations[id] = new(null, "The linked risk is no longer available in this engagement.");
        continue;
      }

      latestByRisk.TryGetValue(id, out var assessment);
      var significant = risk.SignificanceDecision == SignificanceDecisions.Significant || assessment?.Significant == true;
      var fraud = assessment?.FraudRisk == true;
      var criticalEstimate = HasEstimateIndicator(risk.Description) || HasEstimateIndicator(risk.Drivers);
      var highInherentRisk = assessment is { LikelihoodScore: 3, MagnitudeScore: >= 2 };
      var qualitativeBand = assessment?.Band ?? RiskBandRules.Band(1, 1, significant, fraud);
      var qualitativeFloor = criticalEstimate || highInherentRisk ? RiskBands.Red : qualitativeBand;
      var qualitativeExplanation = ExplainQualitative(risk, assessment, qualitativeBand, criticalEstimate, highInherentRisk, significant, fraud);

      if (source is null)
      {
        if (materialityBlocker is not null)
          evaluations[id] = new(null, materialityBlocker);
        else
          evaluations[id] = new(new(qualitativeFloor, qualitativeBand, risk.Id, assessment?.Id, null, null,
            null, null, null, null, null, null, null, null, null, null,
            $"{RiskBandRules.RuleVersion}+{FsliRiskBandRules.RuleVersion}",
            $"{qualitativeExplanation}. No current mapped FSLI source is available; routing uses the linked qualitative risk assessment."), null);
        continue;
      }

      if (materialityBlocker is not null || calculation is null)
      {
        evaluations[id] = new(null, materialityBlocker ?? "Current approved materiality is unavailable.");
        continue;
      }

      var materiality = calculation.Calculation;
      var matchingGroups = source.Lines
        .Where(line =>
          (!string.IsNullOrWhiteSpace(line.AuditArea) && string.Equals(risk.AccountArea.Trim(), line.AuditArea.Trim(), StringComparison.OrdinalIgnoreCase)) ||
          string.Equals(risk.AccountArea.Trim(), line.DestinationCode.Trim(), StringComparison.OrdinalIgnoreCase))
        .GroupBy(line => (line.DestinationCode, line.StatementSection, line.AuditArea))
        .Select(group => new
        {
          Group = group.Key,
          Balance = group.Sum(line => line.Amount)
        })
        .Select(group => new
        {
          group.Group,
          group.Balance,
          Band = FsliRiskBandRules.Band(group.Balance, materiality.TolerableError,
            materiality.PlanningMateriality, criticalEstimate, highInherentRisk, significant, fraud)
        })
        .OrderByDescending(x => BandRank(x.Band))
        .ThenByDescending(x => Math.Abs(x.Balance))
        .ThenBy(x => x.Group.DestinationCode, StringComparer.Ordinal)
        .ToList();

      if (matchingGroups.Count == 0)
      {
        evaluations[id] = new(new(qualitativeFloor, qualitativeBand, risk.Id, assessment?.Id, calculation.AssessmentId,
          materiality.Id, materiality.MappingVersionId, materiality.MappingVersionNumber,
          materiality.DatasetId, materiality.DatasetDigest, null, null, null, source.Dataset.Currency,
          materiality.TolerableError, materiality.PlanningMateriality,
          $"{RiskBandRules.RuleVersion}+{FsliRiskBandRules.RuleVersion}",
          $"{qualitativeExplanation}. No mapped FSLI matches risk area '{risk.AccountArea}'; routing uses the linked qualitative risk assessment. Materiality is current at {source.Dataset.Currency} {materiality.TolerableError:N2} TE / {materiality.PlanningMateriality:N2} PM."), null);
        continue;
      }

      var strongest = matchingGroups[0];
      var effectiveBand = HigherBand(qualitativeFloor, strongest.Band);
      var explanation = FsliRiskBandRules.Explain(strongest.Balance, materiality.TolerableError,
        materiality.PlanningMateriality, strongest.Band, criticalEstimate, highInherentRisk, significant, fraud);
      evaluations[id] = new(new(effectiveBand, qualitativeBand, risk.Id, assessment?.Id, calculation.AssessmentId,
        materiality.Id, materiality.MappingVersionId, materiality.MappingVersionNumber, materiality.DatasetId,
        materiality.DatasetDigest, strongest.Group.DestinationCode, strongest.Group.StatementSection,
        strongest.Balance, source.Dataset.Currency, materiality.TolerableError, materiality.PlanningMateriality,
        $"{RiskBandRules.RuleVersion}+{FsliRiskBandRules.RuleVersion}",
        $"{qualitativeExplanation}. Effective {effectiveBand} from qualitative risk and mapped {strongest.Group.DestinationCode} FSLI exposure: {Math.Abs(strongest.Balance):N2} {source.Dataset.Currency}; TE {materiality.TolerableError:N2}, PM {materiality.PlanningMateriality:N2}. {explanation}"), null);
    }
    return evaluations;
  }

  internal static string WithCapturedBasis(string structuredResultJson, EffectiveProcedureRiskBand? current)
  {
    using var document = JsonDocument.Parse(structuredResultJson);
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
      writer.WriteStartObject();
      foreach (var property in document.RootElement.EnumerateObject())
        property.WriteTo(writer);
      writer.WritePropertyName(RiskBasisProperty);
      if (current is null)
        writer.WriteNullValue();
      else
        JsonSerializer.Serialize(writer, ToCapturedBasis(current));
      writer.WriteEndObject();
    }
    return Encoding.UTF8.GetString(stream.ToArray());
  }

  internal static bool CapturedBasisMatches(string structuredResultJson, EffectiveProcedureRiskBand? current)
  {
    try
    {
      using var document = JsonDocument.Parse(structuredResultJson);
      var matches = document.RootElement.EnumerateObject()
        .Where(x => string.Equals(x.Name, RiskBasisProperty, StringComparison.OrdinalIgnoreCase)).ToArray();
      if (current is null)
        return matches.Length == 0 || matches.Length == 1 && matches[0].Value.ValueKind == JsonValueKind.Null;
      if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.Object)
        return false;
      var captured = matches[0].Value.Deserialize<CapturedRiskBasis>();
      return captured == ToCapturedBasis(current);
    }
    catch (JsonException)
    {
      return false;
    }
  }

  internal static bool HasReservedBasisProperty(string structuredResultJson)
  {
    try
    {
      using var document = JsonDocument.Parse(structuredResultJson);
      return document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject()
        .Any(x => string.Equals(x.Name, RiskBasisProperty, StringComparison.OrdinalIgnoreCase));
    }
    catch (JsonException)
    {
      return true;
    }
  }

  internal static bool HasEstimateIndicator(string? value) =>
    !string.IsNullOrWhiteSpace(value) &&
    (value.Contains("critical estimate", StringComparison.OrdinalIgnoreCase) ||
     value.Contains("critical accounting estimate", StringComparison.OrdinalIgnoreCase) ||
     value.Contains("key estimate", StringComparison.OrdinalIgnoreCase) ||
     value.Contains("key accounting estimate", StringComparison.OrdinalIgnoreCase));

  private static CapturedRiskBasis ToCapturedBasis(EffectiveProcedureRiskBand risk) => new(
    risk.Band, risk.QualitativeBand, risk.RiskId, risk.RiskAssessmentId, risk.MaterialityAssessmentId,
    risk.MaterialityCalculationId, risk.MappingVersionId, risk.MappingVersionNumber, risk.DatasetId,
    risk.DatasetDigest, risk.DestinationCode, risk.StatementSection, risk.Balance, risk.Currency,
    risk.TolerableError, risk.PlanningMateriality, risk.RuleVersion, risk.Explanation);

  private static string HigherBand(string first, string second) => BandRank(first) >= BandRank(second) ? first : second;
  private static int BandRank(string band) => band switch
  {
    RiskBands.Red => 3,
    RiskBands.Amber => 2,
    _ => 1
  };

  private static string ExplainQualitative(AuditRisk risk, RiskBandAssessment? assessment, string band,
    bool criticalEstimate, bool highInherentRisk, bool significant, bool fraud)
  {
    var parts = new List<string> { $"Qualitative {band} risk" };
    if (assessment is not null)
      parts.Add($"likelihood {assessment.LikelihoodScore}/3 and magnitude {assessment.MagnitudeScore}/3");
    if (criticalEstimate) parts.Add("critical estimate identified in risk description or drivers");
    if (highInherentRisk) parts.Add("high inherent risk");
    if (significant) parts.Add("significant risk");
    if (fraud) parts.Add("fraud risk");
    if (assessment is not null && !string.IsNullOrWhiteSpace(assessment.Rationale))
      parts.Add($"assessment rationale: {assessment.Rationale}");
    return string.Join("; ", parts);
  }
}
