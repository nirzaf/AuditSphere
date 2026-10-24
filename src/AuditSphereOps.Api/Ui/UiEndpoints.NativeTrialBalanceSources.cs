using System.Globalization;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  // The revision is textual at the browser boundary; no 64-bit value passes through a JavaScript number.
  public sealed record NativeTrialBalanceSourceHttpInput(Guid RequestId, Guid ClientId, Guid EngagementId, Guid PeriodId,
    string ExpectedPeriodRevision, string AsOfDate, string LegalEntityKey, bool PeriodLedgerReviewed,
    string EvidenceReference, string? ReviewBasis = null, bool Reviewed = false, bool AllowActivityOnly = false);

  private static void MapNativeTrialBalanceSourceEndpoints(RouteGroupBuilder group)
  {
    const string route = "/accounting/clients/{clientId:guid}/native-tb-sources";
    group.MapPost(route + "/preview", (Guid clientId, NativeTrialBalanceSourceHttpInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => TryNativeSourceInput(clientId, input, out var request)
        ? NativeTrialBalanceSourceWorkspace.PreviewAsync(db, actor, request!, ct)
        : Task.FromResult(CommandResult<NativeTrialBalanceSourcePreview>.Fail("request.invalid", "Select the exact native client, period revision, as-of date and source intent."))))
      .WithUiResponse<UiValueResponse<NativeTrialBalanceSourcePreview>>(includeConflict: true);
    group.MapPost(route + "/seal", (Guid clientId, NativeTrialBalanceSourceHttpInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => TryNativeSourceInput(clientId, input, out var request)
        ? NativeTrialBalanceSourceWorkspace.SealAsync(db, actor, request!, ct)
        : Task.FromResult(CommandResult<NativeTrialBalanceSourceView>.Fail("request.invalid", "Select the exact reviewed native source intent."))))
      .WithUiResponse<UiValueResponse<NativeTrialBalanceSourceView>>(includeConflict: true);
    group.MapGet(route + "/{datasetId:guid}", (Guid clientId, Guid datasetId, HttpContext http) =>
    {
      http.Response.Headers.CacheControl = "no-store";
      return ReadAsync(http, async (db, actor, ct) =>
      {
        var result = await NativeTrialBalanceSourceWorkspace.ViewAsync(db, actor, datasetId, ct);
        return result.Succeeded && result.Value!.ClientId != clientId
          ? CommandResult<NativeTrialBalanceSourceView>.Fail(ErrorCodes.ScopeDenied, "Access denied.") : result;
      });
    }).WithUiResponse<NativeTrialBalanceSourceView>(includeConflict: true);
    group.MapGet(route + "/{datasetId:guid}/artifact", (Guid clientId, Guid datasetId, HttpContext http) =>
    {
      http.Response.Headers.CacheControl = "no-store";
      return ReadAsync(http, async (db, actor, ct) =>
      {
        var view = await NativeTrialBalanceSourceWorkspace.ViewAsync(db, actor, datasetId, ct);
        if (!view.Succeeded) return CommandResult<NativeTrialBalanceSourceArtifact>.Fail(view.ErrorCode!, view.Message!);
        if (view.Value!.ClientId != clientId)
          return CommandResult<NativeTrialBalanceSourceArtifact>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        return await NativeTrialBalanceSourceWorkspace.ArtifactAsync(db, actor, datasetId, ct);
      });
    }).WithUiResponse<NativeTrialBalanceSourceArtifact>(includeConflict: true);
  }

  private static bool TryNativeSourceInput(Guid clientId, NativeTrialBalanceSourceHttpInput input,
    out NativeTrialBalanceSourceRequest? request)
  {
    request = null;
    if (clientId == Guid.Empty || input.ClientId != clientId ||
        !long.TryParse(input.ExpectedPeriodRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1 ||
        revision.ToString(CultureInfo.InvariantCulture) != input.ExpectedPeriodRevision || !TryDate(input.AsOfDate, out var asOf)) return false;
    request = new(input.RequestId, clientId, input.EngagementId, input.PeriodId, revision, asOf,
      input.LegalEntityKey, input.PeriodLedgerReviewed, input.EvidenceReference, input.ReviewBasis, input.Reviewed, input.AllowActivityOnly);
    return true;
  }
}
