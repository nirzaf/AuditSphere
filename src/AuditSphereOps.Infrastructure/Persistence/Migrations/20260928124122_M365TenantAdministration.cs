using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M365TenantAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_role_grant_scope",
                table: "role_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_m365_consent_attempt_values",
                table: "m365_tenant_consent_attempts");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_sign_in_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "role_grants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "role_grants",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "role_grant_change_evidence",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consent_verified_at",
                table: "m365_tenant_consent_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consenting_object_id",
                table: "m365_tenant_consent_attempts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consenting_tenant_id",
                table: "m365_tenant_consent_attempts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "identity_expires_at",
                table: "m365_tenant_consent_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identity_state_hash",
                table: "m365_tenant_consent_attempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nonce_hash",
                table: "m365_tenant_consent_attempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "m365_administration_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    target_tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    target_object_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_state = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    new_state = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    role_scope_change = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    external_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    result = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_administration_events", x => x.id);
                    table.CheckConstraint("ck_m365_admin_event_values", "length(trim(operation)) > 0 AND length(trim(result)) > 0 AND length(trim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_m365_administration_events_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "m365_managed_directory_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    group_object_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    purpose = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    approval_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_managed_directory_groups", x => x.id);
                    table.UniqueConstraint("AK_m365_managed_groups_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_m365_managed_group_values", "length(trim(tenant_id)) > 0 AND length(trim(group_object_id)) > 0 AND length(trim(display_name)) > 0 AND length(trim(purpose)) > 0 AND length(trim(approval_reason)) > 0 AND (retired_at IS NULL) = (retired_by_user_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_m365_managed_directory_groups_users_firm_id_approved_by_use~",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "m365_tenant_capability_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consent_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    capability = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    permission = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    diagnostic_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    observed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_tenant_capability_verifications", x => x.id);
                    table.CheckConstraint("ck_m365_capability_values", "length(trim(tenant_id)) > 0 AND capability IN ('SIGN_IN','DIRECTORY_READ','SELECTED_SITE','OUTBOUND_MAIL','TENANT_USER_PROVISIONING','GUEST_INVITATION','GROUP_MEMBERSHIP') AND state IN ('VERIFIED','NOT_GRANTED','FAILED','BLOCKED_EXTERNAL') AND length(trim(permission)) > 0 AND length(trim(diagnostic_code)) > 0");
                    table.ForeignKey(
                        name: "FK_m365_tenant_capability_verifications_m365_connection_revisi~",
                        columns: x => new { x.firm_id, x.connection_revision_id },
                        principalTable: "m365_connection_revisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_m365_tenant_capability_verifications_users_firm_id_observed~",
                        columns: x => new { x.firm_id, x.observed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "m365_external_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_descriptor = table.Column<string>(type: "character varying(640)", maxLength: 640, nullable: false),
                    display_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    result_object_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    result_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reconciliation_result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    requested_role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requested_scope_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    requested_client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_engagement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    managed_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bound_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bound_role_grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_m365_external_operations", x => x.id);
                    table.UniqueConstraint("AK_m365_external_operations_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_m365_external_operation_values", "length(trim(idempotency_key)) >= 16 AND kind IN ('CREATE_TENANT_USER','INVITE_GUEST','ADD_GROUP_MEMBER','REMOVE_GROUP_MEMBER') AND state IN ('REQUESTED','AUTHORIZED','DISPATCHING','ACCEPTED','FAILED','UNKNOWN','RECONCILED','BOUND','CONFLICT_REQUIRES_REVIEW') AND request_fingerprint ~ '^[0-9a-f]{64}$' AND length(trim(tenant_id)) > 0 AND length(trim(target_descriptor)) > 0 AND length(trim(reason)) > 0 AND attempt_count >= 0 AND (state <> 'BOUND' OR result_object_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_m365_external_operations_m365_managed_directory_groups_firm~",
                        columns: x => new { x.firm_id, x.managed_group_id },
                        principalTable: "m365_managed_directory_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_m365_external_operations_users_firm_id_requested_by_user_id",
                        columns: x => new { x.firm_id, x.requested_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_grants_active_expiry",
                table: "role_grants",
                column: "expires_at",
                filter: "revoked_at IS NULL AND expires_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_role_grant_scope",
                table: "role_grants",
                sql: "length(role) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL) AND (expires_at IS NULL OR expires_at > granted_at)");

            migrationBuilder.CreateIndex(
                name: "IX_m365_tenant_consent_attempts_identity_state_hash",
                table: "m365_tenant_consent_attempts",
                column: "identity_state_hash",
                unique: true,
                filter: "identity_state_hash IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_m365_consent_attempt_values",
                table: "m365_tenant_consent_attempts",
                sql: "length(trim(initiator_object_id)) > 0 AND length(trim(expected_tenant_id)) > 0 AND length(trim(application_client_id)) > 0 AND state_hash ~ '^[0-9a-f]{64}$' AND state IN ('PENDING','RETURNED_UNVERIFIED','IDENTITY_PENDING','CONSENT_VERIFIED','DENIED','EXPIRED') AND initiating_session_epoch >= 0 AND expires_at > created_at AND (identity_state_hash IS NULL OR identity_state_hash ~ '^[0-9a-f]{64}$') AND (nonce_hash IS NULL OR nonce_hash ~ '^[0-9a-f]{64}$') AND (state <> 'CONSENT_VERIFIED' OR (consenting_object_id IS NOT NULL AND consenting_tenant_id = expected_tenant_id))");

            migrationBuilder.CreateIndex(
                name: "IX_m365_administration_events_firm_id_actor_user_id",
                table: "m365_administration_events",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_administration_events_firm_id_created_at",
                table: "m365_administration_events",
                columns: new[] { "firm_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_administration_events_firm_id_target_user_id_created_at",
                table: "m365_administration_events",
                columns: new[] { "firm_id", "target_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_external_operations_firm_id_idempotency_key",
                table: "m365_external_operations",
                columns: new[] { "firm_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_m365_external_operations_firm_id_managed_group_id",
                table: "m365_external_operations",
                columns: new[] { "firm_id", "managed_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_external_operations_firm_id_requested_by_user_id",
                table: "m365_external_operations",
                columns: new[] { "firm_id", "requested_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_external_operations_firm_id_state_created_at",
                table: "m365_external_operations",
                columns: new[] { "firm_id", "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_managed_directory_groups_firm_id_approved_by_user_id",
                table: "m365_managed_directory_groups",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_managed_directory_groups_firm_id_tenant_id_group_objec~",
                table: "m365_managed_directory_groups",
                columns: new[] { "firm_id", "tenant_id", "group_object_id" },
                unique: true,
                filter: "retired_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_m365_tenant_capability_verifications_firm_id_connection_rev~",
                table: "m365_tenant_capability_verifications",
                columns: new[] { "firm_id", "connection_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_tenant_capability_verifications_firm_id_observed_by_us~",
                table: "m365_tenant_capability_verifications",
                columns: new[] { "firm_id", "observed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_tenant_capability_verifications_firm_id_tenant_id_capa~",
                table: "m365_tenant_capability_verifications",
                columns: new[] { "firm_id", "tenant_id", "capability", "observed_at" });

            // Administration audit events and capability observations are immutable evidence (§17).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_m365_administration_evidence_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Microsoft 365 administration evidence is append-only.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_m365_administration_events_append_only
                BEFORE UPDATE OR DELETE ON m365_administration_events
                FOR EACH ROW EXECUTE FUNCTION enforce_m365_administration_evidence_append_only();
                CREATE TRIGGER trg_m365_capability_verifications_append_only
                BEFORE UPDATE OR DELETE ON m365_tenant_capability_verifications
                FOR EACH ROW EXECUTE FUNCTION enforce_m365_administration_evidence_append_only();
                CREATE TRIGGER trg_role_grant_change_evidence_append_only
                BEFORE UPDATE OR DELETE ON role_grant_change_evidence
                FOR EACH ROW EXECUTE FUNCTION enforce_m365_administration_evidence_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM m365_administration_events) OR
                       EXISTS (SELECT 1 FROM m365_tenant_capability_verifications) OR
                       EXISTS (SELECT 1 FROM m365_external_operations) THEN
                        RAISE EXCEPTION 'Microsoft 365 administration downgrade would discard immutable evidence.';
                    END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_m365_administration_events_append_only ON m365_administration_events;
                DROP TRIGGER IF EXISTS trg_m365_capability_verifications_append_only ON m365_tenant_capability_verifications;
                DROP TRIGGER IF EXISTS trg_role_grant_change_evidence_append_only ON role_grant_change_evidence;
                DROP FUNCTION IF EXISTS enforce_m365_administration_evidence_append_only();
                """);
            migrationBuilder.DropTable(
                name: "m365_administration_events");

            migrationBuilder.DropTable(
                name: "m365_external_operations");

            migrationBuilder.DropTable(
                name: "m365_tenant_capability_verifications");

            migrationBuilder.DropTable(
                name: "m365_managed_directory_groups");

            migrationBuilder.DropIndex(
                name: "ix_role_grants_active_expiry",
                table: "role_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_role_grant_scope",
                table: "role_grants");

            migrationBuilder.DropIndex(
                name: "IX_m365_tenant_consent_attempts_identity_state_hash",
                table: "m365_tenant_consent_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_m365_consent_attempt_values",
                table: "m365_tenant_consent_attempts");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_sign_in_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "role_grants");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "role_grants");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "role_grant_change_evidence");

            migrationBuilder.DropColumn(
                name: "consent_verified_at",
                table: "m365_tenant_consent_attempts");

            migrationBuilder.DropColumn(
                name: "consenting_object_id",
                table: "m365_tenant_consent_attempts");

            migrationBuilder.DropColumn(
                name: "consenting_tenant_id",
                table: "m365_tenant_consent_attempts");

            migrationBuilder.DropColumn(
                name: "identity_expires_at",
                table: "m365_tenant_consent_attempts");

            migrationBuilder.DropColumn(
                name: "identity_state_hash",
                table: "m365_tenant_consent_attempts");

            migrationBuilder.DropColumn(
                name: "nonce_hash",
                table: "m365_tenant_consent_attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_role_grant_scope",
                table: "role_grants",
                sql: "length(role) > 0 AND (engagement_id IS NULL OR client_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_m365_consent_attempt_values",
                table: "m365_tenant_consent_attempts",
                sql: "length(trim(initiator_object_id)) > 0 AND length(trim(expected_tenant_id)) > 0 AND length(trim(application_client_id)) > 0 AND state_hash ~ '^[0-9a-f]{64}$' AND state IN ('PENDING','RETURNED_UNVERIFIED','DENIED','EXPIRED') AND initiating_session_epoch >= 0 AND expires_at > created_at");
        }
    }
}
