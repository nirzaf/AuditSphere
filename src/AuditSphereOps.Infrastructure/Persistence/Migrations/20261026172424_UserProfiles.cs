using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    mobile_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    joined_date = table.Column<DateOnly>(type: "date", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.user_id);
                    table.CheckConstraint("ck_user_profiles", "revision >= 1 AND (gender IS NULL OR gender IN ('FEMALE','MALE','OTHER','PREFER_NOT_TO_SAY')) AND (position IS NULL OR length(position) >= 1) AND (mobile_number IS NULL OR length(mobile_number) >= 7) AND (joined_date IS NULL OR date_of_birth IS NULL OR joined_date > date_of_birth)");
                    table.ForeignKey(
                        name: "FK_user_profiles_users_firm_id_user_id",
                        columns: x => new { x.firm_id, x.user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_firm_id_user_id",
                table: "user_profiles",
                columns: new[] { "firm_id", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_profiles");
        }
    }
}
