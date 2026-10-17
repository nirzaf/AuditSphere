using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcAcceptanceReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents");

            migrationBuilder.AddColumn<long>(
                name: "request_revision",
                table: "pbc_upload_intents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<long>(
                name: "request_revision",
                table: "pbc_upload_intents",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "pbc_acceptance_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_request_revision = table.Column<long>(type: "bigint", nullable: false),
                    source_request_revision = table.Column<long>(type: "bigint", nullable: false),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pbc_upload_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    upload_intent_revision = table.Column<long>(type: "bigint", nullable: false),
                    uploader_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    content_sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    transfer_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_identity = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    provider_receipt_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_acceptance_receipts", x => x.id);
                    table.UniqueConstraint("AK_pbc_acceptance_receipts_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_pbc_acceptance_receipt_values", "accepted_request_revision >= 1 AND source_request_revision >= 1 AND upload_intent_revision >= 1 AND byte_count > 0 AND length(trim(file_name)) BETWEEN 1 AND 255 AND length(trim(provider_identity)) BETWEEN 1 AND 1000 AND content_sha256_hex ~ '^[0-9a-f]{64}$' AND provider_receipt_digest ~ '^[0-9a-f]{64}$' AND source_digest ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_pbc_acceptance_receipts_durable_operations_firm_id_transfer~",
                        columns: x => new { x.firm_id, x.transfer_operation_id },
                        principalTable: "durable_operations",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_acceptance_receipts_pbc_requests_firm_id_client_id_enga~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_request_id },
                        principalTable: "pbc_requests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_acceptance_receipts_pbc_upload_intents_firm_id_client_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_upload_intent_id },
                        principalTable: "pbc_upload_intents",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_acceptance_receipts_users_firm_id_reviewer_user_id",
                        columns: x => new { x.firm_id, x.reviewer_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_acceptance_receipts_users_firm_id_uploader_user_id",
                        columns: x => new { x.firm_id, x.uploader_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents",
                sql: "state IN ('STARTED','CHUNKING','STAGED','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND request_revision >= 0 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'STAGED' AND transfer_operation_id IS NOT NULL) OR state NOT IN ('STAGED')) AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex AND provider_registered_at IS NOT NULL AND provider_receipt_digest ~ '^[0-9a-f]{64}$' AND transfer_operation_id IS NOT NULL) OR state <> 'RECEIVED')");

            migrationBuilder.CreateIndex(
                name: "IX_pbc_acceptance_receipts_firm_id_client_id_engagement_id_pb~1",
                table: "pbc_acceptance_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_upload_intent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_acceptance_receipts_firm_id_client_id_engagement_id_pbc~",
                table: "pbc_acceptance_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_acceptance_receipts_firm_id_pbc_upload_intent_id",
                table: "pbc_acceptance_receipts",
                columns: new[] { "firm_id", "pbc_upload_intent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_acceptance_receipts_firm_id_reviewer_user_id",
                table: "pbc_acceptance_receipts",
                columns: new[] { "firm_id", "reviewer_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_acceptance_receipts_firm_id_transfer_operation_id",
                table: "pbc_acceptance_receipts",
                columns: new[] { "firm_id", "transfer_operation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_acceptance_receipts_firm_id_uploader_user_id",
                table: "pbc_acceptance_receipts",
                columns: new[] { "firm_id", "uploader_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pbc_acceptance_receipt_request",
                table: "pbc_acceptance_receipts",
                columns: new[] { "firm_id", "pbc_request_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_pbc_acceptance_receipt_source() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE request_row pbc_requests%ROWTYPE;
                        upload_row pbc_upload_intents%ROWTYPE;
                        operation_row durable_operations%ROWTYPE;
                BEGIN
                  SELECT * INTO request_row FROM pbc_requests
                    WHERE firm_id = NEW.firm_id AND id = NEW.pbc_request_id FOR KEY SHARE;
                  IF NOT FOUND OR request_row.client_id <> NEW.client_id OR
                     request_row.engagement_id <> NEW.engagement_id OR request_row.state <> 'ACCEPTED' OR
                     request_row.revision <> NEW.accepted_request_revision OR
                     request_row.reviewer_user_id <> NEW.reviewer_user_id OR
                     request_row.accepted_by_user_id IS DISTINCT FROM NEW.reviewer_user_id OR
                     request_row.accepted_at IS DISTINCT FROM NEW.accepted_at THEN
                    RAISE EXCEPTION 'PBC acceptance must match the final assigned-reviewer request decision.' USING ERRCODE = '23514';
                  END IF;

                  SELECT * INTO upload_row FROM pbc_upload_intents
                    WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id AND
                      engagement_id = NEW.engagement_id AND id = NEW.pbc_upload_intent_id FOR KEY SHARE;
                  IF NOT FOUND OR upload_row.pbc_request_id <> NEW.pbc_request_id OR
                     upload_row.request_revision <> NEW.source_request_revision OR upload_row.state <> 'RECEIVED' OR
                     upload_row.revision <> NEW.upload_intent_revision OR upload_row.uploader_user_id <> NEW.uploader_user_id OR
                     upload_row.uploader_user_id = NEW.reviewer_user_id OR upload_row.file_name <> NEW.file_name OR
                     upload_row.declared_byte_count <> NEW.byte_count OR
                     upload_row.final_sha256_hex <> NEW.content_sha256_hex OR
                     upload_row.provider_receipt_digest <> NEW.provider_receipt_digest OR
                     upload_row.transfer_operation_id <> NEW.transfer_operation_id OR
                     upload_row.provider_registered_at IS DISTINCT FROM NEW.provider_registered_at THEN
                    RAISE EXCEPTION 'PBC acceptance does not match the exact provider-verified upload.' USING ERRCODE = '23514';
                  END IF;

                  SELECT * INTO operation_row FROM durable_operations
                    WHERE firm_id = NEW.firm_id AND id = NEW.transfer_operation_id FOR KEY SHARE;
                  IF NOT FOUND OR operation_row.operation_kind <> 'TransferPbcDocument.v1' OR
                     operation_row.status <> 'COMPLETED' OR operation_row.target_id <> NEW.pbc_upload_intent_id OR
                     operation_row.client_id <> NEW.client_id OR operation_row.engagement_id <> NEW.engagement_id OR
                     operation_row.expected_revision <> NEW.upload_intent_revision - 1 OR
                     operation_row.result_identity IS DISTINCT FROM NEW.provider_identity OR
                     operation_row.result_digest IS DISTINCT FROM NEW.provider_receipt_digest THEN
                    RAISE EXCEPTION 'PBC acceptance does not match the completed durable provider transfer.' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER trg_pbc_acceptance_receipt_source
                  AFTER INSERT ON pbc_acceptance_receipts DEFERRABLE INITIALLY DEFERRED
                  FOR EACH ROW EXECUTE FUNCTION enforce_pbc_acceptance_receipt_source();

                CREATE OR REPLACE FUNCTION prevent_pbc_acceptance_receipt_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'PBC acceptance receipts are immutable professional evidence.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_pbc_acceptance_receipt_append_only
                  BEFORE UPDATE OR DELETE ON pbc_acceptance_receipts
                  FOR EACH ROW EXECUTE FUNCTION prevent_pbc_acceptance_receipt_mutation();
                CREATE TRIGGER trg_pbc_acceptance_receipt_no_truncate
                  BEFORE TRUNCATE ON pbc_acceptance_receipts
                  FOR EACH STATEMENT EXECUTE FUNCTION prevent_pbc_acceptance_receipt_mutation();

                CREATE OR REPLACE FUNCTION prevent_accepted_pbc_upload_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pbc_acceptance_receipts r
                    WHERE r.firm_id = OLD.firm_id AND r.pbc_upload_intent_id = OLD.id) THEN
                    RAISE EXCEPTION 'An accepted PBC upload version is immutable.' USING ERRCODE = '55000';
                  END IF;
                  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_pbc_accepted_upload_immutable
                  BEFORE UPDATE OR DELETE ON pbc_upload_intents
                  FOR EACH ROW EXECUTE FUNCTION prevent_accepted_pbc_upload_mutation();

                CREATE OR REPLACE FUNCTION prevent_accepted_pbc_request_rewrite() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.state IN ('ACCEPTED','CLOSED') AND EXISTS (SELECT 1 FROM pbc_acceptance_receipts r
                    WHERE r.firm_id = OLD.firm_id AND r.pbc_request_id = OLD.id) THEN
                    IF TG_OP = 'DELETE' THEN
                      RAISE EXCEPTION 'An accepted PBC request is immutable evidence.' USING ERRCODE = '55000';
                    END IF;
                    IF (to_jsonb(NEW) - ARRAY['state','revision','updated_at']) IS DISTINCT FROM
                       (to_jsonb(OLD) - ARRAY['state','revision','updated_at']) OR
                       NEW.state NOT IN ('ACCEPTED','CLOSED') OR
                       (NEW.state IS DISTINCT FROM OLD.state AND NOT (OLD.state = 'ACCEPTED' AND NEW.state = 'CLOSED')) OR
                       NEW.revision <> OLD.revision + 1 THEN
                      RAISE EXCEPTION 'An accepted PBC decision and source identity cannot be rewritten.' USING ERRCODE = '55000';
                    END IF;
                  END IF;
                  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_pbc_accepted_request_immutable
                  BEFORE UPDATE OR DELETE ON pbc_requests
                  FOR EACH ROW EXECUTE FUNCTION prevent_accepted_pbc_request_rewrite();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pbc_acceptance_receipts) THEN
                    RAISE EXCEPTION 'Cannot remove immutable PBC acceptance evidence.' USING ERRCODE = '55000';
                  END IF;
                END;
                $$;
                DROP TRIGGER IF EXISTS trg_pbc_accepted_upload_immutable ON pbc_upload_intents;
                DROP FUNCTION IF EXISTS prevent_accepted_pbc_upload_mutation();
                DROP TRIGGER IF EXISTS trg_pbc_accepted_request_immutable ON pbc_requests;
                DROP FUNCTION IF EXISTS prevent_accepted_pbc_request_rewrite();
                DROP TRIGGER IF EXISTS trg_pbc_acceptance_receipt_no_truncate ON pbc_acceptance_receipts;
                DROP TRIGGER IF EXISTS trg_pbc_acceptance_receipt_append_only ON pbc_acceptance_receipts;
                DROP TRIGGER IF EXISTS trg_pbc_acceptance_receipt_source ON pbc_acceptance_receipts;
                DROP FUNCTION IF EXISTS prevent_pbc_acceptance_receipt_mutation();
                DROP FUNCTION IF EXISTS enforce_pbc_acceptance_receipt_source();
                """);

            migrationBuilder.DropTable(
                name: "pbc_acceptance_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents");

            migrationBuilder.DropColumn(
                name: "request_revision",
                table: "pbc_upload_intents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_upload_intent_values",
                table: "pbc_upload_intents",
                sql: "state IN ('STARTED','CHUNKING','STAGED','RECEIVED','FAILED','EXPIRED') AND revision >= 1 AND length(trim(file_name)) > 0 AND length(trim(content_type)) > 0 AND declared_byte_count > 0 AND declared_byte_count <= 262144000 AND received_byte_count >= 0 AND received_byte_count <= declared_byte_count AND declared_sha256_hex ~ '^[0-9a-f]{64}$' AND capability_hash ~ '^[0-9a-f]{64}$' AND expires_at > created_at AND ((state = 'STAGED' AND transfer_operation_id IS NOT NULL) OR state NOT IN ('STAGED')) AND ((state = 'RECEIVED' AND completed_at IS NOT NULL AND final_sha256_hex = declared_sha256_hex AND provider_registered_at IS NOT NULL AND provider_receipt_digest ~ '^[0-9a-f]{64}$' AND transfer_operation_id IS NOT NULL) OR state <> 'RECEIVED')");
        }
    }
}
