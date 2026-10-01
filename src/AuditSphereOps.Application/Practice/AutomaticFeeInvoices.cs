using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record AutomaticFeeInvoicePolicy(bool Enabled, Guid FinanceUserId, Guid ApprovingAdministratorId);
internal sealed record FeeInvoiceIntent(Guid MilestoneId, Guid AgreementId, string Kind, Guid FinanceUserId, long FinanceEpoch, Guid AdministratorId, long AdministratorEpoch);

/// <summary>Standing authorization creates drafts only. Existing independent approval, posting and mail gates remain mandatory.</summary>
public sealed class AutomaticFeeInvoiceHandler(AutomaticFeeInvoicePolicy policy) : IOperationHandler
{
  public const string Kind = "DraftContractFeeInvoice.v1";
  public OperationDefinition Definition { get; } = new(Kind, OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION);

  public string NormalizePayload(OperationRequest request)
  {
    try
    {
      var intent = JsonSerializer.Deserialize<FeeInvoiceIntent>(request.PayloadJson);
      if (!policy.Enabled || intent is null || intent.MilestoneId != request.TargetId || request.ExpectedRevision != 1 || request.ClientId is null ||
          intent.AgreementId == Guid.Empty || intent.Kind is not (FeeMilestoneKinds.Advance or FeeMilestoneKinds.Balance) ||
          intent.FinanceUserId != policy.FinanceUserId || intent.AdministratorId != policy.ApprovingAdministratorId ||
          intent.FinanceEpoch < 1 || intent.AdministratorEpoch < 1 || request.OriginatorId != policy.FinanceUserId)
        throw new OperationBlockedException("fee-invoice-policy-or-payload-invalid", authorization: true);
      return JsonSerializer.Serialize(intent);
    }
    catch (JsonException) { throw new OperationBlockedException("fee-invoice-payload-invalid"); }
  }

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
  {
    var intent = JsonSerializer.Deserialize<FeeInvoiceIntent>(op.PayloadJson)!;
    var finance = new ActorContext(intent.FinanceUserId, op.FirmId, intent.FinanceEpoch, ["FinanceManager"]);
    var administrator = new ActorContext(intent.AdministratorId, op.FirmId, intent.AdministratorEpoch, ["Administrator"]);
    if (!policy.Enabled || op.OriginatorId != policy.FinanceUserId || intent.FinanceUserId != policy.FinanceUserId || intent.AdministratorId != policy.ApprovingAdministratorId ||
        !(await AuthorizationDecision.AuthorizeAsync(db, finance, new(op.FirmId, RequiredRoles: ["FinanceManager"], InternalOnly: true, RequireFirmWide: true), ct)).Succeeded ||
        !(await AuthorizationDecision.AuthorizeAsync(db, administrator, new(op.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true, RequireFirmWide: true), ct)).Succeeded)
      throw new OperationBlockedException("fee-invoice-standing-authority-revoked", authorization: true);
    var milestone = await db.FeeMilestones.FromSqlInterpolated($"SELECT * FROM fee_milestones WHERE id = {op.TargetId} AND firm_id = {op.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == op.FirmId && x.Id == intent.AgreementId && x.PracticeClientId == op.ClientId && x.EngagementId == op.EngagementId, ct);
    if (milestone is null || agreement is null || milestone.AgreementId != agreement.Id || milestone.Kind != intent.Kind)
      throw new OperationBlockedException("fee-invoice-scope-mismatch", authorization: true);
    if (intent.Kind == FeeMilestoneKinds.Advance && !await HasCurrentLetterAsync(db, agreement, ct))
      throw new OperationBlockedException("fee-invoice-letter-approval-stale");
    if (intent.Kind == FeeMilestoneKinds.Balance && !await db.Releases.AnyAsync(x => x.FirmId == op.FirmId && x.ClientId == op.ClientId && x.EngagementId == op.EngagementId &&
        db.ReleaseCandidates.Any(c => c.FirmId == op.FirmId && c.Id == x.ReleaseCandidateId && c.TargetKind == ReleaseTargetKinds.FinancialPackage), ct))
      throw new OperationBlockedException("fee-invoice-final-package-not-released");
  }

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op, OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    var intent = JsonSerializer.Deserialize<FeeInvoiceIntent>(op.PayloadJson)!;
    var actor = new ActorContext(intent.FinanceUserId, op.FirmId, intent.FinanceEpoch, ["FinanceManager"]);
    var draft = intent.Kind == FeeMilestoneKinds.Advance
      ? await FeeAgreementService.IssueAdvanceInvoiceAsync(db, actor, intent.AgreementId, ct)
      : await FeeAgreementService.IssueBalanceInvoiceAsync(db, actor, intent.AgreementId, ct);
    if (!draft.Succeeded) throw new OperationBlockedException("fee-invoice-draft-gate-blocked", draft.ErrorCode == ErrorCodes.ScopeDenied);
    db.OperationEvents.Add(new OperationEvent { Id = Guid.CreateVersion7(), OperationId = op.Id, Token = op.AttemptToken, Kind = "fee.invoice-draft.created.v1", Executor = op.LeaseOwner!, OccurredAt = DateTimeOffset.UtcNow });
    return new(draft.Value.ToString("D"), Hashing.Sha256Hex($"fee-invoice:{intent.MilestoneId:D}:{draft.Value:D}"));
  }

