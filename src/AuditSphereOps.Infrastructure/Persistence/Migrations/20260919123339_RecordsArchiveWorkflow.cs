using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordsArchiveWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_archives_firm_id_client_id_engagement_id",
                table: "archives");

            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_values",
                table: "archives");

            migrationBuilder.AddColumn<long>(
                name: "profile_version",
                table: "archives",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM archives
                        WHERE status NOT IN ('ISSUED','ASSEMBLY_IN_PROGRESS','MANIFEST_BUILT','ASSEMBLY_REVIEWED','RECORDS_ACTION_REQUESTED','PROTECTION_OBSERVED','ARCHIVE_VERIFIED')
                    ) THEN
                        RAISE EXCEPTION 'RecordsArchiveWorkflow refuses archives with unsupported legacy status';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_archives_scope_id",
                table: "archives",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "archive_manifests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    manifest_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_count = table.Column<int>(type: "integer", nullable: false),
                    completeness_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    completeness_exception = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archive_manifests", x => x.id);
                    table.UniqueConstraint("AK_archive_manifests_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_archive_manifests_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_archive_manifest_values", "version >= 1 AND status IN ('BUILT','REVIEWED') AND manifest_digest ~ '^[0-9a-f]{64}$' AND entry_count >= 0 AND completeness_status IN ('COMPLETE','INCOMPLETE') AND ((completeness_status = 'INCOMPLETE' AND length(trim(completeness_exception)) > 0) OR (completeness_status = 'COMPLETE' AND completeness_exception IS NULL))");
                    table.ForeignKey(
                        name: "FK_archive_manifests_archives_firm_id_client_id_engagement_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_id },
                        principalTable: "archives",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_archive_manifests_engagements_firm_id_client_id_engagement_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "legal_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hold_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    external_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_holds", x => x.id);
                    table.CheckConstraint("ck_legal_hold_values", "length(trim(hold_reference)) > 0 AND state IN ('REQUESTED','APPLIED','OBSERVED','RELEASED') AND ((state IN ('APPLIED','OBSERVED') AND applied_at IS NOT NULL) OR state IN ('REQUESTED','RELEASED')) AND ((state = 'OBSERVED' AND observed_at IS NOT NULL) OR state <> 'OBSERVED') AND ((state = 'RELEASED' AND released_at IS NOT NULL) OR state <> 'RELEASED')");
                    table.ForeignKey(
                        name: "FK_legal_holds_archives_firm_id_client_id_engagement_id_archiv~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_id },
                        principalTable: "archives",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_legal_holds_users_firm_id_requested_by_user_id",
                        columns: x => new { x.firm_id, x.requested_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "records_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    record_class = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    jurisdiction = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    service_route = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    retention_trigger = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    retention_duration_days = table.Column<int>(type: "integer", nullable: true),
                    protection_mode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    legal_hold_behavior = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amendment_route = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    disposition_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    backup_requirements = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved = table.Column<bool>(type: "boolean", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_records_profiles", x => x.id);
                    table.UniqueConstraint("AK_records_profiles_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_records_profile_values", "version >= 1 AND length(trim(profile_code)) > 0 AND length(trim(record_class)) > 0 AND length(trim(jurisdiction)) > 0 AND length(trim(service_route)) > 0 AND length(trim(retention_trigger)) > 0 AND (retention_duration_days IS NULL OR retention_duration_days > 0) AND length(trim(protection_mode)) > 0 AND length(trim(label_id)) > 0 AND length(trim(legal_hold_behavior)) > 0 AND length(trim(amendment_route)) > 0 AND length(trim(disposition_owner)) > 0 AND length(trim(backup_requirements)) > 0 AND ((approved = false AND approved_at IS NULL AND approved_by_user_id IS NULL) OR (approved = true AND approved_at IS NOT NULL AND approved_by_user_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_records_profiles_users_firm_id_approved_by_user_id",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_records_profiles_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archive_manifest_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    entry_kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    relative_name = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    metadata_json = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archive_manifest_entries", x => x.id);
                    table.CheckConstraint("ck_archive_manifest_entry_values", "ordinal >= 1 AND length(trim(entry_kind)) > 0 AND length(trim(source_kind)) > 0 AND length(trim(relative_name)) > 0 AND content_hash ~ '^[0-9a-f]{64}$' AND byte_count >= 0 AND length(metadata_json) <= 16384");
                    table.ForeignKey(
                        name: "FK_archive_manifest_entries_archive_manifests_firm_id_client_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_manifest_id },
                        principalTable: "archive_manifests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "records_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desired_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    desired_protection = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    observed_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    observed_protection = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    external_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    observed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    exception = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_records_actions", x => x.id);
                    table.CheckConstraint("ck_records_action_values", "length(trim(desired_label)) > 0 AND length(trim(desired_protection)) > 0 AND state IN ('REQUESTED','OBSERVED','FAILED') AND ((state = 'OBSERVED' AND length(trim(observed_label)) > 0 AND length(trim(observed_protection)) > 0 AND observed_at IS NOT NULL AND length(trim(observed_by)) > 0) OR (state = 'FAILED' AND length(trim(exception)) > 0) OR state = 'REQUESTED')");
                    table.ForeignKey(
                        name: "FK_records_actions_archive_manifests_firm_id_client_id_engagem~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_manifest_id },
                        principalTable: "archive_manifests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_records_actions_archives_firm_id_client_id_engagement_id_ar~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_id },
                        principalTable: "archives",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_records_actions_users_firm_id_requested_by_user_id",
                        columns: x => new { x.firm_id, x.requested_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_values",
                table: "archives",
                sql: "length(trim(profile_id)) > 0 AND profile_version >= 1 AND status IN ('ISSUED','ASSEMBLY_IN_PROGRESS','MANIFEST_BUILT','ASSEMBLY_REVIEWED','RECORDS_ACTION_REQUESTED','PROTECTION_OBSERVED','ARCHIVE_VERIFIED')");

            migrationBuilder.CreateIndex(
                name: "IX_archive_manifest_entries_firm_id_client_id_engagement_id_ar~",
                table: "archive_manifest_entries",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_manifest_id" });

            migrationBuilder.CreateIndex(
                name: "ux_archive_manifest_entry_ordinal",
                table: "archive_manifest_entries",
                columns: new[] { "firm_id", "archive_manifest_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_archive_manifests_firm_id_client_id_engagement_id_archive_id",
                table: "archive_manifests",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_id" });

            migrationBuilder.CreateIndex(
                name: "ux_archive_manifest_archive_version",
                table: "archive_manifests",
                columns: new[] { "firm_id", "archive_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_holds_firm_id_client_id_engagement_id_archive_id",
                table: "legal_holds",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_id" });

            migrationBuilder.CreateIndex(
                name: "IX_legal_holds_firm_id_requested_by_user_id",
                table: "legal_holds",
                columns: new[] { "firm_id", "requested_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_legal_hold_reference",
                table: "legal_holds",
                columns: new[] { "firm_id", "archive_id", "hold_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_records_actions_firm_id_client_id_engagement_id_archive_id",
                table: "records_actions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_id" });

            migrationBuilder.CreateIndex(
                name: "IX_records_actions_firm_id_client_id_engagement_id_archive_man~",
                table: "records_actions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_manifest_id" });

            migrationBuilder.CreateIndex(
                name: "IX_records_actions_firm_id_requested_by_user_id",
                table: "records_actions",
                columns: new[] { "firm_id", "requested_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_records_action_archive",
                table: "records_actions",
                columns: new[] { "firm_id", "archive_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_records_profiles_firm_id_approved_by_user_id",
                table: "records_profiles",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_records_profiles_firm_id_created_by_user_id",
                table: "records_profiles",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_records_profile_code_version",
                table: "records_profiles",
                columns: new[] { "firm_id", "profile_code", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM archive_manifest_entries)
                       OR EXISTS (SELECT 1 FROM archive_manifests)
                       OR EXISTS (SELECT 1 FROM legal_holds)
                       OR EXISTS (SELECT 1 FROM records_actions)
                       OR EXISTS (SELECT 1 FROM records_profiles) THEN
                        RAISE EXCEPTION 'Records archive downgrade would discard archive, hold or profile evidence.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "archive_manifest_entries");

            migrationBuilder.DropTable(
                name: "legal_holds");

            migrationBuilder.DropTable(
                name: "records_actions");

            migrationBuilder.DropTable(
                name: "records_profiles");

            migrationBuilder.DropTable(
                name: "archive_manifests");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_archives_scope_id",
                table: "archives");

            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_values",
                table: "archives");

            migrationBuilder.DropColumn(
                name: "profile_version",
                table: "archives");

            migrationBuilder.CreateIndex(
                name: "IX_archives_firm_id_client_id_engagement_id",
                table: "archives",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_values",
                table: "archives",
                sql: "length(trim(profile_id)) > 0 AND length(trim(status)) > 0");
        }
    }
}
