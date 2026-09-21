using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResolveScheduleControlSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_import_batch_id",
                table: "audit_schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_schedules_firm_id_client_id_engagement_id_source_impo~",
                table: "audit_schedules",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_import_batch_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_audit_schedules_source_import_batches_firm_id_client_id_eng~",
                table: "audit_schedules",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_import_batch_id" },
                principalTable: "source_import_batches",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_schedules_source_import_batches_firm_id_client_id_eng~",
                table: "audit_schedules");

            migrationBuilder.DropIndex(
                name: "IX_audit_schedules_firm_id_client_id_engagement_id_source_impo~",
                table: "audit_schedules");

            migrationBuilder.DropColumn(
                name: "source_import_batch_id",
                table: "audit_schedules");
        }
    }
}
