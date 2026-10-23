using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveCopyClientScopeAndReadback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_client_share_point_sites_firm_id_client_id_id",
                table: "client_share_point_sites",
                columns: new[] { "firm_id", "client_id", "id" });

            migrationBuilder.CreateTable(
                name: "archive_copy_target_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_share_point_site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_group_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    connection_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    site_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    drive_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    library_root_item_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    expected_permissions_json = table.Column<string>(type: "text", nullable: false),
                    expected_permissions_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authorized_staff_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    configuration_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archive_copy_target_revisions", x => x.id);
                    table.UniqueConstraint("AK_archive_copy_target_revisions_firm_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.UniqueConstraint("AK_archive_copy_target_revisions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_archive_copy_target_revision_values", "target_code = 'SECONDARY_ARCHIVE' AND revision >= 1 AND length(trim(tenant_id)) > 0 AND length(trim(site_id)) > 0 AND length(trim(drive_id)) > 0 AND length(trim(library_root_item_id)) > 0 AND length(trim(expected_permissions_json)) > 0 AND expected_permissions_sha256 ~ '^[0-9a-f]{64}$' AND staff_group_id ~ '^[1-9][0-9]*$' AND authorized_staff_sha256 ~ '^[0-9a-f]{64}$' AND configuration_sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_archive_copy_target_revisions_client_share_point_sites_firm~",
                        columns: x => new { x.firm_id, x.client_id, x.client_share_point_site_id },
                        principalTable: "client_share_point_sites",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_target_revisions_m365_connection_revisions_fir~",
                        columns: x => new { x.firm_id, x.connection_revision_id },
                        principalTable: "m365_connection_revisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_target_revisions_practice_clients_firm_id_clie~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_target_revisions_users_firm_id_created_by_user~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archive_copy_file_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_token = table.Column<long>(type: "bigint", nullable: false),
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_manifest_version = table.Column<long>(type: "bigint", nullable: false),
                    archive_manifest_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_revision = table.Column<long>(type: "bigint", nullable: false),
                    target_configuration_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_key_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manifest_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_version = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_byte_count = table.Column<long>(type: "bigint", nullable: false),
                    relative_path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    destination_tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    destination_site_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    destination_drive_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    destination_library_root_item_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    has_exact_target_binding = table.Column<bool>(type: "boolean", nullable: false),
                    destination_item_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    destination_version = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    read_back_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    read_back_byte_count = table.Column<long>(type: "bigint", nullable: false),
                    verification_outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    read_back_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archive_copy_file_receipts", x => x.id);
                    table.CheckConstraint("ck_archive_copy_file_receipt_values", "attempt_token >= 1 AND archive_manifest_version >= 1 AND archive_manifest_sha256 ~ '^[0-9a-f]{64}$' AND target_revision >= 1 AND target_configuration_sha256 ~ '^[0-9a-f]{64}$' AND source_kind IN ('CHECKPOINT_MANIFEST','RELEASED_ARTIFACT','REVIEWED_MANIFEST_ENTRY') AND source_key_sha256 ~ '^[0-9a-f]{64}$' AND source_sha256 ~ '^[0-9a-f]{64}$' AND source_byte_count >= 0 AND length(trim(source_version)) > 0 AND length(trim(relative_path)) > 0 AND length(trim(destination_tenant_id)) > 0 AND length(trim(destination_site_id)) > 0 AND length(trim(destination_drive_id)) > 0 AND length(trim(destination_library_root_item_id)) > 0 AND length(trim(destination_item_id)) > 0 AND length(trim(destination_version)) > 0 AND read_back_sha256 ~ '^[0-9a-f]{64}$' AND read_back_byte_count >= 0 AND verification_outcome IN ('MATCH','MISMATCH') AND ((source_kind = 'REVIEWED_MANIFEST_ENTRY' AND manifest_entry_id IS NOT NULL) OR  (source_kind <> 'REVIEWED_MANIFEST_ENTRY' AND manifest_entry_id IS NULL)) AND (source_kind <> 'CHECKPOINT_MANIFEST' OR source_id = checkpoint_id)");
                    table.ForeignKey(
                        name: "FK_archive_copy_file_receipts_archive_copy_target_revisions_fi~",
                        columns: x => new { x.firm_id, x.client_id, x.target_revision_id },
                        principalTable: "archive_copy_target_revisions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_file_receipts_archive_manifests_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_manifest_id },
                        principalTable: "archive_manifests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_file_receipts_archives_firm_id_client_id_engag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_id },
                        principalTable: "archives",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_file_receipts_durable_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "durable_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_file_receipts_engagements_firm_id_client_id_en~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_file_receipts_release_checkpoints_firm_id_clie~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.checkpoint_id },
                        principalTable: "release_checkpoints",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_file_receipts_releases_firm_id_release_id",
                        columns: x => new { x.firm_id, x.release_id },
                        principalTable: "releases",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archive_copy_permission_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_token = table.Column<long>(type: "bigint", nullable: false),
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_manifest_version = table.Column<long>(type: "bigint", nullable: false),
                    archive_manifest_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_revision = table.Column<long>(type: "bigint", nullable: false),
                    target_configuration_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authorized_staff_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    staff_group_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expected_permissions_json = table.Column<string>(type: "text", nullable: false),
                    expected_permissions_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    observed_permissions_json = table.Column<string>(type: "text", nullable: false),
                    observed_permissions_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    has_unique_permission_scope = table.Column<bool>(type: "boolean", nullable: false),
                    has_exact_target_binding = table.Column<bool>(type: "boolean", nullable: false),
                    observed_staff_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    observed_tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    observed_site_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    observed_drive_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    observed_library_root_item_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    comparison_outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archive_copy_permission_observations", x => x.id);
                    table.CheckConstraint("ck_archive_copy_permission_observation_values", "attempt_token >= 1 AND archive_manifest_version >= 1 AND archive_manifest_sha256 ~ '^[0-9a-f]{64}$' AND target_revision >= 1 AND target_configuration_sha256 ~ '^[0-9a-f]{64}$' AND authorized_staff_sha256 ~ '^[0-9a-f]{64}$' AND observed_staff_sha256 ~ '^[0-9a-f]{64}$' AND staff_group_id ~ '^[1-9][0-9]*$' AND length(trim(expected_permissions_json)) > 0 AND expected_permissions_sha256 ~ '^[0-9a-f]{64}$' AND length(trim(observed_permissions_json)) > 0 AND observed_permissions_sha256 ~ '^[0-9a-f]{64}$' AND length(trim(observed_tenant_id)) > 0 AND length(trim(observed_site_id)) > 0 AND length(trim(observed_drive_id)) > 0 AND length(trim(observed_library_root_item_id)) > 0 AND comparison_outcome IN ('MATCH','MISMATCH')");
                    table.ForeignKey(
                        name: "FK_archive_copy_permission_observations_archive_copy_target_re~",
                        columns: x => new { x.firm_id, x.client_id, x.target_revision_id },
                        principalTable: "archive_copy_target_revisions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_permission_observations_archive_manifests_firm~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_manifest_id },
                        principalTable: "archive_manifests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_permission_observations_durable_operations_ope~",
                        column: x => x.operation_id,
                        principalTable: "durable_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_permission_observations_engagements_firm_id_cl~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_copy_permission_observations_releases_firm_id_relea~",
                        columns: x => new { x.firm_id, x.release_id },
                        principalTable: "releases",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archive_copy_file_receipt_archive",
                table: "archive_copy_file_receipts",
                columns: new[] { "firm_id", "archive_id", "archive_manifest_version", "target_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_file_receipts_firm_id_client_id_engagement_id_~",
                table: "archive_copy_file_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_file_receipts_firm_id_client_id_engagement_id~1",
                table: "archive_copy_file_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_manifest_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_file_receipts_firm_id_client_id_engagement_id~2",
                table: "archive_copy_file_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "checkpoint_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_file_receipts_firm_id_client_id_target_revisio~",
                table: "archive_copy_file_receipts",
                columns: new[] { "firm_id", "client_id", "target_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_file_receipts_firm_id_release_id",
                table: "archive_copy_file_receipts",
                columns: new[] { "firm_id", "release_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_file_receipts_operation_id",
                table: "archive_copy_file_receipts",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ux_archive_copy_file_receipt_attempt_source",
                table: "archive_copy_file_receipts",
                columns: new[] { "firm_id", "operation_id", "attempt_token", "source_key_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_copy_permission_observation_target",
                table: "archive_copy_permission_observations",
                columns: new[] { "firm_id", "target_revision_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_permission_observations_firm_id_client_id_enga~",
                table: "archive_copy_permission_observations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_manifest_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_permission_observations_firm_id_client_id_targ~",
                table: "archive_copy_permission_observations",
                columns: new[] { "firm_id", "client_id", "target_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_permission_observations_firm_id_release_id",
                table: "archive_copy_permission_observations",
                columns: new[] { "firm_id", "release_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_permission_observations_operation_id",
                table: "archive_copy_permission_observations",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ux_archive_copy_permission_observation_attempt",
                table: "archive_copy_permission_observations",
                columns: new[] { "firm_id", "operation_id", "attempt_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_target_revisions_firm_id_client_id_client_shar~",
                table: "archive_copy_target_revisions",
                columns: new[] { "firm_id", "client_id", "client_share_point_site_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_target_revisions_firm_id_connection_revision_id",
                table: "archive_copy_target_revisions",
                columns: new[] { "firm_id", "connection_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_target_revisions_firm_id_created_by_user_id",
                table: "archive_copy_target_revisions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_archive_copy_target_client_revision",
                table: "archive_copy_target_revisions",
                columns: new[] { "firm_id", "client_id", "target_code", "revision" },
                unique: true);

            migrationBuilder.Sql("""
            CREATE FUNCTION reject_archive_copy_evidence_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              RAISE EXCEPTION 'Archive-copy evidence is append-only; % is refused on %.', TG_OP, TG_TABLE_NAME
                USING ERRCODE = '55000';
            END;
            $$;
            CREATE TRIGGER archive_copy_target_revision_append_only
              BEFORE UPDATE OR DELETE ON archive_copy_target_revisions
              FOR EACH ROW EXECUTE FUNCTION reject_archive_copy_evidence_mutation();
            CREATE TRIGGER archive_copy_target_revision_no_truncate
              BEFORE TRUNCATE ON archive_copy_target_revisions
              FOR EACH STATEMENT EXECUTE FUNCTION reject_archive_copy_evidence_mutation();
            CREATE TRIGGER archive_copy_file_receipt_append_only
              BEFORE UPDATE OR DELETE ON archive_copy_file_receipts
              FOR EACH ROW EXECUTE FUNCTION reject_archive_copy_evidence_mutation();
            CREATE TRIGGER archive_copy_file_receipt_no_truncate
              BEFORE TRUNCATE ON archive_copy_file_receipts
              FOR EACH STATEMENT EXECUTE FUNCTION reject_archive_copy_evidence_mutation();
            CREATE TRIGGER archive_copy_permission_observation_append_only
              BEFORE UPDATE OR DELETE ON archive_copy_permission_observations
              FOR EACH ROW EXECUTE FUNCTION reject_archive_copy_evidence_mutation();
            CREATE TRIGGER archive_copy_permission_observation_no_truncate
              BEFORE TRUNCATE ON archive_copy_permission_observations
              FOR EACH STATEMENT EXECUTE FUNCTION reject_archive_copy_evidence_mutation();
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            LOCK TABLE archive_copy_target_revisions,archive_copy_file_receipts,archive_copy_permission_observations
              IN ACCESS EXCLUSIVE MODE;
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM archive_copy_target_revisions)
                OR EXISTS (SELECT 1 FROM archive_copy_file_receipts)
                OR EXISTS (SELECT 1 FROM archive_copy_permission_observations) THEN
                RAISE EXCEPTION 'Retained archive-copy configuration or readback evidence must be preserved; downgrade refused.'
                  USING ERRCODE = '55000';
              END IF;
            END $$;
            DROP TRIGGER archive_copy_target_revision_append_only ON archive_copy_target_revisions;
            DROP TRIGGER archive_copy_target_revision_no_truncate ON archive_copy_target_revisions;
            DROP TRIGGER archive_copy_file_receipt_append_only ON archive_copy_file_receipts;
            DROP TRIGGER archive_copy_file_receipt_no_truncate ON archive_copy_file_receipts;
            DROP TRIGGER archive_copy_permission_observation_append_only ON archive_copy_permission_observations;
            DROP TRIGGER archive_copy_permission_observation_no_truncate ON archive_copy_permission_observations;
            DROP FUNCTION reject_archive_copy_evidence_mutation();
            """);

            migrationBuilder.DropTable(
                name: "archive_copy_file_receipts");

            migrationBuilder.DropTable(
                name: "archive_copy_permission_observations");

            migrationBuilder.DropTable(
                name: "archive_copy_target_revisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_client_share_point_sites_firm_id_client_id_id",
                table: "client_share_point_sites");
        }
    }
}
