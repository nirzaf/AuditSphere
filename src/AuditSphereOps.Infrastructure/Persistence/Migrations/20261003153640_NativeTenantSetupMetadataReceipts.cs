using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeTenantSetupMetadataReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_m365_administration_events_firm_id_actor_user_id",
                table: "m365_administration_events");

            migrationBuilder.AddColumn<Guid>(
                name: "setup_draft_id",
                table: "m365_administration_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "setup_request_hash",
                table: "m365_administration_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "setup_request_id",
                table: "m365_administration_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "setup_revision_after",
                table: "m365_administration_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_m365_administration_events_firm_id_actor_user_id_setup_requ~",
                table: "m365_administration_events",
                columns: new[] { "firm_id", "actor_user_id", "setup_request_id" },
                unique: true,
                filter: "setup_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_m365_administration_events_firm_id_setup_draft_id",
                table: "m365_administration_events",
                columns: new[] { "firm_id", "setup_draft_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_m365_setup_request_receipt",
                table: "m365_administration_events",
                sql: "(operation <> 'TENANT_SETUP_METADATA_SAVED' AND setup_request_id IS NULL AND setup_request_hash IS NULL AND setup_draft_id IS NULL AND setup_revision_after IS NULL) OR (operation = 'TENANT_SETUP_METADATA_SAVED' AND setup_request_id IS NOT NULL AND setup_request_hash IS NOT NULL AND setup_request_hash ~ '^[a-f0-9]{64}$' AND setup_draft_id IS NOT NULL AND setup_revision_after IS NOT NULL AND setup_revision_after > 1)");

            migrationBuilder.AddForeignKey(
                name: "FK_m365_administration_events_m365_setup_drafts_firm_id_setup_~",
                table: "m365_administration_events",
                columns: new[] { "firm_id", "setup_draft_id" },
                principalTable: "m365_setup_drafts",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM m365_administration_events WHERE setup_request_id IS NOT NULL) THEN
                    RAISE EXCEPTION 'Retained tenant setup receipts prohibit destructive rollback';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_m365_administration_events_m365_setup_drafts_firm_id_setup_~",
                table: "m365_administration_events");

            migrationBuilder.DropIndex(
                name: "IX_m365_administration_events_firm_id_actor_user_id_setup_requ~",
                table: "m365_administration_events");

            migrationBuilder.DropIndex(
                name: "IX_m365_administration_events_firm_id_setup_draft_id",
                table: "m365_administration_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_m365_setup_request_receipt",
                table: "m365_administration_events");

            migrationBuilder.DropColumn(
                name: "setup_draft_id",
                table: "m365_administration_events");

            migrationBuilder.DropColumn(
                name: "setup_request_hash",
                table: "m365_administration_events");

            migrationBuilder.DropColumn(
                name: "setup_request_id",
                table: "m365_administration_events");

            migrationBuilder.DropColumn(
                name: "setup_revision_after",
                table: "m365_administration_events");

            migrationBuilder.CreateIndex(
                name: "IX_m365_administration_events_firm_id_actor_user_id",
                table: "m365_administration_events",
                columns: new[] { "firm_id", "actor_user_id" });
        }
    }
}
