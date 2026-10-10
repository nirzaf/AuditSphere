using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class ConfirmationWorkspace
{
  public static async Task<CommandResult<ConfirmationBatchValue>> CreateBatchAsync(
    IAuditSphereDbContext db, ActorContext actor, ConfirmationBatchRequest request,
    string reviewToken, bool reviewed, CancellationToken ct = default)
  {
    var cases = request.Cases;
    if (!(await Auth(db, actor, request.EngagementId, PrepareRoles, true, ct)).Succeeded)
      return Denied<ConfirmationBatchValue>();
    if (cases is null || cases.Count is < 1 or > 100 ||
        !AuditConfirmationAreaCodes.IsSupported(request.AreaCode) || request.AreaCode.Length > 40 ||
        cases.Any(x => x is null || string.IsNullOrWhiteSpace(x.SourceRecordId) || x.SourceRecordId.Length > 200 ||
          string.IsNullOrWhiteSpace(x.Respondent) || x.Respondent.Length > 500 ||
          string.IsNullOrWhiteSpace(x.ContactValidationSource) || x.ContactValidationSource.Length > 2000 ||
          x.BookedAmount <= -100000000000000m || x.BookedAmount >= 100000000000000m || decimal.Round(x.BookedAmount, 6) != x.BookedAmount))
      return CommandResult<ConfirmationBatchValue>.Fail("request.invalid", "Review one to 100 bounded cases with exact supported amounts.");
    var sources = cases.Select(x => x.SourceRecordId.Trim()).ToArray();
    if (sources.Distinct(StringComparer.Ordinal).Count() != sources.Length)
      return CommandResult<ConfirmationBatchValue>.Fail("request.invalid", "Each batch source record must be unique.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await Lock(db, actor, request.EngagementId, ct);
    if (!(await Auth(db, actor, request.EngagementId, PrepareRoles, true, ct)).Succeeded)
      return Denied<ConfirmationBatchValue>();
    if (!reviewed || reviewToken != await CreateToken(db, actor, request.EngagementId, ct))
      return CommandResult<ConfirmationBatchValue>.Fail(ErrorCodes.StaleRevision, "Refresh and review the current register and every batch case.");
    var writable = await FileFreezeService.RequireWritableAsync(db, actor, request.EngagementId, "prepare confirmation batch", ct);
    if (!writable.Succeeded)
    {
      await tx.CommitAsync(ct);
      return CommandResult<ConfirmationBatchValue>.Fail(writable.ErrorCode!, writable.Message!);
    }
    var area = request.AreaCode.Trim().ToUpperInvariant();
    if (await db.AuditConfirmationCases.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
        x.AreaCode == area && x.ConfirmationDate == request.ConfirmationDate && sources.Contains(x.SourceRecordId), ct))
      return CommandResult<ConfirmationBatchValue>.Fail(ErrorCodes.ProtectedState, "A source/date confirmation already exists. The entire batch was refused; review persisted cases.");
    var result = await AuditConfirmationBatchService.CreateConfirmationBatchAsync(db, actor, request, ct);
    if (result.Succeeded) await tx.CommitAsync(ct);
    return result;
  }
}
