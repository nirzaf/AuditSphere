using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientAccountingProfileSourceMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_mode",
                table: "client_accounting_profiles",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "EXTERNAL_SOURCE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_mode",
                table: "client_accounting_profiles");
        }
    }
}
