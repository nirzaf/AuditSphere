using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcUploadCapability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pbc_upload_intents) THEN
                    RAISE EXCEPTION 'PBC capability migration refuses existing upload intents because their secret cannot be reconstructed.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents");

            migrationBuilder.AddColumn<string>(
                name: "capability_hash",
                table: "pbc_upload_intents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "staged_path",
                table: "pbc_upload_chunks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents",
                sql: "state IN ('STARTED','CHUNKING','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex) OR state <> 'RECEIVED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pbc_upload_chunks)
                     OR EXISTS (SELECT 1 FROM pbc_upload_intents) THEN
                    RAISE EXCEPTION 'PBC capability downgrade would discard upload evidence or capability binding.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents");

            migrationBuilder.DropColumn(
                name: "capability_hash",
                table: "pbc_upload_intents");

            migrationBuilder.DropColumn(
                name: "staged_path",
                table: "pbc_upload_chunks");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents",
                sql: "state IN ('STARTED','CHUNKING','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex) OR state <> 'RECEIVED')");
        }
    }
}
