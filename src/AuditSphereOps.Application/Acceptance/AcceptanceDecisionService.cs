using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record RecordAcceptanceDecisionRequest(
  Guid PracticeClientId,
  Guid? EngagementId,
  string ServiceRoute,
  string Decision,
  string Rationale,
  string? Conditions,
  long ExpectedGeneration);

/// <summary>Persists professional acceptance evidence and the client workspace intent together.</summary>
public static class AcceptanceDecisionService
{
  private static readonly string[] PartnerRoles = ["Partner"];

  public static async Task<CommandResult<Guid>> RecordAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordAcceptanceDecisionRequest request,
    CancellationToken ct = default)
  {
    var decision = NormalizeDecision(request.Decision);
    if (request.PracticeClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.ServiceRoute) ||
        string.IsNullOrWhiteSpace(request.Rationale) || request.ExpectedGeneration < 1 || decision is null)
      return CommandResult<Guid>.Fail("acceptance.invalid", "Client, service, decision, rationale and current generation are required.");
    if (decision == "AcceptedWithConditions" && string.IsNullOrWhiteSpace(request.Conditions))
      return CommandResult<Guid>.Fail("acceptance.invalid", "Conditional acceptance requires conditions.");
    if (decision == "Accepted" && !string.IsNullOrWhiteSpace(request.Conditions))
      return CommandResult<Guid>.Fail("acceptance.invalid", "Unconditional acceptance cannot carry blocking conditions.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.PracticeClientId, request.EngagementId,
        RequiredRoles: PartnerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (!await AcceptanceCommandAuthority.LockFirmAsync(db, actor, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await db.PracticeClients.SingleOrDefaultAsync(x =>
      x.Id == request.PracticeClientId && x.FirmId == actor.FirmId, ct);
    if (client is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var clientGuard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {client.Id} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (clientGuard is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    auth = await AcceptanceCommandAuthority.CheckAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.PracticeClientId, request.EngagementId, RequiredRoles: PartnerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (clientGuard.InputGeneration != request.ExpectedGeneration)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The client evaluation changed; reload before recording the decision.");

    if (request.EngagementId.HasValue)
    {
      var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == request.EngagementId.Value && x.FirmId == actor.FirmId && x.PracticeClientId == client.Id, ct);
      if (engagement is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }

    var serviceRoute = request.ServiceRoute.Trim();
    var existing = await db.AcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == client.Id &&
                  x.EngagementId == request.EngagementId && x.ServiceRoute == serviceRoute &&
                  x.Generation == request.ExpectedGeneration && x.Decision != "Pending")
      .OrderByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
    if (existing is not null)
    {
      return existing.Decision == decision && string.Equals(existing.Rationale, request.Rationale.Trim(), StringComparison.Ordinal) &&
             string.Equals(existing.Conditions, TrimOrNull(request.Conditions), StringComparison.Ordinal)
        ? CommandResult<Guid>.Ok(existing.Id)
        : CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "A decision already exists for this client generation and service scope.");
    }

    // The path (new client vs continuance), the questions, evidence rules and adverse-answer clearances all come from
    // stored records at the locked generation; the request carries no path and cannot choose an easier checklist.
    var checklist = await AcceptanceChecklistService.LoadAsync(db, actor.FirmId, client.Id, request.EngagementId, clientGuard.InputGeneration, ct);
    if (decision is "Accepted" or "AcceptedWithConditions" && !checklist.Ready)
    {
      var shown = string.Join(" ", checklist.Blockers.Take(3).Select(x => x.Message));
      var more = checklist.Blockers.Count > 3 ? $" (+{checklist.Blockers.Count - 3} more)" : string.Empty;
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        $"Acceptance is blocked by {checklist.Blockers.Count} outstanding item(s) on the {(checklist.Path == AcceptancePaths.Continuance ? "continuance" : "new-client")} checklist. {shown}{more}");
    }

    var templateVersion = checklist.Items.Count == 0 ? "NONE" : string.Join("|", checklist.Items
      .Select(x => $"{x.Question.Bank}:{x.Question.TemplateVersion}").Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
      .Append(checklist.Path));
    var snapshot = string.Join("|", checklist.Items.Where(x => x.Answer is not null)
      .OrderBy(x => x.Question.Bank, StringComparer.Ordinal).ThenBy(x => x.Question.Code, StringComparer.Ordinal)
      .Select(x => $"{x.Question.Bank}:{x.Question.Code}:{x.Answer!.Revision}:{x.Answer.Answer}:{x.Answer.EvidenceReference}"));
    var acceptance = new AcceptanceDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = client.Id,
      EngagementId = request.EngagementId, Decision = decision, ServiceRoute = serviceRoute,
      Generation = clientGuard.InputGeneration, Rationale = request.Rationale.Trim(),
      Conditions = TrimOrNull(request.Conditions), Path = checklist.Path, PriorDecisionId = checklist.PriorDecision?.Id,
      EvaluationTemplateVersion = templateVersion,
      EvaluationSnapshotDigest = Digest(snapshot), DecidedByUserId = actor.UserId, DecidedAt = DateTimeOffset.UtcNow
    };
    db.AcceptanceDecisions.Add(acceptance);

    if (decision == "Accepted")
    {
      client.Status = "ACCEPTED";
      var workspace = await db.ClientWorkspaces.SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.PracticeClientId == client.Id && x.Purpose == "PRIMARY", ct);
      if (workspace is null)
      {
        db.ClientWorkspaces.Add(new ClientWorkspace
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = client.Id,
          AcceptanceDecisionId = acceptance.Id, Purpose = "PRIMARY",
          LogicalKey = $"client-workspace/{actor.FirmId:D}/{client.Id:D}",
          State = ClientWorkspaceStates.WaitingForIntegration, CreatedAt = DateTimeOffset.UtcNow
        });
      }
    }

    await db.SaveChangesAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.PracticeClientId, request.EngagementId, RequiredRoles: PartnerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(acceptance.Id);
  }

  private static string? NormalizeDecision(string value) => value.Trim().ToUpperInvariant() switch
  {
    "ACCEPTED" => "Accepted",
    "ACCEPTED_WITH_CONDITIONS" or "ACCEPTEDWITHCONDITIONS" => "AcceptedWithConditions",
    "DECLINED" => "Declined",
    "DEFERRED" => "Deferred",
    _ => null
  };

  private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
  private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
