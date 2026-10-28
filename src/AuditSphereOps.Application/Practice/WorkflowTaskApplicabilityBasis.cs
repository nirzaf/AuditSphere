using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

internal sealed record WorkflowTaskApplicabilityBasis(Guid TaskId, string Condition, string ConditionDigest,
  string Json, string Hash, long TaskRevision, int BindingGeneration, long EngagementGeneration);

/// <summary>
/// Exact retained evidence for an already-authorized conditional task. This does not interpret the condition or decide
/// applicability. Callers own their read snapshot or existing workflow write locks and must recheck current authority.
/// </summary>
internal sealed record WorkflowTaskApplicabilityBasisInput(WorkflowTaskBinding Binding, Guid ReceiptId);

internal static class WorkflowTaskApplicabilityBasisQuery
{
  internal const int MaximumBasisCharacters = 32768;

  internal static async Task<CommandResult<WorkflowTaskApplicabilityBasis>> GetAsync(IAuditSphereDbContext db,
    WorkflowTaskBinding binding, Guid receiptId, CancellationToken ct) =>
    (await GetManyAsync(db, binding.FirmId, [new(binding, receiptId)], ct))[binding.Id];

  // One bounded page shares source facts. The caller owns the coherent snapshot or workflow write locks;
  // this cache never survives that read and never supplies authorization or a stored applicability flag.
  internal static async Task<IReadOnlyDictionary<Guid, CommandResult<WorkflowTaskApplicabilityBasis>>> GetManyAsync(
    IAuditSphereDbContext db, Guid firmId, IReadOnlyList<WorkflowTaskApplicabilityBasisInput> inputs, CancellationToken ct)
  {
    if (inputs.Count > WorkflowTaskEvidenceQuery.MaximumBindings ||
        inputs.Any(x => x.Binding.FirmId != firmId) || inputs.Select(x => x.Binding.Id).Distinct().Count() != inputs.Count)
      throw new InvalidOperationException("Applicability basis reads require unique, same-firm bindings in a bounded page.");
    if (inputs.Count == 0) return new Dictionary<Guid, CommandResult<WorkflowTaskApplicabilityBasis>>();
    var data = await ReadPageAsync(db, firmId, inputs, ct);
    var output = new Dictionary<Guid, CommandResult<WorkflowTaskApplicabilityBasis>>();
    foreach (var input in inputs)
      output.Add(input.Binding.Id, await BuildAsync(db, input.Binding, input.ReceiptId, data, ct));
    return output;
  }

  private sealed record BasisPage(
    IReadOnlyDictionary<Guid, WorkflowTemplateStep> Steps,
    IReadOnlyDictionary<Guid, WorkflowTemplateRevision> Templates,
    IReadOnlyDictionary<Guid, WorkTask> Tasks,
    IReadOnlyDictionary<Guid, Engagement> Engagements,
    IReadOnlyDictionary<Guid, PbcAcceptanceReceipt> Receipts,
    IReadOnlyDictionary<Guid, PbcRequest> Requests,
    IReadOnlyDictionary<Guid, PbcUploadIntent> Uploads,
    IReadOnlyDictionary<Guid, Guid> LatestUploads,
    IReadOnlySet<Guid> PendingRequests,
    IReadOnlyDictionary<Guid, DurableOperation> Transfers,
    IReadOnlyDictionary<Guid, long> Generations,
    IReadOnlyList<WorkflowTaskEvidenceRule> Rules,
    Dictionary<(Guid ClientId, Guid EngagementId, string Kind, Guid SourceId), WorkflowEvidenceEvaluation> Sources);

