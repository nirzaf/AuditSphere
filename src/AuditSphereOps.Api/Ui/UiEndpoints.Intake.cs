using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public const long MaxTrialBalanceUploadBytes = 25 * 1024 * 1024;
  public sealed record IntakeAllocationInput(string AccountCode, string Destination, string Section);
  public sealed record IntakeDraftInput(string TaxonomyVersion, IReadOnlyList<IntakeAllocationInput> Allocations);

  private static bool ValidReviewThreshold(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{1,14}(\.\d{1,6})?$");

  private static void MapIntakeEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{id:guid}/tb-intake", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => TrialBalanceIntakeWorkspaceQuery.GetAsync(db, actor, id, ct)));
    // Preview validates without persisting anything, but still requires antiforgery because it accepts an upload.
    group.MapPost("/engagements/{id:guid}/tb-intake/preview", async (Guid id, HttpContext http) =>
    {
      var file = await ReadUploadAsync(http, "file", MaxTrialBalanceUploadBytes);
      if (file is null) return Invalid("Choose an Excel or CSV file up to 25 MB.");
      return await CommandAsync(http, async (db, actor, ct) =>
      {
        return await TrialBalanceUploadWorkspace.PreviewAsync(db, actor, id, file.Value.Name, file.Value.Content, ct);
      });
    });
    group.MapPost("/engagements/{id:guid}/tb-intake/import", async (Guid id, HttpContext http) =>
    {
      var file = await ReadUploadAsync(http, "file", MaxTrialBalanceUploadBytes);
      if (file is null) return Invalid("Choose an Excel or CSV file up to 25 MB.");
      return await CommandAsync(http, async (db, actor, ct) =>
      {
        var form = await http.Request.ReadFormAsync(ct);
        return await TrialBalanceUploadWorkspace.ImportAsync(db, actor, id, file.Value.Name, file.Value.Content,
          form["revision"].ToString(), form["fileSha256"].ToString(), form["reviewed"].ToString() == "true", ct);
      });
    });
    // Reconciliation parses the reselected exact file and reads receipts; it performs no import or retry.
    group.MapPost("/engagements/{id:guid}/tb-intake/reconcile", async (Guid id, HttpContext http) =>
    {
      var file = await ReadUploadAsync(http, "file", MaxTrialBalanceUploadBytes);
      if (file is null) return Invalid("Choose an Excel or CSV file up to 25 MB.");
      return await CommandAsync(http, (db, actor, ct) => TrialBalanceUploadWorkspace.PreviewAsync(db, actor, id, file.Value.Name, file.Value.Content, ct));
    });
    group.MapGet("/datasets/{id:guid}/mapping-memory", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => MappingMemoryService.ProposeAsync(db, actor, id, ct)));
    group.MapGet("/datasets/{id:guid}/currency-review", (Guid id, string presentation, string? percent, string? amount, HttpContext http) =>
    {
      var p = 10m; var a = 0m;
      if ((percent is not null && (!ValidReviewThreshold(percent) || !TryDecimal(percent, out p))) || (amount is not null && (!ValidReviewThreshold(amount) || !TryDecimal(amount, out a))))
        return Task.FromResult(Invalid("Enter thresholds as numbers."));
      return ReadAsync(http, (db, actor, ct) => TrialBalanceCurrencyReviewQuery.GetAsync(db, actor, id, presentation ?? "", p, a, ct));
    });
    group.MapPost("/datasets/{id:guid}/draft-mapping", (Guid id, IntakeDraftInput input, HttpContext http) =>
    {
      var overrides = (input.Allocations ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Destination)).Select(x => new MappingAllocationInput(x.AccountCode,
        x.Destination.Trim().ToUpperInvariant(), (x.Section ?? "").Trim().ToUpperInvariant(), 1m, "Mapped at intake")).ToList();
      return CommandAsync(http, (db, actor, ct) => TrialBalanceIntakeWorkspaceQuery.CreateDraftAsync(db, actor, id, input.TaxonomyVersion ?? "", overrides, ct));
    });
  }
}
