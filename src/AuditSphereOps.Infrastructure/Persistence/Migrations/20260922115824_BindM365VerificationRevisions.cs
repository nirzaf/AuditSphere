using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindM365VerificationRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "connection_revision_id",
                table: "m365_verification_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "connection_revision_id",
                table: "m365_setup_drafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_m365_verification_evidence_firm_id_connection_revision_id",
                table: "m365_verification_evidence",
                columns: new[] { "firm_id", "connection_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_m365_setup_drafts_firm_id_connection_revision_id",
                table: "m365_setup_drafts",
                columns: new[] { "firm_id", "connection_revision_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_m365_setup_drafts_m365_connection_revisions_firm_id_connect~",
                table: "m365_setup_drafts",
                columns: new[] { "firm_id", "connection_revision_id" },
                principalTable: "m365_connection_revisions",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_m365_verification_evidence_m365_connection_revisions_firm_i~",
                table: "m365_verification_evidence",
                columns: new[] { "firm_id", "connection_revision_id" },
                principalTable: "m365_connection_revisions",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_m365_setup_drafts_m365_connection_revisions_firm_id_connect~",
                table: "m365_setup_drafts");

            migrationBuilder.DropForeignKey(
                name: "FK_m365_verification_evidence_m365_connection_revisions_firm_i~",
                table: "m365_verification_evidence");

            migrationBuilder.DropIndex(
                name: "IX_m365_verification_evidence_firm_id_connection_revision_id",
                table: "m365_verification_evidence");

            migrationBuilder.DropIndex(
                name: "IX_m365_setup_drafts_firm_id_connection_revision_id",
                table: "m365_setup_drafts");

            migrationBuilder.DropColumn(
                name: "connection_revision_id",
                table: "m365_verification_evidence");

            migrationBuilder.DropColumn(
                name: "connection_revision_id",
                table: "m365_setup_drafts");
        }
    }
}
