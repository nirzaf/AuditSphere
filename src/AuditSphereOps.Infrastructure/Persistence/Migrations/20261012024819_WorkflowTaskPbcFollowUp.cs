using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskPbcFollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pbc_request_id",
                table: "workflow_task_bindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_task_bindings_firm_id_client_id_engagement_id_pbc_~",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id" });

            migrationBuilder.CreateIndex(
                name: "UX_workflow_task_bindings_pbc_request",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "pbc_request_id" },
                unique: true,
                filter: "pbc_request_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_task_bindings_pbc_requests_firm_id_client_id_engag~",
                table: "workflow_task_bindings",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id" },
                principalTable: "pbc_requests",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_task_bindings_pbc_requests_firm_id_client_id_engag~",
                table: "workflow_task_bindings");

            migrationBuilder.DropIndex(
                name: "IX_workflow_task_bindings_firm_id_client_id_engagement_id_pbc_~",
                table: "workflow_task_bindings");

            migrationBuilder.DropIndex(
                name: "UX_workflow_task_bindings_pbc_request",
                table: "workflow_task_bindings");

            migrationBuilder.DropColumn(
                name: "pbc_request_id",
                table: "workflow_task_bindings");
        }
    }
}
