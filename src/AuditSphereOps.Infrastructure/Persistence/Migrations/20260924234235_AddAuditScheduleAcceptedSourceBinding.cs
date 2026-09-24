using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditScheduleAcceptedSourceBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "accepted_source_decision_id",
                table: "audit_schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accepted_source_hash",
                table: "audit_schedules",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_audit_schedule_accepted_source",
                table: "audit_schedules",
                columns: new[] { "firm_id", "engagement_id", "accepted_source_decision_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_schedule_accepted_source",
                table: "audit_schedules");

            migrationBuilder.DropColumn(
                name: "accepted_source_decision_id",
                table: "audit_schedules");

            migrationBuilder.DropColumn(
                name: "accepted_source_hash",
                table: "audit_schedules");
        }
    }
}
