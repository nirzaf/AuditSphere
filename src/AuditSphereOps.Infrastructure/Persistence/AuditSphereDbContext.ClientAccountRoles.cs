using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientAccountRoles(ModelBuilder b)
  {
    var c = b.Entity<ClientAccountRoleConfiguration>();
    c.HasAlternateKey(x => new { x.FirmId, x.ClientId, x.Id });
    c.Property(x => x.Role).HasMaxLength(30); c.Property(x => x.Reason).HasMaxLength(2000);
    c.HasIndex(x => new { x.FirmId, x.ClientId, x.ChartVersionId, x.Role, x.EffectiveFrom });
    c.HasOne<ClientChartVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ChartVersionId }).HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    c.HasOne<ClientAccount>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.AccountId }).HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    c.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ProposedByUserId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    c.ToTable("client_account_role_configurations", t => t.HasCheckConstraint("ck_client_account_role_config", "role IN ('AR','AP','TAX_RECOVERABLE','TAX_PAYABLE','REVENUE','PURCHASE_EXPENSE','PURCHASE_ASSET','RETAINED_EARNINGS','ROUNDING','FX') AND length(trim(reason))>0 AND (effective_to IS NULL OR effective_to>=effective_from)"));
    var d = b.Entity<ClientAccountRoleDecision>();
    d.HasIndex(x => new { x.FirmId, x.ClientId, x.ConfigurationId }).IsUnique();
    d.Property(x => x.Decision).HasMaxLength(10); d.Property(x => x.Reason).HasMaxLength(2000);
    d.HasOne<ClientAccountRoleConfiguration>().WithMany().HasForeignKey(x => new { x.FirmId, x.ClientId, x.ConfigurationId }).HasPrincipalKey(x => new { x.FirmId, x.ClientId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    d.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.ReviewedByUserId }).HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    d.ToTable("client_account_role_decisions", t => t.HasCheckConstraint("ck_client_account_role_decision", "decision IN ('APPROVE','REJECT') AND length(trim(reason))>0"));
  }
}