  public Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct) => throw new OperationBlockedException("local-operation-has-no-provider-effect");
  public Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct) => throw new OperationBlockedException("local-operation-has-no-provider-effect");

  internal static Task<bool> HasCurrentLetterAsync(IAuditSphereDbContext db, EngagementFeeAgreement agreement, CancellationToken ct) =>
    db.CommercialDocuments.AnyAsync(d => d.FirmId == agreement.FirmId && d.ProposalId == agreement.ProposalId && d.QuotationVersionId == agreement.QuotationVersionId && d.Kind == CommercialDocumentKinds.EngagementLetter &&
      d.AcceptanceDecisionId != null && db.AcceptanceDecisions.Any(a => a.Id == d.AcceptanceDecisionId && a.FirmId == agreement.FirmId && a.PracticeClientId == agreement.PracticeClientId && a.Decision == "Accepted" && a.EngagementId == null && a.DecidedAt != null && a.DecidedByUserId != null &&
        !db.AcceptanceDecisions.Any(n => n.FirmId == a.FirmId && n.PracticeClientId == a.PracticeClientId && n.ServiceRoute == a.ServiceRoute && n.EngagementId == null && n.Decision != "Pending" &&
          (n.Generation > a.Generation || n.Generation == a.Generation && n.DecidedAt > a.DecidedAt)) &&
        db.ClientSafetyStates.Any(g => g.FirmId == a.FirmId && g.Id == a.PracticeClientId && g.InputGeneration == a.Generation)), ct);
}

public sealed class AutomaticFeeInvoiceDiscovery(IAuditSphereDbContextFactory factory, IOperationStore store, AutomaticFeeInvoiceHandler handler, WorkerOptions options,
  AutomaticFeeInvoicePolicy policy) : IPendingOperationDiscovery
{
  public async Task<int> EnqueuePendingAsync(CancellationToken ct)
  {
    if (!policy.Enabled) return 0;
    await using var read = await factory.CreateAsync(ct);
    var finance = await read.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == policy.FinanceUserId && x.FirmId == options.FirmId && !x.Disabled, ct);
    var admin = await read.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == policy.ApprovingAdministratorId && x.FirmId == options.FirmId && !x.Disabled, ct);
    if (finance is null || admin is null ||
        !(await AuthorizationDecision.AuthorizeAsync(read, new(finance.Id, options.FirmId, finance.SessionEpoch, ["FinanceManager"]), new(options.FirmId, RequiredRoles: ["FinanceManager"], InternalOnly: true, RequireFirmWide: true), ct)).Succeeded ||
        !(await AuthorizationDecision.AuthorizeAsync(read, new(admin.Id, options.FirmId, admin.SessionEpoch, ["Administrator"]), new(options.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true, RequireFirmWide: true), ct)).Succeeded) return 0;
    var milestones = await read.FeeMilestones.AsNoTracking().Where(x => x.FirmId == options.FirmId && x.InvoiceId == null &&
      !read.DurableOperations.Any(o => o.FirmId == x.FirmId && o.TargetId == x.Id && o.OperationKind == AutomaticFeeInvoiceHandler.Kind &&
        o.Status != OperationState.AUTHORIZATION_BLOCKED && o.Status != OperationState.PROVIDER_BLOCKED && o.Status != OperationState.DEAD_LETTER))
      .OrderBy(x => x.CreatedAt).Take(25).ToListAsync(ct);
    var count = 0;
    foreach (var milestone in milestones)
    {
      var agreement = await read.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.Id == milestone.AgreementId && x.FirmId == options.FirmId, ct);
      if (milestone.Kind == FeeMilestoneKinds.Advance && !await AutomaticFeeInvoiceHandler.HasCurrentLetterAsync(read, agreement, ct)) continue;
      if (milestone.Kind == FeeMilestoneKinds.Balance && (agreement.EngagementId is null ||
          !await read.FeeMilestones.AnyAsync(x => x.FirmId == options.FirmId && x.AgreementId == agreement.Id && x.Kind == FeeMilestoneKinds.Advance && x.State == FeeMilestoneStates.Paid, ct) ||
          !await read.Releases.AnyAsync(x => x.FirmId == options.FirmId && x.ClientId == agreement.PracticeClientId && x.EngagementId == agreement.EngagementId &&
            read.ReleaseCandidates.Any(c => c.FirmId == options.FirmId && c.Id == x.ReleaseCandidateId && c.TargetKind == ReleaseTargetKinds.FinancialPackage), ct))) continue;
      var intent = new FeeInvoiceIntent(milestone.Id, agreement.Id, milestone.Kind, finance.Id, finance.SessionEpoch, admin.Id, admin.SessionEpoch);
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var result = await store.EnqueueAsync(db, new(options.FirmId, agreement.PracticeClientId, agreement.EngagementId, AutomaticFeeInvoiceHandler.Kind, milestone.Id, 1,
        $"auto-fee:{milestone.Id:D}:{finance.SessionEpoch}:{admin.SessionEpoch}", JsonSerializer.Serialize(intent), finance.Id), handler, ct);
      if (!result.Succeeded) continue;
      await tx.CommitAsync(ct); count++;
    }
    return count;
  }
}
