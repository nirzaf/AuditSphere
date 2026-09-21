using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalComponentPackCompatibilityBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_external_component_pack_values",
                table: "external_component_packs");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "compatibility_bridge_approved_at",
                table: "external_component_packs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "compatibility_bridge_approved_by_user_id",
                table: "external_component_packs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "compatibility_bridge_reference",
                table: "external_component_packs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "compatibility_bridge_status",
                table: "external_component_packs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE external_component_packs SET compatibility_bridge_status = 'NONE' WHERE compatibility_bridge_status = '';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_external_component_pack_values",
                table: "external_component_packs",
                sql: "period_start ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_end ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_start <= period_end AND framework <> '' AND reporting_currency ~ '^[A-Z]{3}$' AND length(trim(period_basis)) > 0 AND length(trim(taxonomy_version)) > 0 AND length(trim(mapping_version)) > 0 AND length(trim(source_reference)) > 0 AND raw_source_hash ~ '^[0-9a-f]{64}$' AND normalized_source_digest ~ '^[0-9a-f]{64}$' AND pack_digest ~ '^[0-9a-f]{64}$' AND status IN ('SUBMITTED','RESUBMITTED','RETURNED','APPROVED') AND reconciliation_status IN ('PENDING','RECONCILED') AND compatibility_bridge_status IN ('NONE','PENDING','APPROVED') AND (compatibility_bridge_status = 'NONE' OR length(trim(compatibility_bridge_reference)) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_external_component_pack_values",
                table: "external_component_packs");

            migrationBuilder.DropColumn(
                name: "compatibility_bridge_approved_at",
                table: "external_component_packs");

            migrationBuilder.DropColumn(
                name: "compatibility_bridge_approved_by_user_id",
                table: "external_component_packs");

            migrationBuilder.DropColumn(
                name: "compatibility_bridge_reference",
                table: "external_component_packs");

            migrationBuilder.DropColumn(
                name: "compatibility_bridge_status",
                table: "external_component_packs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_external_component_pack_values",
                table: "external_component_packs",
                sql: "period_start ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_end ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' AND period_start <= period_end AND framework <> '' AND reporting_currency ~ '^[A-Z]{3}$' AND length(trim(period_basis)) > 0 AND length(trim(taxonomy_version)) > 0 AND length(trim(mapping_version)) > 0 AND length(trim(source_reference)) > 0 AND raw_source_hash ~ '^[0-9a-f]{64}$' AND normalized_source_digest ~ '^[0-9a-f]{64}$' AND pack_digest ~ '^[0-9a-f]{64}$' AND status IN ('SUBMITTED','RESUBMITTED','RETURNED','APPROVED') AND reconciliation_status IN ('PENDING','RECONCILED')");
        }
    }
}
