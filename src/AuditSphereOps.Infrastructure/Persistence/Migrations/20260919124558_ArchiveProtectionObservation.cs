using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveProtectionObservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_values",
                table: "archives");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "observed_protection_at",
                table: "archives",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observed_protection_state",
                table: "archives",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_values",
                table: "archives",
                sql: "length(trim(profile_id)) > 0 AND profile_version >= 1 AND status IN ('ISSUED','ASSEMBLY_IN_PROGRESS','MANIFEST_BUILT','ASSEMBLY_REVIEWED','RECORDS_ACTION_REQUESTED','PROTECTION_OBSERVED','ARCHIVE_VERIFIED') AND ((status IN ('PROTECTION_OBSERVED','ARCHIVE_VERIFIED') AND length(trim(observed_protection_state)) > 0 AND observed_protection_at IS NOT NULL) OR status IN ('ISSUED','ASSEMBLY_IN_PROGRESS','MANIFEST_BUILT','ASSEMBLY_REVIEWED','RECORDS_ACTION_REQUESTED'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM archives
                        WHERE observed_protection_state IS NOT NULL
                           OR observed_protection_at IS NOT NULL
                           OR status IN ('PROTECTION_OBSERVED','ARCHIVE_VERIFIED')
                    ) THEN
                        RAISE EXCEPTION 'Archive protection downgrade would discard observed protection evidence.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_values",
                table: "archives");

            migrationBuilder.DropColumn(
                name: "observed_protection_at",
                table: "archives");

            migrationBuilder.DropColumn(
                name: "observed_protection_state",
                table: "archives");

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_values",
                table: "archives",
                sql: "length(trim(profile_id)) > 0 AND profile_version >= 1 AND status IN ('ISSUED','ASSEMBLY_IN_PROGRESS','MANIFEST_BUILT','ASSEMBLY_REVIEWED','RECORDS_ACTION_REQUESTED','PROTECTION_OBSERVED','ARCHIVE_VERIFIED')");
        }
    }
}
