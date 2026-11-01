using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcRequestDelegationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Retain the global-ID EF relationship and add the exact request-scope reference.
            migrationBuilder.AddForeignKey(
                name: "fk_pbc_request_delegations_request_scope",
                table: "pbc_request_delegations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id" },
                principalTable: "pbc_requests",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pbc_request_delegations_request_scope",
                table: "pbc_request_delegations");
        }
    }
}
