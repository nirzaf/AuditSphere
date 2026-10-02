using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Testing;
internal static class RemeasurementFixture
{
  internal sealed record Inputs(Guid Period, Guid RateSet, Guid Policy, Guid Line, Guid Snapshot);
  internal static async Task<Inputs> SeedAsync(AuditSphereDbContext db, PbcSeed.Fixture f)
  {
    var now = DateTimeOffset.UtcNow;
    Guid period = Guid.NewGuid(), rate = Guid.NewGuid(), policy = Guid.NewGuid(), line = Guid.NewGuid(), snapshot = Guid.NewGuid(), binding = Guid.NewGuid(), document = Guid.NewGuid(), batch = Guid.NewGuid(), transaction = Guid.NewGuid();
    db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", f.ClientId, f.EngagementId), PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer", f.ClientId, f.EngagementId));
    db.ClientAccountingProfiles.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, Jurisdiction = "QA", FunctionalCurrency = "QAR", Status = AccountingWorkflowStates.Active, CreatedByUserId = f.Admin.Id, CreatedAt = now });
    db.ClientReportingPeriods.Add(new() { Id = period, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "FY26-SYNTHETIC", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), Basis = "IFRS", Currency = "QAR", Status = AccountingWorkflowStates.Active, CreatedByUserId = f.Admin.Id, CreatedAt = now });
    db.ExchangeRateSetVersions.Add(new() { Id = rate, FirmId = f.FirmId, Code = "SYN-FX", Version = 1, Source = "Synthetic approved rate evidence", EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), Status = AccountingWorkflowStates.Approved, CreatedByUserId = f.Staff.Id, ApprovedByUserId = f.Reviewer.Id, CreatedAt = now, ApprovedAt = now });
    db.TranslationPolicyVersions.Add(new() { Id = policy, FirmId = f.FirmId, Code = "SYN-POLICY", FunctionalCurrency = "QAR", PresentationCurrency = "QAR", ClosingRateRule = "CLOSING", AverageRateRule = "AVERAGE", HistoricalRateRule = "HISTORICAL", Status = AccountingWorkflowStates.Approved, CreatedByUserId = f.Staff.Id, ApprovedByUserId = f.Reviewer.Id, CreatedAt = now, ApprovedAt = now });
    db.ExchangeRates.AddRange(new ExchangeRate { Id = Guid.NewGuid(), FirmId = f.FirmId, RateSetVersionId = rate, FromCurrency = "USD", ToCurrency = "QAR", RateDate = new(2026, 12, 31), RateType = "CLOSING", Rate = 3.7m, Direction = "DIRECT", CreatedAt = now }, new ExchangeRate { Id = Guid.NewGuid(), FirmId = f.FirmId, RateSetVersionId = rate, FromCurrency = "USD", ToCurrency = "QAR", RateDate = new(2026, 1, 1), RateType = "HISTORICAL", Rate = 3.6m, Direction = "DIRECT", CreatedAt = now });
    db.RepositoryBindings.Add(new() { Id = binding, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, TenantId = "synthetic-tenant", SiteId = "synthetic-site", DriveId = "synthetic-drive", RootFolderId = "synthetic-root", Classification = "WORKING", DesiredAccess = "APP_MEDIATED", ObservedAccess = "APP_MEDIATED", CapabilityProfile = "TEST", CreatedAt = now });
    db.DocumentReferences.Add(new() { Id = document, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, RepositoryBindingId = binding, Provider = "SharePoint", DriveId = "synthetic-drive", ItemId = "synthetic-evidence", Path = "/Test/open-items.xlsx", Purpose = "Evidence", CreatedAt = now });
    db.DocumentSnapshots.Add(new() { Id = snapshot, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, DocumentReferenceId = document, DriveId = "synthetic-drive", ItemId = "synthetic-evidence", VersionId = "v1", Sha256Hex = new string('a', 64), ByteCount = 100, CapturedBy = "synthetic-fixture", CapturedAt = now });
    db.SourceImportBatches.Add(new() { Id = batch, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, PeriodId = period, SourceKind = "GL", ProfileVersion = "synthetic-v1", ParserVersion = "synthetic-v1", RawFileSha256Hex = new string('c', 64), NormalizedDatasetDigest = new string('d', 64), LegalEntityKey = "SYNTHETIC-ENTITY", Currency = "QAR", RowCount = 2, ExpectedTransactionCount = 1, ExpectedLineCount = 2, AcceptedTransactionCount = 1, AcceptedLineCount = 2, Status = "SEALED", ReceiptReference = "synthetic-gl", CreatedByUserId = f.Staff.Id, CreatedAt = now });
    db.GeneralLedgerTransactions.Add(new() { Id = transaction, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ImportBatchId = batch, StableJournalId = "SYN-FX-JOURNAL", PostingDate = new(2026, 12, 31), Currency = "QAR", SourceSystem = "synthetic-test", CreatedAt = now });
    db.GeneralLedgerLines.AddRange(new GeneralLedgerLine { Id = line, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ImportBatchId = batch, TransactionId = transaction, StableLineId = "SYN-FOREIGN-LINE", AccountCode = "1200", Debit = 370m, OriginalCurrency = "USD", OriginalAmount = 100m, FunctionalAmount = 370m, CreatedAt = now }, new GeneralLedgerLine { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ImportBatchId = batch, TransactionId = transaction, StableLineId = "SYN-OFFSET-LINE", AccountCode = "4000", Credit = 370m, OriginalCurrency = "QAR", OriginalAmount = -370m, FunctionalAmount = -370m, CreatedAt = now });
    await db.SaveChangesAsync();
    return new(period, rate, policy, line, snapshot);
  }
}
