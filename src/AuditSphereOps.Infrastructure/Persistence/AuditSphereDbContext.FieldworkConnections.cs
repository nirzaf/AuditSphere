using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

// Fieldwork connections: sampling calculation log, client evidence links, physical file index and ad hoc steps.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureFieldworkConnections(ModelBuilder b)
  {
    b.Entity<AuditSamplingRun>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.SelectionId }).IsUnique();
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.CreatedAt });
      e.HasOne<AuditSelection>().WithMany().HasForeignKey(x => x.SelectionId).OnDelete(DeleteBehavior.Restrict);
      foreach (var p in new[] { nameof(AuditSamplingRun.Interval), nameof(AuditSamplingRun.KeyItemThreshold), nameof(AuditSamplingRun.PopulationAbsoluteTotal), nameof(AuditSamplingRun.SelectedAbsoluteTotal) })
        e.Property(p).HasPrecision(28, 6);
      e.Property(x => x.CoveragePercent).HasPrecision(9, 4);
      e.ToTable("audit_sampling_runs", t => t.HasCheckConstraint("ck_audit_sampling_run_values",
        "method IN ('MUS','KEY_ITEM','RANDOM','SYSTEMATIC','STRATIFIED') AND selected_count > 0 AND selected_count <= population_count AND length(source_digest) = 64 AND length(selection_digest) = 64"));
    });
    b.Entity<ProcedureEvidenceLink>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ProcedureId, x.PbcUploadIntentId }).IsUnique();
      e.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<PbcUploadIntent>().WithMany().HasForeignKey(x => x.PbcUploadIntentId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("procedure_evidence_links", t => t.HasCheckConstraint("ck_procedure_evidence_link_values", "length(content_sha256) = 64"));
    });
    b.Entity<PhysicalEvidenceItem>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.EngagementId, x.FileIndex }).IsUnique();
      e.ToTable("physical_evidence_items", t => t.HasCheckConstraint("ck_physical_evidence_item_values",
        "length(file_index) BETWEEN 1 AND 40 AND length(box_reference) BETWEEN 1 AND 40 AND length(current_location) > 0"));
    });
    b.Entity<PhysicalEvidenceMovement>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.PhysicalEvidenceItemId, x.MovedAt });
      e.HasOne<PhysicalEvidenceItem>().WithMany().HasForeignKey(x => x.PhysicalEvidenceItemId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("physical_evidence_movements", t => t.HasCheckConstraint("ck_physical_evidence_movement_values", "length(to_location) > 0 AND length(reason) > 0"));
    });
    b.Entity<ProcedurePhysicalLink>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ProcedureId, x.PhysicalEvidenceItemId }).IsUnique();
      e.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<PhysicalEvidenceItem>().WithMany().HasForeignKey(x => x.PhysicalEvidenceItemId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("procedure_physical_links");
    });
    b.Entity<AdHocProcedureInsertion>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ProcedureId }).IsUnique();
      e.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("ad_hoc_procedure_insertions", t => t.HasCheckConstraint("ck_ad_hoc_procedure_insertion_values", "length(reason) > 0"));
    });
    b.Entity<AdHocProcedureRevision>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.ProcedureId, x.Revision }).IsUnique();
      e.HasOne<AuditProcedure>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("ad_hoc_procedure_revisions", t => t.HasCheckConstraint("ck_ad_hoc_procedure_revision_values", "revision >= 1 AND length(wording) > 0"));
    });
  }
}
