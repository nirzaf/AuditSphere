using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Microsoft365OnboardingControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "m365_connection_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    login_client_id_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    runtime_credential_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cloud_profile = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    consent_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_connection_revisions", x => x.id);
                    table.UniqueConstraint("AK_m365_connection_revisions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_m365_connection_values", "revision >= 1 AND length(trim(tenant_id)) > 0 AND length(trim(login_client_id_reference)) > 0 AND length(trim(runtime_credential_reference)) > 0 AND state IN ('DRAFT','VALIDATING','VERIFIED','ACTIVE','CONSENT_REQUIRED','SUSPENDED','BLOCKED')");
                });

            migrationBuilder.CreateTable(
                name: "m365_folder_template_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    manifest_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    manifest_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_folder_template_versions", x => x.id);
                    table.UniqueConstraint("AK_m365_folder_templates_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_m365_template_values", "version >= 1 AND length(trim(purpose)) > 0 AND length(trim(manifest_json)) > 0 AND manifest_digest ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "m365_setup_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    bootstrap_proof_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capability_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    claimed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_setup_sessions", x => x.id);
                    table.UniqueConstraint("AK_m365_setup_sessions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_m365_setup_session_values", "length(trim(installation_id)) > 0 AND bootstrap_proof_hash ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND state IN ('UNCLAIMED','CLAIMED','ACTIVE','EXPIRED') AND revision >= 1 AND expires_at > claimed_at");
                });

            migrationBuilder.CreateTable(
                name: "firm_workspace_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    site_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    drive_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    root_folder_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    display_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    access_profile = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    folder_template_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_for_future_clients = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firm_workspace_configurations", x => x.id);
                    table.CheckConstraint("ck_m365_workspace_values", "length(trim(tenant_id)) > 0 AND length(trim(site_id)) > 0 AND length(trim(drive_id)) > 0 AND length(trim(root_folder_id)) > 0 AND length(trim(display_url)) > 0 AND access_profile IN ('APP_MEDIATED','DIRECT_STAFF_COLLABORATION')");
                    table.ForeignKey(
                        name: "FK_firm_workspace_configurations_m365_connection_revisions_fir~",
                        columns: x => new { x.firm_id, x.connection_revision_id },
                        principalTable: "m365_connection_revisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_firm_workspace_configurations_m365_folder_template_versions~",
                        columns: x => new { x.firm_id, x.folder_template_version_id },
                        principalTable: "m365_folder_template_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "m365_setup_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    setup_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    expected_tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_display_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    site_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    site_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    drive_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    root_folder_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    access_profile = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    mail_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    records_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_setup_drafts", x => x.id);
                    table.UniqueConstraint("AK_m365_setup_drafts_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_m365_setup_draft_values", "state IN ('DRAFT','VALIDATING','VERIFIED','ACTIVE','CONSENT_REQUIRED','SUSPENDED','BLOCKED') AND revision >= 1 AND access_profile IN ('APP_MEDIATED','DIRECT_STAFF_COLLABORATION') AND mail_state IN ('NOT_CONFIGURED','CONFIGURED') AND records_state IN ('NOT_CONFIGURED','CONFIGURED')");
                    table.ForeignKey(
                        name: "FK_m365_setup_drafts_m365_setup_sessions_firm_id_setup_session~",
                        columns: x => new { x.firm_id, x.setup_session_id },
                        principalTable: "m365_setup_sessions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "m365_verification_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    setup_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    identity_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_verification_evidence", x => x.id);
                    table.CheckConstraint("ck_m365_evidence_values", "length(trim(resource_kind)) > 0 AND length(trim(resource_id)) > 0 AND length(trim(operation)) > 0 AND length(trim(identity_reference)) > 0 AND result IN ('PASS','FAIL','BLOCKED') AND length(trim(evidence_reference)) > 0");
                    table.ForeignKey(
                        name: "FK_m365_verification_evidence_m365_setup_drafts_firm_id_setup_~",
                        columns: x => new { x.firm_id, x.setup_draft_id },
                        principalTable: "m365_setup_drafts",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_firm_workspace_configurations_firm_id_connection_revision_id",
                table: "firm_workspace_configurations",
                columns: new[] { "firm_id", "connection_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_firm_workspace_configurations_firm_id_default_for_future_cl~",
                table: "firm_workspace_configurations",
                columns: new[] { "firm_id", "default_for_future_clients" },
                filter: "default_for_future_clients");

            migrationBuilder.CreateIndex(
                name: "IX_firm_workspace_configurations_firm_id_folder_template_versi~",
                table: "firm_workspace_configurations",
                columns: new[] { "firm_id", "folder_template_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_connection_revisions_firm_id_revision",
                table: "m365_connection_revisions",
                columns: new[] { "firm_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_m365_folder_template_versions_firm_id_purpose_version",
                table: "m365_folder_template_versions",
                columns: new[] { "firm_id", "purpose", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_m365_setup_drafts_firm_id_setup_session_id",
                table: "m365_setup_drafts",
                columns: new[] { "firm_id", "setup_session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_m365_setup_sessions_firm_id_installation_id",
                table: "m365_setup_sessions",
                columns: new[] { "firm_id", "installation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_m365_verification_evidence_firm_id_setup_draft_id_observed_~",
                table: "m365_verification_evidence",
                columns: new[] { "firm_id", "setup_draft_id", "observed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "firm_workspace_configurations");

            migrationBuilder.DropTable(
                name: "m365_verification_evidence");

            migrationBuilder.DropTable(
                name: "m365_connection_revisions");

            migrationBuilder.DropTable(
                name: "m365_folder_template_versions");

            migrationBuilder.DropTable(
                name: "m365_setup_drafts");

            migrationBuilder.DropTable(
                name: "m365_setup_sessions");
        }
    }
}
