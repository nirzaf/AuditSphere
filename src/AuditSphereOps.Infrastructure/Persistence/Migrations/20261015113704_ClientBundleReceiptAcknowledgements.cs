using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientBundleReceiptAcknowledgements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_commercial_deliverable_bundles_firm_id_client_id_engagement~",
                table: "commercial_deliverable_bundles",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "client_bundle_acknowledgements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bundle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_sha256 = table.Column<string>(type: "text", nullable: false),
                    manifest_sha256 = table.Column<string>(type: "text", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_bundle_acknowledgements", x => x.id);
                    table.CheckConstraint("ck_client_bundle_acknowledgement_hashes", "content_sha256 ~ '^[0-9a-f]{64}$' AND manifest_sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_client_bundle_acknowledgements_commercial_deliverable_bundl~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.bundle_id },
                        principalTable: "commercial_deliverable_bundles",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_bundle_acknowledgements_engagements_firm_id_client_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_bundle_acknowledgements_users_firm_id_acknowledged_b~",
                        columns: x => new { x.firm_id, x.acknowledged_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_bundle_acknowledgements_firm_id_acknowledged_by_user~",
                table: "client_bundle_acknowledgements",
                columns: new[] { "firm_id", "acknowledged_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_bundle_acknowledgements_firm_id_bundle_id_acknowledg~",
                table: "client_bundle_acknowledgements",
                columns: new[] { "firm_id", "bundle_id", "acknowledged_by_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_bundle_acknowledgements_firm_id_client_id_engagement~",
                table: "client_bundle_acknowledgements",
                columns: new[] { "firm_id", "client_id", "engagement_id", "bundle_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_bundle_acknowledgements_firm_id_engagement_id_acknow~",
                table: "client_bundle_acknowledgements",
                columns: new[] { "firm_id", "engagement_id", "acknowledged_at" });

            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_client_bundle_acknowledgement_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Client bundle acknowledgements are append-only.' USING ERRCODE = '55000';
                END; $$;
                CREATE TRIGGER trg_client_bundle_acknowledgements_append_only
                  BEFORE UPDATE OR DELETE ON client_bundle_acknowledgements
                  FOR EACH ROW EXECUTE FUNCTION prevent_client_bundle_acknowledgement_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_client_bundle_acknowledgements_append_only ON client_bundle_acknowledgements; DROP FUNCTION IF EXISTS prevent_client_bundle_acknowledgement_mutation();");
            migrationBuilder.DropTable(
                name: "client_bundle_acknowledgements");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_commercial_deliverable_bundles_firm_id_client_id_engagement~",
                table: "commercial_deliverable_bundles");
        }
    }
}
