using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcTrustedCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pbc_upload_intents WHERE state = 'RECEIVED') THEN
                    RAISE EXCEPTION 'PBC trusted completion migration refuses previously received upload intents because their provider registration evidence cannot be reconstructed.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents");

            migrationBuilder.AddColumn<string>(
                name: "provider_receipt_digest",
                table: "pbc_upload_intents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provider_registered_at",
                table: "pbc_upload_intents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_operation_id",
                table: "pbc_upload_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_durable_operations_firm_id_id",
                table: "durable_operations",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_upload_intents_firm_id_transfer_operation_id",
                table: "pbc_upload_intents",
                columns: new[] { "firm_id", "transfer_operation_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents",
                sql: "state IN ('STARTED','CHUNKING','STAGED','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'STAGED' AND transfer_operation_id IS NOT NULL) OR state NOT IN ('STAGED')) AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex AND provider_registered_at IS NOT NULL AND provider_receipt_digest ~ '^[0-9a-f]{64}$' AND transfer_operation_id IS NOT NULL) OR state <> 'RECEIVED')");

            migrationBuilder.AddForeignKey(
                name: "FK_pbc_upload_intents_durable_operations_firm_id_transfer_oper~",
                table: "pbc_upload_intents",
                columns: new[] { "firm_id", "transfer_operation_id" },
                principalTable: "durable_operations",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pbc_upload_intents)
                     OR EXISTS (SELECT 1 FROM pbc_upload_chunks) THEN
                    RAISE EXCEPTION 'PBC trusted completion downgrade would discard upload evidence or provider binding.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_pbc_upload_intents_durable_operations_firm_id_transfer_oper~",
                table: "pbc_upload_intents");

            migrationBuilder.DropIndex(
                name: "IX_pbc_upload_intents_firm_id_transfer_operation_id",
                table: "pbc_upload_intents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_durable_operations_firm_id_id",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "provider_receipt_digest",
                table: "pbc_upload_intents");

            migrationBuilder.DropColumn(
                name: "provider_registered_at",
                table: "pbc_upload_intents");

            migrationBuilder.DropColumn(
                name: "transfer_operation_id",
                table: "pbc_upload_intents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents",
                sql: "state IN ('STARTED','CHUNKING','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex) OR state <> 'RECEIVED')");
        }
    }
}
