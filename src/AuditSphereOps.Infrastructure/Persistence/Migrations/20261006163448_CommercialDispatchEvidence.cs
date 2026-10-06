using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialDispatchEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications");

            migrationBuilder.AddColumn<string>(
                name: "respondent_email",
                table: "proposals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "respondent_name",
                table: "proposals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "response_evidence_reference",
                table: "proposals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "response_offer_sha256",
                table: "proposals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sent_offer_sha256",
                table: "proposals",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "fee_milestone_id",
                table: "commercial_notifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "document_id",
                table: "commercial_notifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "commercial_notifications",
                type: "text",
                nullable: false,
                defaultValue: "RECEIPT");

            migrationBuilder.AddColumn<string>(
                name: "offer_sha256",
                table: "commercial_notifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "practice_client_id",
                table: "commercial_notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "proposal_id",
                table: "commercial_notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_commercial_notifications_firm_id_proposal_id",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "proposal_id" },
                unique: true,
                filter: "proposal_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications",
                sql: "delivery_state IN ('QUEUED','SENT','FAILED') AND kind IN ('RECEIPT','PROPOSAL') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state = 'SENT') = (delivered_at IS NOT NULL)) AND ((kind = 'RECEIPT') = (fee_milestone_id IS NOT NULL)) AND ((kind = 'PROPOSAL') = (proposal_id IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_commercial_notifications_firm_id_proposal_id",
                table: "commercial_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications");

            migrationBuilder.DropColumn(
                name: "respondent_email",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "respondent_name",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "response_evidence_reference",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "response_offer_sha256",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "sent_offer_sha256",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "commercial_notifications");

            migrationBuilder.DropColumn(
                name: "offer_sha256",
                table: "commercial_notifications");

            migrationBuilder.DropColumn(
                name: "practice_client_id",
                table: "commercial_notifications");

            migrationBuilder.DropColumn(
                name: "proposal_id",
                table: "commercial_notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "fee_milestone_id",
                table: "commercial_notifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "document_id",
                table: "commercial_notifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications",
                sql: "delivery_state IN ('QUEUED','SENT','FAILED') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state = 'SENT') = (delivered_at IS NOT NULL))");
        }
    }
}
