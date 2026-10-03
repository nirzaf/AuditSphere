using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class AccountingAnalysisReviewQuery
{
  private static async Task<Retained?> RetainedAsync(IClientAccountingDbContext db, Guid firm, string kind, Guid id, CancellationToken ct)
  {
    var amounts = new List<AnalysisAmount>(); var details = new List<AnalysisText>(); var problems = new List<string>();
    void Amount(string key, string label, decimal? value, string unit = "REPORTING_CURRENCY") => amounts.Add(new(key, label, value.HasValue ? Exact(value.Value) : null, unit));
    void Text(string key, string label, string? value) => details.Add(new(key, label, value ?? ""));
    if (kind is "ECL" or "INVENTORY")
    {
      var e = kind == "ECL" ? await db.EclAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == firm, ct) : null;
      var i = kind == "INVENTORY" ? await db.InventoryValuationAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == firm, ct) : null;
      if (e is null && i is null) return null;
      var client = e?.ClientId ?? i!.ClientId; var engagement = e?.EngagementId ?? i!.EngagementId;
      var recId = e?.ReconciliationId ?? i!.ReconciliationId;
      var rec = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firm && x.ClientId == client && x.EngagementId == engagement && x.Id == recId, ct);
      if (rec is null) return null;
      if (e is not null)
      {
        Text("METHOD", "Method", e.Method); Text("METHODOLOGY", "Methodology version", e.MethodologyVersion); Text("AS_OF", "As of", e.AsOfDate.ToString("yyyy-MM-dd"));
        Amount("EXPOSURE", "Eligible exposure", e.EligibleExposure); Amount("PD", "Probability of default", e.ProbabilityOfDefault, "RATIO");
        Amount("LGD", "Loss given default", e.LossGivenDefault, "RATIO"); Amount("OVERLAY", "Management overlay", e.ManagementOverlay);
        Amount("CALCULATED", "Retained expected loss", e.CalculatedExpectedLoss); Amount("MANAGEMENT", "Management expected loss", e.ManagementExpectedLoss);
        Amount("BOOKED", "Booked amount", e.BookedAmount); Amount("DIFFERENCE", "Retained difference", e.Difference);
        if (e.Method != "PROVISION_MATRIX_V1" || e.MethodologyVersion.Length == 0 || Digest(e.AssumptionsHash) is null ||
          e.EligibleExposure < 0 || e.ProbabilityOfDefault is < 0 or > 1 || e.LossGivenDefault is < 0 or > 1 || e.ManagementOverlay < 0 || e.BookedAmount < 0 || e.ManagementExpectedLoss < 0 ||
          e.EligibleExposure != Math.Max(0m, rec.SourceTotal) || e.CalculatedExpectedLoss != AccountingAnalysisService.CalculateEcl(e.EligibleExposure, e.ProbabilityOfDefault, e.LossGivenDefault, e.ManagementOverlay) ||
          e.Difference != MoneyPolicy.Normalize(e.CalculatedExpectedLoss - e.BookedAmount))
          problems.Add("The retained ECL method, assumptions or exact amounts do not match the supported provision-matrix inputs. No substitute calculation is supplied.");
        return new(e.Id, kind, client, engagement, rec.PeriodId, rec.Area, e.Status, e.Version, e.InputGeneration, recId, null, null,
          e.ReconciliationSourceHash, e.AssumptionsHash, null, null, e.ProposedJournalId, e.CreatedByUserId, e.CreatedAt, e.ReviewedByUserId, e.ReviewedAt,
          amounts, details, problems, JsonSerializer.Serialize(new { e, rec }));
      }
      Text("METHOD", "Method", "LOWER_OF_COST_AND_NRV"); Text("METHODOLOGY", "Methodology version", i!.MethodologyVersion); Text("AS_OF", "As of", i.AsOfDate.ToString("yyyy-MM-dd"));
      Amount("QUANTITY", "Quantity", i.Quantity, "QUANTITY"); Amount("COST", "Unit cost", i.UnitCost); Amount("NRV", "Net realizable value per unit", i.NrvPerUnit);
      Amount("RESERVE", "Obsolescence reserve", i.ObsolescenceReserve); Amount("BOOKED", "Book amount", i.BookAmount); Amount("CALCULATED", "Retained valuation", i.CalculatedAmount);
      Amount("DIFFERENCE", "Retained difference", i.Difference);
      if (i.Quantity < 0 || i.UnitCost < 0 || i.NrvPerUnit < 0 || i.ObsolescenceReserve < 0 || i.MethodologyVersion.Length == 0 || Digest(i.AssumptionsHash) is null ||
        i.CalculatedAmount != AccountingAnalysisService.CalculateInventory(i.Quantity, i.UnitCost, i.NrvPerUnit, i.ObsolescenceReserve) || i.Difference != MoneyPolicy.Normalize(i.CalculatedAmount - i.BookAmount))
        problems.Add("The retained inventory methodology, assumptions or exact amounts do not match the supported cost/NRV inputs. No substitute calculation is supplied.");
      return new(i.Id, kind, client, engagement, rec.PeriodId, rec.Area, i.Status, i.Version, i.InputGeneration, recId, null, null,
        i.ReconciliationSourceHash, i.AssumptionsHash, null, null, i.ProposedJournalId, i.CreatedByUserId, i.CreatedAt, i.ReviewedByUserId, i.ReviewedAt,
        amounts, details, problems, JsonSerializer.Serialize(new { i, rec }));
    }
    if (kind == "SPECIALIST")
    {
      var s = await db.SpecialistAccountingSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == firm, ct); if (s is null) return null;
      Text("METHODOLOGY", "Methodology version", s.MethodologyVersion); Text("EVIDENCE", "Evidence reference", s.EvidenceReference); Text("CONCLUSION", "Retained reviewer conclusion", s.ReviewConclusion);
      Amount("MANAGEMENT", "Management amount", s.ManagementAmount); Amount("CALCULATED", "Retained calculated amount", s.CalculatedAmount); Amount("CLOSING", "Retained closing amount", s.ClosingAmount); Amount("DIFFERENCE", "Retained difference", s.Difference);
      switch (s.Area)
      {
        case "ASSETS":
          Text("DEPRECIATION_METHOD", "Declared depreciation method", s.DepreciationMethod); Text("USEFUL_LIFE", "Useful life in months", s.UsefulLifeMonths?.ToString());
          Amount("OPENING", "Opening amount", s.OpeningAmount); Amount("ADDITIONS", "Additions", s.AdditionsAmount); Amount("DISPOSALS", "Disposals", s.DisposalsAmount);
          Amount("DEPRECIATION", "Depreciation", s.DepreciationAmount); Amount("IMPAIRMENT", "Impairment", s.ImpairmentAmount); break;
        case "PAYROLL":
          Amount("GROSS", "Gross amount", s.PayrollGrossAmount); Amount("DEDUCTIONS", "Deductions", s.PayrollDeductionsAmount); Amount("NET", "Net amount", s.PayrollNetAmount);
          Text("CONTRACT", "Contract reference", s.PayrollContractReference); Text("PAYMENT", "Bank payment evidence", s.PayrollBankPaymentReference); break;
        case "LOANS":
          Amount("OPENING", "Opening amount", s.OpeningAmount); Amount("ADDITIONS", "Additions", s.AdditionsAmount); Amount("REPAYMENT", "Repayments", s.LoanRepaymentAmount);
          Amount("INTEREST", "Interest", s.InterestAmount); Amount("CURRENT", "Current portion", s.CurrentPortion); Amount("NON_CURRENT", "Non-current portion", s.NonCurrentPortion);
          Text("MATURITY", "Maturity date", s.LoanMaturityDate?.ToString("yyyy-MM-dd")); Text("COVENANT", "Covenant evidence", s.LoanCovenantReference); break;
        case "EQUITY":
          Amount("OPENING", "Opening amount", s.OpeningAmount); Amount("PROFIT", "Profit or loss", s.EquityProfitOrLossAmount); Amount("OCI", "Other comprehensive income", s.EquityOciAmount);
          Amount("CAPITAL", "Capital movement", s.CapitalMovement); Amount("DIVIDENDS", "Dividends", s.Dividends); Text("DISCLOSURE", "Related-party disclosure", s.RelatedPartyDisclosureReference); break;
        case "RELATED_PARTIES":
          Amount("OPENING", "Opening amount", s.OpeningAmount); Amount("ADDITIONS", "Additions", s.AdditionsAmount); Amount("DISPOSALS", "Disposals", s.DisposalsAmount);
          Text("DISCLOSURE", "Related-party disclosure", s.RelatedPartyDisclosureReference); break;
        case "TAX":
          Text("JURISDICTION", "Tax jurisdiction", s.TaxJurisdiction); Text("TAX_RULE", "Tax rule version", s.TaxRuleVersion); Amount("TAX_BASE", "Tax base", s.TaxBaseAmount); Amount("TAX_RATE", "Tax rate", s.TaxRate, "RATIO");
          Amount("TAX_PAID", "Tax paid", s.TaxPaid); Text("RETURN", "Return evidence", s.TaxReturnEvidenceReference); Text("PAYMENT", "Payment evidence", s.TaxPaymentEvidenceReference); Text("CORRESPONDENCE", "Correspondence", s.TaxCorrespondenceReference); break;
        case "FORECAST":
          Text("OWNER", "Management owner", s.ForecastOwner); Text("HORIZON", "Forecast horizon", s.ForecastHorizonEnd?.ToString("yyyy-MM-dd"));
          Amount("CASH", "Cash input", s.ForecastCashInputAmount); Amount("DEBT", "Debt input", s.ForecastDebtInputAmount);
          Text("SENSITIVITY", "Sensitivity evidence", s.ForecastSensitivityReference); Text("SENSITIVITY_RESULT", "Sensitivity result", s.ForecastSensitivityResult); break;
      }
      var invalid = AccountingAnalysisService.ValidateRetainedSpecialist(s);
      if (invalid is not null) problems.Add(invalid);
      if (Digest(s.AssumptionsHash) is null || s.MethodologyVersion.Length == 0 || s.EvidenceReference.Length == 0)
        problems.Add("The retained specialist methodology, assumptions or evidence reference is unavailable.");
      return new(s.Id, kind, s.ClientId, s.EngagementId, s.PeriodId, s.Area, s.Status, null, s.InputGeneration, null, null, null,
        null, s.AssumptionsHash, null, null, null, s.CreatedByUserId, s.CreatedAt, s.ReviewedByUserId, s.ReviewedAt, amounts, details, problems, JsonSerializer.Serialize(s));
    }
    if (kind == "ANALYTICAL")
    {
      var a = await db.AnalyticalReviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == firm, ct); if (a is null) return null;
      Text("MEASURE", "Measure", a.Measure); Text("DENOMINATOR", "Denominator basis", a.DenominatorBasis); Text("FORMULA", "Declared formula version", a.FormulaVersion);
      Text("MOVEMENT", "Movement indicators", a.MovementFlags); Text("SEASONALITY", "Seasonality explanation", a.SeasonalityExplanation); Text("EXPLANATION", "Preparer explanation", a.Explanation); Text("CONCLUSION", "Retained reviewer conclusion", a.ReviewConclusion);
      Amount("CURRENT", "Current amount", a.CurrentAmount); Amount("PRIOR", "Prior amount", a.PriorAmount); Amount("BUDGET", "Budget amount", a.BudgetAmount); Amount("RATIO", "Retained movement ratio", a.Ratio, "RATIO");
      var replay = ReplayMatches(a);
      if (!replay) problems.Add("The replay digest or typed analytical input snapshot does not match the retained inputs. No replay is inferred.");
      if (a.Ratio is null) problems.Add("The prior amount does not provide a denominator. Insufficient data is retained explicitly; no zero ratio is supplied.");
      if (!await db.ClientReportingPeriods.AnyAsync(x => x.Id == a.PeriodId && x.FirmId == firm && x.ClientId == a.ClientId && x.Currency == a.Currency, ct))
        problems.Add("The analytical currency differs from the exact reporting period.");
      if (a.ComparisonPeriodId is { } comparison)
      {
        var p = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == comparison && x.FirmId == firm && x.ClientId == a.ClientId, ct);
        if (p is null) return null; Text("COMPARISON", "Comparison period", p.PeriodCode);
        if (p.Currency != a.Currency) problems.Add("The comparison currency differs from the retained analytical currency.");
      }
      return new(a.Id, kind, a.ClientId, a.EngagementId, a.PeriodId, a.Area, a.Status, null, a.InputGeneration, null, null, null,
        null, null, a.InputHash, replay, null, a.CreatedByUserId, a.CreatedAt, a.ReviewedByUserId, a.ReviewedAt, amounts, details, problems, JsonSerializer.Serialize(a));
    }
    var risk = await db.JournalRiskFlags.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == firm, ct); if (risk is null) return null;
    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == risk.ImportBatchId && x.FirmId == firm && x.ClientId == risk.ClientId && x.EngagementId == risk.EngagementId, ct);
    if (batch is null) return null;
    Text("RULE", "Rule code", risk.RuleCode); Text("REASON", "Indicator reason", risk.Reason); Text("SELECTED", "Selected for testing", risk.SelectedForTesting ? "Yes" : "No");
    Text("EXPLANATION", "Management explanation", risk.ManagementExplanation); Text("CORROBORATION", "Corroboration reference", risk.CorroborationReference);
    Text("DISPOSITION", "Retained human disposition", risk.Disposition); Text("EVIDENCE", "Evidence reference", risk.EvidenceReference);
    Text("AUTHORITY", "Decision authority", "A journal-risk indicator is not a fraud conclusion or an autonomous audit opinion."); Amount("SCORE", "Indicator score", risk.Score, "SCORE");
    if (risk.Score is < 0 or > 100 || risk.RuleCode.Length == 0 || risk.Reason.Length == 0 || risk.EvidenceReference.Length == 0)
      problems.Add("The retained journal-risk indicator has incomplete or unsupported inputs.");
    return new(risk.Id, kind, risk.ClientId, risk.EngagementId, batch.PeriodId, risk.RuleCode, risk.Status, null, null, null, batch.Id, risk.TransactionId,
      null, null, null, null, null, risk.CreatedByUserId, risk.CreatedAt, risk.ReviewedByUserId, risk.ReviewedAt, amounts, details, problems, JsonSerializer.Serialize(new { risk, batch }));
  }

  private static bool ReplayMatches(AnalyticalReview a)
  {
    if (a.InputSnapshotJson.Length is 0 or > 64000 || Digest(a.InputHash) is null || Hashing.Sha256Hex(a.InputSnapshotJson) != Digest(a.InputHash)) return false;
    try
    {
      using var document = JsonDocument.Parse(a.InputSnapshotJson); var p = document.RootElement;
      bool Text(string key, string value) => p.GetProperty(key).GetString() == value;
      bool Amount(string key, decimal? value) => value is null ? p.GetProperty(key).ValueKind == JsonValueKind.Null : p.GetProperty(key).GetDecimal() == value;
      var ratio = a.PriorAmount == 0 ? (decimal?)null : MoneyPolicy.Normalize((a.CurrentAmount - a.PriorAmount) / Math.Abs(a.PriorAmount));
      return p.GetProperty("ClientId").GetGuid() == a.ClientId && p.GetProperty("EngagementId").GetGuid() == a.EngagementId && p.GetProperty("PeriodId").GetGuid() == a.PeriodId &&
        (a.ComparisonPeriodId is null ? p.GetProperty("ComparisonPeriodId").ValueKind == JsonValueKind.Null : p.GetProperty("ComparisonPeriodId").GetGuid() == a.ComparisonPeriodId) &&
        Text("Area", a.Area) && Text("Measure", a.Measure) && Text("Currency", a.Currency) && Text("DenominatorBasis", a.DenominatorBasis) && Text("FormulaVersion", a.FormulaVersion) &&
        Text("SeasonalityExplanation", a.SeasonalityExplanation) && Text("Explanation", a.Explanation) && Amount("CurrentAmount", a.CurrentAmount) && Amount("PriorAmount", a.PriorAmount) && Amount("BudgetAmount", a.BudgetAmount) &&
        string.Join(',', p.GetProperty("MovementFlags").EnumerateArray().Select(x => x.GetString())) == a.MovementFlags && a.Ratio == ratio && a.Measure.Length > 0 && a.DenominatorBasis.Length > 0 && a.FormulaVersion.Length > 0;
    }
    catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { return false; }
  }
}
