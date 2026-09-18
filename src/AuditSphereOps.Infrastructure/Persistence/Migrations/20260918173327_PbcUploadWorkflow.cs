using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcUploadWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pbc_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    entity_scope = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    period_start = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    period_end = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_format = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    control_totals = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    client_owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    confidentiality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    acceptance_criteria = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    clarification_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_requests", x => x.id);
                    table.UniqueConstraint("AK_pbc_requests_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_pbc_requests_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_pbc_request_values", "state IN ('DRAFT','SENT','ACKNOWLEDGED','PARTIALLY_RECEIVED','RECEIVED','UNDER_REVIEW','ACCEPTED','CLOSED','CLARIFICATION_REQUIRED','RESUBMITTED') AND revision >= 1 AND length(trim(objective)) > 0 AND length(trim(entity_scope)) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(trim(area)) > 0 AND length(trim(requested_format)) > 0 AND length(trim(due_date)) = 10 AND length(trim(confidentiality)) > 0 AND length(trim(acceptance_criteria)) > 0 AND ((state = 'ACCEPTED' AND accepted_at IS NOT NULL AND accepted_by_user_id IS NOT NULL) OR state <> 'ACCEPTED')");
                    table.ForeignKey(
                        name: "FK_pbc_requests_engagements_firm_id_client_id_engagement_id",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_requests_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_requests_users_firm_id_accepted_by_user_id",
                        columns: x => new { x.firm_id, x.accepted_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_requests_users_firm_id_client_owner_user_id",
                        columns: x => new { x.firm_id, x.client_owner_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_requests_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_requests_users_firm_id_firm_owner_user_id",
                        columns: x => new { x.firm_id, x.firm_owner_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_requests_users_firm_id_reviewer_user_id",
                        columns: x => new { x.firm_id, x.reviewer_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pbc_upload_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploader_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    declared_byte_count = table.Column<long>(type: "bigint", nullable: false),
                    declared_sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    received_byte_count = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    final_sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_upload_intents", x => x.id);
                    table.UniqueConstraint("AK_pbc_upload_intents_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_pbc_upload_intents_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_pbc_upload_intent_values", "state IN ('STARTED','CHUNKING','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex) OR state <> 'RECEIVED')");
                    table.ForeignKey(
                        name: "FK_pbc_upload_intents_pbc_requests_firm_id_client_id_engagemen~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_request_id },
                        principalTable: "pbc_requests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_upload_intents_users_firm_id_uploader_user_id",
                        columns: x => new { x.firm_id, x.uploader_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pbc_upload_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_upload_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    offset = table.Column<long>(type: "bigint", nullable: false),
                    byte_count = table.Column<int>(type: "integer", nullable: false),
                    sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_upload_chunks", x => x.id);
                    table.CheckConstraint("ck_pbc_upload_chunk_values", "chunk_index >= 0 AND \"offset\" >= 0 AND byte_count > 0 AND byte_count <= 8388608 AND sha256_hex ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_pbc_upload_chunks_pbc_upload_intents_firm_id_client_id_enga~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_upload_intent_id },
                        principalTable: "pbc_upload_intents",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_requests_firm_id_accepted_by_user_id",
                table: "pbc_requests",
                columns: new[] { "firm_id", "accepted_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_requests_firm_id_client_owner_user_id",
                table: "pbc_requests",
                columns: new[] { "firm_id", "client_owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_requests_firm_id_created_by_user_id",
                table: "pbc_requests",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_requests_firm_id_engagement_id_state_due_date",
                table: "pbc_requests",
                columns: new[] { "firm_id", "engagement_id", "state", "due_date" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_requests_firm_id_firm_owner_user_id",
                table: "pbc_requests",
                columns: new[] { "firm_id", "firm_owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_requests_firm_id_reviewer_user_id",
                table: "pbc_requests",
                columns: new[] { "firm_id", "reviewer_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_upload_chunks_firm_id_client_id_engagement_id_pbc_uploa~",
                table: "pbc_upload_chunks",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_upload_intent_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pbc_upload_chunk_identity",
                table: "pbc_upload_chunks",
                columns: new[] { "firm_id", "pbc_upload_intent_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pbc_upload_intents_firm_id_client_id_engagement_id_pbc_requ~",
                table: "pbc_upload_intents",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_upload_intents_firm_id_pbc_request_id_created_at",
                table: "pbc_upload_intents",
                columns: new[] { "firm_id", "pbc_request_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_upload_intents_firm_id_uploader_user_id",
                table: "pbc_upload_intents",
                columns: new[] { "firm_id", "uploader_user_id" });

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_pbc_upload_chunks_append_only
                  BEFORE UPDATE OR DELETE ON pbc_upload_chunks
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pbc_upload_chunks)
                     OR EXISTS (SELECT 1 FROM pbc_upload_intents)
                     OR EXISTS (SELECT 1 FROM pbc_requests) THEN
                    RAISE EXCEPTION 'PBC downgrade would discard request or upload evidence.';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_pbc_upload_chunks_append_only ON pbc_upload_chunks;
                """);
            migrationBuilder.DropTable(
                name: "pbc_upload_chunks");

            migrationBuilder.DropTable(
                name: "pbc_upload_intents");

            migrationBuilder.DropTable(
                name: "pbc_requests");
        }
    }
}
