using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalPreparedByAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "prepared_by_user_id",
                table: "proposals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposals_firm_id_prepared_by_user_id",
                table: "proposals",
                columns: new[] { "firm_id", "prepared_by_user_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_proposals_users_firm_id_prepared_by_user_id",
                table: "proposals",
                columns: new[] { "firm_id", "prepared_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_proposals_users_firm_id_prepared_by_user_id",
                table: "proposals");

            migrationBuilder.DropIndex(
                name: "IX_proposals_firm_id_prepared_by_user_id",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "prepared_by_user_id",
                table: "proposals");
        }
    }
}
