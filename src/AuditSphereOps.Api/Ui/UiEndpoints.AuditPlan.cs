using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record MaterialityInput(string BenchmarkSource, string BenchmarkVersion, string Rationale, string BenchmarkAmount, string RateApplied,
    string OverallMateriality, string PerformanceMateriality, string ClearlyTrivialThreshold);
  public sealed record RiskInput(string Area, string Assertion, string Description, string Drivers, string Significance, string Response);
  public sealed record PopulationInput(string Purpose, string Assertion, string SourceReceiptReference, string ExtractionParameters, int RowCount,
    string MonetaryControlTotal, string Currency);
  public sealed record FindingInput(string FindingType, string ImpactDescription, string? MonetaryAmount);
  public sealed record MaterialityCalculationInput(string BenchmarkKind, string? DestinationCode, string RatePercent, string PerformancePercent, string TrivialPercent, string Rationale);
  public sealed record RiskBandInput(int Likelihood, int Magnitude, bool Fraud, string Rationale);
  public sealed record RiskOwnerInput(Guid OwnerUserId);
  public sealed record PartnerClearInput(string Note);

  private static void MapAuditPlanEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{id:guid}/audit-plan", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AuditPlanWorkspaceQuery.GetAsync(db, actor, id, ct)));
    group.MapPost("/engagements/{id:guid}/audit-plan/materiality", (Guid id, MaterialityInput i, HttpContext http) =>
      TryDecimal(i.BenchmarkAmount, out var amount) && TryDecimal(i.RateApplied, out var rate) && TryDecimal(i.OverallMateriality, out var overall) &&
      TryDecimal(i.PerformanceMateriality, out var performance) && TryDecimal(i.ClearlyTrivialThreshold, out var trivial)
        ? CommandAsync(http, (db, actor, ct) => AuditPlanningService.CreateMaterialityAssessmentAsync(db, actor,
            new CreateMaterialityRequest(id, i.BenchmarkSource, i.BenchmarkVersion, i.Rationale, amount, rate, overall, performance, trivial, null), ct))
        : Task.FromResult(Invalid("Enter every materiality amount and the rate as numbers.")));
    group.MapPost("/materiality/{assessmentId:guid}/approve", (Guid assessmentId, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.ApproveMaterialityAssessmentAsync(db, actor, assessmentId, ct)));
    group.MapPost("/engagements/{id:guid}/audit-plan/materiality/calculate", (Guid id, MaterialityCalculationInput i, HttpContext http) =>
      TryDecimal(i.RatePercent, out var rate) && TryDecimal(i.PerformancePercent, out var performance) && TryDecimal(i.TrivialPercent, out var trivial)
        ? CommandAsync(http, (db, actor, ct) => MaterialityEngineService.CalculateAsync(db, actor,
            new CalculateMaterialityRequest(id, i.BenchmarkKind, string.IsNullOrEmpty(i.DestinationCode) ? null : i.DestinationCode, rate, performance, trivial, i.Rationale ?? ""), ct))
        : Task.FromResult(Invalid("Enter the rate, TE and SAD percentages as numbers.")));
    group.MapPost("/engagements/{id:guid}/audit-plan/risks", (Guid id, RiskInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditPlanningService.CreateAuditRiskAsync(db, actor,
        new CreateAuditRiskRequest(id, i.Area, i.Assertion, i.Description, i.Drivers, i.Significance, null, i.Response), ct)));
    group.MapPost("/engagements/{id:guid}/audit-plan/populations", (Guid id, PopulationInput i, HttpContext http) =>
      TryDecimal(i.MonetaryControlTotal, out var total)
        ? CommandAsync(http, (db, actor, ct) => AuditPlanningService.CreatePopulationVersionAsync(db, actor,
            new CreatePopulationRequest(id, i.Purpose, i.Assertion, i.SourceReceiptReference, i.ExtractionParameters, i.RowCount, total, i.Currency, null), ct))
        : Task.FromResult(Invalid("Enter the monetary control total as a number.")));
    group.MapPost("/engagements/{id:guid}/audit-plan/findings", (Guid id, FindingInput i, HttpContext http) =>
    {
      decimal? amount = TryDecimal(i.MonetaryAmount, out var a) ? a : null;
      if (!string.IsNullOrWhiteSpace(i.MonetaryAmount) && amount is null) return Task.FromResult(Invalid("Enter the monetary amount as a number."));
      return CommandAsync(http, (db, actor, ct) => AuditPlanningService.CreateFindingAsync(db, actor,
        new CreateFindingRequest(id, i.FindingType, i.ImpactDescription, false, amount, null), ct));
    });
    group.MapPost("/risks/{riskId:guid}/band", (Guid riskId, RiskBandInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => RiskBandService.AssessAsync(db, actor, new AssessRiskBandRequest(riskId, i.Likelihood, i.Magnitude, i.Fraud, i.Rationale ?? ""), ct)));
    group.MapPost("/risks/{riskId:guid}/owner", (Guid riskId, RiskOwnerInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => RiskBandService.AssignOwnerAsync(db, actor, riskId, i.OwnerUserId, ct)));
    group.MapPost("/risks/{riskId:guid}/partner-review", (Guid riskId, PartnerClearInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => RiskBandService.PartnerClearAsync(db, actor, riskId, i.Note ?? "", ct)));
  }
}
