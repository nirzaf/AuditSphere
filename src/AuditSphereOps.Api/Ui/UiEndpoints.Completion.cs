using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record CriticalityInput(bool Critical);
  public sealed record SrmInput(string Recommendations);
  public sealed record ClearanceInput(Guid SrmId, string KeyRiskAreasComment, string NotesComment);
  public sealed record OpinionInput(string OpinionType, string? FocusArea, string? Basis);
  public sealed record ReportInput(string Kind);
  public sealed record CommentResolutionInput(string Resolution);
  public sealed record ScanVerificationInput(string ExpectedSha256, string Reason, bool Reviewed);
  public sealed record BundleInput(bool StatementsReviewed);
  public sealed record AmendmentInput(string Reason);
  public sealed record EarlyLockInput(string ExpectedRevision, bool PartnerConfirmed, string? Rationale, string? ArchiveReadinessDigest);
  public sealed record LockInput(string DocumentKey);
  public sealed record ReleaseCandidateInput(Guid PackageId);
  public sealed record WorkprogramApprovalInput(string Rationale);

  private static void MapCompletionEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{id:guid}/completion", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => EngagementCompletionWorkspaceQuery.GetAsync(db, actor, id, DateTimeOffset.UtcNow, ct)));
    group.MapPost("/engagements/{id:guid}/completion/release-candidate", (Guid id, ReleaseCandidateInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => EngagementCompletionWorkspaceQuery.PrepareReleaseCandidateAsync(db, actor, id, i.PackageId, ct)));
    group.MapPost("/confirmations/{caseId:guid}/criticality", (Guid caseId, CriticalityInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditDeliverableService.SetConfirmationCriticalityAsync(db, actor, caseId, i.Critical,
        i.Critical ? "Material to the audit opinion" : "Reassessed as not critical to the report", ct)));
    group.MapPost("/engagements/{id:guid}/completion/srm", (Guid id, SrmInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditDeliverableService.GenerateSummaryReviewMemorandumAsync(db, actor, id, i.Recommendations ?? "", ct)));
    group.MapPost("/engagements/{id:guid}/completion/workprogram-approval", (Guid id, WorkprogramApprovalInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditFieldworkService.ApproveWorkprogramsAsync(db, actor, new(id, i.Rationale ?? ""), ct)));
    group.MapPost("/engagements/{id:guid}/completion/clearance", (Guid id, ClearanceInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditDeliverableService.PartnerClearAsync(db, actor, i.SrmId, i.KeyRiskAreasComment ?? "", i.NotesComment ?? "", ct)));
    group.MapPost("/engagements/{id:guid}/completion/opinion", (Guid id, OpinionInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditDeliverableService.DecideOpinionAsync(db, actor, id, i.OpinionType, i.FocusArea, i.Basis, ct)));
    group.MapPost("/engagements/{id:guid}/completion/reports", (Guid id, ReportInput i, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        // A report attempt may legitimately produce only a holding letter; that is reported as not generated.
        var r = await AuditDeliverableService.GenerateReportAsync(db, actor, id, i.Kind, ct);
        if (!r.Succeeded) return CommandResult<object>.Fail(r.ErrorCode!, r.Message!);
        return r.Value!.DeliverableId is null
          ? CommandResult<object>.Fail(ErrorCodes.GateBlocked, r.Value.Message)
          : CommandResult<object>.Ok(new { r.Value.DeliverableId, r.Value.Message });
      }));
    group.MapPost("/deliverables/{id:guid}/share", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditDeliverableService.ShareWithClientAsync(db, actor, id, ct)));
    group.MapPost("/deliverables/{id:guid}/sign", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditDeliverableService.SignIndependentReportAsync(db, actor, id, ct)));
    group.MapPost("/deliverable-comments/{id:guid}/resolve", (Guid id, CommentResolutionInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditDeliverableService.ResolveCommentAsync(db, actor, id, i.Resolution ?? "", ct)));
    group.MapPost("/representation-scans/{id:guid}/verify", (Guid id, ScanVerificationInput i, HttpContext http) =>
      i.Reviewed
        ? CommandAsync(http, (db, actor, ct) => AuditDeliverableService.VerifySignedRepresentationAsync(db, actor, id, i.ExpectedSha256 ?? "", i.Reason ?? "", ct))
        : Task.FromResult(Invalid("Confirm you reviewed this exact scan.")));
    group.MapPost("/engagements/{id:guid}/completion/bundle", (Guid id, BundleInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AuditDeliverableService.AssembleBundleAsync(db, actor, id, i.StatementsReviewed, ct)));
    group.MapUiPost("/signatures", async http =>
    {
      var png = await ReadUploadAsync(http, "file", AuditDeliverableService.MaxSignatureBytes);
      return png is null ? Invalid("Upload a PNG of at most 512 KB.")
        : await CommandAsync(http, (db, actor, ct) => AuditDeliverableService.RegisterSignatureAsync(db, actor, png.Value.Content, ct));
    });
    group.MapUiPost("/firm-seal", async http =>
    {
      var png = await ReadUploadAsync(http, "file", AuditDeliverableService.MaxSignatureBytes);
      return png is null ? Invalid("Seal PNG must be at most 512 KB.")
        : await CommandAsync(http, (db, actor, ct) => AuditDeliverableService.RegisterFirmSealAsync(db, actor, png.Value.Content, ct));
    });
    group.MapPost("/engagements/{id:guid}/amendments", (Guid id, AmendmentInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FileFreezeService.RequestAmendmentAsync(db, actor, id, i.Reason ?? "", ct)));
    group.MapGet("/engagements/{id:guid}/early-lock/readiness", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FileFreezeService.GetArchiveReadinessAsync(db, actor, id, DateTimeOffset.UtcNow, ct)));
    group.MapPost("/engagements/{id:guid}/early-lock", (Guid id, EarlyLockInput i, HttpContext http) =>
      long.TryParse(i.ExpectedRevision, out var revision)
        ? CommandAsync(http, (db, actor, ct) => FileFreezeService.RequestEarlyComplianceLockAsync(db, actor,
            new FileFreezeService.EarlyComplianceLockRequest(id, revision, i.PartnerConfirmed, i.Rationale ?? "", i.ArchiveReadinessDigest ?? ""),
            DateTimeOffset.UtcNow, ct))
        : Task.FromResult(Invalid("The countdown revision must be a whole number.")));
    group.MapPost("/amendments/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FileFreezeService.ApproveAmendmentAsync(db, actor, id, ct)));
    group.MapPost("/amendments/{id:guid}/close", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FileFreezeService.CloseAmendmentAsync(db, actor, id, ct)));
    group.MapPost("/engagements/{id:guid}/locks", (Guid id, LockInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => EngagementActivityQuery.LockAsync(db, actor, id, i.DocumentKey ?? "", ct)));
    group.MapPost("/locks/{id:guid}/release", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => EngagementActivityQuery.UnlockAsync(db, actor, id, ct)));
  }
}
