using AuditSphereOps.Application.Audit;
using System.Globalization;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record BankReconciliationItemInputDto(string StableItemId, string ItemType, string SignedAmount,
    string Description, string SourceReference, string EvidenceReference, Guid? SourceScheduleId = null, Guid? ProposedJournalId = null);
  public sealed record BankReconciliationCreateInput(Guid? ProcedureId, Guid LedgerScheduleId, Guid StatementScheduleId,
    DateOnly AsOfDate, IReadOnlyList<BankReconciliationItemInputDto>? Items);
  public sealed record BankReconciliationReviewInput(Guid ReconciliationId, string Decision, string Conclusion);
  public sealed record BankReconciliationCommandInput(Guid RequestId, string ExpectedGeneration, string ReviewBasis,
    BankReconciliationCreateInput? Create = null, BankReconciliationReviewInput? Review = null,
    string? ExpectedRequestHash = null, bool Reviewed = false);

  private static void MapBankReconciliationInspectionEndpoints(RouteGroupBuilder group)
  {
    const string path = "/engagements/{id:guid}/fieldwork/bank-reconciliations";
    group.MapGet(path, (Guid id, int? offset, int? limit, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => BankReconciliationInspectionQuery.ListAsync(db, actor, id, offset ?? 0, limit ?? 25, ct)))
      .WithUiResponse<BankReconciliationInspectionPage>();
    group.MapGet(path + "/{reconciliationId:guid}", (Guid id, Guid reconciliationId, int? offset, int? limit, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => BankReconciliationInspectionQuery.DetailAsync(db, actor, id, reconciliationId, offset ?? 0, limit ?? 100, ct)))
      .WithUiResponse<BankReconciliationInspectionDetail>();
    UiEndpointContractMetadata.WithUiResponse<BankReconciliationCommandState>(group.MapGet(path + "/command-state",
      (Guid id, Guid? reconciliationId, HttpContext http) => ReadAsync(http,
        (db, actor, ct) => BankReconciliationWorkspace.StateAsync(db, actor, id, reconciliationId, ct))));
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<BankReconciliationCommandPreview>>(group.MapPost(path + "/create/preview",
      (Guid id, BankReconciliationCommandInput? input, HttpContext http) =>
        !TryCommand(input, out var request) || request!.Create is null || request.Review is not null
          ? Task.FromResult(Invalid("Provide one exact bank-reconciliation creation intent."))
          : CommandAsync(http, (db, actor, ct) => BankReconciliationWorkspace.PreviewCreateAsync(db, actor, id, request, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<BankReconciliationCommandReceiptView>>(group.MapPost(path + "/create",
      (Guid id, BankReconciliationCommandInput? input, HttpContext http) =>
        !TryCommand(input, out var request) || request!.Create is null || request.Review is not null
          ? Task.FromResult(Invalid("Preview and confirm one exact bank-reconciliation creation intent."))
          : CommandAsync(http, (db, actor, ct) => BankReconciliationWorkspace.ExecuteCreateAsync(db, actor, id, request, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<BankReconciliationCommandPreview>>(group.MapPost(path + "/{reconciliationId:guid}/review/preview",
      (Guid id, Guid reconciliationId, BankReconciliationCommandInput? input, HttpContext http) =>
        !TryCommand(input, out var request) || request!.Review is null || request.Create is not null || request.Review.ReconciliationId != reconciliationId
          ? Task.FromResult(Invalid("Provide one exact bank-reconciliation review intent."))
          : CommandAsync(http, (db, actor, ct) => BankReconciliationWorkspace.PreviewReviewAsync(db, actor, id, reconciliationId, request, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<BankReconciliationCommandReceiptView>>(group.MapPost(path + "/{reconciliationId:guid}/review",
      (Guid id, Guid reconciliationId, BankReconciliationCommandInput? input, HttpContext http) =>
        !TryCommand(input, out var request) || request!.Review is null || request.Create is not null || request.Review.ReconciliationId != reconciliationId
          ? Task.FromResult(Invalid("Preview and confirm one exact bank-reconciliation review intent."))
          : CommandAsync(http, (db, actor, ct) => BankReconciliationWorkspace.ExecuteReviewAsync(db, actor, id, request, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<BankReconciliationCommandLookup>(group.MapGet(path + "/receipts/{requestId:guid}",
      (Guid id, Guid requestId, string? requestHash, string? action, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => BankReconciliationWorkspace.LookupAsync(db, actor, id, requestId,
          requestHash ?? "", (action ?? "").ToUpperInvariant(), ct))), includeConflict: true);
  }

  private static bool TryCommand(BankReconciliationCommandInput? input, out BankReconciliationCommandRequest? request)
  {
    request = null;
    if (input is null) return false;
    BankReconciliationCreateFields? create = null;
    if (input.Create is { } fields)
    {
      if (fields.Items is null || fields.Items.Count is 0 or > 1000 || fields.Items.Any(x => x is null || !TryBankAmount(x.SignedAmount, out _))) return false;
      create = new(fields.ProcedureId, fields.LedgerScheduleId, fields.StatementScheduleId, fields.AsOfDate,
        fields.Items.Select(x => new BankReconciliationItemInput(x.StableItemId, x.ItemType,
          decimal.Parse(x.SignedAmount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
          x.Description, x.SourceReference, x.EvidenceReference, x.SourceScheduleId, x.ProposedJournalId)).ToArray());
    }
    var review = input.Review is { } decision
      ? new BankReconciliationReviewFields(decision.ReconciliationId, decision.Decision, decision.Conclusion)
      : null;
    request = new(input.RequestId, input.ExpectedGeneration, input.ReviewBasis, create, review, input.ExpectedRequestHash, input.Reviewed);
    return true;
  }

  private static bool TryBankAmount(string? value, out decimal amount) =>
    decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) &&
    decimal.Round(amount, 6) == amount && amount is >= -9_999_999_999_999.999999m and <= 9_999_999_999_999.999999m;
}
