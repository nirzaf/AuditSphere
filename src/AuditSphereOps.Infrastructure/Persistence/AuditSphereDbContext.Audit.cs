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
  // Audit planning and execution evidence (§§19–23, 27.2, 42.3–42.4). Every row carries the full
  // (firm, client, engagement) triple and binds it to a real engagement through a composite foreign
  // key, so a cross-scope link is rejected by PostgreSQL rather than only by application code.
  private static void ConfigureAudit(ModelBuilder b)
  {
    var materiality = b.Entity<MaterialityAssessment>();
    materiality.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_materiality_assessments_firm_id_id");
    materiality.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_materiality_assessments_scope_id");
    materiality.Property(x => x.BenchmarkSource).HasMaxLength(200);
    materiality.Property(x => x.BenchmarkVersion).HasMaxLength(100);
    materiality.Property(x => x.Status).HasMaxLength(30);
    materiality.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status, x.CreatedAt })
      .HasDatabaseName("ix_materiality_scope_status_created");
    materiality.ToTable("materiality_assessments", t => t.HasCheckConstraint("ck_materiality_values",
      "length(trim(benchmark_source)) > 0 AND length(trim(benchmark_version)) > 0 AND length(trim(rationale)) > 0" +
      " AND benchmark_amount > 0 AND rate_applied > 0 AND rate_applied <= 1 AND overall_materiality > 0" +
      " AND performance_materiality < overall_materiality AND clearly_trivial_threshold < performance_materiality" +
      " AND status IN ('DRAFT','APPROVED')"));
    ScopeToEngagement(materiality, nameof(MaterialityAssessment.FirmId),
      nameof(MaterialityAssessment.ClientId), nameof(MaterialityAssessment.EngagementId));
    materiality.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    var materialityApproval = b.Entity<MaterialityApproval>();
    materialityApproval.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_materiality_approvals_scope_id");
    materialityApproval.HasIndex(x => new { x.FirmId, x.MaterialityAssessmentId }).IsUnique()
      .HasDatabaseName("ux_materiality_approval_assessment");
    materialityApproval.ToTable("materiality_approvals");
    ScopeToEngagement(materialityApproval, nameof(MaterialityApproval.FirmId), nameof(MaterialityApproval.ClientId), nameof(MaterialityApproval.EngagementId));
    materialityApproval.HasOne<MaterialityAssessment>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.MaterialityAssessmentId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    materialityApproval.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var risk = b.Entity<AuditRisk>();
    risk.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_risks_firm_id_id");
    risk.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_risks_scope_id");
    risk.Property(x => x.AccountArea).HasMaxLength(200);
    risk.Property(x => x.Assertion).HasMaxLength(100);
    risk.Property(x => x.Severity).HasMaxLength(30);
    risk.Property(x => x.SignificanceDecision).HasMaxLength(30);
    risk.Property(x => x.Status).HasMaxLength(30);
    risk.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_audit_risks_scope_status");
    risk.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.CreatedAt })
      .HasDatabaseName("ix_audit_risks_scope_created");
    risk.ToTable("audit_risks", t => t.HasCheckConstraint("ck_audit_risk_values",
      "length(trim(account_area)) > 0 AND length(trim(assertion)) > 0 AND length(trim(description)) > 0" +
      " AND length(trim(drivers)) > 0 AND length(trim(response_description)) > 0" +
      " AND significance_decision IN ('SIGNIFICANT','NORMAL') AND status IN ('IDENTIFIED','ASSESSED','RESPONDED')" +
      " AND severity = CASE WHEN significance_decision = 'SIGNIFICANT' THEN 'Significant' ELSE 'Normal' END"));
    ScopeToEngagement(risk, nameof(AuditRisk.FirmId), nameof(AuditRisk.ClientId), nameof(AuditRisk.EngagementId));
    risk.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var programVersion = b.Entity<AuditProgramVersion>();
    programVersion.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_audit_program_versions_firm_id_id");
    programVersion.Property(x => x.ProgramCode).HasMaxLength(100);
    programVersion.Property(x => x.Version).HasMaxLength(100);
    programVersion.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength();
    programVersion.Property(x => x.Status).HasMaxLength(20);
    programVersion.HasIndex(x => new { x.FirmId, x.ProgramCode, x.Version }).IsUnique()
      .HasDatabaseName("ux_audit_program_version_code_version");
    programVersion.ToTable("audit_program_versions", t => t.HasCheckConstraint("ck_audit_program_version_values",
      "length(trim(program_code)) > 0 AND length(trim(version)) > 0 AND length(source_hash) = 64" +
      " AND status IN ('DRAFT','PUBLISHED','RETIRED')" +
      " AND ((status = 'PUBLISHED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL) OR status <> 'PUBLISHED')"));
    programVersion.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.CreatedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    programVersion.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApprovedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var programProcedure = b.Entity<AuditProgramProcedure>();
    programProcedure.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_audit_program_procedures_firm_id_id");
    programProcedure.HasAlternateKey(x => new { x.FirmId, x.ProgramVersionId, x.SourceProcedureId })
      .HasName("AK_audit_program_procedures_version_source");
    programProcedure.Property(x => x.SourceProcedureId).HasMaxLength(20);
    programProcedure.Property(x => x.SectionTitle).HasMaxLength(200);
    programProcedure.Property(x => x.SourceWording).HasMaxLength(2000);
    programProcedure.Property(x => x.ApplicabilityCondition).HasMaxLength(1000);
    programProcedure.Property(x => x.ExpectedEvidence).HasMaxLength(2000);
    programProcedure.ToTable("audit_program_procedures", t => t.HasCheckConstraint("ck_audit_program_procedure_values",
      "section_number BETWEEN 1 AND 20 AND ordinal >= 1 AND length(trim(source_procedure_id)) > 0" +
      " AND length(trim(section_title)) > 0 AND length(trim(source_wording)) > 0"));
    programProcedure.HasIndex(x => new { x.FirmId, x.ProgramVersionId, x.SectionNumber, x.Ordinal })
      .HasDatabaseName("ix_audit_program_procedures_version_order");
    programProcedure.HasOne<AuditProgramVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ProgramVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var engagementProgram = b.Entity<EngagementAuditProgram>();
    engagementProgram.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_engagement_audit_programs_firm_id_id");
    engagementProgram.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProgramVersionId })
      .HasName("AK_engagement_audit_programs_scope_version");
    engagementProgram.Property(x => x.Status).HasMaxLength(20);
    engagementProgram.ToTable("engagement_audit_programs", t => t.HasCheckConstraint("ck_engagement_audit_program_values",
      "status IN ('ADOPTED','SUPERSEDED')"));
    ScopeToEngagement(engagementProgram, nameof(EngagementAuditProgram.FirmId),
      nameof(EngagementAuditProgram.ClientId), nameof(EngagementAuditProgram.EngagementId));
    engagementProgram.HasOne<AuditProgramVersion>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ProgramVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    engagementProgram.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.AdoptedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var population = b.Entity<PopulationVersion>();
    population.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_population_versions_firm_id_id");
    population.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_population_versions_scope_id");
    population.Property(x => x.Purpose).HasMaxLength(300);
    population.Property(x => x.Assertion).HasMaxLength(100);
    population.Property(x => x.SourceReceiptReference).HasColumnName("source_receipt_ref").HasMaxLength(200);
    population.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
    population.Property(x => x.Status).HasMaxLength(30);
    population.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_population_scope_status");
    population.ToTable("population_versions", t => t.HasCheckConstraint("ck_population_values",
      "length(trim(purpose)) > 0 AND length(trim(assertion)) > 0 AND length(trim(source_receipt_ref)) > 0" +
      " AND length(trim(extraction_parameters)) > 0 AND row_count >= 0 AND monetary_control_total >= 0" +
      " AND currency ~ '^[A-Z]{3}$' AND status IN ('PENDING_APPROVAL','APPROVED','REJECTED')"));
    ScopeToEngagement(population, nameof(PopulationVersion.FirmId), nameof(PopulationVersion.ClientId),
      nameof(PopulationVersion.EngagementId));
    population.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var workpaper = b.Entity<Workpaper>();
    workpaper.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_workpapers_firm_id_id");
    workpaper.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_workpapers_scope_id");
    workpaper.Property(x => x.Index).HasColumnName("wp_index").HasMaxLength(30);
    workpaper.Property(x => x.Title).HasMaxLength(300);
    workpaper.Property(x => x.TemplateVersion).HasMaxLength(100);
    workpaper.Property(x => x.Status).HasMaxLength(30);
    workpaper.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_workpapers_scope_status");
    workpaper.ToTable("workpapers", t => t.HasCheckConstraint("ck_workpaper_values",
      "revision > 0 AND length(trim(wp_index)) > 0 AND length(trim(title)) > 0 AND length(trim(objective)) > 0" +
      " AND length(trim(template_version)) > 0 AND length(trim(procedure)) > 0" +
      " AND status IN ('WORKING','SUBMITTED_SNAPSHOT')" +
      " AND ((status = 'SUBMITTED_SNAPSHOT' AND submitted_at IS NOT NULL AND length(trim(conclusion)) > 0)" +
      "   OR (status = 'WORKING' AND submitted_at IS NULL))"));
    ScopeToEngagement(workpaper, nameof(Workpaper.FirmId), nameof(Workpaper.ClientId),
      nameof(Workpaper.EngagementId));
    workpaper.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    // A null procedure reference means "not procedure-driven"; an empty GUID in a scope key would
    // defeat duplicate prevention (§42.4), so the column is genuinely optional.
    workpaper.HasOne<AuditProcedure>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.ProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);

    var submission = b.Entity<WorkpaperSubmission>();
    submission.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_workpaper_submissions_firm_id_id");
    submission.Property(x => x.Conclusion).HasMaxLength(20000);
    submission.Property(x => x.WorkPerformed).HasMaxLength(100000);
    submission.HasIndex(x => new { x.FirmId, x.WorkpaperId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_workpaper_submission_revision");
    submission.ToTable("workpaper_submissions", t => t.HasCheckConstraint("ck_workpaper_submission_values",
      "revision > 0 AND length(trim(conclusion)) > 0 AND length(trim(work_performed)) > 0"));
    submission.HasOne<Workpaper>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    submission.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var draft = b.Entity<WorkpaperDraft>();
    draft.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_workpaper_drafts_firm_id_id");
    draft.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId, x.OwnerUserId })
      .HasName("AK_workpaper_drafts_scope_owner");
    draft.Property(x => x.WorkPerformed).HasMaxLength(100000);
    draft.Property(x => x.Conclusion).HasMaxLength(20000);
    draft.Property(x => x.Lifecycle).HasMaxLength(16);
    draft.HasIndex(x => new { x.FirmId, x.WorkpaperId, x.OwnerUserId })
      .IsUnique().HasDatabaseName("ux_workpaper_drafts_owner");
    draft.ToTable("workpaper_drafts", t => t.HasCheckConstraint("ck_workpaper_draft_values",
      "base_workpaper_revision > 0 AND base_input_generation > 0 AND base_policy_generation > 0" +
      " AND draft_revision > 0 AND length(work_performed) <= 100000 AND length(conclusion) <= 20000" +
      " AND lifecycle IN ('ACTIVE','CONSUMED','DISCARDED')"));
    ScopeToEngagement(draft, nameof(WorkpaperDraft.FirmId), nameof(WorkpaperDraft.ClientId),
      nameof(WorkpaperDraft.EngagementId));
    draft.HasOne<Workpaper>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.OwnerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var procedure = b.Entity<AuditProcedure>();
    procedure.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_audit_procedures_firm_id_id");
    procedure.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_audit_procedures_scope_id");
    procedure.Property(x => x.SourceProcedureId).HasMaxLength(20);
    procedure.Property(x => x.SourceSectionTitle).HasMaxLength(200);
    procedure.Property(x => x.SourceWording).HasMaxLength(2000);
    procedure.Property(x => x.ApplicabilityStatus).HasMaxLength(30);
    procedure.ToTable("audit_procedures", t => t.HasCheckConstraint("ck_audit_procedure_values",
      "length(trim(title)) > 0 AND status IN ('PLANNED','IN_PROGRESS','SUBMITTED','IN_REVIEW','CHANGES_REQUIRED','REVIEWED')" +
      " AND applicability_status IN ('PENDING','APPLICABLE','NA_PENDING_REVIEW','NA_APPROVED')"));
    procedure.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.SourceProcedureId })
      .IsUnique().HasDatabaseName("ux_audit_procedure_scope_source");
    ScopeToEngagement(procedure, nameof(AuditProcedure.FirmId), nameof(AuditProcedure.ClientId),
      nameof(AuditProcedure.EngagementId));
    procedure.HasOne<AuditRisk>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.RiskId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedure.HasOne<EngagementAuditProgram>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.EngagementProgramId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedure.HasOne<AuditProgramProcedure>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ProgramProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedure.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ApplicabilityDecidedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var procedureResult = b.Entity<AuditProcedureResult>();
    procedureResult.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_audit_procedure_results_firm_id_id");
    procedureResult.Property(x => x.WorkPerformed).HasMaxLength(100000);
    procedureResult.Property(x => x.StructuredResultJson).HasMaxLength(100000);
    procedureResult.Property(x => x.EvidenceReferencesJson).HasMaxLength(20000);
    procedureResult.Property(x => x.Conclusion).HasMaxLength(20000);
    procedureResult.Property(x => x.Status).HasMaxLength(30);
    procedureResult.HasIndex(x => new { x.FirmId, x.AuditProcedureId, x.Revision }).IsUnique()
      .HasDatabaseName("ux_audit_procedure_result_revision");
    procedureResult.ToTable("audit_procedure_results", t => t.HasCheckConstraint("ck_audit_procedure_result_values",
      "revision > 0 AND input_generation > 0 AND length(trim(work_performed)) > 0" +
      " AND length(trim(structured_result_json)) > 0 AND length(trim(conclusion)) > 0" +
      " AND status IN ('SUBMITTED','CHANGES_REQUIRED','REVIEWED')" +
      " AND ((status = 'REVIEWED' AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL) OR status <> 'REVIEWED')"));
    ScopeToEngagement(procedureResult, nameof(AuditProcedureResult.FirmId),
      nameof(AuditProcedureResult.ClientId), nameof(AuditProcedureResult.EngagementId));
    procedureResult.HasOne<AuditProcedure>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedureResult.HasOne<Workpaper>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.WorkpaperId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedureResult.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.PreparedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    procedureResult.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var procedureReview = b.Entity<AuditProcedureReview>();
    procedureReview.HasAlternateKey(x => new { x.FirmId, x.Id })
      .HasName("AK_audit_procedure_reviews_firm_id_id");
    procedureReview.Property(x => x.Decision).HasMaxLength(30);
    procedureReview.Property(x => x.Comment).HasMaxLength(20000);
    procedureReview.HasIndex(x => new { x.FirmId, x.AuditProcedureResultId, x.CreatedAt })
      .HasDatabaseName("ix_audit_procedure_reviews_result_created");
    procedureReview.ToTable("audit_procedure_reviews", t => t.HasCheckConstraint("ck_audit_procedure_review_values",
      "result_revision > 0 AND decision IN ('REVIEWED','CHANGES_REQUIRED')"));
    ScopeToEngagement(procedureReview, nameof(AuditProcedureReview.FirmId),
      nameof(AuditProcedureReview.ClientId), nameof(AuditProcedureReview.EngagementId));
    procedureReview.HasOne<AuditProcedureResult>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditProcedureResultId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedureReview.HasOne<AuditProcedure>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.AuditProcedureId })
      .HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .OnDelete(DeleteBehavior.Restrict);
    procedureReview.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ReviewerUserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var finding = b.Entity<Finding>();
    finding.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_findings_firm_id_id");
    finding.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.EngagementId, x.Id })
      .HasName("AK_findings_scope_id");
    finding.Property(x => x.FindingType).HasMaxLength(100);
    finding.Property(x => x.Status).HasMaxLength(30);
    finding.HasIndex(x => new { x.FirmId, x.EngagementId, x.Status }).HasDatabaseName("ix_findings_scope_status");
    finding.HasIndex(x => new { x.FirmId, x.ClientId, x.EngagementId, x.CreatedAt })
      .HasDatabaseName("ix_findings_scope_created");
    finding.ToTable("findings", t => t.HasCheckConstraint("ck_finding_values",
      "length(trim(finding_type)) > 0 AND length(trim(impact_description)) > 0" +
      " AND (monetary_amount IS NULL OR monetary_amount >= 0) AND status IN ('OPEN','CORRECTED','EVALUATED')" +
      " AND ((corrected AND status <> 'OPEN') OR NOT corrected)"));
    ScopeToEngagement(finding, nameof(Finding.FirmId), nameof(Finding.ClientId), nameof(Finding.EngagementId));
    finding.HasOne<AppUser>().WithMany()
      .HasForeignKey(x => new { x.FirmId, x.ActorId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
