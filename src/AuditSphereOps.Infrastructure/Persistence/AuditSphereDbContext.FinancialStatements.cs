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
  private static void ConfigureMappingAndFinancialStatements(ModelBuilder b)
  {
    var dataset = b.Entity<TrialBalanceDataset>();
    dataset.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_trial_balance_datasets_scope_id");

    var acceptance = b.Entity<SourceAcceptanceDecision>();
    acceptance.Property(x => x.SourceKind).HasMaxLength(10);
    acceptance.Property(x => x.SourceIdentityHash).HasMaxLength(64);
    acceptance.Property(x => x.Decision).HasMaxLength(20);
    acceptance.Property(x => x.EvidenceReference).HasMaxLength(2000);
    acceptance.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceKind, x.CreatedAt })
      .HasDatabaseName("ix_source_acceptance_pointer");
    acceptance.HasIndex(x => new { x.FirmId, x.TrialBalanceDatasetId }).IsUnique()
      .HasDatabaseName("ux_source_acceptance_tb_dataset").HasFilter("trial_balance_dataset_id IS NOT NULL");
    acceptance.HasIndex(x => new { x.FirmId, x.ImportBatchId }).IsUnique()
      .HasDatabaseName("ux_source_acceptance_gl_batch").HasFilter("import_batch_id IS NOT NULL");
    acceptance.ToTable("source_acceptance_decisions", t => t.HasCheckConstraint("ck_source_acceptance_values",
      "source_kind IN ('TB','GL') AND decision IN ('ACCEPTED') AND length(trim(evidence_reference)) > 0 " +
      "AND ((source_kind = 'TB' AND trial_balance_dataset_id IS NOT NULL AND import_batch_id IS NULL) " +
      "OR (source_kind = 'GL' AND import_batch_id IS NOT NULL AND trial_balance_dataset_id IS NULL))"));
    acceptance.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.TrialBalanceDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    acceptance.HasOne<SourceImportBatch>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ImportBatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var tbIssue = b.Entity<TrialBalanceValidationIssue>();
    tbIssue.Property(x => x.RowKey).HasMaxLength(200);
    tbIssue.Property(x => x.Severity).HasMaxLength(20);
    tbIssue.Property(x => x.Code).HasMaxLength(100);
    tbIssue.Property(x => x.Message).HasMaxLength(1000);
    tbIssue.HasIndex(x => new { x.FirmId, x.DatasetId, x.Severity }).HasDatabaseName("ix_tb_validation_issues");

    var layoutVersion = b.Entity<StatementLayoutVersion>();
    layoutVersion.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id }).HasName("AK_statement_layout_versions_scope_id");
    layoutVersion.Property(x => x.FrameworkPolicy).HasMaxLength(100);
    layoutVersion.Property(x => x.Name).HasMaxLength(200);
    layoutVersion.Property(x => x.Status).HasMaxLength(30);
    layoutVersion.Property(x => x.LayoutHash).HasMaxLength(64);
    layoutVersion.Property(x => x.ValidationSummary).HasMaxLength(4000);
    layoutVersion.Property(x => x.PublishReason).HasMaxLength(2000);
    layoutVersion.HasIndex(x => new { x.FirmId, x.ClientId, x.FrameworkPolicy, x.Name, x.VersionNumber }).IsUnique()
      .HasDatabaseName("ux_statement_layout_version_identity");
    layoutVersion.ToTable("statement_layout_versions", t => t.HasCheckConstraint("ck_statement_layout_version_values",
      "version_number >= 1 AND line_count >= 0 AND status IN ('DRAFT','VALIDATED','PUBLISHED') " +
      "AND ((status = 'PUBLISHED' AND published_by_user_id IS NOT NULL AND published_at IS NOT NULL) " +
      "OR (status <> 'PUBLISHED' AND published_by_user_id IS NULL AND published_at IS NULL))"));
    layoutVersion.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var layoutLine = b.Entity<StatementLayoutLine>();
    layoutLine.Property(x => x.LineCode).HasMaxLength(60);
    layoutLine.Property(x => x.Label).HasMaxLength(300);
    layoutLine.Property(x => x.LineKind).HasMaxLength(40);
    layoutLine.Property(x => x.Section).HasMaxLength(40);
    layoutLine.Property(x => x.DisplaySign).HasMaxLength(20);
    layoutLine.Property(x => x.TaxonomyNodeCode).HasMaxLength(100);
    layoutLine.Property(x => x.DenominatorLineCode).HasMaxLength(60);
    layoutLine.HasIndex(x => new { x.FirmId, x.LayoutVersionId, x.LineCode }).IsUnique()
      .HasDatabaseName("ux_statement_layout_line_code");
    layoutLine.ToTable("statement_layout_lines", t => t.HasCheckConstraint("ck_statement_layout_line_values",
      "line_order >= 0 AND length(trim(line_code)) > 0 AND length(trim(label)) > 0 " +
      "AND line_kind IN ('HEADING','MAPPED_TAXONOMY_BALANCE','SUM_CHILD_LINES','TOTAL_REFERENCED_LINES','RATIO') " +
      "AND section IN ('FINANCIAL_POSITION','PROFIT_OR_LOSS','OCI','CHANGES_IN_EQUITY','CASH_FLOWS','NOTES')"));
    layoutLine.HasOne<StatementLayoutVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.LayoutVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var mapping = b.Entity<MappingVersion>();
    mapping.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_mapping_versions_firm_id_id");
    mapping.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_mapping_versions_scope_id");
    mapping.Property(x => x.TaxonomyVersion).HasMaxLength(100);
    mapping.Property(x => x.PeriodStart).HasMaxLength(10);
    mapping.Property(x => x.PeriodEnd).HasMaxLength(10);
    mapping.Property(x => x.Status).HasMaxLength(16);
    mapping.HasIndex(x => new { x.FirmId, x.EngagementId, x.DatasetId, x.Version })
      .IsUnique().HasDatabaseName("ux_mapping_version_identity");
    mapping.ToTable("mapping_versions", t => t.HasCheckConstraint("ck_mapping_version_values",
      "version >= 1 AND generation >= 1 AND length(taxonomy_version) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND status IN ('DRAFT','APPROVED') AND ((status = 'DRAFT' AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (status = 'APPROVED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))"));
    mapping.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    mapping.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    mapping.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.DatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    mapping.HasOne<ClientChartVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.ClientChartVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    mapping.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    mapping.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var allocation = b.Entity<MappingAllocation>();
    allocation.Property(x => x.SourceAccountCode).HasMaxLength(100);
    allocation.Property(x => x.DestinationCode).HasMaxLength(100);
    allocation.Property(x => x.StatementSection).HasMaxLength(50);
    allocation.Property(x => x.AuditArea).HasMaxLength(100);
    allocation.Property(x => x.Rationale).HasMaxLength(2000);
    allocation.Property(x => x.ResidualPolicy).HasMaxLength(40).HasDefaultValue("LAST_DESTINATION");
    allocation.HasIndex(x => new { x.FirmId, x.MappingVersionId, x.SourceAccountCode, x.DestinationCode })
      .IsUnique().HasDatabaseName("ux_mapping_allocation_identity");
    allocation.ToTable("mapping_allocations", t => t.HasCheckConstraint("ck_mapping_allocation_values",
      "length(source_account_code) > 0 AND length(destination_code) > 0 AND length(statement_section) > 0 AND fraction > 0 AND fraction <= 1 AND length(rationale) > 0 AND residual_policy = 'LAST_DESTINATION'"));
    allocation.HasOne<MappingVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.MappingVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var adjusted = b.Entity<AdjustedTrialBalanceSnapshot>();
    adjusted.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_adjusted_tb_snapshots_firm_id_id");
    adjusted.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_adjusted_tb_snapshots_scope_id");
    adjusted.Property(x => x.Currency).HasMaxLength(3);
    adjusted.Property(x => x.ResultHash).HasMaxLength(64);
    adjusted.HasIndex(x => new { x.FirmId, x.AdjustmentPlanId })
      .IsUnique().HasDatabaseName("ux_adjusted_tb_snapshot_plan");
    adjusted.ToTable("adjusted_tb_snapshots", t => t.HasCheckConstraint("ck_adjusted_tb_snapshot_values",
      "revision >= 1 AND currency ~ '^[A-Z]{3}$' AND result_hash ~ '^[0-9a-f]{64}$'"));
    adjusted.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    adjusted.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    adjusted.HasOne<TrialBalanceDataset>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.BaseDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    adjusted.HasOne<AdjustmentPlan>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AdjustmentPlanId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    adjusted.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var adjustedRow = b.Entity<AdjustedTrialBalanceRow>();
    adjustedRow.Property(x => x.AccountCode).HasMaxLength(100);
    adjustedRow.Property(x => x.Currency).HasMaxLength(3);
    adjustedRow.HasIndex(x => new { x.SnapshotId, x.AccountCode }).IsUnique()
      .HasDatabaseName("ux_adjusted_tb_row_account");
    adjustedRow.ToTable("adjusted_tb_rows", t => t.HasCheckConstraint("ck_adjusted_tb_row_values",
      "length(account_code) > 0 AND currency ~ '^[A-Z]{3}$'"));
    adjustedRow.HasOne<AdjustedTrialBalanceSnapshot>().WithMany()
      .HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);

    var package = b.Entity<FinancialPackage>();
    package.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_financial_packages_firm_id_id");
    package.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_financial_packages_scope_id");
    package.Property(x => x.Basis).HasMaxLength(50);
    package.Property(x => x.Framework).HasMaxLength(100);
    package.Property(x => x.PeriodStart).HasMaxLength(10);
    package.Property(x => x.PeriodEnd).HasMaxLength(10);
    package.Property(x => x.TaxonomyVersion).HasMaxLength(100);
    package.Property(x => x.TemplateVersion).HasMaxLength(100);
    package.Property(x => x.CalculationEngineVersion).HasMaxLength(100);
    package.Property(x => x.CalculationHash).HasMaxLength(64);
    package.Property(x => x.Currency).HasMaxLength(3);
    package.Property(x => x.Status).HasMaxLength(20);
    package.Property(x => x.SupplementaryHash).HasMaxLength(64);
    package.Property(x => x.EquityHash).HasMaxLength(64);
    package.Property(x => x.ComparativeBasis).HasMaxLength(100);
    package.Property(x => x.ComparativeEvidenceReference).HasMaxLength(2000);
    package.HasIndex(x => new { x.FirmId, x.AdjustmentPlanId, x.MappingVersionId, x.TemplateVersion })
      .IsUnique().HasDatabaseName("ux_financial_package_identity");
    package.ToTable("financial_packages", t => t.HasCheckConstraint("ck_financial_package_values",
      "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED') AND (period_id IS NULL OR length(trim(basis)) > 0) AND (book_id IS NULL OR period_id IS NOT NULL) AND ((cash_beginning IS NULL AND cash_ending IS NULL AND supplementary_hash IS NULL AND equity_hash IS NULL) OR (cash_beginning IS NOT NULL AND cash_ending IS NOT NULL AND supplementary_hash ~ '^[0-9a-f]{64}$' AND (equity_hash IS NULL OR equity_hash ~ '^[0-9a-f]{64}$')))"));
    package.HasOne<PracticeClient>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<Engagement>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId })
      .HasPrincipalKey(x => new { x.FirmId, x.PracticeClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<AdjustedTrialBalanceSnapshot>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AdjustedDatasetId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<MappingVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.MappingVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<AdjustmentPlan>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AdjustmentPlanId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<ClientReportingPeriod>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<ClientReportingBook>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.BookId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    package.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ComparativePackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var packageArtifact = b.Entity<FinancialPackageArtifact>();
    packageArtifact.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_financial_package_artifacts_scope_id");
    packageArtifact.Property(x => x.PackageHash).HasMaxLength(64);
    packageArtifact.Property(x => x.ArtifactVersion).HasMaxLength(100);
    packageArtifact.Property(x => x.FrameworkVersion).HasMaxLength(100);
    packageArtifact.Property(x => x.TemplateVersion).HasMaxLength(100);
    packageArtifact.Property(x => x.ArtifactSha256Hex).HasMaxLength(64);
    packageArtifact.Property(x => x.ArtifactBytes).HasColumnType("bytea");
    packageArtifact.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.PackageRevision, x.PackageGeneration, x.ArtifactVersion })
      .IsUnique().HasDatabaseName("ux_financial_package_artifact_version");
    packageArtifact.ToTable("financial_package_artifacts", t => t.HasCheckConstraint("ck_financial_package_artifact_values",
      "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$' AND length(trim(artifact_version)) > 0 AND length(trim(framework_version)) > 0 AND length(trim(template_version)) > 0 AND artifact_sha256_hex ~ '^[0-9a-f]{64}$' AND octet_length(artifact_bytes) > 0"));
    packageArtifact.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    packageArtifact.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var packageLine = b.Entity<FinancialPackageLine>();
    packageLine.Property(x => x.SourceAccountCode).HasMaxLength(100);
    packageLine.Property(x => x.DestinationCode).HasMaxLength(100);
    packageLine.Property(x => x.StatementSection).HasMaxLength(50);
    packageLine.Property(x => x.Currency).HasMaxLength(3);
    packageLine.Property(x => x.RoundingResidual).HasPrecision(19, 6);
    packageLine.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.SourceAccountCode, x.DestinationCode })
      .IsUnique().HasDatabaseName("ux_financial_package_line_identity");
    packageLine.ToTable("financial_package_lines", t => t.HasCheckConstraint("ck_financial_package_line_values",
      "length(source_account_code) > 0 AND length(destination_code) > 0 AND length(statement_section) > 0 AND fraction > 0 AND fraction <= 1 AND currency ~ '^[A-Z]{3}$'"));
    packageLine.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    packageLine.HasOne<AdjustedTrialBalanceSnapshot>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AdjustedSnapshotId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var validation = b.Entity<FinancialPackageValidation>();
    validation.Property(x => x.Code).HasMaxLength(100);
    validation.Property(x => x.Detail).HasMaxLength(2000);
    validation.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.Code }).IsUnique()
      .HasDatabaseName("ux_financial_package_validation_code");
    validation.ToTable("financial_package_validations", t => t.HasCheckConstraint("ck_financial_package_validation_values",
      "length(code) > 0 AND length(detail) > 0"));
    validation.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var cashFlow = b.Entity<FinancialPackageCashFlowLine>();
    cashFlow.Property(x => x.Section).HasMaxLength(20);
    cashFlow.Property(x => x.Description).HasMaxLength(2000);
    cashFlow.Property(x => x.Currency).HasMaxLength(3);
    cashFlow.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.Section, x.Description })
      .IsUnique().HasDatabaseName("ux_financial_package_cash_flow_line_identity");
    cashFlow.ToTable("financial_package_cash_flow_lines", t => t.HasCheckConstraint("ck_financial_package_cash_flow_values",
      "section IN ('OPERATING','INVESTING','FINANCING') AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    cashFlow.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var packageSeal = b.Entity<FinancialPackageSeal>();
    packageSeal.Property(x => x.PackageHash).HasMaxLength(64).IsFixedLength();
    packageSeal.Property(x => x.ContentManifestDigest).HasMaxLength(64).IsFixedLength();
    packageSeal.Property(x => x.ArtifactManifestDigest).HasMaxLength(64).IsFixedLength();
    packageSeal.Property(x => x.SealVersion).HasMaxLength(40);
    packageSeal.Property(x => x.ArtifactManifestJson).HasMaxLength(20000);
    packageSeal.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.PackageRevision, x.PackageGeneration, x.ArtifactManifestDigest })
      .IsUnique().HasDatabaseName("ux_financial_package_seal_identity");
    packageSeal.ToTable("financial_package_seals", t => t.HasCheckConstraint("ck_financial_package_seal_values",
      "artifact_count > 0 AND total_byte_length > 0 AND length(trim(seal_version)) > 0 " +
      "AND package_hash ~ '^[0-9a-f]{64}$' AND content_manifest_digest ~ '^[0-9a-f]{64}$' " +
      "AND artifact_manifest_digest ~ '^[0-9a-f]{64}$'"));
    packageSeal.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var fxCashEffect = b.Entity<FinancialPackageFxEffect>();
    fxCashEffect.Property(x => x.CurrencyPair).HasMaxLength(20);
    fxCashEffect.Property(x => x.Currency).HasMaxLength(3);
    fxCashEffect.Property(x => x.EvidenceReference).HasMaxLength(2000);
    fxCashEffect.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.CurrencyPair, x.EvidenceReference })
      .HasDatabaseName("ux_financial_package_fx_effect_identity");
    fxCashEffect.ToTable("financial_package_fx_effects", t => t.HasCheckConstraint("ck_financial_package_fx_effect_values",
      "length(trim(currency_pair)) > 0 AND length(trim(evidence_reference)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    fxCashEffect.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var disclosure = b.Entity<FinancialPackageDisclosure>();
    disclosure.Property(x => x.Code).HasMaxLength(100);
    disclosure.Property(x => x.Response).HasMaxLength(4000);
    disclosure.Property(x => x.Rationale).HasMaxLength(2000);
    disclosure.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.Code })
      .IsUnique().HasDatabaseName("ux_financial_package_disclosure_code");
    disclosure.ToTable("financial_package_disclosures", t => t.HasCheckConstraint("ck_financial_package_disclosure_values",
      "length(trim(code)) > 0 AND ((not_applicable = false AND length(trim(response)) > 0) OR (not_applicable = true AND length(trim(rationale)) > 0))"));
    disclosure.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var equity = b.Entity<FinancialPackageEquityLine>();
    equity.Property(x => x.LineCode).HasMaxLength(100);
    equity.Property(x => x.Description).HasMaxLength(2000);
    equity.Property(x => x.Currency).HasMaxLength(3);
    equity.Property(x => x.EvidenceReference).HasMaxLength(2000);
    equity.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.LineCode })
      .IsUnique().HasDatabaseName("ux_financial_package_equity_line_identity");
    equity.ToTable("financial_package_equity_lines", t => t.HasCheckConstraint("ck_financial_package_equity_line_values",
      "length(trim(line_code)) > 0 AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0"));
    equity.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var noteLine = b.Entity<FinancialPackageNoteLine>();
    noteLine.Property(x => x.NoteCode).HasMaxLength(100);
    noteLine.Property(x => x.FaceDestinationCode).HasMaxLength(100);
    noteLine.Property(x => x.Currency).HasMaxLength(3);
    noteLine.Property(x => x.EvidenceReference).HasMaxLength(2000);
    noteLine.HasIndex(x => new { x.FirmId, x.FinancialPackageId, x.NoteCode, x.FaceDestinationCode })
      .IsUnique().HasDatabaseName("ux_financial_package_note_line_identity");
    noteLine.ToTable("financial_package_note_lines", t => t.HasCheckConstraint("ck_financial_package_note_line_values",
      "length(trim(note_code)) > 0 AND length(trim(face_destination_code)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0"));
    noteLine.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var packageReview = b.Entity<FinancialPackageReviewDecision>();
    packageReview.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_financial_package_review_decisions_scope_id");
    packageReview.Property(x => x.PackageHash).HasMaxLength(64);
    packageReview.Property(x => x.ArtifactVersion).HasMaxLength(100);
    packageReview.Property(x => x.ArtifactSha256Hex).HasMaxLength(64);
    packageReview.Property(x => x.Stage).HasMaxLength(40);
    packageReview.Property(x => x.Decision).HasMaxLength(30);
    packageReview.Property(x => x.EvidenceMode).HasMaxLength(20);
    packageReview.Property(x => x.EvidenceReference).HasMaxLength(2000);
    packageReview.Property(x => x.Comment).HasMaxLength(4000);
    packageReview.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId, x.Stage, x.DecidedAt })
      .HasDatabaseName("ix_financial_package_review_stage");
    packageReview.ToTable("financial_package_review_decisions", t => t.HasCheckConstraint("ck_financial_package_review_values",
      "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$'" +
      " AND artifact_sha256_hex ~ '^[0-9a-f]{64}$' AND length(trim(artifact_version)) > 0" +
      " AND stage IN ('MANAGEMENT_APPROVAL','ACCOUNTING_REVIEW','PARTNER_APPROVAL')" +
      " AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED')" +
      " AND evidence_mode IN ('SIGNED_IN','OFFLINE') AND length(trim(evidence_reference)) > 0" +
      " AND ((evidence_mode = 'SIGNED_IN' AND decided_by_user_id IS NOT NULL) OR evidence_mode = 'OFFLINE')"));
    ScopeToEngagement(packageReview, nameof(FinancialPackageReviewDecision.FirmId), nameof(FinancialPackageReviewDecision.ClientId), nameof(FinancialPackageReviewDecision.EngagementId));
    packageReview.HasOne<FinancialPackage>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    packageReview.HasOne<FinancialPackageArtifact>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.FinancialPackageArtifactId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    packageReview.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.DecidedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    b.Entity<QuestionnaireTemplate>(entity =>
    {
      entity.ToTable("questionnaire_templates");
      entity.HasKey(x => x.Id);
      entity.HasIndex(x => new { x.Bank, x.Version }).IsUnique();
    });

    b.Entity<QuestionDefinition>(entity =>
    {
      entity.ToTable("question_definitions");
      entity.HasKey(x => x.Id);
      entity.HasIndex(x => new { x.TemplateId, x.QuestionCode }).IsUnique();
      entity.HasOne<QuestionnaireTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
    });
  }
}
