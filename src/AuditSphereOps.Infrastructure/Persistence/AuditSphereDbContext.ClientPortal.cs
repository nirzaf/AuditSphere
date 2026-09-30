using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

// Client portal onboarding: first-sign-in requirement, request delegation and the conversion-time portal intent.
public sealed partial class AuditSphereDbContext
{
  private static void ConfigureClientPortal(ModelBuilder b)
  {
    b.Entity<ClientPortalFirstSignIn>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.UserId }).IsUnique();
      e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("client_portal_first_sign_ins", t => t.HasCheckConstraint("ck_client_portal_first_sign_in_values",
        "identity_path IN ('PROVISIONED_MEMBER','EXTERNAL_IDENTITY','UNOBSERVED_IDENTITY') AND length(terms_version) > 0 AND (identity_path <> 'PROVISIONED_MEMBER' OR sign_in_observed_at IS NOT NULL)"));
    });
    b.Entity<PbcRequestDelegation>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.PbcRequestId, x.DelegateUserId }).IsUnique().HasFilter("revoked_at IS NULL");
      e.HasIndex(x => new { x.FirmId, x.DelegateUserId });
      e.HasOne<PbcRequest>().WithMany().HasForeignKey(x => x.PbcRequestId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("pbc_request_delegations", t => t.HasCheckConstraint("ck_pbc_request_delegation_values",
        "delegator_user_id <> delegate_user_id AND ((revoked_at IS NULL) = (revoked_by_user_id IS NULL))"));
    });
    b.Entity<ClientPortalIntent>(e =>
    {
      e.HasIndex(x => new { x.FirmId, x.PracticeClientId }).IsUnique();
      e.HasOne<PracticeClient>().WithMany().HasForeignKey(x => x.PracticeClientId).OnDelete(DeleteBehavior.Restrict);
      e.HasOne<ClientContact>().WithMany().HasForeignKey(x => x.ClientContactId).OnDelete(DeleteBehavior.Restrict);
      e.ToTable("client_portal_intents", t => t.HasCheckConstraint("ck_client_portal_intent_values",
        "state IN ('AWAITING_ACCEPTANCE','READY_TO_INVITE','INVITED') AND length(recipient_email) > 3 AND (state = 'AWAITING_ACCEPTANCE' OR activated_engagement_id IS NOT NULL)"));
    });
  }
}
