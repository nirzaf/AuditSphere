// Guarded TB import command (§§8.3, 16, 28.3): the server parses, checks scope and
// assignment, stamps the source hash, and promotes the dataset in one transaction.
// A repeated upload of identical bytes returns a duplicate conflict — it never
// appends a second silent copy. Validation itself stays with the outbox worker.
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static class TrialBalanceImportService
{
  public static async Task<CommandResult<Guid>> ImportAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid clientId,
    Guid engagementId,
    string csvText,
    CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || engagementId == Guid.Empty)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    ParsedCsvFile parsed;
    try
    {
      parsed = TrialBalanceCsvImporter.Parse(csvText);
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<Guid>.Fail("import.rejected", ex.Message);
    }

    // Resolve the engagement first so the firm scope comes from the stored record.
    var engagement = await db.Engagements.AsNoTracking()
      .SingleOrDefaultAsync(e => e.Id == engagementId, ct);
    if (engagement is null || engagement.PracticeClientId != clientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(engagement.FirmId, clientId, engagementId), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var duplicate = await db.TrialBalanceDatasets.AsNoTracking().AnyAsync(d =>
      d.FirmId == engagement.FirmId && d.EngagementId == engagementId &&
      d.Sha256Hex == parsed.SourceHash, ct);
    if (duplicate)
      return CommandResult<Guid>.Fail("import.duplicate",
        "Identical source bytes were already imported for this engagement; reuse that dataset.");

    var revision = await db.TrialBalanceDatasets
      .Where(d => d.FirmId == engagement.FirmId && d.EngagementId == engagementId)
      .Select(d => (long?)d.Revision).MaxAsync(ct) ?? 0;
    var dataset = new TrialBalanceDataset
    {
      Id = Guid.CreateVersion7(),
      FirmId = engagement.FirmId,
      ClientId = clientId,
      EngagementId = engagementId,
      SourceKind = "Raw",
      Revision = revision + 1,
      Currency = parsed.Currency,
      Sha256Hex = parsed.SourceHash,
      ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = actor.UserId
    };
    db.TrialBalanceDatasets.Add(dataset);
    foreach (var row in parsed.Rows)
    {
      db.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.CreateVersion7(),
        DatasetId = dataset.Id,
        AccountCode = row.AccountCode,
        AccountName = row.AccountName,
        Amount = row.Amount,
        Currency = row.Currency,
        Entity = row.Entity,
        MappingCode = row.MappingCode
      });
    }
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail("import.duplicate",
        "Identical source bytes were already imported for this engagement; reuse that dataset.");
    }
    return CommandResult<Guid>.Ok(dataset.Id);
  }
}
