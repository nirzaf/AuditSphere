using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record TenantSetupFields(string? TenantDisplayName, string MailState, string RecordsState);
public sealed record TenantSetupEditRequest(Guid RequestId, Guid DraftId, string ExpectedRevision,
  TenantSetupFields Fields, bool Reviewed = false, string? RequestHash = null, string? ReviewBasis = null);
public sealed record TenantSetupPreview(Guid RequestId, Guid DraftId, string ExpectedRevision,
  string RequestHash, string ReviewBasis, TenantSetupFields Before, TenantSetupFields Fields);
public sealed record TenantSetupReceipt(Guid Id, Guid RequestId, Guid DraftId, string RequestHash,
  string AppliedRevision, string PreviousFingerprint, string AppliedFingerprint, DateTimeOffset RecordedAt);
public sealed record TenantSetupReceiptLookup(bool Found, TenantSetupReceipt? Receipt);

/// <summary>Local setup labels/states only. No consent, transport, credential or Microsoft permission is changed.</summary>
public static class TenantSetupMetadataWorkspace
{
  private const string Operation = "TENANT_SETUP_METADATA_SAVED";

  public static async Task<CommandResult<TenantSetupPreview>> PreviewAsync(IAuditSphereDbContext db,
    ActorContext actor, TenantSetupEditRequest input, string configuredTenant, CancellationToken ct = default)
  {
    var normalized = Normalize(input);
    if (normalized is null) return Invalid<TenantSetupPreview>();
    input = normalized;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await LockActorAsync(db, actor, ct)) return TenantAdministration.Denied<TenantSetupPreview>();
    var current = await CurrentDraftAsync(db, actor, input, configuredTenant, ct);
    if (!current.Succeeded) return CommandResult<TenantSetupPreview>.Fail(current.ErrorCode!, current.Message!);
    var draft = current.Value!.Draft;
    var preview = new TenantSetupPreview(input.RequestId, draft.Id, input.ExpectedRevision,
      IntentHash(actor, input), Basis(draft, current.Value.ConnectionState), Fields(draft), input.Fields);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<TenantSetupPreview>();
    await tx.CommitAsync(ct);
    return CommandResult<TenantSetupPreview>.Ok(preview);
  }

  public static async Task<CommandResult<TenantSetupReceipt>> SaveAsync(IAuditSphereDbContext db,
    ActorContext actor, TenantSetupEditRequest input, string configuredTenant, DateTimeOffset now, CancellationToken ct = default)
  {
    var normalized = Normalize(input);
    if (normalized is null || !input.Reviewed || !Hash(input.RequestHash) || !Hash(input.ReviewBasis)) return Invalid<TenantSetupReceipt>();
    input = normalized;
    if (IntentHash(actor, input) != input.RequestHash) return Invalid<TenantSetupReceipt>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await LockActorAsync(db, actor, ct)) return TenantAdministration.Denied<TenantSetupReceipt>();
    var existing = await ReceiptAsync(db, actor, input.RequestId, input.RequestHash!, ct);
    if (!existing.Succeeded) return CommandResult<TenantSetupReceipt>.Fail(existing.ErrorCode!, existing.Message!);
    if (existing.Value!.Receipt is { } committed)
    {
      if (committed.DraftId != input.DraftId || committed.PreviousFingerprint != input.ReviewBasis) return Invalid<TenantSetupReceipt>();
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<TenantSetupReceipt>();
      await tx.CommitAsync(ct);
      return CommandResult<TenantSetupReceipt>.Ok(committed);
    }
    var current = await CurrentDraftAsync(db, actor, input, configuredTenant, ct);
    if (!current.Succeeded) return CommandResult<TenantSetupReceipt>.Fail(current.ErrorCode!, current.Message!);
    var draft = current.Value!.Draft;
    var before = Basis(draft, current.Value.ConnectionState);
    if (before != input.ReviewBasis) return CommandResult<TenantSetupReceipt>.Fail(ErrorCodes.StaleRevision, "Setup changed. Refresh and review the exact current values.");
    var nextRevision = checked(draft.Revision + 1);
    var changed = await db.Microsoft365SetupDrafts.Where(x => x.Id == draft.Id && x.FirmId == actor.FirmId && x.Revision == draft.Revision)
      .ExecuteUpdateAsync(x => x.SetProperty(d => d.TenantDisplayName, input.Fields.TenantDisplayName)
        .SetProperty(d => d.MailState, input.Fields.MailState).SetProperty(d => d.RecordsState, input.Fields.RecordsState)
        .SetProperty(d => d.Revision, nextRevision).SetProperty(d => d.UpdatedAt, now), ct);
    if (changed != 1) return CommandResult<TenantSetupReceipt>.Fail(ErrorCodes.StaleRevision, "Setup changed. Refresh before reviewing another action.");
    var after = TenantAdministration.Fingerprint(draft.Id.ToString("D"), nextRevision.ToString(CultureInfo.InvariantCulture),
      draft.ExpectedTenantId, input.Fields.TenantDisplayName, input.Fields.MailState, input.Fields.RecordsState);
    var evidence = new Microsoft365AdministrationEvent { Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      ActorUserId = actor.UserId, Operation = Operation, TargetTenantId = draft.ExpectedTenantId,
      OldState = before, NewState = after, SetupRequestId = input.RequestId, SetupRequestHash = input.RequestHash,
      SetupDraftId = draft.Id, SetupRevisionAfter = nextRevision,
      Reason = "Administrator reviewed local setup metadata. No Microsoft permission or AuditSphere role changed.",
      Result = "SAVED_LOCAL_METADATA", CreatedAt = now };
    db.Microsoft365AdministrationEvents.Add(evidence);
    await db.SaveChangesAsync(ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<TenantSetupReceipt>();
    await tx.CommitAsync(ct);
    return CommandResult<TenantSetupReceipt>.Ok(new(evidence.Id, input.RequestId, draft.Id, input.RequestHash!, nextRevision.ToString(CultureInfo.InvariantCulture),
      evidence.OldState, evidence.NewState, evidence.CreatedAt));
  }

  public static async Task<CommandResult<TenantSetupReceiptLookup>> LookupAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid requestId, string requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Invalid<TenantSetupReceiptLookup>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Same lock as publication: an in-flight write must finish before absence can be established.
    if (!await LockActorAsync(db, actor, ct)) return TenantAdministration.Denied<TenantSetupReceiptLookup>();
    var result = await ReceiptAsync(db, actor, requestId, requestHash, ct);
    if (!result.Succeeded) return result;
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<TenantSetupReceiptLookup>();
    await tx.CommitAsync(ct);
    return result;
  }

  private static async Task<bool> LockActorAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return false;
    if (await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return false;
    return await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct);
  }

  private sealed record CurrentDraft(Microsoft365SetupDraft Draft, string? ConnectionState);
  private static async Task<CommandResult<CurrentDraft>> CurrentDraftAsync(IAuditSphereDbContext db, ActorContext actor,
    TenantSetupEditRequest input, string configuredTenant, CancellationToken ct)
  {
    var draft = await db.Microsoft365SetupDrafts.FromSqlInterpolated(
      $"SELECT * FROM m365_setup_drafts WHERE firm_id={actor.FirmId} AND id={input.DraftId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (draft is null || !Guid.TryParse(configuredTenant, out var configured) || !Guid.TryParse(draft.ExpectedTenantId, out var expected) || configured != expected)
      return TenantAdministration.Denied<CurrentDraft>();
    var connection = draft.ConnectionRevisionId is Guid id ? await db.Microsoft365ConnectionRevisions.FromSqlInterpolated(
      $"SELECT * FROM m365_connection_revisions WHERE firm_id={actor.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) : null;
    if (draft.ConnectionRevisionId is not null && (connection is null || !Guid.TryParse(connection.TenantId, out var connectionTenant) || connectionTenant != configured)) return TenantAdministration.Denied<CurrentDraft>();
    if (draft.State is Microsoft365RevisionStates.Active or Microsoft365RevisionStates.Suspended or Microsoft365RevisionStates.Blocked ||
        connection?.State is Microsoft365RevisionStates.Active or Microsoft365RevisionStates.Suspended or Microsoft365RevisionStates.Blocked)
      return CommandResult<CurrentDraft>.Fail(ErrorCodes.ProtectedState, "Protected setup cannot be edited in place. Prepare a separately reviewed replacement draft.");
    if (draft.Revision.ToString(CultureInfo.InvariantCulture) != input.ExpectedRevision || draft.Revision == long.MaxValue)
      return CommandResult<CurrentDraft>.Fail(ErrorCodes.StaleRevision, "Reload the current setup revision before review.");
    return CommandResult<CurrentDraft>.Ok(new(draft, connection?.State));
  }

  private static async Task<CommandResult<TenantSetupReceiptLookup>> ReceiptAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid requestId, string requestHash, CancellationToken ct)
  {
    var rows = await db.Microsoft365AdministrationEvents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId &&
      x.Operation == Operation && x.SetupRequestId == requestId).Take(2).ToListAsync(ct);
    if (rows.Count == 0) return CommandResult<TenantSetupReceiptLookup>.Ok(new(false, null));
    if (rows.Count != 1) return Invalid<TenantSetupReceiptLookup>();
    var e = rows[0];
    if (e.SetupDraftId is not Guid draftId || draftId == Guid.Empty || e.SetupRequestHash != requestHash ||
        !Hash(e.OldState) || !Hash(e.NewState) || e.SetupRevisionAfter is not long revision || revision < 2)
      return Invalid<TenantSetupReceiptLookup>();
    return CommandResult<TenantSetupReceiptLookup>.Ok(new(true, new(e.Id, requestId, draftId, requestHash,
      revision.ToString(CultureInfo.InvariantCulture), e.OldState, e.NewState, e.CreatedAt)));
  }

  private static TenantSetupEditRequest? Normalize(TenantSetupEditRequest i)
  {
    if (i.RequestId == Guid.Empty || i.DraftId == Guid.Empty || i.Fields is null ||
        !long.TryParse(i.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var rev) || rev < 1 || rev == long.MaxValue ||
        i.Fields.TenantDisplayName is { Length: > 300 } || i.Fields.TenantDisplayName?.Any(char.IsControl) == true ||
        i.Fields.MailState is not ("CONFIGURED" or "NOT_CONFIGURED") || i.Fields.RecordsState is not ("CONFIGURED" or "NOT_CONFIGURED")) return null;
    return i with { ExpectedRevision = rev.ToString(CultureInfo.InvariantCulture), Fields = i.Fields with {
      TenantDisplayName = string.IsNullOrWhiteSpace(i.Fields.TenantDisplayName) ? null : i.Fields.TenantDisplayName.Trim() } };
  }
  private static TenantSetupFields Fields(Microsoft365SetupDraft d) => new(d.TenantDisplayName, d.MailState, d.RecordsState);
  private static string Basis(Microsoft365SetupDraft d, string? connectionState) => TenantAdministration.Fingerprint(d.Id.ToString("D"),
    d.Revision.ToString(CultureInfo.InvariantCulture), d.ExpectedTenantId, d.TenantDisplayName, d.MailState, d.RecordsState,
    d.State, d.ConnectionRevisionId?.ToString("D"), connectionState);
  private static string IntentHash(ActorContext a, TenantSetupEditRequest i) => TenantAdministration.Fingerprint(a.FirmId.ToString("D"),
    a.UserId.ToString("D"), a.SessionEpoch.ToString(CultureInfo.InvariantCulture), i.RequestId.ToString("D"), i.DraftId.ToString("D"),
    i.ExpectedRevision, i.Fields.TenantDisplayName, i.Fields.MailState, i.Fields.RecordsState);
  private static bool Hash(string? s) => s is { Length: 64 } && s.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static CommandResult<T> Invalid<T>() => CommandResult<T>.Fail("m365.setup.review", "A matching exact setup review or receipt reference is required.");
}
