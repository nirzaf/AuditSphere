using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditFieldworkService
{
  public static async Task<CommandResult<ConfirmationValue>> CreateConfirmationAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateConfirmationRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.AreaCode) || string.IsNullOrWhiteSpace(request.SourceRecordId) ||
        string.IsNullOrWhiteSpace(request.Respondent) || string.IsNullOrWhiteSpace(request.ContactValidationSource) || !IsCurrency(request.Currency))
      return Invalid<ConfirmationValue>("A confirmation requires a record, respondent, validated contact source and currency.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.ProcedureId is not null && !await IsApplicableProcedureAsync(db, actor.FirmId, auth.ClientId, request.EngagementId, request.ProcedureId.Value, ct))
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "The linked procedure is not applicable.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var confirmation = new AuditConfirmationCase
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ProcedureId = request.ProcedureId, AreaCode = request.AreaCode.Trim().ToUpperInvariant(), SourceRecordId = request.SourceRecordId.Trim(),
      BookedAmount = request.BookedAmount, Currency = request.Currency.ToUpperInvariant(), ConfirmationDate = request.ConfirmationDate,
      Respondent = request.Respondent.Trim(), ContactValidationSource = request.ContactValidationSource.Trim(),
      InputGeneration = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditConfirmationCases.Add(confirmation);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(confirmation.Id, confirmation.Status));
  }

  public static async Task<CommandResult<ConfirmationValue>> ApproveConfirmationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid confirmationCaseId, CancellationToken ct = default) =>
    await SetConfirmationStatusAsync(db, actor, confirmationCaseId, AuditConfirmationStatuses.Approved, ReviewRoles, ct);

  public static async Task<CommandResult<ConfirmationValue>> RecordDispatchEvidenceAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordConfirmationDispatchRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.DispatchReference))
      return Invalid<ConfirmationValue>("Dispatch requires an observed provider reference.");
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditConfirmationCases.SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    if (live is null || live.Status != AuditConfirmationStatuses.Approved)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "Only an approved confirmation can record dispatch evidence.");
    live.DispatchReference = request.DispatchReference.Trim();
    live.Status = AuditConfirmationStatuses.Dispatched;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(live.Id, live.Status));
  }

  public static async Task<CommandResult<ConfirmationValue>> RecordConfirmationResponseAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordConfirmationResponseRequest request, CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditConfirmationDecisions.Agreed or AuditConfirmationDecisions.Difference or AuditConfirmationDecisions.NoResponse or AuditConfirmationDecisions.AlternativeRequired) ||
        string.IsNullOrWhiteSpace(request.Origin) || string.IsNullOrWhiteSpace(request.Channel) ||
        string.IsNullOrWhiteSpace(request.ResponseReference) || string.IsNullOrWhiteSpace(request.AuthenticityAssessment))
      return Invalid<ConfirmationValue>("A response requires provenance, a receipt reference, authenticity assessment and an explicit decision.");
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditConfirmationCases.SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    if (live is null || live.Status != AuditConfirmationStatuses.Dispatched)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "A response must follow observed dispatch evidence.");
    var revision = (await db.AuditConfirmationResponses.AsNoTracking().Where(x => x.ConfirmationCaseId == live.Id).MaxAsync(x => (long?)x.Revision, ct) ?? 0) + 1;
    var response = new AuditConfirmationResponse
    {
      Id = Guid.CreateVersion7(), FirmId = live.FirmId, ClientId = live.ClientId, EngagementId = live.EngagementId,
      ConfirmationCaseId = live.Id, Revision = revision, Origin = request.Origin.Trim(), Channel = request.Channel.Trim(),
      ReceivedAt = DateTimeOffset.UtcNow, ResponseReference = request.ResponseReference.Trim(), ConfirmedAmount = request.ConfirmedAmount,
      DifferenceAmount = request.ConfirmedAmount.HasValue ? request.ConfirmedAmount.Value - live.BookedAmount : null,
      AuthenticityAssessment = request.AuthenticityAssessment.Trim(), Decision = decision, CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditConfirmationResponses.Add(response);
    live.Status = decision switch
    {
      AuditConfirmationDecisions.NoResponse => AuditConfirmationStatuses.NoResponse,
      AuditConfirmationDecisions.AlternativeRequired => AuditConfirmationStatuses.AlternativeRequired,
      _ => AuditConfirmationStatuses.ResponseReceived
    };
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(live.Id, live.Status));
  }

  public static async Task<CommandResult> ReviewConfirmationResponseAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewConfirmationResponseRequest request, CancellationToken ct = default)
  {
    var response = await db.AuditConfirmationResponses.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ConfirmationResponseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, response, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (response!.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The response preparer cannot review the same response.");
    var latestRevision = await db.AuditConfirmationResponses.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ConfirmationCaseId == response.ConfirmationCaseId)
      .MaxAsync(x => (long?)x.Revision, ct);
    if (latestRevision != response.Revision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Only the current confirmation response can be reviewed.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditConfirmationResponses.SingleAsync(x => x.Id == response.Id && x.FirmId == actor.FirmId, ct);
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<ConfirmationValue>> RecordAlternativeProcedureAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordAlternativeProcedureRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Purpose) || request.EvidenceReferences is null || request.EvidenceReferences.Count == 0 ||
        request.EvidenceReferences.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(request.Conclusion))
      return Invalid<ConfirmationValue>("Alternative work requires purpose, evidence and a conclusion.");
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (existing is null || existing.Status is not (AuditConfirmationStatuses.NoResponse or AuditConfirmationStatuses.AlternativeRequired))
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "Alternative work requires an outstanding or non-response confirmation.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var alternative = new AuditAlternativeProcedure
    {
      Id = Guid.CreateVersion7(), FirmId = existing.FirmId, ClientId = existing.ClientId, EngagementId = existing.EngagementId,
      ConfirmationCaseId = existing.Id, Purpose = request.Purpose.Trim(), EvidenceReferencesJson = JsonSerializer.Serialize(request.EvidenceReferences),
      Conclusion = request.Conclusion.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditAlternativeProcedures.Add(alternative);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(existing.Id, existing.Status));
  }

  public static async Task<CommandResult> ReviewAlternativeProcedureAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewAlternativeProcedureRequest request, CancellationToken ct = default)
  {
    var alternative = await db.AuditAlternativeProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.AlternativeProcedureId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, alternative, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var live = await db.AuditAlternativeProcedures.SingleOrDefaultAsync(x => x.Id == request.AlternativeProcedureId && x.FirmId == actor.FirmId, ct);
    if (live is null)
      return Denied();
    if (live.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same alternative procedure.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    live.Status = AuditAlternativeStatuses.Reviewed;
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<ConfirmationValue>> CloseConfirmationAsync(
    IAuditSphereDbContext db, ActorContext actor, CloseConfirmationRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Conclusion))
      return Invalid<ConfirmationValue>("A confirmation close requires a reviewer conclusion.");
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ConfirmationCaseId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (existing is null || existing.Status == AuditConfirmationStatuses.Draft)
      return Denied<ConfirmationValue>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var latestResponse = await db.AuditConfirmationResponses.AsNoTracking()
      .Where(x => x.ConfirmationCaseId == existing.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (latestResponse is not null && latestResponse.ReviewedByUserId is null)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "The confirmation response requires independent review before closure.");
    var hasResponse = latestResponse is not null;
    var hasAlternative = await db.AuditAlternativeProcedures.AnyAsync(x => x.ConfirmationCaseId == existing.Id && x.Status == AuditAlternativeStatuses.Reviewed, ct);
    if (!hasResponse && !hasAlternative)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.GateBlocked, "A confirmation cannot close without a response or reviewed alternative work.");
    var live = await db.AuditConfirmationCases.SingleAsync(x => x.Id == existing.Id && x.FirmId == actor.FirmId, ct);
    live.Status = AuditConfirmationStatuses.Closed;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(live.Id, live.Status));
  }

  private static async Task<CommandResult<ConfirmationValue>> SetConfirmationStatusAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid id, string status, IReadOnlyList<string> roles, CancellationToken ct)
  {
    var existing = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, roles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConfirmationValue>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditConfirmationCases.SingleAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (status == AuditConfirmationStatuses.Approved && live.Status != AuditConfirmationStatuses.Draft)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.ProtectedState, "The confirmation is already beyond draft.");
    if (status == AuditConfirmationStatuses.Approved && live.CreatedByUserId == actor.UserId)
      return CommandResult<ConfirmationValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot approve the same confirmation.");
    live.Status = status;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ConfirmationValue>.Ok(new(live.Id, live.Status));
  }
}
