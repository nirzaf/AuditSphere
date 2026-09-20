using System.Text;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AuditSphereOps.Application.Operations;

public interface IAuditSphereDbContext : IAsyncDisposable
{
  DbSet<DurableOperation> DurableOperations { get; }
  DbSet<OperationAttempt> OperationAttempts { get; }
  DbSet<OperationEvent> OperationEvents { get; }
  DbSet<FirmSafetyState> FirmSafetyStates { get; }
  DbSet<RecoverySession> RecoverySessions { get; }
  DbSet<ClientSafetyState> ClientSafetyStates { get; }
  DbSet<TrialBalanceDataset> TrialBalanceDatasets { get; }
  DbSet<TrialBalanceRow> TrialBalanceRows { get; }
  DbSet<MappingVersion> MappingVersions { get; }
  DbSet<MappingRule> MappingRules { get; }
  DbSet<MappingAllocation> MappingAllocations { get; }
  DbSet<AdjustedTrialBalanceSnapshot> AdjustedTrialBalanceSnapshots { get; }
  DbSet<AdjustedTrialBalanceRow> AdjustedTrialBalanceRows { get; }
  DbSet<AdjustmentJournal> AdjustmentJournals { get; }
  DbSet<AdjustmentLine> AdjustmentLines { get; }
  DbSet<JournalSourceReconciliation> JournalSourceReconciliations { get; }
  DbSet<AdjustmentPlan> AdjustmentPlans { get; }
  DbSet<AdjustmentPlanLine> AdjustmentPlanLines { get; }
  DbSet<FinancialPackage> FinancialPackages { get; }
  DbSet<FinancialPackageLine> FinancialPackageLines { get; }
  DbSet<FinancialPackageValidation> FinancialPackageValidations { get; }
  DbSet<FinancialPackageCashFlowLine> FinancialPackageCashFlowLines { get; }
  DbSet<FinancialPackageDisclosure> FinancialPackageDisclosures { get; }

