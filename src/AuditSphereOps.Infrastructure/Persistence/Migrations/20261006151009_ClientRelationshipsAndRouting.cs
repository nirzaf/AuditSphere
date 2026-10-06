using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientRelationshipsAndRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "entity_type",
                table: "practice_clients",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_registration_number",
                table: "practice_clients",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "client_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "client_contacts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signatory_authority",
                table: "client_contacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "client_contacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "client_contact_routings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practice_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_primary_for_purpose = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_contact_routings", x => x.id);
                    table.CheckConstraint("ck_client_contact_routing_effective", "effective_to IS NULL OR effective_from IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_client_contact_routing_purpose", "purpose IN ('COMMERCIAL','FINANCE','AUDIT_FIELDWORK','COMPLETION')");
                });

            migrationBuilder.CreateTable(
                name: "client_relationships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    related_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_kind = table.Column<string>(type: "text", nullable: false),
                    ownership_percentage = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_relationships", x => x.id);
                    table.CheckConstraint("ck_client_relationship_effective", "effective_to IS NULL OR effective_from IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_client_relationship_kind", "relationship_kind IN ('PARENT','SUBSIDIARY','AFFILIATE')");
                    table.CheckConstraint("ck_client_relationship_ownership", "ownership_percentage IS NULL OR (ownership_percentage >= 0 AND ownership_percentage <= 100)");
                    table.CheckConstraint("ck_client_relationship_parties", "primary_client_id <> related_client_id");
                });

            migrationBuilder.CreateTable(
                name: "correspondence_dispatch_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practice_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    recipient_contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_name = table.Column<string>(type: "text", nullable: false),
                    recipient_email = table.Column<string>(type: "text", nullable: false),
                    document_type = table.Column<string>(type: "text", nullable: false),
                    document_reference = table.Column<string>(type: "text", nullable: false),
                    document_revision = table.Column<long>(type: "bigint", nullable: false),
                    document_sha256 = table.Column<string>(type: "text", nullable: false),
                    was_overridden = table.Column<bool>(type: "boolean", nullable: false),
                    overridden_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    override_reason = table.Column<string>(type: "text", nullable: true),
                    dispatched_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_correspondence_dispatch_records", x => x.id);
                    table.CheckConstraint("ck_correspondence_dispatch_content", "length(recipient_name) > 0 AND length(recipient_email) > 0 AND length(document_type) > 0 AND length(document_reference) > 0 AND length(document_sha256) = 64 AND document_revision >= 1");
                    table.CheckConstraint("ck_correspondence_dispatch_purpose", "purpose IN ('COMMERCIAL','FINANCE','AUDIT_FIELDWORK','COMPLETION')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_contact_routings_firm_id_practice_client_id_purpose",
                table: "client_contact_routings",
                columns: new[] { "firm_id", "practice_client_id", "purpose" },
                filter: "revoked_at IS NULL AND is_primary_for_purpose = true");

            migrationBuilder.CreateIndex(
                name: "IX_client_contact_routings_firm_id_practice_client_id_purpose_~",
                table: "client_contact_routings",
                columns: new[] { "firm_id", "practice_client_id", "purpose", "client_contact_id" },
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_client_relationships_firm_id_primary_client_id_related_clie~",
                table: "client_relationships",
                columns: new[] { "firm_id", "primary_client_id", "related_client_id", "relationship_kind" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_client_relationships_firm_id_related_client_id",
                table: "client_relationships",
                columns: new[] { "firm_id", "related_client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_correspondence_dispatch_records_firm_id_engagement_id",
                table: "correspondence_dispatch_records",
                columns: new[] { "firm_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_correspondence_dispatch_records_firm_id_practice_client_id_~",
                table: "correspondence_dispatch_records",
                columns: new[] { "firm_id", "practice_client_id", "dispatched_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_contact_routings");

            migrationBuilder.DropTable(
                name: "client_relationships");

            migrationBuilder.DropTable(
                name: "correspondence_dispatch_records");

            migrationBuilder.DropColumn(
                name: "entity_type",
                table: "practice_clients");

            migrationBuilder.DropColumn(
                name: "tax_registration_number",
                table: "practice_clients");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "client_contacts");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "client_contacts");

            migrationBuilder.DropColumn(
                name: "signatory_authority",
                table: "client_contacts");

            migrationBuilder.DropColumn(
                name: "title",
                table: "client_contacts");
        }
    }
}
