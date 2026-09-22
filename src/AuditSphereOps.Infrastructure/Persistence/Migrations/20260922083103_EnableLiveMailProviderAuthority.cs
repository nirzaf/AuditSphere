using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableLiveMailProviderAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_mode",
                table: "durable_operations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_mode",
                table: "durable_operations",
                sql: "execution_mode IN ('LOCAL','SIMULATED','LIVE') AND authority_mode IN ('LOCAL_VALIDATION','SIMULATION','LIVE_PROVIDER')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_mode",
                table: "durable_operations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_mode",
                table: "durable_operations",
                sql: "execution_mode IN ('LOCAL','SIMULATED','LIVE') AND authority_mode IN ('LOCAL_VALIDATION','SIMULATION')");
        }
    }
}
