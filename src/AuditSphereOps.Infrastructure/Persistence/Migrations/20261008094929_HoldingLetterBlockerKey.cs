using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HoldingLetterBlockerKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_commercial_notifications_firm_id_deliverable_id",
                table: "commercial_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications");

            migrationBuilder.AddColumn<string>(
                name: "dispatch_key",
                table: "commercial_notifications",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_commercial_notifications_firm_id_dispatch_key",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "dispatch_key" },
                unique: true,
                filter: "dispatch_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications",
                sql: "delivery_state IN ('QUEUED','SENT','FAILED') AND kind IN ('RECEIPT','PROPOSAL','HOLDING_LETTER') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state = 'SENT') = (delivered_at IS NOT NULL)) AND ((kind = 'RECEIPT') = (fee_milestone_id IS NOT NULL)) AND ((kind = 'PROPOSAL') = (proposal_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (deliverable_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (dispatch_key IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_commercial_notifications_firm_id_dispatch_key",
                table: "commercial_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications");

            migrationBuilder.DropColumn(
                name: "dispatch_key",
                table: "commercial_notifications");

            migrationBuilder.CreateIndex(
                name: "IX_commercial_notifications_firm_id_deliverable_id",
                table: "commercial_notifications",
                columns: new[] { "firm_id", "deliverable_id" },
                unique: true,
                filter: "deliverable_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_commercial_notification_values",
                table: "commercial_notifications",
                sql: "delivery_state IN ('QUEUED','SENT','FAILED') AND kind IN ('RECEIPT','PROPOSAL','HOLDING_LETTER') AND length(recipient) > 0 AND length(subject) > 0 AND ((delivery_state = 'SENT') = (delivered_at IS NOT NULL)) AND ((kind = 'RECEIPT') = (fee_milestone_id IS NOT NULL)) AND ((kind = 'PROPOSAL') = (proposal_id IS NOT NULL)) AND ((kind = 'HOLDING_LETTER') = (deliverable_id IS NOT NULL))");
        }
    }
}
