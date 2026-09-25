using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Microsoft365;

namespace AuditSphereOps.Infrastructure.Persistence;

// DbContext: one owner, snake_case, composite (firm,client,engagement) FK discipline (§§27, 42).
// Money decimal(19,6); IDs uuid v7-compatible; immutable TB rows have no update path.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientAccounting(ModelBuilder b)
  {
    var capability = b.Entity<AccountingCapabilityProfile>();
    capability.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("ak_accounting_capability_profiles_firm_id_id");
    capability.Property(x => x.ServiceKind).HasMaxLength(40);
    capability.Property(x => x.ServiceRoute).HasMaxLength(50);
    capability.Property(x => x.Framework).HasMaxLength(100);
    capability.Property(x => x.Edition).HasMaxLength(100);
    capability.Property(x => x.PeriodRule).HasMaxLength(100);
    capability.Property(x => x.ReportingCurrency).HasMaxLength(3);
    capability.Property(x => x.AccountingMethod).HasMaxLength(100);
    capability.Property(x => x.ConsolidationMethod).HasMaxLength(100);
    capability.Property(x => x.ReviewHierarchy).HasMaxLength(200);
    capability.Property(x => x.TemplateFamily).HasMaxLength(100);
    capability.Property(x => x.Status).HasMaxLength(30);
    capability.Property(x => x.AcceptanceState).HasMaxLength(40);
    capability.HasIndex(x => new { x.FirmId, x.ClientId, x.GroupId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_accounting_capability_profile_scope_revision");
    capability.ToTable("accounting_capability_profiles", t => t.HasCheckConstraint("ck_accounting_capability_profile_scope",
      "((client_id IS NOT NULL) <> (group_id IS NOT NULL)) AND length(trim(service_kind)) > 0 AND length(trim(framework)) > 0 AND reporting_currency ~ '^[A-Z]{3}$'"));
    capability.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    capability.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var acceptance = b.Entity<AccountingCapabilityAcceptance>();
    acceptance.Property(x => x.Stage).HasMaxLength(40);
    acceptance.Property(x => x.Status).HasMaxLength(30);
    acceptance.Property(x => x.EvidenceReference).HasMaxLength(2000);
    acceptance.HasIndex(x => new { x.FirmId, x.CapabilityProfileId, x.Stage }).IsUnique()
      .HasDatabaseName("ux_accounting_capability_acceptance_stage");
    acceptance.HasOne<AccountingCapabilityProfile>().WithMany().HasForeignKey(x => new { x.FirmId, x.CapabilityProfileId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var profile = b.Entity<ClientAccountingProfile>();
    profile.Property(x => x.Jurisdiction).HasMaxLength(100);
    profile.Property(x => x.FunctionalCurrency).HasMaxLength(3);
    profile.Property(x => x.SourceSystem).HasMaxLength(100);
    profile.Property(x => x.SourceSystemIdentifier).HasMaxLength(200);
    profile.Property(x => x.Status).HasMaxLength(30);
    profile.HasIndex(x => new { x.FirmId, x.ClientId }).IsUnique().HasDatabaseName("ux_client_accounting_profile_client");
    profile.ToTable("client_accounting_profiles", t => t.HasCheckConstraint("ck_client_accounting_profile_values",
      "length(trim(jurisdiction)) > 0 AND functional_currency ~ '^[A-Z]{3}$' AND fiscal_year_start_month BETWEEN 1 AND 12 AND fiscal_year_start_day BETWEEN 1 AND 31"));
    profile.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var group = b.Entity<ClientGroup>();
    group.Property(x => x.Code).HasMaxLength(100);
    group.Property(x => x.Name).HasMaxLength(300);
    group.Property(x => x.Status).HasMaxLength(30);
    group.HasIndex(x => new { x.FirmId, x.Code }).IsUnique().HasDatabaseName("ux_client_group_code");
    group.ToTable("client_groups", t => t.HasCheckConstraint("ck_client_group_values",
      "length(trim(code)) > 0 AND length(trim(name)) > 0"));

    var membership = b.Entity<ClientGroupMembership>();
    membership.Property(x => x.ControlMethod).HasMaxLength(100);
    membership.Property(x => x.EvidenceReference).HasMaxLength(2000);
    membership.Property(x => x.Status).HasMaxLength(30);
    membership.HasIndex(x => new { x.FirmId, x.GroupId, x.ClientId, x.EffectiveFrom }).IsUnique()
      .HasDatabaseName("ux_client_group_membership_effective");
    membership.ToTable("client_group_memberships", t => t.HasCheckConstraint("ck_client_group_membership_values",
      "effective_to IS NULL OR effective_from <= effective_to" +
      " AND ownership_percent >= 0 AND ownership_percent <= 100" +
      " AND economic_interest_percent >= 0 AND economic_interest_percent <= 100"));
    membership.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    membership.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var groupAccess = b.Entity<GroupAccessGrant>();
    groupAccess.Property(x => x.Role).HasMaxLength(50);
    groupAccess.HasIndex(x => new { x.FirmId, x.GroupId, x.UserId, x.Role }).IsUnique()
      .HasDatabaseName("ux_group_access_grant");
    groupAccess.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    groupAccess.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.UserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var period = b.Entity<ClientReportingPeriod>();
    period.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id }).HasName("ak_client_reporting_periods_scope_id");
    period.Property(x => x.PeriodCode).HasMaxLength(50);
    period.Property(x => x.Basis).HasMaxLength(50);
    period.Property(x => x.Currency).HasMaxLength(3);
    period.Property(x => x.Status).HasMaxLength(30);
    period.Property(x => x.CloseReason).HasMaxLength(2000);
    period.HasIndex(x => new { x.FirmId, x.ClientId, x.PeriodCode, x.Basis }).IsUnique()
      .HasDatabaseName("ux_client_reporting_period_identity");
    period.ToTable("client_reporting_periods", t => t.HasCheckConstraint("ck_client_reporting_period_values",
      "start_date <= end_date AND length(trim(period_code)) > 0 AND length(trim(basis)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    period.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    period.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PriorPeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var amendment = b.Entity<ClientPeriodAmendment>();
    amendment.Property(x => x.Reason).HasMaxLength(4000);
    amendment.HasIndex(x => new { x.FirmId, x.ClientId, x.PeriodId, x.AmendmentRevision }).IsUnique()
      .HasDatabaseName("ux_client_period_amendment_revision");
    amendment.ToTable("client_period_amendments", t => t.HasCheckConstraint("ck_client_period_amendment_values",
      "previous_revision >= 1 AND amendment_revision = previous_revision + 1 AND length(trim(reason)) > 0"));
    amendment.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    amendment.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var book = b.Entity<ClientReportingBook>();
    book.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id }).HasName("ak_client_reporting_books_scope_id");
    book.Property(x => x.Code).HasMaxLength(50);
    book.Property(x => x.Basis).HasMaxLength(50);
    book.Property(x => x.InclusionRule).HasMaxLength(100);
    book.Property(x => x.Currency).HasMaxLength(3);
    book.Property(x => x.Status).HasMaxLength(30);
    book.HasIndex(x => new { x.FirmId, x.ClientId, x.PeriodId, x.Code }).IsUnique()
      .HasDatabaseName("ux_client_reporting_book_identity");
    book.ToTable("client_reporting_books", t => t.HasCheckConstraint("ck_client_reporting_book_values",
      "length(trim(code)) > 0 AND length(trim(basis)) > 0 AND length(trim(inclusion_rule)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    book.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var opening = b.Entity<OpeningBalanceBridge>();
    opening.Property(x => x.SourceHash).HasMaxLength(64);
    opening.Property(x => x.Status).HasMaxLength(30);
    opening.Property(x => x.EvidenceReference).HasMaxLength(2000);
    opening.HasIndex(x => new { x.FirmId, x.ClientId, x.CurrentPeriodId }).IsUnique()
      .HasDatabaseName("ux_opening_balance_bridge_period");
    opening.ToTable("opening_balance_bridges", t => t.HasCheckConstraint("ck_opening_balance_bridge_values",
      "length(trim(status)) > 0"));
    opening.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.CurrentPeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var restatement = b.Entity<ClientPeriodRestatement>();
    restatement.Property(x => x.OriginalPackageHash).HasMaxLength(64);
    restatement.Property(x => x.RevisedPackageHash).HasMaxLength(64);
    restatement.Property(x => x.RevisedBasis).HasMaxLength(100);
    restatement.Property(x => x.ChangeType).HasMaxLength(30);
    restatement.Property(x => x.AffectedPeriods).HasMaxLength(500);
    restatement.Property(x => x.Reason).HasMaxLength(4000);
    restatement.Property(x => x.EvidenceReference).HasMaxLength(2000);
    restatement.Property(x => x.Status).HasMaxLength(30);
    restatement.HasIndex(x => new { x.FirmId, x.OriginalPackageId, x.RevisedPackageId }).IsUnique()
      .HasDatabaseName("ux_client_period_restatement_packages");
    restatement.ToTable("client_period_restatements", t => t.HasCheckConstraint("ck_client_period_restatement_values",
      "original_package_id <> revised_package_id AND original_package_hash ~ '^[0-9a-f]{64}$' AND revised_package_hash ~ '^[0-9a-f]{64}$' AND length(trim(revised_basis)) > 0 AND length(trim(reason)) > 0 AND length(trim(evidence_reference)) > 0 AND status IN ('SUBMITTED','APPROVED','REJECTED') AND ((status = 'SUBMITTED' AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (status = 'APPROVED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))"));
    restatement.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    restatement.HasOne<FinancialPackage>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.OriginalPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    restatement.HasOne<FinancialPackage>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.RevisedPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var chart = b.Entity<ClientChartVersion>();
    chart.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id }).HasName("ak_client_chart_versions_scope_id");
    chart.Property(x => x.SourceScope).HasMaxLength(100);
    chart.Property(x => x.Status).HasMaxLength(30);
    chart.HasIndex(x => new { x.FirmId, x.ClientId, x.Version }).IsUnique().HasDatabaseName("ux_client_chart_version_number");
    chart.ToTable("client_chart_versions", t => t.HasCheckConstraint("ck_client_chart_version_values",
      "effective_to IS NULL OR effective_from <= effective_to"));
    chart.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var quarantine = b.Entity<AccountingBackfillQuarantine>();
    quarantine.Property(x => x.TargetKind).HasMaxLength(50);
    quarantine.Property(x => x.ContextKind).HasMaxLength(50);
    quarantine.Property(x => x.Reason).HasMaxLength(500);
    quarantine.HasIndex(x => new { x.FirmId, x.TargetKind, x.TargetId, x.ContextKind }).IsUnique()
      .HasDatabaseName("ux_accounting_backfill_quarantine_target");
    quarantine.ToTable("accounting_backfill_quarantines", t => t.HasCheckConstraint("ck_accounting_backfill_quarantine_values",
      "length(trim(target_kind)) > 0 AND length(trim(context_kind)) > 0 AND candidate_count >= 0 AND length(trim(reason)) > 0"));
    quarantine.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var account = b.Entity<ClientAccount>();
    account.Property(x => x.StableIdentity).HasMaxLength(100);
    account.Property(x => x.AccountCode).HasMaxLength(100);
    account.Property(x => x.AccountName).HasMaxLength(300);
    account.Property(x => x.AccountType).HasMaxLength(50);
    account.Property(x => x.NormalBalance).HasMaxLength(20);
    account.Property(x => x.Status).HasMaxLength(30);
    account.HasIndex(x => new { x.FirmId, x.ClientId, x.ChartVersionId, x.AccountCode }).IsUnique()
      .HasDatabaseName("ux_client_account_chart_code");
    account.HasIndex(x => new { x.FirmId, x.ClientId, x.StableIdentity }).IsUnique()
      .HasDatabaseName("ux_client_account_stable_identity");
    account.ToTable("client_accounts", t => t.HasCheckConstraint("ck_client_account_values",
      "length(trim(stable_identity)) > 0 AND length(trim(account_code)) > 0 AND length(trim(account_name)) > 0 AND length(trim(account_type)) > 0"));
    account.HasOne<ClientChartVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ChartVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    account.HasOne<ClientAccount>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ParentAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var alias = b.Entity<SourceAccountAlias>();
    alias.Property(x => x.SourceSystem).HasMaxLength(100);
    alias.Property(x => x.AliasCode).HasMaxLength(100);
    alias.Property(x => x.AliasName).HasMaxLength(300);
    alias.HasIndex(x => new { x.FirmId, x.ClientId, x.ChartVersionId, x.SourceSystem, x.AliasCode }).IsUnique()
      .HasDatabaseName("ux_source_account_alias");
    alias.HasOne<ClientChartVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ChartVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    alias.HasOne<ClientAccount>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ClientAccountId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var taxonomy = b.Entity<ReportingTaxonomyVersion>();
    taxonomy.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("ak_reporting_taxonomy_versions_firm_id_id");
    taxonomy.Property(x => x.OverlayScope).HasMaxLength(200);
    taxonomy.Property(x => x.Code).HasMaxLength(100);
    taxonomy.Property(x => x.Framework).HasMaxLength(100);
    taxonomy.Property(x => x.Name).HasMaxLength(300);
    taxonomy.Property(x => x.Status).HasMaxLength(30);
    taxonomy.HasIndex(x => new { x.FirmId, x.Code }).IsUnique().HasDatabaseName("ux_reporting_taxonomy_code");
    taxonomy.ToTable("reporting_taxonomy_versions", t => t.HasCheckConstraint("ck_reporting_taxonomy_values",
      "length(trim(code)) > 0 AND length(trim(framework)) > 0 AND length(trim(name)) > 0 AND (overlay_scope = 'BASE' OR overlay_scope LIKE 'INDUSTRY:%' OR overlay_scope LIKE 'GROUP:%') AND (effective_to IS NULL OR effective_from <= effective_to)"));
    taxonomy.HasOne<ReportingTaxonomyVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.BaseTaxonomyVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var taxonomyNode = b.Entity<ReportingTaxonomyNode>();
    taxonomyNode.Property(x => x.Code).HasMaxLength(100);
    taxonomyNode.Property(x => x.Name).HasMaxLength(300);
    taxonomyNode.Property(x => x.StatementSection).HasMaxLength(50);
    taxonomyNode.Property(x => x.DisplaySign).HasMaxLength(20);
    taxonomyNode.Property(x => x.NormalBalance).HasMaxLength(20);
    taxonomyNode.Property(x => x.DisclosureArea).HasMaxLength(100);
    taxonomyNode.Property(x => x.Applicability).HasMaxLength(100);
    taxonomyNode.HasIndex(x => new { x.FirmId, x.TaxonomyVersionId, x.Code }).IsUnique()
      .HasDatabaseName("ux_reporting_taxonomy_node_code");
    taxonomyNode.HasOne<ReportingTaxonomyVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.TaxonomyVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    taxonomyNode.HasOne<ReportingTaxonomyNode>().WithMany().HasForeignKey(x => new { x.FirmId, x.TaxonomyVersionId, x.ParentNodeId })
      .HasPrincipalKey(x => new { x.FirmId, x.TaxonomyVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    taxonomyNode.HasAlternateKey(x => new { x.FirmId, x.TaxonomyVersionId, x.Id }).HasName("ak_reporting_taxonomy_nodes_scope_id");

    var dimension = b.Entity<ClientAccountingDimensionDefinition>();
    dimension.Property(x => x.DimensionType).HasMaxLength(40);
    dimension.Property(x => x.Code).HasMaxLength(100);
    dimension.Property(x => x.Name).HasMaxLength(300);
    dimension.Property(x => x.Status).HasMaxLength(30);
    dimension.HasIndex(x => new { x.FirmId, x.ClientId, x.DimensionType, x.Code }).IsUnique()
      .HasDatabaseName("ux_client_accounting_dimension_definition");
    dimension.ToTable("client_accounting_dimension_definitions", t => t.HasCheckConstraint("ck_client_accounting_dimension_values",
      "dimension_type IN ('BRANCH','COST_CENTRE','DEPARTMENT','PROJECT','INTERCOMPANY_COUNTERPARTY') AND length(trim(code)) > 0 AND length(trim(name)) > 0"));
    dimension.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var import = b.Entity<SourceImportBatch>();
    import.Property(x => x.SourceKind).HasMaxLength(30);
    import.Property(x => x.ProfileVersion).HasMaxLength(100);
    import.Property(x => x.ParserVersion).HasMaxLength(100);
    import.Property(x => x.RawFileSha256Hex).HasMaxLength(64);
    import.Property(x => x.NormalizedDatasetDigest).HasMaxLength(64);
    import.Property(x => x.LegalEntityKey).HasMaxLength(200);
    import.Property(x => x.Currency).HasMaxLength(3);
    import.Property(x => x.Status).HasMaxLength(20);
    import.Property(x => x.ReceiptReference).HasMaxLength(300);
    import.HasIndex(x => new { x.FirmId, x.EngagementId, x.RawFileSha256Hex }).IsUnique()
      .HasDatabaseName("ux_source_import_raw_hash").HasFilter("length(raw_file_sha256_hex) > 0");
    import.ToTable("source_import_batches", t => t.HasCheckConstraint("ck_source_import_batch_values",
      "length(trim(source_kind)) > 0 AND length(trim(profile_version)) > 0 AND length(trim(parser_version)) > 0 AND currency ~ '^[A-Z]{3}$'" +
      " AND expected_chunk_count >= 0 AND expected_transaction_count >= 0 AND expected_line_count >= 0" +
      " AND accepted_chunk_count >= 0 AND accepted_transaction_count >= 0 AND accepted_line_count >= 0" +
      " AND accepted_chunk_count <= expected_chunk_count AND accepted_transaction_count <= expected_transaction_count" +
      " AND accepted_line_count <= expected_line_count AND status IN ('LOADING','SEALED','REJECTED')"));
    ScopeToEngagement(import, nameof(SourceImportBatch.FirmId), nameof(SourceImportBatch.ClientId), nameof(SourceImportBatch.EngagementId));

    var glChunk = b.Entity<GeneralLedgerImportChunk>();
    glChunk.Property(x => x.ChunkDigest).HasMaxLength(64);
    glChunk.HasIndex(x => new { x.FirmId, x.ImportBatchId, x.ChunkNumber }).IsUnique()
      .HasDatabaseName("ux_gl_import_chunk_number");
    glChunk.HasIndex(x => new { x.FirmId, x.ImportBatchId, x.ChunkDigest }).IsUnique()
      .HasDatabaseName("ux_gl_import_chunk_digest");
    glChunk.ToTable("general_ledger_import_chunks", t => t.HasCheckConstraint("ck_gl_import_chunk_values",
      "chunk_number >= 0 AND chunk_digest ~ '^[0-9a-f]{64}$' AND transaction_count > 0 AND line_count > 0"));
    ScopeToEngagement(glChunk, nameof(GeneralLedgerImportChunk.FirmId), nameof(GeneralLedgerImportChunk.ClientId), nameof(GeneralLedgerImportChunk.EngagementId));
    glChunk.HasOne<SourceImportBatch>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ImportBatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var glTransaction = b.Entity<GeneralLedgerTransaction>();
    glTransaction.Property(x => x.StableJournalId).HasMaxLength(200);
    glTransaction.Property(x => x.DocumentNumber).HasMaxLength(200);
    glTransaction.Property(x => x.SourceUser).HasMaxLength(200);
    glTransaction.Property(x => x.SourceSystem).HasMaxLength(100);
    glTransaction.Property(x => x.ReversalReference).HasMaxLength(200);
    glTransaction.Property(x => x.Currency).HasMaxLength(3);
    glTransaction.HasIndex(x => new { x.FirmId, x.ImportBatchId, x.StableJournalId }).IsUnique()
      .HasDatabaseName("ux_gl_transaction_stable_journal");
    ScopeToEngagement(glTransaction, nameof(GeneralLedgerTransaction.FirmId), nameof(GeneralLedgerTransaction.ClientId), nameof(GeneralLedgerTransaction.EngagementId));
    glTransaction.HasOne<SourceImportBatch>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ImportBatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    glTransaction.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).HasName("ak_gl_transactions_scope_id");

    var glLine = b.Entity<GeneralLedgerLine>();
    glLine.Property(x => x.StableLineId).HasMaxLength(200);
    glLine.Property(x => x.AccountCode).HasMaxLength(100);
    glLine.Property(x => x.OriginalCurrency).HasMaxLength(3);
    glLine.Property(x => x.PartyIdentifier).HasMaxLength(200);
    glLine.Property(x => x.Branch).HasMaxLength(100);
    glLine.Property(x => x.CostCentre).HasMaxLength(100);
    glLine.Property(x => x.Department).HasMaxLength(100);
    glLine.Property(x => x.Project).HasMaxLength(100);
    glLine.Property(x => x.IntercompanyCounterparty).HasMaxLength(200);
    glLine.HasIndex(x => new { x.FirmId, x.ImportBatchId, x.StableLineId }).IsUnique()
      .HasDatabaseName("ux_gl_line_stable_line");
    glLine.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("ak_gl_lines_scope_id");
    glLine.ToTable("general_ledger_lines", t => t.HasCheckConstraint("ck_gl_line_values",
      "length(trim(account_code)) > 0 AND debit >= 0 AND credit >= 0 AND NOT (debit > 0 AND credit > 0) AND original_currency ~ '^[A-Z]{3}$'"));
    ScopeToEngagement(glLine, nameof(GeneralLedgerLine.FirmId), nameof(GeneralLedgerLine.ClientId), nameof(GeneralLedgerLine.EngagementId));
    glLine.HasOne<GeneralLedgerTransaction>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.TransactionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var completeness = b.Entity<GeneralLedgerCompletenessBridge>();
    completeness.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("ak_gl_completeness_bridges_scope_id");
    completeness.Property(x => x.TrialBalanceHash).HasMaxLength(64);
    completeness.Property(x => x.GeneralLedgerHash).HasMaxLength(64);
    completeness.Property(x => x.OpeningTrialBalanceHash).HasMaxLength(64);
    completeness.Property(x => x.AccountResidualDigest).HasMaxLength(64);
    completeness.Property(x => x.OpeningMovementResidualDigest).HasMaxLength(64);
    completeness.Property(x => x.Status).HasMaxLength(30);
    completeness.Property(x => x.EvidenceReference).HasMaxLength(2000);
    completeness.Property(x => x.CompletenessDisclosure).HasMaxLength(2000);
    completeness.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.TrialBalanceDatasetId, x.ImportBatchId })
      .IsUnique().HasDatabaseName("ux_gl_completeness_bridge_input");
    completeness.ToTable("general_ledger_completeness_bridges", t => t.HasCheckConstraint("ck_gl_completeness_bridge_values",
      "trial_balance_hash ~ '^[0-9a-f]{64}$' AND general_ledger_hash ~ '^[0-9a-f]{64}$' AND account_residual_digest ~ '^[0-9a-f]{64}$'" +
      " AND trial_balance_account_count > 0 AND general_ledger_account_count > 0 AND matched_account_count >= 0" +
      " AND mismatched_account_count >= 0 AND absolute_residual >= 0 AND coverage_start <= coverage_end" +
      " AND length(trim(evidence_reference)) > 0 AND status IN ('RECONCILED','UNRECONCILED','APPROVED','REJECTED')" +
      " AND ((status IN ('APPROVED','REJECTED') AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL)" +
      " OR status IN ('RECONCILED','UNRECONCILED'))"));
    ScopeToEngagement(completeness, nameof(GeneralLedgerCompletenessBridge.FirmId), nameof(GeneralLedgerCompletenessBridge.ClientId), nameof(GeneralLedgerCompletenessBridge.EngagementId));
    completeness.HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<ClientReportingBook>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.BookId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.TrialBalanceDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.OpeningTrialBalanceDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<SourceImportBatch>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ImportBatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    completeness.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    ConfigureClientAccountingSchedules(b);
    ConfigureConsolidation(b);
  }
}
