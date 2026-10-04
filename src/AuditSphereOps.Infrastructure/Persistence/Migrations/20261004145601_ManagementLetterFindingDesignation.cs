using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManagementLetterFindingDesignation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "letter_designated_at",
                table: "findings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "letter_designated_by_user_id",
                table: "findings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "letter_recommendation",
                table: "findings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "letter_designated_at",
                table: "findings");

            migrationBuilder.DropColumn(
                name: "letter_designated_by_user_id",
                table: "findings");

            migrationBuilder.DropColumn(
                name: "letter_recommendation",
                table: "findings");
        }
    }
}
