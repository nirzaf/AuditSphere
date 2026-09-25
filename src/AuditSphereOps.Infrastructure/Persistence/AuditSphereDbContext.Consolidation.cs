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
  private static void ConfigureConsolidation(ModelBuilder b)
  {
    var scope = b.Entity<ConsolidationScopeVersion>();
    scope.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.Id }).HasName("ak_consolidation_scopes_group_id");
    scope.Property(x => x.GroupRevision);
    scope.Property(x => x.OpeningRunHash).HasMaxLength(64);
    scope.Property(x => x.OpeningTranslationManifestHash).HasMaxLength(64);
    scope.Property(x => x.OpeningTranslationReserve).HasPrecision(20, 6);
    scope.Property(x => x.RecurringEliminationManifest).HasMaxLength(64);
    scope.Property(x => x.ReportingCurrency).HasMaxLength(3);
    scope.Property(x => x.Method).HasMaxLength(100);
    scope.Property(x => x.Status).HasMaxLength(30);
    scope.Property(x => x.OpeningBasis).HasMaxLength(100);
    scope.Property(x => x.TranslationRateType).HasMaxLength(30);
    scope.HasIndex(x => new { x.FirmId, x.GroupId, x.PeriodId, x.Version }).IsUnique().HasDatabaseName("ux_consolidation_scope_version");
    scope.ToTable("consolidation_scope_versions", t => t.HasCheckConstraint("ck_consolidation_scope_values",
      "reporting_currency ~ '^[A-Z]{3}$' AND length(trim(method)) > 0 AND length(trim(opening_basis)) > 0"));
    scope.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    scope.HasOne<ExchangeRateSetVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ExchangeRateSetVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    scope.HasOne<TranslationPolicyVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.TranslationPolicyVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var component = b.Entity<ConsolidationComponent>();
    component.Property(x => x.PackageHash).HasMaxLength(64);
    component.Property(x => x.SourceType).HasMaxLength(30);
    component.Property(x => x.PeriodBasis).HasMaxLength(100);
    component.Property(x => x.TaxonomyVersion).HasMaxLength(100);
    component.Property(x => x.MappingVersion).HasMaxLength(100);
    component.Property(x => x.Currency).HasMaxLength(3);
    component.Property(x => x.ControlMethod).HasMaxLength(100);
    component.Property(x => x.Status).HasMaxLength(30);
    component.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.ClientId }).IsUnique().HasDatabaseName("ux_consolidation_component_client");
    component.ToTable("consolidation_components", t => t.HasCheckConstraint("ck_consolidation_component_values",
      "package_hash ~ '^[0-9a-f]{64}$' AND source_type IN ('INTERNAL_PACKAGE','EXTERNAL_PACK') AND ((source_type = 'INTERNAL_PACKAGE' AND package_id IS NOT NULL AND external_component_pack_id IS NULL) OR (source_type = 'EXTERNAL_PACK' AND package_id IS NULL AND external_component_pack_id IS NOT NULL)) AND currency ~ '^[A-Z]{3}$' AND ownership_percent >= 0 AND ownership_percent <= 100"));
    component.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    component.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    component.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    component.HasOne<FinancialPackage>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PackageId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    component.HasOne<ExternalComponentPack>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ExternalComponentPackId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var externalPack = b.Entity<ExternalComponentPack>();
    externalPack.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_external_component_packs_scope_id");
    externalPack.Property(x => x.PeriodStart).HasMaxLength(10);
    externalPack.Property(x => x.PeriodEnd).HasMaxLength(10);
    externalPack.Property(x => x.Framework).HasMaxLength(100);
    externalPack.Property(x => x.ReportingCurrency).HasMaxLength(3);
    externalPack.Property(x => x.PeriodBasis).HasMaxLength(100);
    externalPack.Property(x => x.TaxonomyVersion).HasMaxLength(100);
    externalPack.Property(x => x.MappingVersion).HasMaxLength(100);
    externalPack.Property(x => x.SourceReference).HasMaxLength(2000);
    externalPack.Property(x => x.RawSourceHash).HasMaxLength(64);
    externalPack.Property(x => x.NormalizedSourceDigest).HasMaxLength(64);
    externalPack.Property(x => x.PackDigest).HasMaxLength(64);
    externalPack.Property(x => x.ReconciliationStatus).HasMaxLength(30);
    externalPack.Property(x => x.ReconciliationReference).HasMaxLength(2000);
    externalPack.Property(x => x.CompatibilityBridgeStatus).HasMaxLength(30);
    externalPack.Property(x => x.CompatibilityBridgeReference).HasMaxLength(2000);
    externalPack.Property(x => x.ReturnReason).HasMaxLength(2000);
    externalPack.Property(x => x.Status).HasMaxLength(30);
    externalPack.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.ClientId, x.Version }).IsUnique()
      .HasDatabaseName("ux_external_component_pack_version");
    externalPack.ToTable("external_component_packs", t => t.HasCheckConstraint("ck_external_component_pack_values",
      "period_start ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_end ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_start <= period_end AND framework <> '' AND reporting_currency ~ '^[A-Z]{3}$' AND length(trim(period_basis)) > 0 AND length(trim(taxonomy_version)) > 0 AND length(trim(mapping_version)) > 0 AND length(trim(source_reference)) > 0 AND raw_source_hash ~ '^[0-9a-f]{64}$' AND normalized_source_digest ~ '^[0-9a-f]{64}$' AND pack_digest ~ '^[0-9a-f]{64}$' AND status IN ('SUBMITTED','RESUBMITTED','RETURNED','APPROVED') AND reconciliation_status IN ('PENDING','RECONCILED') AND compatibility_bridge_status IN ('NONE','PENDING','APPROVED') AND (compatibility_bridge_status = 'NONE' OR length(trim(compatibility_bridge_reference)) > 0)"));
    externalPack.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    externalPack.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    externalPack.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    externalPack.HasOne<ExternalComponentPack>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.PriorPackId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var externalPackLine = b.Entity<ExternalComponentPackLine>();
    externalPackLine.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_external_component_pack_lines_scope_id");
    externalPackLine.Property(x => x.TaxonomyCode).HasMaxLength(100);
    externalPackLine.Property(x => x.Currency).HasMaxLength(3);
    externalPackLine.Property(x => x.SourceLineReference).HasMaxLength(200);
    externalPackLine.HasIndex(x => new { x.FirmId, x.ExternalComponentPackId, x.TaxonomyCode }).HasDatabaseName("ix_external_component_pack_line");
    externalPackLine.ToTable("external_component_pack_lines", t => t.HasCheckConstraint("ck_external_component_pack_line_values",
      "length(trim(taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(source_line_reference)) > 0"));
    externalPackLine.HasOne<ExternalComponentPack>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ExternalComponentPackId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var ownership = b.Entity<OwnershipInterestVersion>();
    ownership.Property(x => x.ControlAssessment).HasMaxLength(100);
    ownership.Property(x => x.Method).HasMaxLength(100);
    ownership.Property(x => x.EvidenceReference).HasMaxLength(2000);
    ownership.Property(x => x.Status).HasMaxLength(30);
    ownership.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.ParentClientId, x.ChildClientId }).IsUnique()
      .HasDatabaseName("ux_ownership_interest_pair");
    ownership.ToTable("ownership_interest_versions", t => t.HasCheckConstraint("ck_ownership_interest_values",
      "effective_to IS NULL OR effective_from <= effective_to" +
      " AND ownership_percent >= 0 AND ownership_percent <= 100 AND economic_interest_percent >= 0 AND economic_interest_percent <= 100"));
    ownership.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    ownership.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    ownership.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ParentClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    ownership.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.ChildClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var match = b.Entity<IntercompanyMatch>();
    match.Property(x => x.AccountNature).HasMaxLength(100);
    match.Property(x => x.MatchMode).HasMaxLength(20);
    match.Property(x => x.MatchGroupReference).HasMaxLength(200);
    match.Property(x => x.SellerTaxonomyCode).HasMaxLength(100);
    match.Property(x => x.BuyerTaxonomyCode).HasMaxLength(100);
    match.Property(x => x.PeriodCode).HasMaxLength(50);
    match.Property(x => x.Currency).HasMaxLength(3);
    match.Property(x => x.TransactionReference).HasMaxLength(200);
    match.Property(x => x.Status).HasMaxLength(30);
    match.Property(x => x.DifferenceReason).HasMaxLength(2000);
    match.Property(x => x.EvidenceReference).HasMaxLength(2000);
    match.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.SellerClientId, x.BuyerClientId, x.TransactionReference }).IsUnique()
      .HasDatabaseName("ux_intercompany_match_identity");
    match.ToTable("intercompany_matches", t => t.HasCheckConstraint("ck_intercompany_match_values",
      "length(trim(account_nature)) > 0 AND match_mode IN ('ONE_TO_ONE','GROUPED') AND (match_mode <> 'GROUPED' OR length(trim(match_group_reference)) > 0) AND length(trim(seller_taxonomy_code)) > 0 AND length(trim(buyer_taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    match.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    match.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    match.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.SellerClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    match.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.BuyerClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var journal = b.Entity<ConsolidationJournal>();
    journal.Property(x => x.JournalNumber).HasMaxLength(100);
    journal.Property(x => x.JournalType).HasMaxLength(100);
    journal.Property(x => x.Currency).HasMaxLength(3);
    journal.Property(x => x.EvidenceReference).HasMaxLength(2000);
    journal.Property(x => x.ReturnReason).HasMaxLength(2000);
    journal.Property(x => x.Status).HasMaxLength(30);
    journal.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.JournalNumber }).IsUnique().HasDatabaseName("ux_consolidation_journal_number");
    journal.ToTable("consolidation_journals", t => t.HasCheckConstraint("ck_consolidation_journal_values",
      "currency ~ '^[A-Z]{3}$' AND total_debits = total_credits_abs"));
    journal.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    journal.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var methodSchedule = b.Entity<AdvancedConsolidationMethodSchedule>();
    methodSchedule.Property(x => x.Method).HasMaxLength(100);
    methodSchedule.Property(x => x.Framework).HasMaxLength(100);
    methodSchedule.Property(x => x.SourceManifestJson).HasMaxLength(20000);
    methodSchedule.Property(x => x.SourceManifestDigest).HasMaxLength(64);
    methodSchedule.Property(x => x.InputSnapshotJson).HasMaxLength(20000);
    methodSchedule.Property(x => x.InputSnapshotDigest).HasMaxLength(64);
    methodSchedule.Property(x => x.Status).HasMaxLength(30);
    methodSchedule.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.Method, x.InputSnapshotDigest })
      .IsUnique().HasDatabaseName("ux_advanced_method_schedule_input");
    methodSchedule.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id })
      .HasName("ak_advanced_method_schedules_scope_id");
    methodSchedule.ToTable("advanced_consolidation_method_schedules", t => t.HasCheckConstraint("ck_advanced_method_schedule_values",
      "group_revision >= 1 AND method IN ('FOREIGN_CURRENCY_RESERVE_V1','ACQUISITION_NCI_V1','OWNERSHIP_CHANGE_V1','NESTED_GROUP_V1','ASSET_TRANSFER_ELIMINATION_V1') AND length(trim(framework)) > 0 AND length(trim(source_manifest_json)) > 0 AND source_manifest_digest ~ '^[0-9a-f]{64}$' AND length(trim(input_snapshot_json)) > 0 AND input_snapshot_digest ~ '^[0-9a-f]{64}$' AND status IN ('SUBMITTED','APPROVED')"));
    methodSchedule.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    methodSchedule.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    methodSchedule.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var advancedExecution = b.Entity<AdvancedConsolidationExecution>();
    advancedExecution.Property(x => x.Method).HasMaxLength(100);
    advancedExecution.Property(x => x.Framework).HasMaxLength(100);
    advancedExecution.Property(x => x.EngineVersion).HasMaxLength(100);
    advancedExecution.Property(x => x.InputManifestJson).HasMaxLength(30000);
    advancedExecution.Property(x => x.InputManifestDigest).HasMaxLength(64);
    advancedExecution.Property(x => x.ComparativeStatementJson).HasMaxLength(30000);
    advancedExecution.Property(x => x.ComparativeStatementDigest).HasMaxLength(64);
    advancedExecution.Property(x => x.CurrentStatementJson).HasMaxLength(30000);
    advancedExecution.Property(x => x.CurrentStatementDigest).HasMaxLength(64);
    advancedExecution.Property(x => x.OutputManifest).HasMaxLength(10000);
    advancedExecution.Property(x => x.OutputDigest).HasMaxLength(64);
    advancedExecution.Property(x => x.Status).HasMaxLength(30);
    advancedExecution.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.Method, x.InputManifestDigest })
      .IsUnique().HasDatabaseName("ux_advanced_consolidation_execution");
    advancedExecution.ToTable("advanced_consolidation_executions", t => t.HasCheckConstraint("ck_advanced_consolidation_execution_values",
      "group_revision >= 1 AND method IN ('FOREIGN_CURRENCY_RESERVE_V1','ACQUISITION_NCI_V1','OWNERSHIP_CHANGE_V1','NESTED_GROUP_V1','ASSET_TRANSFER_ELIMINATION_V1') AND length(trim(framework)) > 0 AND length(trim(engine_version)) > 0 AND length(trim(output_manifest)) > 0 AND input_manifest_digest ~ '^[0-9a-f]{64}$' AND comparative_statement_digest ~ '^[0-9a-f]{64}$' AND current_statement_digest ~ '^[0-9a-f]{64}$' AND output_digest ~ '^[0-9a-f]{64}$' AND status IN ('VERIFIED','APPROVED')"));
    advancedExecution.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    advancedExecution.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    advancedExecution.HasOne<AdvancedConsolidationMethodSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    advancedExecution.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var journalLine = b.Entity<ConsolidationJournalLine>();
    journalLine.Property(x => x.TaxonomyCode).HasMaxLength(100);
    journalLine.Property(x => x.Currency).HasMaxLength(3);
    journalLine.Property(x => x.Description).HasMaxLength(1000);
    journalLine.HasIndex(x => new { x.FirmId, x.ConsolidationJournalId, x.TaxonomyCode }).HasDatabaseName("ix_consolidation_journal_line");
    journalLine.ToTable("consolidation_journal_lines", t => t.HasCheckConstraint("ck_consolidation_journal_line_values",
      "length(trim(taxonomy_code)) > 0 AND debit >= 0 AND credit >= 0 AND NOT (debit > 0 AND credit > 0) AND currency ~ '^[A-Z]{3}$'"));
    journalLine.HasOne<ConsolidationJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ConsolidationJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    journalLine.HasOne<IntercompanyMatch>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.IntercompanyMatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    journal.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_consolidation_journals_scope_id");
    match.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_intercompany_matches_scope_id");

    var run = b.Entity<ConsolidationRun>();
    run.Property(x => x.EngineVersion).HasMaxLength(100);
    run.Property(x => x.InputManifest).HasMaxLength(20000);
    run.Property(x => x.RunHash).HasMaxLength(64);
    run.Property(x => x.ReportingCurrency).HasMaxLength(3);
    run.Property(x => x.Status).HasMaxLength(30);
    run.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.RunHash }).IsUnique().HasDatabaseName("ux_consolidation_run_hash");
    run.ToTable("consolidation_runs", t => t.HasCheckConstraint("ck_consolidation_run_values",
      "engine_version <> '' AND run_hash ~ '^[0-9a-f]{64}$' AND reporting_currency ~ '^[A-Z]{3}$'"));
    run.HasOne<ClientGroup>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    run.HasOne<ConsolidationScopeVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var runLine = b.Entity<ConsolidationRunLine>();
    runLine.Property(x => x.TaxonomyCode).HasMaxLength(100);
    runLine.Property(x => x.Currency).HasMaxLength(3);
    runLine.HasIndex(x => new { x.FirmId, x.RunId, x.TaxonomyCode, x.ComponentId, x.ConsolidationJournalId })
      .HasDatabaseName("ix_consolidation_run_line");
    runLine.ToTable("consolidation_run_lines", t => t.HasCheckConstraint("ck_consolidation_run_line_values",
      "length(trim(taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$'"));
    runLine.HasOne<ConsolidationRun>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.RunId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    runLine.HasOne<ConsolidationComponent>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ComponentId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    runLine.HasOne<ConsolidationJournal>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ConsolidationJournalId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    runLine.HasOne<IntercompanyMatch>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.IntercompanyMatchId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    run.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_consolidation_runs_scope_id");
    component.HasAlternateKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).HasName("ak_consolidation_components_scope_id");

    var rateSet = b.Entity<ExchangeRateSetVersion>();
    rateSet.Property(x => x.Code).HasMaxLength(100);
    rateSet.Property(x => x.Version);
    rateSet.Property(x => x.Source).HasMaxLength(200);
    rateSet.Property(x => x.Status).HasMaxLength(30);
    rateSet.HasIndex(x => new { x.FirmId, x.Code }).IsUnique().HasDatabaseName("ux_exchange_rate_set_code");
    rateSet.ToTable("exchange_rate_set_versions", t => t.HasCheckConstraint("ck_exchange_rate_set_values",
      "length(trim(code)) > 0 AND length(trim(source)) > 0 AND version > 0 AND (effective_from IS NULL OR effective_to IS NULL OR effective_from <= effective_to)"));

    var rate = b.Entity<ExchangeRate>();
    rate.Property(x => x.FromCurrency).HasMaxLength(3);
    rate.Property(x => x.ToCurrency).HasMaxLength(3);
    rate.Property(x => x.RateType).HasMaxLength(30);
    rate.Property(x => x.Direction).HasMaxLength(30);
    rate.HasAlternateKey(x => new { x.FirmId, x.RateSetVersionId, x.Id }).HasName("ak_exchange_rates_set_id");
    rate.HasIndex(x => new { x.FirmId, x.RateSetVersionId, x.FromCurrency, x.ToCurrency, x.RateDate, x.RateType }).IsUnique()
      .HasDatabaseName("ux_exchange_rate_identity");
    rate.ToTable("exchange_rates", t => t.HasCheckConstraint("ck_exchange_rate_values",
      "from_currency ~ '^[A-Z]{3}$' AND to_currency ~ '^[A-Z]{3}$' AND rate > 0 AND from_currency <> to_currency"));
    rate.HasOne<ExchangeRateSetVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.RateSetVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var policy = b.Entity<TranslationPolicyVersion>();
    policy.Property(x => x.Code).HasMaxLength(100);
    policy.Property(x => x.FunctionalCurrency).HasMaxLength(3);
    policy.Property(x => x.PresentationCurrency).HasMaxLength(3);
    policy.Property(x => x.ClosingRateRule).HasMaxLength(100);
    policy.Property(x => x.AverageRateRule).HasMaxLength(100);
    policy.Property(x => x.HistoricalRateRule).HasMaxLength(100);
    policy.Property(x => x.Status).HasMaxLength(30);
    policy.HasIndex(x => new { x.FirmId, x.Code }).IsUnique().HasDatabaseName("ux_translation_policy_code");
    policy.ToTable("translation_policy_versions", t => t.HasCheckConstraint("ck_translation_policy_values",
      "functional_currency ~ '^[A-Z]{3}$' AND presentation_currency ~ '^[A-Z]{3}$'"));

    var translation = b.Entity<TranslationResult>();
    translation.Property(x => x.SourcePackageHash).HasMaxLength(64);
    translation.Property(x => x.CalculationVersion).HasMaxLength(40);
    translation.Property(x => x.RateType).HasMaxLength(30);
    translation.Property(x => x.FromCurrency).HasMaxLength(3);
    translation.Property(x => x.ToCurrency).HasMaxLength(3);
    translation.Property(x => x.Status).HasMaxLength(30);
    translation.HasIndex(x => new { x.FirmId, x.ScopeVersionId, x.ComponentId, x.RateSetVersionId, x.TranslationPolicyVersionId, x.CalculationVersion }).IsUnique()
      .HasDatabaseName("ux_translation_result_input");
    translation.ToTable("translation_results", t => t.HasCheckConstraint("ck_translation_result_values",
      "from_currency ~ '^[A-Z]{3}$' AND to_currency ~ '^[A-Z]{3}$'"));
    translation.HasOne<ConsolidationComponent>().WithMany().HasForeignKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.ComponentId })
      .HasPrincipalKey(x => new { x.FirmId, x.GroupId, x.ScopeVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    translation.HasOne<ExchangeRateSetVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.RateSetVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    translation.HasOne<TranslationPolicyVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.TranslationPolicyVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var remeasurement = b.Entity<CurrencyRemeasurementSchedule>();
    remeasurement.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("ak_currency_remeasurement_schedules_scope_id");
    remeasurement.Property(x => x.FunctionalCurrency).HasMaxLength(3);
    remeasurement.Property(x => x.InputHash).HasMaxLength(64);
    remeasurement.Property(x => x.Status).HasMaxLength(30);
    remeasurement.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.PeriodId, x.InputHash }).IsUnique()
      .HasDatabaseName("ux_currency_remeasurement_input");
    remeasurement.ToTable("currency_remeasurement_schedules", t => t.HasCheckConstraint("ck_currency_remeasurement_schedule_values",
      "functional_currency ~ '^[A-Z]{3}$' AND input_hash ~ '^[0-9a-f]{64}$' AND item_count > 0 " +
      "AND status IN ('SUBMITTED','APPROVED','STALE') AND ((status = 'APPROVED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL) " +
      "OR (status <> 'APPROVED' AND approved_by_user_id IS NULL AND approved_at IS NULL))"));
    remeasurement.HasOne<ClientReportingPeriod>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.PeriodId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    remeasurement.HasOne<ExchangeRateSetVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.RateSetVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    remeasurement.HasOne<TranslationPolicyVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.TranslationPolicyVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    remeasurement.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    remeasurement.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var remeasurementItem = b.Entity<CurrencyRemeasurementItem>();
    remeasurementItem.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId, x.Id })
      .HasName("ak_currency_remeasurement_items_scope_id");
    remeasurementItem.Property(x => x.StableItemReference).HasMaxLength(200);
    remeasurementItem.Property(x => x.SourceEvidenceSha256).HasMaxLength(64);
    remeasurementItem.Property(x => x.SourceGlLineDigest).HasMaxLength(64);
    remeasurementItem.Property(x => x.ForeignCurrency).HasMaxLength(3);
    remeasurementItem.Property(x => x.RateType).HasMaxLength(30);
    remeasurementItem.HasIndex(x => new { x.FirmId, x.ScheduleId, x.StableItemReference }).IsUnique()
      .HasDatabaseName("ux_currency_remeasurement_item_reference");
    remeasurementItem.ToTable("currency_remeasurement_items", t =>
    {
      t.HasCheckConstraint("ck_currency_remeasurement_item_values",
        "source_evidence_sha256 ~ '^[0-9a-f]{64}$' AND foreign_currency ~ '^[A-Z]{3}$' AND stable_item_reference <> '' " +
        "AND applied_rate > 0 AND rate_type <> ''");
      t.HasCheckConstraint("ck_currency_remeasurement_gl_source",
        "(source_general_ledger_line_id IS NULL AND source_gl_line_digest = '') OR " +
        "(source_general_ledger_line_id IS NOT NULL AND source_gl_line_digest ~ '^[0-9a-f]{64}$')");
    });
    remeasurementItem.HasOne<CurrencyRemeasurementSchedule>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ScheduleId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    remeasurementItem.HasOne<DocumentSnapshot>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.EvidenceSnapshotId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    remeasurementItem.HasOne<GeneralLedgerLine>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceGeneralLedgerLineId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    remeasurementItem.HasOne<ExchangeRate>().WithMany().HasForeignKey(x => new { x.FirmId, x.RateSetVersionId, x.ExchangeRateId })
      .HasPrincipalKey(x => new { x.FirmId, x.RateSetVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
