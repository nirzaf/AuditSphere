using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmPortalLogoFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "logo_png_bytes",
                table: "firm_commercial_profiles",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_sha256_hex",
                table: "firm_commercial_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_firm_profile_portal_logo",
                table: "firm_commercial_profiles",
                sql: "(logo_png_bytes IS NULL AND logo_sha256_hex IS NULL) OR (logo_png_bytes IS NOT NULL AND logo_sha256_hex IS NOT NULL AND octet_length(logo_png_bytes) BETWEEN 1 AND 131072 AND logo_sha256_hex ~ '^[0-9a-f]{64}$')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$
                BEGIN
                  IF EXISTS (SELECT 1 FROM firm_commercial_profiles
                             WHERE logo_png_bytes IS NOT NULL OR logo_sha256_hex IS NOT NULL) THEN
                    RAISE EXCEPTION 'Retained portal logo evidence prevents downgrade.' USING ERRCODE = 'P0001';
                  END IF;
                END
                $guard$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_firm_profile_portal_logo",
                table: "firm_commercial_profiles");

            migrationBuilder.DropColumn(
                name: "logo_png_bytes",
                table: "firm_commercial_profiles");

            migrationBuilder.DropColumn(
                name: "logo_sha256_hex",
                table: "firm_commercial_profiles");
        }
    }
}
