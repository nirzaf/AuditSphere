using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveCopyOperationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_durable_operation_scope_id",
                table: "durable_operations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                unique: true);

            // Keep nullable general-operation scopes. PostgreSQL can reference this ordinary
            // unique index without turning its optional columns into an EF principal key.
            migrationBuilder.AddForeignKey(
                name: "fk_archive_copy_file_operation_scope",
                table: "archive_copy_file_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "operation_id" },
                principalTable: "durable_operations",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_archive_copy_permission_operation_scope",
                table: "archive_copy_permission_observations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "operation_id" },
                principalTable: "durable_operations",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_archive_copy_file_operation_scope",
                table: "archive_copy_file_receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_archive_copy_permission_operation_scope",
                table: "archive_copy_permission_observations");

            migrationBuilder.DropIndex(
                name: "ux_durable_operation_scope_id",
                table: "durable_operations");

        }
    }
}
