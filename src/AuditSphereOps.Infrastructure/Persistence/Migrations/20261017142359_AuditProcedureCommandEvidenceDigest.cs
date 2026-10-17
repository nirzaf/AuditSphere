using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditProcedureCommandEvidenceDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "evidence_digest",
                table: "audit_procedure_command_receipts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "evidence_digest",
                table: "audit_procedure_command_receipts");
        }
    }
}
