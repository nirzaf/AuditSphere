using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

// Resource planning, engagement staffing, the materiality engine and risk-band routing.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureResourcePlanning(ModelBuilder b)
  {
    b.Entity<EngagementStaffAssignment>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.UserId }).IsUnique().HasFilter("revoked_at IS NULL");
      e.HasIndex(x => new { x.FirmId, x.EngagementId }).IsUnique().HasFilter("revoked_at IS NULL AND staffing_level = 'ENGAGEMENT_PARTNER'")
        .HasDatabaseName("ix_engagement_staff_assignments_single_partner");
      e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<RoleGrant>().WithMany().HasForeignKey(x => x.RoleGrantId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("engagement_staff_assignments", t => t.HasCheckConstraint("ck_engagement_staff_assignment_values",
        "staffing_level IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE') AND ((revoked_at IS NULL) = (revoked_by_user_id IS NULL))"));
    });
    b.Entity<StaffProfile>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.UserId }).IsUnique();
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
      e.Property(x => x.TargetUtilizationPercent).HasPrecision(5, 2);
      e.ToTable("staff_profiles", t => t.HasCheckConstraint("ck_staff_profile_values",
        "weekly_capacity_minutes BETWEEN 0 AND 4800 AND target_utilization_percent BETWEEN 0 AND 100 AND length(department) > 0"));
    });
    b.Entity<StaffCertification>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.UserId });
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("staff_certifications", t => t.HasCheckConstraint("ck_staff_certification_values", "length(name) > 0"));
    });
    b.Entity<StaffAvailability>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.UserId, x.StartDate });
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("staff_availabilities", t => t.HasCheckConstraint("ck_staff_availability_values",
        "end_date >= start_date AND minutes_per_day BETWEEN 1 AND 1440 AND kind IN ('LEAVE','TRAINING','PUBLIC_HOLIDAY')"));
    });
    b.Entity<StaffAllocation>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.UserId, x.WeekStart }).IsUnique();
      e.HasIndex(x => new { x.FirmId, x.WeekStart });
      e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("staff_allocations", t => t.HasCheckConstraint("ck_staff_allocation_values",
        "planned_minutes BETWEEN 1 AND 4800 AND extract(isodow from week_start) = 1"));
    });
    b.Entity<MaterialityCalculation>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.MaterialityAssessmentId }).IsUnique();
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.CreatedAt });
      e.HasOne<MaterialityAssessment>().WithMany().HasForeignKey(x => x.MaterialityAssessmentId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<AuditSphereOps.Domain.Accounting.MappingVersion>().WithMany().HasForeignKey(x => x.MappingVersionId).OnDelete(DeleteBehavior.Restrict);
      foreach (var p in new[] { nameof(MaterialityCalculation.RatePercent), nameof(MaterialityCalculation.PerformancePercent), nameof(MaterialityCalculation.TrivialPercent) })
        e.Property<decimal>(p).HasPrecision(9, 4);
      foreach (var p in new[] { nameof(MaterialityCalculation.BenchmarkAmount), nameof(MaterialityCalculation.PlanningMateriality), nameof(MaterialityCalculation.TolerableError), nameof(MaterialityCalculation.SadThreshold) })
        e.Property<decimal>(p).HasPrecision(28, 6);
      e.ToTable("materiality_calculations", t => t.HasCheckConstraint("ck_materiality_calculation_values",
        "benchmark_kind IN ('REVENUE','PROFIT_BEFORE_TAX','TOTAL_ASSETS','NET_ASSETS','TOTAL_EXPENSES','MAPPED_LINE') AND ((benchmark_kind = 'MAPPED_LINE') = (destination_code IS NOT NULL)) AND benchmark_amount > 0 AND planning_materiality > 0 AND tolerable_error > 0 AND tolerable_error < planning_materiality AND sad_threshold > 0 AND sad_threshold < tolerable_error AND source_line_count > 0 AND length(input_hash) = 64 AND currency ~ '^[A-Z]{3}$'"));
    });
    b.Entity<RiskBandAssessment>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.RiskId, x.AssessedAt });
      e.HasOne<AuditRisk>().WithMany().HasForeignKey(x => x.RiskId).OnDelete(DeleteBehavior.Restrict);
      // The colour cannot be edited independently of its recorded inputs.
      e.ToTable("risk_band_assessments", t => t.HasCheckConstraint("ck_risk_band_assessment_rule",
        "likelihood_score BETWEEN 1 AND 3 AND magnitude_score BETWEEN 1 AND 3 AND length(rationale) > 0 AND band = CASE WHEN significant OR fraud_risk THEN 'RED' WHEN likelihood_score * magnitude_score >= 6 THEN 'RED' WHEN likelihood_score * magnitude_score >= 3 THEN 'AMBER' ELSE 'GREEN' END"));
    });
    b.Entity<RiskPartnerClearance>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.RiskBandAssessmentId }).IsUnique();
      e.HasOne<RiskBandAssessment>().WithMany().HasForeignKey(x => x.RiskBandAssessmentId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("risk_partner_clearances", t => t.HasCheckConstraint("ck_risk_partner_clearance_values", "length(note) > 0"));
    });
    b.Entity<RiskOwnerAssignment>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.RiskBandAssessmentId });
      e.HasOne<RiskBandAssessment>().WithMany().HasForeignKey(x => x.RiskBandAssessmentId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("risk_owner_assignments", t => t.HasCheckConstraint("ck_risk_owner_assignment_values",
        "owner_staffing_level IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')"));
    });
  }
}
