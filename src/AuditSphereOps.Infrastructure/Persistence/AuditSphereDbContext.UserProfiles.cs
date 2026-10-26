using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed partial class AuditSphereDbContext
{
  private static void ConfigureUserProfiles(ModelBuilder b)
  {
    var profile = b.Entity<UserProfile>();
    profile.ToTable("user_profiles", t => t.HasCheckConstraint("ck_user_profiles",
      "revision >= 1" +
      " AND (gender IS NULL OR gender IN ('FEMALE','MALE','OTHER','PREFER_NOT_TO_SAY'))" +
      " AND (position IS NULL OR length(position) >= 1)" +
      " AND (mobile_number IS NULL OR length(mobile_number) >= 7)" +
      " AND (joined_date IS NULL OR date_of_birth IS NULL OR joined_date > date_of_birth)"));
    profile.HasKey(x => x.UserId);
    profile.Property(x => x.Position).HasMaxLength(UserProfileRules.PositionMaxLength);
    profile.Property(x => x.Gender).HasMaxLength(20);
    profile.Property(x => x.MobileNumber).HasMaxLength(UserProfileRules.MobileMaxLength);
    profile.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.UserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
