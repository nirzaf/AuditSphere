using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class AccountingAnalysisService
{
  public static async Task<CommandResult<Guid>> CreateEclAssessmentAsync(
    IClientAccountingDbContext db, ActorContext actor, EclAssessmentRequest request,
    CancellationToken ct = default)
  {
    if (request.Method.Trim().ToUpperInvariant() != "PROVISION_MATRIX_V1" || string.IsNullOrWhiteSpace(request.MethodologyVersion) ||
        request.ProbabilityOfDefault is < 0 or > 1 || request.LossGivenDefault is < 0 or > 1 ||
        request.ManagementOverlay < 0m || request.ManagementExpectedLoss < 0m || request.BookedAmount is < 0m ||
        !IsSha256(request.AssumptionsHash))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only the approved ECL method with explicit assumptions is enabled.");
    var reconciliation = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ReconciliationId && x.FirmId == actor.FirmId, ct);
    if (reconciliation is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, reconciliation.ClientId, reconciliation.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (reconciliation.Status != "RECONCILED")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "ECL requires a reconciled source-bound schedule.");
    if (!await HasProposedAdjustmentAsync(db, reconciliation, request.ProposedJournalId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The proposed ECL adjustment is outside the reconciliation scope.");
    var exposure = Math.Max(0m, reconciliation.SourceTotal);
    var expected = MoneyPolicy.Normalize(exposure * request.ProbabilityOfDefault * request.LossGivenDefault + request.ManagementOverlay);
    var bookedAmount = MoneyPolicy.Normalize(request.BookedAmount ?? request.ManagementExpectedLoss);
    var assessment = new EclAssessment
    {
      Id = Guid.CreateVersion7(), FirmId = reconciliation.FirmId, ClientId = reconciliation.ClientId, EngagementId = reconciliation.EngagementId,
      ReconciliationId = reconciliation.Id, Version = (await db.EclAssessments.Where(x => x.FirmId == actor.FirmId && x.ReconciliationId == reconciliation.Id)
        .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1, AsOfDate = request.AsOfDate,
      ReconciliationSourceHash = reconciliation.SourceHash, InputGeneration = reconciliation.InputGeneration,
      Method = request.Method.Trim().ToUpperInvariant(), MethodologyVersion = request.MethodologyVersion.Trim(), EligibleExposure = exposure,
      ProbabilityOfDefault = request.ProbabilityOfDefault, LossGivenDefault = request.LossGivenDefault, ManagementOverlay = request.ManagementOverlay,
      CalculatedExpectedLoss = expected, ManagementExpectedLoss = MoneyPolicy.Normalize(request.ManagementExpectedLoss),
      BookedAmount = bookedAmount, Difference = MoneyPolicy.Normalize(expected - bookedAmount),
      ProposedJournalId = request.ProposedJournalId, AssumptionsHash = request.AssumptionsHash.Trim().ToLowerInvariant(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.EclAssessments.Add(assessment);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(assessment.Id);
  }

  public static async Task<CommandResult<Guid>> CreateInventoryValuationAsync(
    IClientAccountingDbContext db, ActorContext actor, InventoryValuationRequest request,
    CancellationToken ct = default)
  {
    if (request.Quantity < 0m || request.UnitCost < 0m || request.NrvPerUnit < 0m || request.ObsolescenceReserve < 0m ||
        string.IsNullOrWhiteSpace(request.MethodologyVersion) || !IsSha256(request.AssumptionsHash))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Inventory valuation needs explicit non-negative inputs and assumptions.");
    var reconciliation = await db.AccountingReconciliations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ReconciliationId && x.FirmId == actor.FirmId, ct);
    if (reconciliation is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, reconciliation.ClientId, reconciliation.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (reconciliation.Status != "RECONCILED")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Inventory valuation requires a reconciled count/cost source.");
    if (!await HasProposedAdjustmentAsync(db, reconciliation, request.ProposedJournalId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The proposed inventory adjustment is outside the reconciliation scope.");
    var calculated = MoneyPolicy.Normalize(request.Quantity * Math.Min(request.UnitCost, request.NrvPerUnit) - request.ObsolescenceReserve);
    var assessment = new InventoryValuationAssessment
    {
      Id = Guid.CreateVersion7(), FirmId = reconciliation.FirmId, ClientId = reconciliation.ClientId, EngagementId = reconciliation.EngagementId,
      ReconciliationId = reconciliation.Id, Version = (await db.InventoryValuationAssessments.Where(x => x.FirmId == actor.FirmId && x.ReconciliationId == reconciliation.Id)
        .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1, AsOfDate = request.AsOfDate,
      ReconciliationSourceHash = reconciliation.SourceHash, InputGeneration = reconciliation.InputGeneration,
      Quantity = request.Quantity, UnitCost = request.UnitCost, NrvPerUnit = request.NrvPerUnit, ObsolescenceReserve = request.ObsolescenceReserve,
      BookAmount = MoneyPolicy.Normalize(request.BookAmount), CalculatedAmount = calculated,
      Difference = MoneyPolicy.Normalize(calculated - request.BookAmount), ProposedJournalId = request.ProposedJournalId,
      MethodologyVersion = request.MethodologyVersion.Trim(),
      AssumptionsHash = request.AssumptionsHash.Trim().ToLowerInvariant(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.InventoryValuationAssessments.Add(assessment);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(assessment.Id);
  }

  public static async Task<CommandResult<Guid>> RecordSpecialistScheduleAsync(
    IClientAccountingDbContext db, ActorContext actor, SpecialistScheduleRequest request,
    CancellationToken ct = default)
  {
    var area = request.Area?.Trim().ToUpperInvariant() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(request.MethodologyVersion) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || !IsSha256(request.AssumptionsHash))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Specialist schedules need an approved method, evidence and assumptions.");
    var profileError = ValidateSpecialistProfile(request, area, out var calculated);
    if (profileError is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, profileError);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.Id == request.PeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The schedule period is outside the client scope.");
    var clientState = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ClientId && x.FirmId == actor.FirmId, ct);
    if (clientState is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client accounting safety state is unavailable.");
    var schedule = new SpecialistAccountingSchedule
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, InputGeneration = clientState.InputGeneration, Area = area,
      MethodologyVersion = request.MethodologyVersion.Trim(),
      DepreciationMethod = request.DepreciationMethod?.Trim().ToUpperInvariant() ?? string.Empty,
      UsefulLifeMonths = request.UsefulLifeMonths,
      PayrollGrossAmount = request.PayrollGrossAmount.HasValue ? MoneyPolicy.Normalize(request.PayrollGrossAmount.Value) : null,
      PayrollDeductionsAmount = request.PayrollDeductionsAmount.HasValue ? MoneyPolicy.Normalize(request.PayrollDeductionsAmount.Value) : null,
      PayrollNetAmount = request.PayrollNetAmount.HasValue ? MoneyPolicy.Normalize(request.PayrollNetAmount.Value) : null,
      PayrollContractReference = request.PayrollContractReference?.Trim() ?? string.Empty,
      PayrollBankPaymentReference = request.PayrollBankPaymentReference?.Trim() ?? string.Empty,
      LoanRepaymentAmount = request.LoanRepaymentAmount.HasValue ? MoneyPolicy.Normalize(request.LoanRepaymentAmount.Value) : null,
      LoanMaturityDate = request.LoanMaturityDate,
      LoanCovenantReference = request.LoanCovenantReference?.Trim() ?? string.Empty,
      EquityProfitOrLossAmount = request.EquityProfitOrLossAmount.HasValue ? MoneyPolicy.Normalize(request.EquityProfitOrLossAmount.Value) : null,
      EquityOciAmount = request.EquityOciAmount.HasValue ? MoneyPolicy.Normalize(request.EquityOciAmount.Value) : null,
      RelatedPartyDisclosureReference = request.RelatedPartyDisclosureReference?.Trim() ?? string.Empty,
      TaxJurisdiction = request.TaxJurisdiction?.Trim() ?? string.Empty,
      TaxRuleVersion = request.TaxRuleVersion?.Trim() ?? string.Empty,
      TaxBaseAmount = request.TaxBaseAmount.HasValue ? MoneyPolicy.Normalize(request.TaxBaseAmount.Value) : null,
      TaxRate = request.TaxRate.HasValue ? MoneyPolicy.Normalize(request.TaxRate.Value) : null,
      TaxReturnEvidenceReference = request.TaxReturnEvidenceReference?.Trim() ?? string.Empty,
      TaxPaymentEvidenceReference = request.TaxPaymentEvidenceReference?.Trim() ?? string.Empty,
      TaxCorrespondenceReference = request.TaxCorrespondenceReference?.Trim() ?? string.Empty,
      ForecastOwner = request.ForecastOwner?.Trim() ?? string.Empty,
      ForecastHorizonEnd = request.ForecastHorizonEnd,
      ForecastCashInputAmount = request.ForecastCashInputAmount.HasValue ? MoneyPolicy.Normalize(request.ForecastCashInputAmount.Value) : null,
      ForecastDebtInputAmount = request.ForecastDebtInputAmount.HasValue ? MoneyPolicy.Normalize(request.ForecastDebtInputAmount.Value) : null,
      ForecastSensitivityReference = request.ForecastSensitivityReference?.Trim() ?? string.Empty,
      ForecastSensitivityResult = request.ForecastSensitivityResult?.Trim() ?? string.Empty,
      OpeningAmount = MoneyPolicy.Normalize(request.OpeningAmount), AdditionsAmount = MoneyPolicy.Normalize(request.AdditionsAmount),
      DisposalsAmount = MoneyPolicy.Normalize(request.DisposalsAmount), DepreciationAmount = MoneyPolicy.Normalize(request.DepreciationAmount),
      ImpairmentAmount = MoneyPolicy.Normalize(request.ImpairmentAmount), InterestAmount = MoneyPolicy.Normalize(request.InterestAmount),
      CurrentPortion = MoneyPolicy.Normalize(request.CurrentPortion), NonCurrentPortion = MoneyPolicy.Normalize(request.NonCurrentPortion),
      CapitalMovement = MoneyPolicy.Normalize(request.CapitalMovement), Dividends = MoneyPolicy.Normalize(request.Dividends),
      TaxPaid = MoneyPolicy.Normalize(request.TaxPaid), ManagementAmount = MoneyPolicy.Normalize(request.ManagementAmount),
      CalculatedAmount = MoneyPolicy.Normalize(calculated), ClosingAmount = MoneyPolicy.Normalize(calculated),
      Difference = MoneyPolicy.Normalize(calculated - request.ManagementAmount),
      AssumptionsHash = request.AssumptionsHash.Trim().ToLowerInvariant(), EvidenceReference = request.EvidenceReference.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.SpecialistAccountingSchedules.Add(schedule);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(schedule.Id);
  }

  private static string? ValidateSpecialistProfile(
    SpecialistScheduleRequest request, string area, out decimal calculated)
  {
    calculated = 0m;
    switch (area)
    {
      case "ASSETS":
        if (string.IsNullOrWhiteSpace(request.DepreciationMethod) || request.UsefulLifeMonths is not > 0)
          return "Asset schedules need a depreciation method and positive useful life.";
        calculated = request.OpeningAmount + request.AdditionsAmount - request.DisposalsAmount -
          request.DepreciationAmount - request.ImpairmentAmount;
        return calculated < 0m ? "An asset closing balance cannot be negative." : null;

      case "PAYROLL":
        if (request.PayrollGrossAmount is not >= 0m || request.PayrollDeductionsAmount is not >= 0m ||
            request.PayrollNetAmount is not >= 0m || string.IsNullOrWhiteSpace(request.PayrollContractReference) ||
            string.IsNullOrWhiteSpace(request.PayrollBankPaymentReference))
          return "Payroll schedules need non-negative gross, deductions and net amounts plus contract and bank-payment evidence.";
        calculated = MoneyPolicy.Normalize(request.PayrollGrossAmount.Value - request.PayrollDeductionsAmount.Value);
        return calculated != MoneyPolicy.Normalize(request.PayrollNetAmount.Value)
          ? "Payroll net pay must equal gross pay less deductions." : null;

      case "LOANS":
        if (request.LoanRepaymentAmount is not >= 0m || request.LoanMaturityDate is null ||
            request.CurrentPortion < 0m || request.NonCurrentPortion < 0m ||
            string.IsNullOrWhiteSpace(request.LoanCovenantReference))
          return "Loan schedules need repayments, maturity, current/non-current split and covenant evidence.";
        calculated = request.OpeningAmount + request.AdditionsAmount - request.LoanRepaymentAmount.Value + request.InterestAmount;
        return MoneyPolicy.Normalize(request.CurrentPortion + request.NonCurrentPortion) != MoneyPolicy.Normalize(calculated)
          ? "Loan current and non-current portions must reconcile to the calculated closing balance." : null;

      case "EQUITY":
        if (request.EquityProfitOrLossAmount is null || request.EquityOciAmount is null ||
            string.IsNullOrWhiteSpace(request.RelatedPartyDisclosureReference))
          return "Equity schedules need profit/OCI inputs and a related-party disclosure reference.";
        calculated = request.OpeningAmount + request.EquityProfitOrLossAmount.Value + request.EquityOciAmount.Value +
          request.CapitalMovement - request.Dividends;
        return null;

      case "RELATED_PARTIES":
        if (string.IsNullOrWhiteSpace(request.RelatedPartyDisclosureReference))
          return "Related-party schedules need a disclosure reference.";
        calculated = request.OpeningAmount + request.AdditionsAmount - request.DisposalsAmount;
        return null;

      case "TAX":
        if (string.IsNullOrWhiteSpace(request.TaxJurisdiction) || string.IsNullOrWhiteSpace(request.TaxRuleVersion) ||
            request.TaxBaseAmount is not >= 0m || request.TaxRate is not >= 0m ||
            string.IsNullOrWhiteSpace(request.TaxReturnEvidenceReference) ||
            string.IsNullOrWhiteSpace(request.TaxPaymentEvidenceReference) ||
            string.IsNullOrWhiteSpace(request.TaxCorrespondenceReference))
          return "Tax schedules need an approved jurisdiction/rule, explicit base/rate and return, payment and correspondence evidence.";
        calculated = MoneyPolicy.Normalize(request.TaxBaseAmount.Value * request.TaxRate.Value);
        return null;

      case "FORECAST":
        if (string.IsNullOrWhiteSpace(request.ForecastOwner) || request.ForecastHorizonEnd is null ||
            request.ForecastCashInputAmount is not >= 0m || request.ForecastDebtInputAmount is not >= 0m ||
            string.IsNullOrWhiteSpace(request.ForecastSensitivityReference) ||
            string.IsNullOrWhiteSpace(request.ForecastSensitivityResult))
          return "Going-concern forecasts need management ownership, horizon, cash/debt inputs and sensitivity evidence.";
        calculated = MoneyPolicy.Normalize(request.ForecastCashInputAmount.Value - request.ForecastDebtInputAmount.Value);
        return null;

      default:
        return "This specialist accounting area is not enabled for the current approved method.";
    }
  }
}
