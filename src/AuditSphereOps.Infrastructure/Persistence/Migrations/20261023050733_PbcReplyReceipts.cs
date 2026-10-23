using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcReplyReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_communication_values",
                table: "pbc_communications");

            migrationBuilder.AddColumn<Guid>(
                name: "client_request_id",
                table: "pbc_communications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_request_sha256",
                table: "pbc_communications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_pbc_communications_client_reply_request",
                table: "pbc_communications",
                columns: new[] { "firm_id", "author_user_id", "client_request_id" },
                unique: true,
                filter: "kind = 'CLIENT_MESSAGE' AND client_request_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_communication_values",
                table: "pbc_communications",
                sql: "kind IN ('REQUEST','STAFF_MESSAGE','CLIENT_MESSAGE','CLIENT_ACTION','EMAIL') AND length(trim(body)) > 0 AND (delivery_state IS NULL OR delivery_state IN ('QUEUED','SENT','FAILED','UNCERTAIN')) AND ((kind = 'EMAIL' AND recipient_email IS NOT NULL AND subject IS NOT NULL AND portal_url IS NOT NULL AND delivery_state IS NOT NULL) OR (kind <> 'EMAIL' AND recipient_email IS NULL AND subject IS NULL AND portal_url IS NULL AND delivery_state IS NULL)) AND ((client_request_id IS NULL AND client_request_sha256 IS NULL) OR (kind = 'CLIENT_MESSAGE' AND client_request_id IS NOT NULL AND client_request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND client_request_sha256 IS NOT NULL AND client_request_sha256 ~ '^[0-9a-f]{64}$'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE pbc_communications IN ACCESS EXCLUSIVE MODE;
                DO $pbc_reply_receipts_downgrade$
                BEGIN
                  IF EXISTS (
                    SELECT 1
                    FROM pbc_communications
                    WHERE client_request_id IS NOT NULL OR client_request_sha256 IS NOT NULL)
                  THEN
                    RAISE EXCEPTION 'Cannot drop retained PBC reply operation receipts'
                      USING ERRCODE = '55000';
                  END IF;
                END;
                $pbc_reply_receipts_downgrade$;
                """);

            migrationBuilder.DropIndex(
                name: "UX_pbc_communications_client_reply_request",
                table: "pbc_communications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_communication_values",
                table: "pbc_communications");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                table: "pbc_communications");

            migrationBuilder.DropColumn(
                name: "client_request_sha256",
                table: "pbc_communications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_communication_values",
                table: "pbc_communications",
                sql: "kind IN ('REQUEST','STAFF_MESSAGE','CLIENT_MESSAGE','CLIENT_ACTION','EMAIL') AND length(trim(body)) > 0 AND (delivery_state IS NULL OR delivery_state IN ('QUEUED','SENT','FAILED','UNCERTAIN')) AND ((kind = 'EMAIL' AND recipient_email IS NOT NULL AND subject IS NOT NULL AND portal_url IS NOT NULL AND delivery_state IS NOT NULL) OR (kind <> 'EMAIL' AND recipient_email IS NULL AND subject IS NULL AND portal_url IS NULL AND delivery_state IS NULL))");
        }
    }
}
