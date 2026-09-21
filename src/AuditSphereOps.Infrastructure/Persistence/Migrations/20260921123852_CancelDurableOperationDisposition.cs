using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CancelDurableOperationDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_disposition",
                table: "durable_operations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_cancellation",
                table: "durable_operations",
                sql: "status <> 'CANCELLED_WITH_DISPOSITION' OR length(trim(cancellation_disposition)) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_cancellation",
                table: "durable_operations");

            migrationBuilder.DropColumn(
                name: "cancellation_disposition",
                table: "durable_operations");
        }
    }
}
