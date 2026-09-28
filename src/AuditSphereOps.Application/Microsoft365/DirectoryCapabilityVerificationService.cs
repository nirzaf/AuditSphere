using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

/// <summary>Records a bounded provider read for the current connection revision, never consent or activation.</summary>
public static class DirectoryCapabilityVerificationService
{
  public static async Task<CommandResult> VerifyAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftDirectoryReader reader,
    string configuredTenantId, string readerClientId, DateTimeOffset now,
    CancellationToken ct = default)
  {
    var auth = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
      InternalOnly: true, RequireFirmWide: true);
    if (!Guid.TryParse(configuredTenantId, out var tenant) ||
        !Guid.TryParse(readerClientId, out var clientId) ||
        !(await AuthorizationDecision.AuthorizeAsync(db, actor, auth, ct)).Succeeded)
      return Denied();
    var administrator = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == actor.UserId && !x.Disabled, ct);
    if (administrator is null || !Guid.TryParse(administrator.Subject, out var objectId) ||
        !string.Equals(administrator.TenantId, tenant.ToString("D"), StringComparison.OrdinalIgnoreCase))
      return Denied();
    var draft = await db.Microsoft365SetupDrafts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ExpectedTenantId == tenant.ToString("D"))
      .OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
    if (draft?.ConnectionRevisionId is not { } revisionId ||
        !await db.Microsoft365ConnectionRevisions.AsNoTracking().AnyAsync(x =>
          x.FirmId == actor.FirmId && x.Id == revisionId &&
          x.TenantId == tenant.ToString("D") &&
          x.State != Microsoft365RevisionStates.Active, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Prepare the exact tenant connection before verification.");

    var passed = false;
    try
    {
      var result = await reader.GetByIdAsync(tenant.ToString("D"), objectId.ToString("D"), ct);
      passed = string.Equals(result.TenantId, tenant.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(result.ObjectId, objectId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
        result.AccountEnabled && result.UserType == "Member";
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch { /* The public result remains blocked; no provider detail or token is stored. */ }

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var currentConnection = await db.Microsoft365ConnectionRevisions.FromSqlInterpolated($"""
      SELECT * FROM m365_connection_revisions
      WHERE firm_id = {actor.FirmId} AND id = {revisionId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, auth, ct)).Succeeded ||
        currentConnection is null || currentConnection.State == Microsoft365RevisionStates.Active ||
        !await db.Microsoft365SetupDrafts.AsNoTracking().AnyAsync(x =>
          x.FirmId == actor.FirmId && x.Id == draft.Id &&
          x.ConnectionRevisionId == revisionId && x.ExpectedTenantId == tenant.ToString("D"), ct))
      return Denied();
    db.IntegrationVerificationEvidences.Add(new IntegrationVerificationEvidence
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, SetupDraftId = draft.Id,
      ConnectionRevisionId = revisionId, ResourceKind = "DIRECTORY",
      ResourceId = tenant.ToString("D"), Operation = "EXACT_USER_READ",
      IdentityReference = clientId.ToString("D"),
      Result = passed ? "PASS" : "BLOCKED",
      EvidenceReference = passed ? "Graph exact-member read with sole User.Read.All app role" :
        "Graph exact-member read unavailable or inconsistent", ObservedAt = now
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return passed ? CommandResult.Ok() :
      CommandResult.Fail(ErrorCodes.GateBlocked, "Directory access could not be verified.");
  }

  private static CommandResult Denied() =>
    CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
}