  DbSet<Engagement> Engagements { get; }
  DbSet<EngagementHold> EngagementHolds { get; }
  DbSet<RepositoryBinding> RepositoryBindings { get; }
  DbSet<SyncCursor> SyncCursors { get; }
  DbSet<IntegrationCapability> IntegrationCapabilities { get; }
  DbSet<DocumentReference> DocumentReferences { get; }
  DbSet<DocumentSnapshot> DocumentSnapshots { get; }
  DbSet<PbcRequest> PbcRequests { get; }
  DbSet<PbcUploadIntent> PbcUploadIntents { get; }
  DbSet<PbcUploadChunk> PbcUploadChunks { get; }
  DbSet<Workpaper> Workpapers { get; }
  DbSet<WorkpaperDraft> WorkpaperDrafts { get; }
  DbSet<WorkpaperSubmission> WorkpaperSubmissions { get; }
  DbSet<MaterialityAssessment> MaterialityAssessments { get; }
  DbSet<PopulationVersion> PopulationVersions { get; }
  DbSet<AuditRisk> AuditRisks { get; }
  DbSet<AuditProcedure> AuditProcedures { get; }
  DbSet<Finding> Findings { get; }
  DbSet<ReviewPoint> ReviewPoints { get; }
  DbSet<Approval> Approvals { get; }
  DbSet<ApprovalApplicability> ApprovalApplicabilities { get; }
  DbSet<ReleaseCandidate> ReleaseCandidates { get; }
  DbSet<Release> Releases { get; }
  DbSet<ReleaseCheckpoint> ReleaseCheckpoints { get; }
  DbSet<SignatureLineage> SignatureLineages { get; }
  DbSet<ProtectionAttestation> ProtectionAttestations { get; }
  DbSet<Archive> Archives { get; }
  DbSet<RecordsProfile> RecordsProfiles { get; }
  DbSet<ArchiveManifest> ArchiveManifests { get; }
  DbSet<ArchiveManifestEntry> ArchiveManifestEntries { get; }
  DbSet<ArchiveStructuredExport> ArchiveStructuredExports { get; }
  DbSet<RecordsActionEvidence> RecordsActionEvidences { get; }
  DbSet<RecordsAction> RecordsActions { get; }
  DbSet<LegalHold> LegalHolds { get; }
  DbSet<EqrCase> EqrCases { get; }
  DbSet<WrittenRepresentation> WrittenRepresentations { get; }
  DbSet<EngagementAssignment> EngagementAssignments { get; }
  DbSet<SpecialistClearance> SpecialistClearances { get; }
  DbSet<QuestionnaireTemplate> QuestionnaireTemplates { get; }
  DbSet<QuestionDefinition> QuestionDefinitions { get; }
  DbSet<SourceReceipt> SourceReceipts { get; }
  DbSet<EvidenceLink> EvidenceLinks { get; }
  DbSet<EvaluationResponse> EvaluationResponses { get; }
  DbSet<AcceptanceDecision> AcceptanceDecisions { get; }
  DbSet<Lead> Leads { get; }
  DbSet<Opportunity> Opportunities { get; }
  DbSet<Proposal> Proposals { get; }
  DbSet<PracticeClient> PracticeClients { get; }
  DbSet<ClientContact> ClientContacts { get; }
  DbSet<WorkTask> WorkTasks { get; }
  DbSet<TimeEntry> TimeEntries { get; }
  DbSet<RateCardVersion> RateCardVersions { get; }
  DbSet<EngagementBudget> EngagementBudgets { get; }
  DbSet<BudgetLine> BudgetLines { get; }
  DbSet<BillingAccount> BillingAccounts { get; }
  DbSet<FirmFinanceProfile> FirmFinanceProfiles { get; }
  DbSet<Invoice> Invoices { get; }
  DbSet<InvoiceLine> InvoiceLines { get; }
  DbSet<Receipt> Receipts { get; }
  DbSet<ReceiptAllocation> ReceiptAllocations { get; }
  DbSet<CreditNote> CreditNotes { get; }
  DbSet<BillingSourceAllocation> BillingSourceAllocations { get; }
  DbSet<FirmAccount> FirmAccounts { get; }
  DbSet<FirmPeriod> FirmPeriods { get; }
  DbSet<FirmJournal> FirmJournals { get; }
  DbSet<FirmJournalLine> FirmJournalLines { get; }
  DbSet<FirmPosting> FirmPostings { get; }
  DbSet<FirmPostingLine> FirmPostingLines { get; }
  DbSet<LedgerSourceLink> LedgerSourceLinks { get; }
  DbSet<LedgerPostingReceipt> LedgerPostingReceipts { get; }
  DbSet<PeriodCloseDecision> PeriodCloseDecisions { get; }
  DbSet<AppUser> Users { get; }
  DbSet<RoleGrant> RoleGrants { get; }
  DatabaseFacade Database { get; }
  Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IAuditSphereDbContextFactory
{
  Task<IAuditSphereDbContext> CreateAsync(CancellationToken ct = default);
}

public sealed record OperationRequest(Guid FirmId, Guid? ClientId, Guid? EngagementId,
  string Kind, Guid TargetId, long ExpectedRevision, string IdempotencyKey,
  string PayloadJson, Guid? OriginatorId = null);

public sealed record OperationResult(string Identity, string Digest);
public sealed record OperationDefinition(string Kind, OperationMode Mode, OperationAuthority Authority,
  int SchemaVersion = 1, string Group = "general");

public sealed record WorkerOptions(Guid FirmId, string EnvironmentName = "Development",
  bool AllowSimulationAdapters = false, bool ExternalEffectsEnabled = false,
  string Group = "general", long DeploymentEpoch = 1, int LeaseSeconds = 60,
  int RenewalSeconds = 20, int MaxAttempts = 5)
{
  public void Validate(IEnumerable<OperationDefinition> definitions)
  {
    if (FirmId == Guid.Empty || DeploymentEpoch < 1 || string.IsNullOrWhiteSpace(Group) ||
        LeaseSeconds < 3 || RenewalSeconds < 1 || RenewalSeconds >= LeaseSeconds || MaxAttempts is < 1 or > 20)
      throw new InvalidOperationException("Invalid worker scope or lease configuration.");
    if (EnvironmentName is not ("Development" or "Test") || ExternalEffectsEnabled)
      throw new InvalidOperationException("Live operation execution is not implemented or approved.");
    if (definitions.Any(d => !Enum.IsDefined(d.Mode) || !Enum.IsDefined(d.Authority) ||
        string.IsNullOrWhiteSpace(d.Kind) || string.IsNullOrWhiteSpace(d.Group) || d.SchemaVersion < 1 ||
        (d.Mode == OperationMode.LOCAL && d.Authority != OperationAuthority.LOCAL_VALIDATION) ||
        (d.Mode == OperationMode.SIMULATED && d.Authority != OperationAuthority.SIMULATION) || d.Mode == OperationMode.LIVE ||
        (d.Mode == OperationMode.SIMULATED && (EnvironmentName != "Test" || !AllowSimulationAdapters))))
      throw new InvalidOperationException("Simulation handlers require explicit Test composition.");
  }
}

public interface IOperationHandler
{
  OperationDefinition Definition { get; }
  string NormalizePayload(OperationRequest request);
  // Called under firm/client guards, before locking the operation row.
  Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation operation, CancellationToken ct);
  // All local publication uses the supplied transaction. Never call a provider here.
  Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation operation,
    OperationResult? verifiedRemoteResult, CancellationToken ct);
  Task<OperationResult> ExecuteEffectAsync(DurableOperation operation, CancellationToken ct);
  Task<OperationResult> ReconcileAsync(DurableOperation operation, CancellationToken ct);
}

