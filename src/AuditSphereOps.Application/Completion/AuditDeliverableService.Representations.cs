using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

public sealed record SignedLetterView(Guid Id, Guid DeliverableId, int Version, string ManagementSignatory, string ContentSha256,
  DateTimeOffset UploadedAt, bool Verified, bool Current);

public static partial class AuditDeliverableService
{
  public const int MaxSignedLetterBytes = 10 * 1024 * 1024;

  public static async Task<CommandResult<Guid>> UploadSignedRepresentationAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid deliverableId, string expectedSha256, string managementSignatory, byte[] pdf, CancellationToken ct = default)
  {
    var letter = await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deliverableId && x.FirmId == actor.FirmId &&
      x.Kind == DeliverableKinds.RepresentationLetter && x.SignedFromDeliverableId == null, ct);
    if (letter is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, letter.ClientId, letter.EngagementId, ["ClientUser"]), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var onboarding = await AuditSphereOps.Application.Documents.ClientPortalService.RequireFirstSignInAsync(db, actor, ct);
    if (!onboarding.Succeeded) return CommandResult<Guid>.Fail(onboarding.ErrorCode!, onboarding.Message!);
    var signatory = (managementSignatory ?? "").Trim();
    if (signatory.Length is < 2 or > 200 || pdf is null || pdf.Length is < 1 or > MaxSignedLetterBytes || !ValidPdf(pdf))
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Enter the management signatory's name and upload a readable, unencrypted PDF scan (maximum 10 MB).");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockDeliverableEngagementAsync(db, actor.FirmId, letter.EngagementId, ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, letter.ClientId, letter.EngagementId, ["ClientUser"]), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var window = await AuditSphereOps.Application.Documents.ClientPortalService.RequireUploadWindowAsync(db, actor, letter.EngagementId, ct);
    if (!window.Succeeded) return CommandResult<Guid>.Fail(window.ErrorCode!, window.Message!);
    if (await db.EngagementFileFreezes.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == letter.EngagementId && x.State == "FROZEN", ct))
    {
      var frozen = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(db, actor, letter.EngagementId, "upload the signed representation scan", ct);
      await tx.CommitAsync(ct); // persist the recorded refused attempt before refusing
      return CommandResult<Guid>.Fail(frozen.ErrorCode ?? ErrorCodes.ProtectedState, frozen.Message ?? "Client uploads are frozen after final release.");
    }
    if (!string.Equals(letter.ContentSha256, expectedSha256, StringComparison.OrdinalIgnoreCase) || !await IsCurrentAsync(db, actor, letter, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "This representation letter is no longer current; reload the current shared version.");
    if (!await db.ClientDeliverableReviews.AnyAsync(x => x.FirmId == actor.FirmId && x.DeliverableId == letter.Id &&
        x.AcknowledgedSha256 == letter.ContentSha256, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Acknowledge the exact shared representation letter before uploading its signed scan.");
    var sha = Hashing.Sha256Hex(pdf);
    var existing = await db.SignedRepresentationLetters.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.DeliverableId == letter.Id && x.ContentSha256 == sha, ct);
    if (existing is not null)
      return existing.ManagementSignatory == signatory ? CommandResult<Guid>.Ok(existing.Id)
        : CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This scan already has a different recorded signatory.");
    var scan = new SignedRepresentationLetter
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = letter.ClientId, EngagementId = letter.EngagementId, DeliverableId = letter.Id,
      DeliverableSha256 = letter.ContentSha256, ContentSha256 = sha, Content = pdf.ToArray(), ManagementSignatory = signatory,
      UploadedByUserId = actor.UserId, UploadedAt = DateTimeOffset.UtcNow
    };
    db.SignedRepresentationLetters.Add(scan);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(scan.Id);
  }

  public static async Task<CommandResult<Guid>> VerifySignedRepresentationAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid scanId, string expectedScanSha256, string reason, CancellationToken ct = default)
  {
    var scan = await db.SignedRepresentationLetters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scanId && x.FirmId == actor.FirmId, ct);
    if (scan is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizePartnerAsync(db, actor, scan.EngagementId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 2000)
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Record how you verified the management signature, authority and completeness of the scanned letter (10 to 2,000 characters).");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockDeliverableEngagementAsync(db, actor.FirmId, scan.EngagementId, ct);
    auth = await AuthorizePartnerAsync(db, actor, scan.EngagementId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var letter = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == scan.DeliverableId && x.FirmId == actor.FirmId, ct);
    if (!await IsCurrentAsync(db, actor, letter, ct) || scan.DeliverableSha256 != letter.ContentSha256 ||
        scan.ContentSha256 != expectedScanSha256 || Hashing.Sha256Hex(scan.Content) != scan.ContentSha256)
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The scan or its source representation letter changed; verify the current exact version.");
    if (await db.EngagementFileFreezes.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == scan.EngagementId && x.State == "FROZEN", ct))
    {
      var frozen = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(db, actor, scan.EngagementId, "verify the signed representation scan", ct);
      await tx.CommitAsync(ct); // persist the recorded refused attempt before refusing
      return CommandResult<Guid>.Fail(frozen.ErrorCode ?? ErrorCodes.ProtectedState, frozen.Message ?? "The engagement file is frozen.");
    }
    var prior = await db.RepresentationLetterVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.SignedLetterId == scan.Id, ct);
    if (prior is not null) return CommandResult<Guid>.Ok(prior.Id);
    var verification = new RepresentationLetterVerification
    {
      Id = Guid.CreateVersion7(), FirmId = scan.FirmId, ClientId = scan.ClientId, EngagementId = scan.EngagementId, SignedLetterId = scan.Id,
      VerifiedByUserId = actor.UserId, VerifiedAt = DateTimeOffset.UtcNow, Reason = reason.Trim()
    };
    db.RepresentationLetterVerifications.Add(verification);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(verification.Id);
  }

  public static async Task<CommandResult<SignedRepresentationLetter>> GetSignedRepresentationAsync(IAuditSphereDbContext db, ActorContext actor, Guid scanId, CancellationToken ct = default)
  {
    var scan = await db.SignedRepresentationLetters.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == scanId, ct);
    if (scan is null) return CommandResult<SignedRepresentationLetter>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = actor.Roles.Contains("ClientUser");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, scan.ClientId, scan.EngagementId, client ? ["ClientUser"] : ReaderRoles, InternalOnly: !client), ct);
    return auth.Succeeded ? CommandResult<SignedRepresentationLetter>.Ok(scan) : CommandResult<SignedRepresentationLetter>.Fail(auth.ErrorCode!, auth.Message!);
  }

  public static async Task<IReadOnlyList<SignedLetterView>> SignedRepresentationsAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return [];
    var client = actor.Roles.Contains("ClientUser");
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, engagement.PracticeClientId, engagementId, client ? ["ClientUser"] : ReaderRoles, InternalOnly: !client), ct)).Succeeded) return [];
    var scans = await db.SignedRepresentationLetters.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.UploadedAt).Take(20).Select(x => new { x.Id, x.DeliverableId, x.ManagementSignatory, x.ContentSha256, x.UploadedAt }).ToListAsync(ct);
    var result = new List<SignedLetterView>();
    foreach (var scan in scans)
    {
      var letter = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == scan.DeliverableId && x.FirmId == actor.FirmId, ct);
      result.Add(new(scan.Id, letter.Id, letter.Version, scan.ManagementSignatory, scan.ContentSha256, scan.UploadedAt,
        await db.RepresentationLetterVerifications.AnyAsync(x => x.FirmId == actor.FirmId && x.SignedLetterId == scan.Id, ct), await IsCurrentAsync(db, actor, letter, ct)));
    }
    return result;
  }

  private static bool ValidPdf(byte[] bytes)
  {
    if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return false;
    try
    {
      using var stream = new MemoryStream(bytes);
      using var document = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
      return document.PageCount > 0 && !document.SecuritySettings.IsEncrypted;
    }
    catch { return false; }
  }

  private static async Task LockDeliverableEngagementAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var clientId = await db.Engagements.AsNoTracking().Where(x => x.Id == engagementId && x.FirmId == firmId).Select(x => x.PracticeClientId).SingleAsync(ct);
    // Same guard order as financial release: an upload cannot cross a concurrent final-release commit.
    _ = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    _ = await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {firmId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    _ = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {firmId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
  }
}
