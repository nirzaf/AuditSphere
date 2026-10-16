using System.Text.Json.Serialization;
namespace AuditSphereOps.Application.Audit;

/// <summary>Workpaper publication actions; autosave retains its existing save identity.</summary>
public static class WorkpaperCommandActions
{
  public const string SaveDraft = "SAVE_DRAFT";
  public const string DiscardDraft = "DISCARD_DRAFT";
  public const string Submit = "SUBMIT";
}
/// <summary>Exact save basis and editor snapshot. Revisions and generations are strings.</summary>
public sealed record WorkpaperSaveFields(string ExpectedDraftRevision,string BaseWorkpaperRevision,string BaseInputGeneration,string BasePolicyGeneration,string WorkPerformed,string Conclusion);
/// <summary>Exact acknowledged draft to freeze. Nullable acknowledgement fields exist for compatibility callers only.</summary>
public sealed record WorkpaperSubmitFields(string ExpectedRevision,string? ExpectedDraftRevision,Guid? DraftSaveId,string WorkPerformed,string Conclusion);
/// <summary>Exact owned draft revision to discard.</summary>
public sealed record WorkpaperDiscardFields(string ExpectedDraftRevision);
/// <summary>One typed workpaper intent.</summary>
public sealed record WorkpaperCommandFields(string Action,WorkpaperSaveFields? Save=null,WorkpaperSubmitFields? Submit=null,WorkpaperDiscardFields? Discard=null);
/// <summary>Save uses RequestId=SaveId without a review dialog. Discard and submit require an exact preview and assent.</summary>
public sealed record WorkpaperCommandRequest(Guid RequestId,WorkpaperCommandFields Fields,string? ReviewBasis=null,string? ExpectedRequestHash=null,bool Reviewed=false);
/// <summary>Original owner draft snapshot, including terminal lifecycle evidence.</summary>
public sealed record WorkpaperOwnedDraft(Guid Id,string BaseWorkpaperRevision,string BaseInputGeneration,string BasePolicyGeneration,string DraftRevision,
  Guid LastSaveId,string WorkPerformed,string Conclusion,string Lifecycle,DateTimeOffset LastSavedAt);
/// <summary>Exact scope, target, current safety basis and owned draft; timestamps are canonical UTC.</summary>
public sealed record WorkpaperCommandSnapshot(Guid WorkpaperId,Guid ClientId,Guid EngagementId,string Index,string Title,Guid? ProcedureId,
  string Revision,string Status,string InputGeneration,string PolicyGeneration,string? WorkPerformed,string? Conclusion,DateTimeOffset? SubmittedAt,WorkpaperOwnedDraft? Draft);
/// <summary>Current scoped command capabilities and safe readable source details.</summary>
public sealed record WorkpaperCommandState(WorkpaperCommandSnapshot Workpaper,string ReviewBasis,bool CanSaveDraft,bool CanDiscardDraft,bool CanSubmit,
  IReadOnlyList<string> Blockers,[property:JsonIgnore]string StateJson);
/// <summary>Exact reviewed final action, before-state and intended immutable effect.</summary>
public sealed record WorkpaperCommandPreview(Guid RequestId,string RequestHash,string ReviewBasis,WorkpaperCommandFields CanonicalInput,
  WorkpaperCommandSnapshot Before,bool CanProceed,IReadOnlyList<string> Blockers,string Summary);
/// <summary>Original real publication before and after, independent of mutable current history.</summary>
public sealed record WorkpaperCommandEventView(Guid Id,Guid ActorId,string ActorEpoch,string Action,DateTimeOffset CreatedAt,WorkpaperCommandSnapshot Before,WorkpaperCommandSnapshot After);
/// <summary>Original actor/request acknowledgement. First return and recovery use persisted precision.</summary>
public sealed record WorkpaperCommandReceiptView(Guid Id,Guid WorkpaperId,Guid ActorId,string ActorEpoch,Guid RequestId,string RequestHash,string Action,DateTimeOffset CreatedAt,
  WorkpaperCommandPreview? Preview,WorkpaperCommandEventView CommandEvent);
/// <summary>Opaque exact actor request recovery within current authority and lifetime.</summary>
public sealed record WorkpaperCommandLookup(bool Found,WorkpaperCommandReceiptView? Receipt);
/// <summary>Read-only canonical autosave intent; retain only its request reference/hash/action before dispatch.</summary>
public sealed record WorkpaperSaveIntent(Guid RequestId,string RequestHash,WorkpaperCommandFields CanonicalInput);
/// <summary>Exact-string HTTP draft projection; in-process legacy draft contracts remain unchanged.</summary>
public sealed record WorkpaperDraftView(Guid WorkpaperId,Guid? DraftId,string BaseWorkpaperRevision,string BaseInputGeneration,string BasePolicyGeneration,
  string DraftRevision,string WorkPerformed,string Conclusion,Guid LastSaveId,DateTimeOffset? LastSavedAt,string Lifecycle);
