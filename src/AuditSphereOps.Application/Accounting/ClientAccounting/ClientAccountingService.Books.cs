using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientAccountingService
{
  public static async Task<CommandResult<Guid>> CreateBookAsync(
    IClientAccountingDbContext db, ActorContext actor, ReportingBookRequest request,
    CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.Currency) ? AccountingDefaults.DefaultCurrency : request.Currency).Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || request.PeriodId == Guid.Empty || string.IsNullOrWhiteSpace(request.Code) ||
        string.IsNullOrWhiteSpace(request.Basis) || string.IsNullOrWhiteSpace(request.InclusionRule) ||
        currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A reporting book must identify its basis and inclusion rule.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var period = await db.ClientReportingPeriods.FromSqlInterpolated(
      $"SELECT * FROM client_reporting_periods WHERE id = {request.PeriodId} AND firm_id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (period is null || period.ClientId != request.ClientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting period is outside the client scope.");
    auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (period.Status == AccountingWorkflowStates.Closed)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "A closed period cannot receive a new book.");
    if (await db.ClientReportingBooks.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.PeriodId == request.PeriodId && x.Code == request.Code.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The reporting book already exists.");
    var book = new ClientReportingBook
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      PeriodId = request.PeriodId, Code = request.Code.Trim(), Basis = request.Basis.Trim(),
      InclusionRule = request.InclusionRule.Trim(), Currency = currency,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientReportingBooks.Add(book);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(book.Id);
  }
}
