using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcStorageCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_pbc_storage_events_scope_id",
                table: "pbc_storage_events",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "pbc_storage_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_upload_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_storage_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pbc_document_registration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    input_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    outcome_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_storage_command_receipts", x => x.id);
                    table.CheckConstraint("ck_pbc_storage_command_receipt_values", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND ((action IN ('ACTIVATE_REMOTE_READ','REVERT_TO_LOCAL_READ','RELEASE_LOCAL_COPY') AND pbc_storage_event_id IS NOT NULL AND pbc_document_registration_id IS NULL) OR (action = 'BACKFILL_REGISTRATION' AND pbc_document_registration_id IS NOT NULL AND pbc_storage_event_id IS NULL)) AND length(input_json) BETWEEN 1 AND 4000 AND length(preview_json) BETWEEN 1 AND 100000 AND length(outcome_json) BETWEEN 1 AND 4000 AND jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object' AND jsonb_typeof(outcome_json::jsonb) = 'object'");
                    table.ForeignKey(
                        name: "FK_pbc_storage_command_receipts_pbc_document_registrations_fir~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_document_registration_id },
                        principalTable: "pbc_document_registrations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_storage_command_receipts_pbc_storage_events_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_storage_event_id },
                        principalTable: "pbc_storage_events",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_storage_command_receipts_pbc_upload_intents_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_upload_intent_id },
                        principalTable: "pbc_upload_intents",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_storage_command_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_storage_command_receipts_firm_id_client_id_engagement_~1",
                table: "pbc_storage_command_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_storage_event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_storage_command_receipts_firm_id_client_id_engagement_~2",
                table: "pbc_storage_command_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_upload_intent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_storage_command_receipts_firm_id_client_id_engagement_i~",
                table: "pbc_storage_command_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_document_registration_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pbc_storage_receipt_actor_request",
                table: "pbc_storage_command_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pbc_storage_receipt_event",
                table: "pbc_storage_command_receipts",
                columns: new[] { "firm_id", "pbc_storage_event_id" },
                unique: true,
                filter: "pbc_storage_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pbc_storage_receipt_registration",
                table: "pbc_storage_command_receipts",
                columns: new[] { "firm_id", "pbc_document_registration_id" },
                unique: true,
                filter: "pbc_document_registration_id IS NOT NULL");

            migrationBuilder.Sql(PbcStorageCommandReceiptSql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PbcStorageCommandReceiptSql.Uninstall);
            migrationBuilder.DropTable(
                name: "pbc_storage_command_receipts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_pbc_storage_events_scope_id",
                table: "pbc_storage_events");
        }
    }
}
