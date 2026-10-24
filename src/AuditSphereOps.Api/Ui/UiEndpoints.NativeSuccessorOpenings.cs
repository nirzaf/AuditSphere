using System.Globalization;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record NativeSuccessorOpeningHttpInput(Guid RequestId, Guid ClientId, Guid EngagementId, Guid PeriodId,
    string ExpectedPeriodRevision, Guid PriorPeriodId, string ExpectedPriorPeriodRevision, Guid NativeTrialBalanceDatasetId,
    string ExpectedSourceIdentityHash, string EvidenceReference, string? ReviewBasis = null, bool Reviewed = false);
  public sealed record NativeSuccessorOpeningApprovalHttpInput(Guid RequestId, Guid ClientId, Guid EngagementId,
    Guid OpeningId, string ExpectedPeriodRevision, string ExpectedOpeningManifestHash, string ExpectedSuccessorProofHash, bool Reviewed);

  private static void MapNativeSuccessorOpeningEndpoints(RouteGroupBuilder group)
  {
    const string path = "/accounting/clients/{clientId:guid}/operational-opening-successors";
    group.MapGet(path + "/catalog/{periodId:guid}", (Guid clientId, Guid periodId, int? page, int? pageSize, HttpContext http) =>
    {
      http.Response.Headers.CacheControl = "no-store";
      return ReadAsync(http, (db, actor, ct) => ClientOperationalOpeningBalanceWorkspace.SuccessorCatalogAsync(
        db, actor, clientId, periodId, page ?? 0, pageSize ?? 5, ct));
    }).WithUiResponse<NativeSuccessorSourceCatalog>(includeConflict: true);
    group.MapPost(path + "/preview", (Guid clientId, NativeSuccessorOpeningHttpInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => TryNativeSuccessorInput(clientId, input, out var request)
        ? ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, actor, request!, ct)
        : Task.FromResult(CommandResult<NativeSuccessorOpeningPreview>.Fail("request.invalid", "Select exact canonical successor revisions and scope."))))
      .WithUiResponse<UiValueResponse<NativeSuccessorOpeningPreview>>(includeConflict: true);
    group.MapPost(path + "/create", (Guid clientId, NativeSuccessorOpeningHttpInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => TryNativeSuccessorInput(clientId, input, out var request)
        ? ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, actor, request!, ct)
        : Task.FromResult(CommandResult<NativeSuccessorOpeningView>.Fail("request.invalid", "Select the exact reviewed successor intent."))))
      .WithUiResponse<UiValueResponse<NativeSuccessorOpeningView>>(includeConflict: true);
    group.MapGet(path + "/{openingId:guid}", (Guid clientId, Guid openingId, HttpContext http) =>
    {
      http.Response.Headers.CacheControl = "no-store";
      return ReadAsync(http, (db, actor, ct) => ClientOperationalOpeningBalanceWorkspace.GetSuccessorAsync(db, actor, clientId, openingId, ct));
    }).WithUiResponse<NativeSuccessorOpeningView>(includeConflict: true);
    group.MapPost(path + "/{openingId:guid}/approve", (Guid clientId, Guid openingId, NativeSuccessorOpeningApprovalHttpInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => input.ClientId == clientId && input.OpeningId == openingId &&
          TryNativeSuccessorRevision(input.ExpectedPeriodRevision, out var revision)
        ? ClientOperationalOpeningBalanceWorkspace.ApproveSuccessorAsync(db, actor,
          new(input.RequestId, clientId, input.EngagementId, openingId, revision, input.ExpectedOpeningManifestHash,
            input.ExpectedSuccessorProofHash, input.Reviewed), ct)
        : Task.FromResult(CommandResult.Fail("request.invalid", "Select the exact scoped opening manifest, proof and revision."))))
      .WithUiResponse<UiValueResponse<bool>>(includeConflict: true);
  }

  private static bool TryNativeSuccessorRevision(string raw, out long revision) =>
    long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision > 0 &&
    revision.ToString(CultureInfo.InvariantCulture) == raw;
  private static bool TryNativeSuccessorInput(Guid clientId, NativeSuccessorOpeningHttpInput input,
    out NativeSuccessorOpeningRequest? request)
  {
    request = null;
    if (clientId == Guid.Empty || input.ClientId != clientId || !TryNativeSuccessorRevision(input.ExpectedPeriodRevision, out var revision) ||
        !TryNativeSuccessorRevision(input.ExpectedPriorPeriodRevision, out var priorRevision)) return false;
    request = new(input.RequestId, clientId, input.EngagementId, input.PeriodId, revision, input.PriorPeriodId,
      priorRevision, input.NativeTrialBalanceDatasetId, input.ExpectedSourceIdentityHash, input.EvidenceReference, input.ReviewBasis, input.Reviewed);
    return true;
  }
}
