using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record CompletenessPrepareInput(Guid TrialBalanceId, Guid? OpeningId, string Revision, string EvidenceReference, bool Reviewed);
  public sealed record CompletenessReviewInput(string Revision, bool Approve, bool Reviewed);
  private static void MapGeneralLedgerEndpoints(RouteGroupBuilder group)
  {
    foreach (var action in new[] { "preview", "reconcile", "import" })
    {
      var command = action;
      group.MapPost("/engagements/{id:guid}/general-ledger/" + action, async (Guid id, HttpContext http) =>
      {
        var file = await ReadUploadAsync(http, "file", GeneralLedgerCsvProfile.MaxBytes);
        if (file is null) return Invalid("Choose a GL CSV file up to 10,000,000 bytes.");
        return await CommandAsync(http, async (db, actor, ct) =>
        {
          if (command != "import") return await GeneralLedgerUploadWorkspace.PreviewAsync(db, actor, id, file.Value.Name, file.Value.Content, ct);
          var form = await http.Request.ReadFormAsync(ct);
          return await GeneralLedgerUploadWorkspace.ImportAsync(db, actor, id, file.Value.Name, file.Value.Content,
            form["revision"].ToString(), form["fileSha256"].ToString(), form["reviewed"].ToString() == "true", ct);
        });
      });
    }
    group.MapGet("/gl-sources/{id:guid}/completeness", (Guid id, int? page, int? openingPage, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => GeneralLedgerCompletenessWorkspace.GetAsync(db, actor, id, page ?? 1, openingPage ?? 1, ct)));
    group.MapGet("/gl-sources/{id:guid}/completeness/plan", (Guid id, Guid trialBalanceId, Guid? openingId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => GeneralLedgerCompletenessWorkspace.PlanAsync(db, actor, id, trialBalanceId, openingId, ct)));
    group.MapPost("/gl-sources/{id:guid}/completeness", (Guid id, CompletenessPrepareInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => GeneralLedgerCompletenessWorkspace.PrepareAsync(db, actor, id, input.TrialBalanceId, input.OpeningId,
        input.Revision, input.EvidenceReference, input.Reviewed, http.RequestServices.GetRequiredService<IOperationStore>(),
        http.RequestServices.GetRequiredService<GeneralLedgerCompletenessHandler>(), ct)));
    group.MapGet("/gl-completeness/{id:guid}", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => GeneralLedgerCompletenessWorkspace.ReviewAsync(db, actor, id, page ?? 1, ct)));
    group.MapPost("/gl-completeness/{id:guid}/review", (Guid id, CompletenessReviewInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => GeneralLedgerCompletenessWorkspace.DecideAsync(db, actor, id, input.Approve, input.Revision, input.Reviewed, ct)));
    group.MapGet("/engagements/{id:guid}/general-ledger", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => GeneralLedgerWorkspace.CatalogueAsync(db, actor, id, page ?? 1, ct)));
    group.MapGet("/engagements/{id:guid}/general-ledger/{batchId:guid}", (Guid id, Guid batchId,
      string? account, DateOnly? from, DateOnly? to, string? journal, string? counterparty, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => GeneralLedgerWorkspace.SourceAsync(db, actor, id, batchId,
        new(account, from, to, journal, counterparty), page ?? 1, ct)));
    group.MapGet("/engagements/{id:guid}/general-ledger/{batchId:guid}/journal", (Guid id, Guid batchId, string journal, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => GeneralLedgerWorkspace.JournalAsync(db, actor, id, batchId, journal, ct)));
  }
}
