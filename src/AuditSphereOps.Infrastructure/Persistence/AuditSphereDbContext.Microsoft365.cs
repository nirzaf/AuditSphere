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
  private static void ConfigureMicrosoft365(ModelBuilder b)
  {
    var session = b.Entity<Microsoft365SetupSession>();
    session.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_m365_setup_sessions_firm_id_id");
    session.HasIndex(x => new { x.FirmId, x.InstallationId }).IsUnique();
    session.Property(x => x.InstallationId).HasMaxLength(200);
    session.Property(x => x.BootstrapProofHash).HasMaxLength(64);
    session.Property(x => x.CapabilityHash).HasMaxLength(64);
    session.Property(x => x.State).HasMaxLength(20);
    session.ToTable("m365_setup_sessions", t => t.HasCheckConstraint("ck_m365_setup_session_values",
      "length(trim(installation_id)) > 0 AND bootstrap_proof_hash ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND state IN ('UNCLAIMED','CLAIMED','ACTIVE','EXPIRED') AND revision >= 1 AND expires_at > claimed_at"));

    var draft = b.Entity<Microsoft365SetupDraft>();
    draft.HasIndex(x => new { x.FirmId, x.SetupSessionId }).IsUnique();
    draft.Property(x => x.State).HasMaxLength(24);
    draft.Property(x => x.ExpectedTenantId).HasMaxLength(200);
    draft.Property(x => x.TenantDisplayName).HasMaxLength(300);
    draft.Property(x => x.SiteUrl).HasMaxLength(2000);
    draft.Property(x => x.SiteId).HasMaxLength(2000);
    draft.Property(x => x.DriveId).HasMaxLength(2000);
    draft.Property(x => x.RootFolderId).HasMaxLength(2000);
    draft.Property(x => x.AccessProfile).HasMaxLength(40);
    draft.Property(x => x.MailState).HasMaxLength(30);
    draft.Property(x => x.RecordsState).HasMaxLength(30);
    draft.ToTable("m365_setup_drafts", t => t.HasCheckConstraint("ck_m365_setup_draft_values",
      "state IN ('DRAFT','VALIDATING','VERIFIED','ACTIVE','CONSENT_REQUIRED','SUSPENDED','BLOCKED') AND revision >= 1 AND access_profile IN ('APP_MEDIATED','DIRECT_STAFF_COLLABORATION') AND mail_state IN ('NOT_CONFIGURED','CONFIGURED') AND records_state IN ('NOT_CONFIGURED','CONFIGURED')"));
    draft.HasOne<Microsoft365SetupSession>().WithMany().HasForeignKey(x => new { x.FirmId, x.SetupSessionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    draft.HasOne<Microsoft365ConnectionRevision>().WithMany().HasForeignKey(x => new { x.FirmId, x.ConnectionRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var connection = b.Entity<Microsoft365ConnectionRevision>();
    connection.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_m365_connection_revisions_firm_id_id");
    connection.HasIndex(x => new { x.FirmId, x.Revision }).IsUnique();
    connection.Property(x => x.TenantId).HasMaxLength(200);
    connection.Property(x => x.LoginClientIdReference).HasMaxLength(500);
    connection.Property(x => x.RuntimeCredentialReference).HasMaxLength(500);
    connection.Property(x => x.CloudProfile).HasMaxLength(30);
    connection.Property(x => x.State).HasMaxLength(30);
    connection.Property(x => x.ConsentState).HasMaxLength(30);
    connection.ToTable("m365_connection_revisions", t => t.HasCheckConstraint("ck_m365_connection_values",
      "revision >= 1 AND length(trim(tenant_id)) > 0 AND length(trim(login_client_id_reference)) > 0 AND length(trim(runtime_credential_reference)) > 0 AND state IN ('DRAFT','VALIDATING','VERIFIED','ACTIVE','CONSENT_REQUIRED','SUSPENDED','BLOCKED')"));

    var workspace = b.Entity<FirmWorkspaceConfiguration>();
    workspace.HasIndex(x => new { x.FirmId, x.DefaultForFutureClients }).HasFilter("default_for_future_clients");
    workspace.Property(x => x.TenantId).HasMaxLength(200);
    workspace.Property(x => x.SiteId).HasMaxLength(2000);
    workspace.Property(x => x.DriveId).HasMaxLength(2000);
    workspace.Property(x => x.RootFolderId).HasMaxLength(2000);
    workspace.Property(x => x.DisplayUrl).HasMaxLength(2000);
    workspace.Property(x => x.AccessProfile).HasMaxLength(40);
    workspace.ToTable("firm_workspace_configurations", t => t.HasCheckConstraint("ck_m365_workspace_values",
      "length(trim(tenant_id)) > 0 AND length(trim(site_id)) > 0 AND length(trim(drive_id)) > 0 AND length(trim(root_folder_id)) > 0 AND length(trim(display_url)) > 0 AND access_profile IN ('APP_MEDIATED','DIRECT_STAFF_COLLABORATION')"));
    workspace.HasOne<Microsoft365ConnectionRevision>().WithMany().HasForeignKey(x => new { x.FirmId, x.ConnectionRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    workspace.HasOne<FolderTemplateVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.FolderTemplateVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var template = b.Entity<FolderTemplateVersion>();
    template.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_m365_folder_templates_firm_id_id");
    template.HasIndex(x => new { x.FirmId, x.Purpose, x.Version }).IsUnique();
    template.Property(x => x.Purpose).HasMaxLength(50);
    template.Property(x => x.ManifestJson).HasMaxLength(20000);
    template.Property(x => x.ManifestDigest).HasMaxLength(64);
    template.ToTable("m365_folder_template_versions", t => t.HasCheckConstraint("ck_m365_template_values",
      "version >= 1 AND length(trim(purpose)) > 0 AND length(trim(manifest_json)) > 0 AND manifest_digest ~ '^[0-9a-f]{64}$'"));

    var evidence = b.Entity<IntegrationVerificationEvidence>();
    evidence.Property(x => x.ResourceKind).HasMaxLength(50);
    evidence.Property(x => x.ResourceId).HasMaxLength(500);
    evidence.Property(x => x.Operation).HasMaxLength(100);
    evidence.Property(x => x.IdentityReference).HasMaxLength(500);
    evidence.Property(x => x.Result).HasMaxLength(30);
    evidence.Property(x => x.EvidenceReference).HasMaxLength(1000);
    evidence.HasIndex(x => new { x.FirmId, x.SetupDraftId, x.ObservedAt });
    evidence.ToTable("m365_verification_evidence", t => t.HasCheckConstraint("ck_m365_evidence_values",
      "length(trim(resource_kind)) > 0 AND length(trim(resource_id)) > 0 AND length(trim(operation)) > 0 AND length(trim(identity_reference)) > 0 AND result IN ('PASS','FAIL','BLOCKED') AND length(trim(evidence_reference)) > 0"));
    evidence.HasOne<Microsoft365SetupDraft>().WithMany().HasForeignKey(x => new { x.FirmId, x.SetupDraftId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    evidence.HasOne<Microsoft365ConnectionRevision>().WithMany().HasForeignKey(x => new { x.FirmId, x.ConnectionRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var clientWorkspace = b.Entity<ClientWorkspace>();
    clientWorkspace.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_client_workspaces_firm_id_id");
    clientWorkspace.HasIndex(x => new { x.FirmId, x.PracticeClientId, x.Purpose }).IsUnique();
    clientWorkspace.HasIndex(x => new { x.FirmId, x.LogicalKey }).IsUnique();
    clientWorkspace.Property(x => x.Purpose).HasMaxLength(30);
    clientWorkspace.Property(x => x.LogicalKey).HasMaxLength(300);
    clientWorkspace.Property(x => x.State).HasMaxLength(40);
    clientWorkspace.Property(x => x.TenantId).HasMaxLength(200);
    clientWorkspace.Property(x => x.SiteId).HasMaxLength(2000);
    clientWorkspace.Property(x => x.DriveId).HasMaxLength(2000);
    clientWorkspace.Property(x => x.RootFolderId).HasMaxLength(2000);
    clientWorkspace.Property(x => x.RemoteItemId).HasMaxLength(500);
    clientWorkspace.Property(x => x.LastErrorCode).HasMaxLength(100);
    clientWorkspace.ToTable("client_workspaces", t => t.HasCheckConstraint("ck_client_workspace_values",
      "purpose = 'PRIMARY' AND length(trim(logical_key)) > 0 AND state IN ('WAITING_FOR_INTEGRATION','QUEUED','PROVISIONING','VERIFYING','READY','BLOCKED_ACCEPTANCE','BLOCKED_CONFIGURATION','RESULT_UNCERTAIN','CONFLICT_REQUIRES_REVIEW','SUSPENDED') AND revision >= 1"));
    clientWorkspace.HasOne<PracticeClient>().WithMany().HasForeignKey(x => new { x.FirmId, x.PracticeClientId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    clientWorkspace.HasOne<AcceptanceDecision>().WithMany().HasForeignKey(x => new { x.FirmId, x.AcceptanceDecisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    clientWorkspace.HasOne<Microsoft365ConnectionRevision>().WithMany().HasForeignKey(x => new { x.FirmId, x.ConnectionRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    clientWorkspace.HasOne<FolderTemplateVersion>().WithMany().HasForeignKey(x => new { x.FirmId, x.FolderTemplateVersionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var observation = b.Entity<DirectoryUserObservation>();
    observation.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_m365_directory_observations_firm_id_id");
    observation.Property(x => x.TenantId).HasMaxLength(200);
    observation.Property(x => x.ObjectId).HasMaxLength(200);
    observation.Property(x => x.DisplayName).HasMaxLength(300);
    observation.Property(x => x.UserPrincipalName).HasMaxLength(320);
    observation.Property(x => x.Mail).HasMaxLength(320);
    observation.Property(x => x.EnabledState).HasMaxLength(16);
    observation.Property(x => x.UserType).HasMaxLength(40);
    observation.Property(x => x.Source).HasMaxLength(32);
    observation.HasIndex(x => new { x.FirmId, x.TenantId, x.ObjectId, x.ObservedAt });
    observation.ToTable("m365_directory_user_observations", t => t.HasCheckConstraint("ck_m365_directory_observation_values",
      "length(trim(tenant_id)) > 0 AND length(trim(object_id)) > 0 AND length(trim(display_name)) > 0 AND enabled_state IN ('ENABLED','DISABLED','UNKNOWN') AND length(trim(source)) > 0"));
    observation.HasOne<Microsoft365ConnectionRevision>().WithMany().HasForeignKey(x => new { x.FirmId, x.ConnectionRevisionId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);

    var invitation = b.Entity<UserAccessInvitation>();
    invitation.HasAlternateKey(x => new { x.FirmId, x.Id }).HasName("AK_m365_user_invitations_firm_id_id");
    invitation.HasIndex(x => new { x.FirmId, x.RoleGrantId }).IsUnique();
    invitation.Property(x => x.RecipientEmail).HasMaxLength(320);
    invitation.Property(x => x.DestinationPath).HasMaxLength(500);
    invitation.Property(x => x.DeliveryState).HasMaxLength(24);
    invitation.Property(x => x.ProviderCorrelationId).HasMaxLength(500);
    invitation.ToTable("m365_user_access_invitations", t => t.HasCheckConstraint("ck_m365_user_invitation_values",
      "length(trim(recipient_email)) > 0 AND length(trim(destination_path)) > 0 AND destination_path NOT LIKE '%://%' AND delivery_state IN ('NOT_SENT','QUEUED','PROVIDER_ACCEPTED','FAILED','UNKNOWN','COPIED') AND attempt_count >= 0"));
    invitation.HasOne<AppUser>().WithMany().HasForeignKey(x => new { x.FirmId, x.UserId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    invitation.HasOne<RoleGrant>().WithMany().HasForeignKey(x => new { x.FirmId, x.RoleGrantId })
      .HasPrincipalKey(x => new { x.FirmId, x.Id }).OnDelete(DeleteBehavior.Restrict);
  }
}