public sealed class OperationBlockedException(string code, bool authorization = false) : Exception(code)
{
  public string Code { get; } = code;
  public bool Authorization { get; } = authorization;
}

// Only handlers that can prove no effect occurred may raise this exception.
public sealed class SafeRetryException(TimeSpan? retryAfter = null) : Exception("safe-retry")
{
  public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class OperationOwnershipLostException() : Exception("operation-ownership-lost");

public interface IOperationStore
{
  Task<CommandResult<Guid>> EnqueueAsync(IAuditSphereDbContext db, OperationRequest request,
    IOperationHandler handler, CancellationToken ct);
  Task<DurableOperation?> ClaimAsync(WorkerOptions options, IReadOnlyList<OperationDefinition> definitions,
    string owner, bool reconciliation, CancellationToken ct);
  Task<bool> ValidateAsync(DurableOperation op, WorkerOptions options, IOperationHandler handler, CancellationToken ct);
  Task<bool> RenewAsync(DurableOperation op, WorkerOptions options, CancellationToken ct);
  Task<bool> TransitionAsync(DurableOperation op, WorkerOptions options, OperationState next,
    string? error, TimeSpan? delay, CancellationToken ct);
  Task<bool> CompleteAsync(DurableOperation op, WorkerOptions options, IOperationHandler handler,
    OperationResult? remote, CancellationToken ct);
  Task<int> ReapAsync(WorkerOptions options, CancellationToken ct);
}

public sealed class DurableOperationRegistry
{
  private readonly IReadOnlyDictionary<string, IOperationHandler> handlers;
  public DurableOperationRegistry(IEnumerable<IOperationHandler> handlers, WorkerOptions options)
  {
    this.handlers = handlers.ToDictionary(h => h.Definition.Kind, StringComparer.Ordinal);
    options.Validate(Definitions);
  }
  public IReadOnlyList<OperationDefinition> Definitions => handlers.Values.Select(h => h.Definition).ToArray();
  public IOperationHandler Resolve(string kind) => handlers.TryGetValue(kind, out var handler)
    ? handler : throw new OperationBlockedException("unsupported-operation");
}

public static class OperationEncoding
{
  // Versioned length-prefixed UTF-8 fields; not a document/approval manifest format.
  public static byte[] Encode(OperationRequest r, OperationDefinition d, string payload)
  {
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
    foreach (var field in new[] { "operation-request.v1", r.FirmId.ToString("D"),
      r.ClientId?.ToString("D") ?? "", r.EngagementId?.ToString("D") ?? "", d.Kind,
      d.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), d.Group,
      d.Mode.ToString(), d.Authority.ToString(), r.TargetId.ToString("D"),
      r.ExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
      r.OriginatorId?.ToString("D") ?? "", payload }) writer.Write(field);
    return stream.ToArray();
  }

  public static bool Matches(DurableOperation op, IOperationHandler handler)
  {
    var d = handler.Definition;
    var request = new OperationRequest(op.FirmId, op.ClientId, op.EngagementId, op.OperationKind,
      op.TargetId, op.ExpectedRevision, op.IdempotencyKey, op.PayloadJson, op.OriginatorId);
    var payload = handler.NormalizePayload(request);
    var bytes = Encode(request, d, payload);
    return op.SchemaVersion == d.SchemaVersion && op.ExecutionGroup == d.Group &&
      op.ExecutionMode == d.Mode && op.AuthorityMode == d.Authority &&
      op.RequestBytes.AsSpan().SequenceEqual(bytes) && Hashing.Sha256Hex(bytes) == op.RequestDigest;
  }
}
