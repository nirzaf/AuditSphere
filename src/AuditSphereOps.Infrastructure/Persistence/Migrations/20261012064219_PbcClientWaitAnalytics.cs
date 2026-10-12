using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcClientWaitAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_communication_values",
                table: "pbc_communications");

            migrationBuilder.CreateIndex(
                name: "IX_pbc_communications_sent_delivery",
                table: "pbc_communications",
                columns: new[] { "firm_id", "delivered_at", "pbc_request_id" },
                filter: "kind = 'EMAIL' AND delivery_state = 'SENT' AND delivered_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_communication_values",
                table: "pbc_communications",
                sql: "kind IN ('REQUEST','STAFF_MESSAGE','CLIENT_MESSAGE','CLIENT_ACTION','EMAIL') AND length(trim(body)) > 0 AND (delivery_state IS NULL OR delivery_state IN ('QUEUED','SENT','FAILED','UNCERTAIN')) AND ((kind = 'EMAIL' AND recipient_email IS NOT NULL AND subject IS NOT NULL AND portal_url IS NOT NULL AND delivery_state IS NOT NULL) OR (kind <> 'EMAIL' AND recipient_email IS NULL AND subject IS NULL AND portal_url IS NULL AND delivery_state IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pbc_communications_sent_delivery",
                table: "pbc_communications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pbc_communication_values",
                table: "pbc_communications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbc_communication_values",
                table: "pbc_communications",
                sql: "kind IN ('REQUEST','STAFF_MESSAGE','CLIENT_MESSAGE','EMAIL') AND length(trim(body)) > 0 AND (delivery_state IS NULL OR delivery_state IN ('QUEUED','SENT','FAILED','UNCERTAIN')) AND ((kind = 'EMAIL' AND recipient_email IS NOT NULL AND subject IS NOT NULL AND portal_url IS NOT NULL AND delivery_state IS NOT NULL) OR (kind <> 'EMAIL' AND recipient_email IS NULL AND subject IS NULL AND portal_url IS NULL AND delivery_state IS NULL))");
        }
    }
}
