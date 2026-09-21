using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindMappingsToClientCharts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_chart_version_id",
                table: "mapping_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_mapping_versions_firm_id_client_id_client_chart_version_id",
                table: "mapping_versions",
                columns: new[] { "firm_id", "client_id", "client_chart_version_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_mapping_versions_client_chart_versions_firm_id_client_id_cl~",
                table: "mapping_versions",
                columns: new[] { "firm_id", "client_id", "client_chart_version_id" },
                principalTable: "client_chart_versions",
                principalColumns: new[] { "firm_id", "client_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mapping_versions_client_chart_versions_firm_id_client_id_cl~",
                table: "mapping_versions");

            migrationBuilder.DropIndex(
                name: "IX_mapping_versions_firm_id_client_id_client_chart_version_id",
                table: "mapping_versions");

            migrationBuilder.DropColumn(
                name: "client_chart_version_id",
                table: "mapping_versions");
        }
    }
}
