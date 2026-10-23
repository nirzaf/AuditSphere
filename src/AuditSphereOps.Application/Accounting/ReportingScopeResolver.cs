using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Resolves caller-supplied accounting scope IDs through their exact firm/client/parent records.
/// Authorization remains with the owning command or query; this helper never chooses a substitute.
/// </summary>
internal static class ReportingScopeResolver
{
  /// <summary>Exact IDs and owner records resolved from the supplied accounting scope.</summary>
  /// <param name="Book">Null means no book ID was supplied; it is not an approved no-book decision.</param>
  internal sealed record ResolvedScope(
    Guid FirmId,
    Guid ClientId,
    Guid EngagementId,
    ClientReportingPeriod Period,
    ClientReportingBook? Book);

  internal static async Task<CommandResult<ResolvedScope>> ResolveAsync(
    IClientAccountingDbContext db,
    Guid firmId,
    Guid clientId,
    Guid engagementId,
    Guid periodId,
    Guid? bookId,
    CancellationToken ct = default)
  {
    if (firmId == Guid.Empty || clientId == Guid.Empty || engagementId == Guid.Empty || periodId == Guid.Empty ||
        bookId == Guid.Empty)
      return CommandResult<ResolvedScope>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var engagementExists = await db.Engagements.AsNoTracking().AnyAsync(x =>
      x.Id == engagementId && x.FirmId == firmId && x.PracticeClientId == clientId, ct);
    if (!engagementExists)
      return CommandResult<ResolvedScope>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == periodId && x.FirmId == firmId && x.ClientId == clientId, ct);
    if (period is null)
      return CommandResult<ResolvedScope>.Fail(ErrorCodes.ScopeDenied,
        "The reporting period is outside the client scope.");

    ClientReportingBook? book = null;
    if (bookId is { } exactBookId)
    {
      book = await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == exactBookId && x.FirmId == firmId && x.ClientId == clientId && x.PeriodId == periodId, ct);
      if (book is null)
        return CommandResult<ResolvedScope>.Fail(ErrorCodes.ScopeDenied,
          "The reporting book is outside the client period.");
    }

    return CommandResult<ResolvedScope>.Ok(new ResolvedScope(firmId, clientId, engagementId, period, book));
  }
}
