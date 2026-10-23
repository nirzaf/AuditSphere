using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveCopySharedSelectedResource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_copy_target_revision_values",
                table: "archive_copy_target_revisions");

            migrationBuilder.AlterColumn<Guid>(
                name: "client_share_point_site_id",
                table: "archive_copy_target_revisions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "selected_resource_draft_id",
                table: "archive_copy_target_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "selected_resource_draft_revision",
                table: "archive_copy_target_revisions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selected_resource_evidence_sha256",
                table: "archive_copy_target_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selected_resource_root_folder_id",
                table: "archive_copy_target_revisions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selected_resource_site_url",
                table: "archive_copy_target_revisions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "selected_resource_verification_id",
                table: "archive_copy_target_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_m365_tenant_capability_verifications_firm_id_id",
                table: "m365_tenant_capability_verifications",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_target_revisions_firm_id_selected_resource_dra~",
                table: "archive_copy_target_revisions",
                columns: new[] { "firm_id", "selected_resource_draft_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_copy_target_revisions_firm_id_selected_resource_ver~",
                table: "archive_copy_target_revisions",
                columns: new[] { "firm_id", "selected_resource_verification_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_copy_target_revision_values",
                table: "archive_copy_target_revisions",
                sql: "target_code = 'SECONDARY_ARCHIVE' AND revision >= 1 AND length(trim(tenant_id)) > 0 AND length(trim(site_id)) > 0 AND length(trim(drive_id)) > 0 AND length(trim(library_root_item_id)) > 0 AND length(trim(expected_permissions_json)) > 0 AND expected_permissions_sha256 ~ '^[0-9a-f]{64}$' AND staff_group_id ~ '^[1-9][0-9]*$' AND authorized_staff_sha256 ~ '^[0-9a-f]{64}$' AND configuration_sha256 ~ '^[0-9a-f]{64}$' AND ((client_share_point_site_id IS NOT NULL AND selected_resource_draft_id IS NULL AND selected_resource_draft_revision IS NULL AND selected_resource_verification_id IS NULL AND selected_resource_site_url IS NULL AND selected_resource_root_folder_id IS NULL AND selected_resource_evidence_sha256 IS NULL) OR (client_share_point_site_id IS NULL AND selected_resource_draft_id IS NOT NULL AND selected_resource_draft_revision IS NOT NULL AND selected_resource_draft_revision >= 1 AND selected_resource_verification_id IS NOT NULL AND selected_resource_site_url IS NOT NULL AND selected_resource_root_folder_id IS NOT NULL AND selected_resource_evidence_sha256 IS NOT NULL AND length(trim(selected_resource_site_url)) > 0 AND length(trim(selected_resource_root_folder_id)) > 0 AND selected_resource_root_folder_id = library_root_item_id AND selected_resource_evidence_sha256 ~ '^[0-9a-f]{64}$'))");

            migrationBuilder.AddForeignKey(
                name: "FK_archive_copy_target_revisions_m365_setup_drafts_firm_id_sel~",
                table: "archive_copy_target_revisions",
                columns: new[] { "firm_id", "selected_resource_draft_id" },
                principalTable: "m365_setup_drafts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_archive_copy_target_revisions_m365_tenant_capability_verifi~",
                table: "archive_copy_target_revisions",
                columns: new[] { "firm_id", "selected_resource_verification_id" },
                principalTable: "m365_tenant_capability_verifications",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE archive_copy_target_revisions IN ACCESS EXCLUSIVE MODE;
                DO $archive_copy_shared_target_downgrade$
                BEGIN
                  IF EXISTS (
                    SELECT 1
                    FROM archive_copy_target_revisions
                    WHERE client_share_point_site_id IS NULL
                       OR selected_resource_draft_id IS NOT NULL
                       OR selected_resource_draft_revision IS NOT NULL
                       OR selected_resource_verification_id IS NOT NULL
                       OR selected_resource_site_url IS NOT NULL
                       OR selected_resource_root_folder_id IS NOT NULL
                       OR selected_resource_evidence_sha256 IS NOT NULL)
                  THEN
                    RAISE EXCEPTION 'Cannot downgrade shared archive-copy target bindings while target revisions are retained'
                      USING ERRCODE = '55000';
                  END IF;
                END;
                $archive_copy_shared_target_downgrade$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_archive_copy_target_revisions_m365_setup_drafts_firm_id_sel~",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_archive_copy_target_revisions_m365_tenant_capability_verifi~",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_m365_tenant_capability_verifications_firm_id_id",
                table: "m365_tenant_capability_verifications");

            migrationBuilder.DropIndex(
                name: "IX_archive_copy_target_revisions_firm_id_selected_resource_dra~",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropIndex(
                name: "IX_archive_copy_target_revisions_firm_id_selected_resource_ver~",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_copy_target_revision_values",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropColumn(
                name: "selected_resource_draft_id",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropColumn(
                name: "selected_resource_draft_revision",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropColumn(
                name: "selected_resource_evidence_sha256",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropColumn(
                name: "selected_resource_root_folder_id",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropColumn(
                name: "selected_resource_site_url",
                table: "archive_copy_target_revisions");

            migrationBuilder.DropColumn(
                name: "selected_resource_verification_id",
                table: "archive_copy_target_revisions");

            migrationBuilder.AlterColumn<Guid>(
                name: "client_share_point_site_id",
                table: "archive_copy_target_revisions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_copy_target_revision_values",
                table: "archive_copy_target_revisions",
                sql: "target_code = 'SECONDARY_ARCHIVE' AND revision >= 1 AND length(trim(tenant_id)) > 0 AND length(trim(site_id)) > 0 AND length(trim(drive_id)) > 0 AND length(trim(library_root_item_id)) > 0 AND length(trim(expected_permissions_json)) > 0 AND expected_permissions_sha256 ~ '^[0-9a-f]{64}$' AND staff_group_id ~ '^[1-9][0-9]*$' AND authorized_staff_sha256 ~ '^[0-9a-f]{64}$' AND configuration_sha256 ~ '^[0-9a-f]{64}$'");
        }
    }
}