  private static async Task<BasisPage> ReadPageAsync(IAuditSphereDbContext db, Guid firmId,
    IReadOnlyList<WorkflowTaskApplicabilityBasisInput> inputs, CancellationToken ct)
  {
    var bindings = inputs.Select(x => x.Binding).ToArray();
    var bindingIds = bindings.Select(x => x.Id).ToArray();
    var stepIds = bindings.Where(x => x.TemplateStepId.HasValue).Select(x => x.TemplateStepId!.Value).Distinct().ToArray();
    var templateIds = bindings.Where(x => x.TemplateRevisionId.HasValue).Select(x => x.TemplateRevisionId!.Value).Distinct().ToArray();
    var taskIds = bindings.Select(x => x.WorkTaskId).Distinct().ToArray();
    var engagementIds = bindings.Select(x => x.EngagementId).Distinct().ToArray();
    var clientIds = bindings.Select(x => x.ClientId).Distinct().ToArray();
    var receiptIds = inputs.Select(x => x.ReceiptId).Distinct().ToArray();
    var steps = await db.WorkflowTemplateSteps.AsNoTracking().Where(x => x.FirmId == firmId && stepIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var templates = await db.WorkflowTemplateRevisions.AsNoTracking().Where(x => x.FirmId == firmId && templateIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var tasks = await db.WorkTasks.AsNoTracking().Where(x => x.FirmId == firmId && taskIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var engagements = await db.Engagements.AsNoTracking().Where(x => x.FirmId == firmId && engagementIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var receipts = await db.PbcAcceptanceReceipts.AsNoTracking().Where(x => x.FirmId == firmId && receiptIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var requestIds = receipts.Values.Select(x => x.PbcRequestId).Distinct().ToArray();
    var uploadIds = receipts.Values.Select(x => x.PbcUploadIntentId).Distinct().ToArray();
    var transferIds = receipts.Values.Select(x => x.TransferOperationId).Distinct().ToArray();
    var requests = await db.PbcRequests.AsNoTracking().Where(x => x.FirmId == firmId && requestIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var uploads = await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == firmId && uploadIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    // Read only each request's latest witness, not its unbounded upload history.
    var latest = await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == firmId && requestIds.Contains(x.PbcRequestId) &&
        !db.PbcUploadIntents.Any(n => n.FirmId == x.FirmId && n.PbcRequestId == x.PbcRequestId &&
          (n.CreatedAt > x.CreatedAt || n.CreatedAt == x.CreatedAt && n.Id.CompareTo(x.Id) > 0)))
      .Select(x => new { x.PbcRequestId, x.Id }).ToDictionaryAsync(x => x.PbcRequestId, x => x.Id, ct);
    var pending = await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == firmId && requestIds.Contains(x.PbcRequestId) &&
        (x.State == PbcUploadStates.Started || x.State == PbcUploadStates.Chunking || x.State == PbcUploadStates.Staged))
      .Select(x => x.PbcRequestId).Distinct().ToListAsync(ct);
    var transfers = await db.DurableOperations.AsNoTracking().Where(x => x.FirmId == firmId && transferIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var generations = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == firmId && clientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => (long)x.InputGeneration, ct);
    var rules = await WorkflowTaskEvidenceQuery.Heads(db, firmId).Where(x => bindingIds.Contains(x.WorkflowTaskBindingId))
      .OrderBy(x => x.RuleKey).Take(WorkflowTaskEvidenceQuery.MaximumBindings * WorkflowTaskEvidenceQuery.MaximumRules + 1).ToListAsync(ct);
    if (rules.Count > WorkflowTaskEvidenceQuery.MaximumBindings * WorkflowTaskEvidenceQuery.MaximumRules)
      throw new InvalidOperationException("The complete applicability evidence page exceeds its supported bound.");
    return new(steps, templates, tasks, engagements, receipts, requests, uploads, latest, pending.ToHashSet(), transfers, generations, rules, []);
  }

  private static async Task<CommandResult<WorkflowTaskApplicabilityBasis>> BuildAsync(IAuditSphereDbContext db,
    WorkflowTaskBinding binding, Guid receiptId, BasisPage data, CancellationToken ct)
  {
    if (binding.TemplateRevisionId is not { } revisionId || binding.TemplateStepId is not { } stepId ||
        binding.SupersededAt.HasValue || binding.HeldAt.HasValue)
      return Blocked("Only a current conditional template task without a task hold can be reviewed.");
    var step = data.Steps.GetValueOrDefault(stepId);
    var template = data.Templates.GetValueOrDefault(revisionId);
    var task = data.Tasks.GetValueOrDefault(binding.WorkTaskId);
    var engagement = data.Engagements.GetValueOrDefault(binding.EngagementId);
    if (step?.TemplateRevisionId != revisionId || task is null || task.ClientId != binding.ClientId ||
        task.EngagementId != binding.EngagementId || engagement is null || engagement.PracticeClientId != binding.ClientId)
      return Blocked("The exact template, task and engagement basis is unavailable.");
    if (step?.RequiredTask != true || string.IsNullOrWhiteSpace(step.ApplicabilityCondition) || template is null ||
        template.Status is not (WorkflowTemplateStates.Published or WorkflowTemplateStates.Superseded) ||
        task is null || task.Status == PracticeTimeStates.TaskCompleted || engagement is null)
      return Blocked("A mandatory, optional, completed or unavailable task cannot receive this conditional applicability proposal.");

    var receipt = data.Receipts.GetValueOrDefault(receiptId);
    if (receipt is null || receipt.ClientId != binding.ClientId || receipt.EngagementId != binding.EngagementId)
      return CommandResult<WorkflowTaskApplicabilityBasis>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var request = data.Requests.GetValueOrDefault(receipt.PbcRequestId);
    var upload = data.Uploads.GetValueOrDefault(receipt.PbcUploadIntentId);
    if (request is null || request.ClientId != binding.ClientId || request.EngagementId != binding.EngagementId ||
        upload is null || upload.ClientId != binding.ClientId || upload.EngagementId != binding.EngagementId || upload.PbcRequestId != receipt.PbcRequestId)
      return Blocked("The accepted source no longer has the exact task scope.");
    // The existing PBC lifecycle closes an acceptance in one revision without accepting another file.
    var currentRequest = request is not null &&
      (request.State == PbcStates.Accepted && request.Revision == receipt.AcceptedRequestRevision ||
       request.State == PbcStates.Closed && receipt.AcceptedRequestRevision < long.MaxValue &&
       request.Revision == receipt.AcceptedRequestRevision + 1);
    if (!currentRequest || request is null || upload is null ||
        request.PeriodStart != engagement.PeriodStart || request.PeriodEnd != engagement.PeriodEnd ||
        request.ReviewerUserId != receipt.ReviewerUserId || request.AcceptedByUserId != receipt.ReviewerUserId ||
        request.AcceptedAt != receipt.AcceptedAt || receipt.ReviewerUserId == receipt.UploaderUserId ||
        upload.State != PbcUploadStates.Received || upload.Revision != receipt.UploadIntentRevision ||
        upload.RequestRevision != receipt.SourceRequestRevision || upload.RequestRevision < 1 ||
        upload.UploaderUserId != receipt.UploaderUserId || upload.FileName != receipt.FileName ||
        upload.DeclaredByteCount != receipt.ByteCount || upload.ReceivedByteCount != receipt.ByteCount ||
        upload.CompletedAt is null || upload.TransferOperationId != receipt.TransferOperationId ||
        upload.ProviderRegisteredAt != receipt.ProviderRegisteredAt ||
        upload.FinalSha256Hex != receipt.ContentSha256Hex || upload.DeclaredSha256Hex != receipt.ContentSha256Hex ||
        upload.ProviderReceiptDigest != receipt.ProviderReceiptDigest || receipt.ProviderReceiptDigest != receipt.ContentSha256Hex)
      return Blocked("The exact independently accepted PBC file is no longer current for this engagement period.");
    if (data.LatestUploads.GetValueOrDefault(request.Id) != upload.Id || data.PendingRequests.Contains(request.Id))
      return Blocked("A later or unfinished submission requires a new source review.");
    var transfer = data.Transfers.GetValueOrDefault(receipt.TransferOperationId);
    if (transfer is null || transfer.ClientId != binding.ClientId || transfer.EngagementId != binding.EngagementId ||
        transfer.TargetId != upload.Id || transfer.OperationKind != PbcDocumentTransferHandler.Kind)
      return Blocked("The exact provider transfer is unavailable.");
    if (transfer?.Status != OperationState.COMPLETED || transfer.ExpectedRevision != upload.Revision - 1 ||
        transfer.ResultIdentity != receipt.ProviderIdentity || transfer.ResultDigest != receipt.ContentSha256Hex ||
        receipt.SourceDigest != AcceptedSourceDigest(receipt))
      return Blocked("The retained provider registration and accepted-source digest must match the exact file receipt.");

    var rules = data.Rules.Where(x => x.WorkflowTaskBindingId == binding.Id).ToArray();
    if (rules.Length > WorkflowTaskEvidenceQuery.MaximumRules)
      return Blocked("The complete evidence basis exceeds the supported task bound.");
    var sourcePins = new List<object>();
    foreach (var rule in rules)
    {
      if (rule.ClientId != binding.ClientId || rule.EngagementId != binding.EngagementId ||
          !WorkflowEvidenceKinds.All.Contains(rule.Kind, StringComparer.Ordinal))
        return Blocked("An evidence reference has unsupported or inconsistent scope.");
      var key = (binding.ClientId, binding.EngagementId, rule.Kind, rule.SourceId);
      if (!data.Sources.TryGetValue(key, out var source))
      {
        source = await WorkflowTaskEvidenceQuery.EvaluateSourceAsync(db, rule, ct);
        data.Sources.Add(key, source);
      }
      sourcePins.Add(new { rule.Id, rule.RuleKey, rule.Version, rule.Kind, rule.State, rule.SourceId,
        source.Satisfied, source.Revision, source.Generation, EvaluatedSourceId = source.SourceId, source.SourceFingerprint });
    }
    if (!data.Generations.TryGetValue(binding.ClientId, out var generation))
      return Blocked("The client evidence generation is unavailable.");
    var conditionDigest = Hashing.Sha256Hex(step.ApplicabilityCondition);
    var json = JsonSerializer.Serialize(new
    {
      Version = "workflow-task-applicability.v1", binding.FirmId, binding.ClientId, binding.EngagementId,
      BindingId = binding.Id, binding.WorkTaskId, binding.Revision, binding.Generation, task.Status,
      task.Title, task.AssigneeUserId, task.DueDate, binding.CancellationReason,
      binding.PbcRequestId, binding.ReviewProcedureResultId, binding.TemplateRevisionId, binding.TemplateStepId,
      template.ContentDigest, TemplateVersion = template.Version, step.RequiredTask, step.ApplicabilityCondition, ConditionDigest = conditionDigest,
      engagement.ServiceRoute, engagement.ServiceProfileId, engagement.PeriodStart, engagement.PeriodEnd,
      EngagementGeneration = engagement.Generation, EngagementStatus = engagement.Status,
      engagement.ProfessionalWorkBlocked, InputGeneration = generation,
      AcceptedSource = new { receipt.Id, receipt.SourceDigest, receipt.ContentSha256Hex, receipt.PbcUploadIntentId,
        receipt.UploadIntentRevision, receipt.AcceptedRequestRevision, receipt.ReviewerUserId, receipt.AcceptedAt },
      RequestState = request.State, RequestRevision = request.Revision, Sources = sourcePins
    });
    if (json.Length > MaximumBasisCharacters) return Blocked("The complete source basis exceeds the supported review bound.");
    return CommandResult<WorkflowTaskApplicabilityBasis>.Ok(new(task.Id, step.ApplicabilityCondition,
      conditionDigest, json, Hashing.Sha256Hex(json), binding.Revision, binding.Generation, engagement.Generation));
  }

  // Same immutable v1 field names and order as PbcService's original source acceptance, with no new acceptance decision.
  private static string AcceptedSourceDigest(PbcAcceptanceReceipt r) => Hashing.Sha256Hex(JsonSerializer.Serialize(new
  {
    Version = "pbc-accepted-source-v1", r.FirmId, r.ClientId, r.EngagementId, RequestId = r.PbcRequestId,
    AcceptedRequestRevision = r.AcceptedRequestRevision, SourceRequestRevision = r.SourceRequestRevision,
    UploadIntentId = r.PbcUploadIntentId, UploadIntentRevision = r.UploadIntentRevision,
    r.UploaderUserId, r.FileName, r.ByteCount, r.ContentSha256Hex, r.TransferOperationId,
    r.ProviderIdentity, r.ProviderReceiptDigest, r.ProviderRegisteredAt, r.ReviewerUserId, r.AcceptedAt
  }));

  private static CommandResult<WorkflowTaskApplicabilityBasis> Blocked(string message) =>
    CommandResult<WorkflowTaskApplicabilityBasis>.Fail(ErrorCodes.GateBlocked, message);
}
