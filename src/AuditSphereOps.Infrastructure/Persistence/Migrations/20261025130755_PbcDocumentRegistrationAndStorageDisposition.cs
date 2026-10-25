using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcDocumentRegistrationAndStorageDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pbc_document_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_upload_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_identity = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    binding_sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_document_registrations", x => x.id);
                    table.UniqueConstraint("AK_pbc_document_registrations_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_pbc_document_registrations_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_pbc_document_registration_values", "length(trim(provider_identity)) BETWEEN 1 AND 1000 AND binding_sha256_hex ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_pbc_document_registrations_document_references_firm_id_clie~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.document_reference_id },
                        principalTable: "document_references",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_document_registrations_document_snapshots_firm_id_clien~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.document_snapshot_id },
                        principalTable: "document_snapshots",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_document_registrations_durable_operations_firm_id_trans~",
                        columns: x => new { x.firm_id, x.transfer_operation_id },
                        principalTable: "durable_operations",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_document_registrations_pbc_upload_intents_firm_id_clien~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_upload_intent_id },
                        principalTable: "pbc_upload_intents",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pbc_storage_dispositions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_upload_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_document_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    local_copy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_storage_dispositions", x => x.id);
                    table.UniqueConstraint("AK_pbc_storage_dispositions_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_pbc_storage_disposition_values", "read_source IN ('LOCAL_STAGED','SELECTED_SITE') AND local_copy IN ('RETAINED','RELEASED') AND revision >= 1 AND NOT (read_source = 'LOCAL_STAGED' AND local_copy = 'RELEASED')");
                    table.ForeignKey(
                        name: "FK_pbc_storage_dispositions_pbc_document_registrations_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_document_registration_id },
                        principalTable: "pbc_document_registrations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_storage_dispositions_pbc_upload_intents_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_upload_intent_id },
                        principalTable: "pbc_upload_intents",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pbc_storage_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_storage_disposition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    read_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    local_copy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_storage_events", x => x.id);
                    table.CheckConstraint("ck_pbc_storage_event_values", "((kind = 'REMOTE_READ_ACTIVATED' AND read_source = 'SELECTED_SITE' AND local_copy = 'RETAINED') OR (kind = 'REMOTE_READ_REVERTED' AND read_source = 'LOCAL_STAGED' AND local_copy = 'RETAINED') OR (kind = 'LOCAL_COPY_RELEASED' AND read_source = 'SELECTED_SITE' AND local_copy = 'RELEASED')) AND revision >= 1 AND length(trim(reason)) BETWEEN 1 AND 500 AND (actor_user_id IS NOT NULL OR operation_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_pbc_storage_events_durable_operations_firm_id_operation_id",
                        columns: x => new { x.firm_id, x.operation_id },
                        principalTable: "durable_operations",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_storage_events_pbc_storage_dispositions_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_storage_disposition_id },
                        principalTable: "pbc_storage_dispositions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_storage_events_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_document_registrations_firm_id_client_id_engagement_id_~",
                table: "pbc_document_registrations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "document_reference_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_document_registrations_firm_id_client_id_engagement_id~1",
                table: "pbc_document_registrations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "document_snapshot_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_document_registrations_firm_id_client_id_engagement_id~2",
                table: "pbc_document_registrations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_upload_intent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_document_registrations_firm_id_transfer_operation_id",
                table: "pbc_document_registrations",
                columns: new[] { "firm_id", "transfer_operation_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pbc_document_registration_reference",
                table: "pbc_document_registrations",
                columns: new[] { "firm_id", "document_reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pbc_document_registration_snapshot",
                table: "pbc_document_registrations",
                columns: new[] { "firm_id", "document_snapshot_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pbc_document_registration_upload",
                table: "pbc_document_registrations",
                columns: new[] { "firm_id", "pbc_upload_intent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pbc_storage_dispositions_firm_id_client_id_engagement_id_p~1",
                table: "pbc_storage_dispositions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_upload_intent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_storage_dispositions_firm_id_client_id_engagement_id_pb~",
                table: "pbc_storage_dispositions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_document_registration_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pbc_storage_disposition_registration",
                table: "pbc_storage_dispositions",
                columns: new[] { "firm_id", "pbc_document_registration_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pbc_storage_disposition_upload",
                table: "pbc_storage_dispositions",
                columns: new[] { "firm_id", "pbc_upload_intent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pbc_storage_events_firm_id_actor_user_id",
                table: "pbc_storage_events",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_storage_events_firm_id_client_id_engagement_id_pbc_stor~",
                table: "pbc_storage_events",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_storage_disposition_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_storage_events_firm_id_operation_id",
                table: "pbc_storage_events",
                columns: new[] { "firm_id", "operation_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pbc_storage_event_revision",
                table: "pbc_storage_events",
                columns: new[] { "firm_id", "pbc_storage_disposition_id", "revision" },
                unique: true);

            migrationBuilder.Sql(PbcDocumentRegistrationSql.Install);
            migrationBuilder.Sql(PbcStorageDispositionSql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PbcStorageDispositionSql.Uninstall);
            migrationBuilder.Sql(PbcDocumentRegistrationSql.Uninstall);
            migrationBuilder.DropTable(
                name: "pbc_storage_events");

            migrationBuilder.DropTable(
                name: "pbc_storage_dispositions");

            migrationBuilder.DropTable(
                name: "pbc_document_registrations");
        }
    }
}
