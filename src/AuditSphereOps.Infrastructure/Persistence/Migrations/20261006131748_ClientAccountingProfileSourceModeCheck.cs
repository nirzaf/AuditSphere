using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientAccountingProfileSourceModeCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_client_accounting_profile_source_mode",
                table: "client_accounting_profiles",
                sql: "source_mode IN ('EXTERNAL_SOURCE', 'NATIVE_BOOKKEEPING')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_client_accounting_profile_source_mode",
                table: "client_accounting_profiles");
        }
    }
}
