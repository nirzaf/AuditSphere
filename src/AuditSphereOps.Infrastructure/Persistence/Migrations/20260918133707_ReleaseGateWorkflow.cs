using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseGateWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The placeholder release table has no candidate or authorized-key history.
            // Do not invent those identities for an existing release event.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM releases) THEN
                    RAISE EXCEPTION 'Release migration requires an explicit disposition for existing release history.';
                  END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "manifest_digest",
                table: "releases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "authorized_release_key",
                table: "releases",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "release_candidate_id",
                table: "releases",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_releases_firm_id_id",
                table: "releases",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "release_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_revision = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    policy_generation = table.Column<long>(type: "bigint", nullable: false),
                    approval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_candidates", x => x.id);
                    table.UniqueConstraint("AK_release_candidates_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_release_candidate_values", "target_kind = 'WORKPAPER' AND revision >= 1 AND target_revision >= 1 AND input_generation >= 1 AND policy_generation >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND status IN ('READY','ISSUED')");
                    table.ForeignKey(
                        name: "FK_release_candidates_approvals_firm_id_approval_id",
                        columns: x => new { x.firm_id, x.approval_id },
                        principalTable: "approvals",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_candidates_engagements_firm_id_client_id_engagement~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_candidates_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_releases_firm_id_client_id_engagement_id",
                table: "releases",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_releases_firm_id_release_candidate_id",
                table: "releases",
                columns: new[] { "firm_id", "release_candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_releases_firm_id_released_by_user_id",
                table: "releases",
                columns: new[] { "firm_id", "released_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_release_authorized_key",
                table: "releases",
                columns: new[] { "firm_id", "authorized_release_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_values",
                table: "releases",
                sql: "package_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND length(authorized_release_key) > 0 AND external_checkpoint");

            migrationBuilder.CreateIndex(
                name: "IX_release_candidates_firm_id_approval_id",
                table: "release_candidates",
                columns: new[] { "firm_id", "approval_id" });

            migrationBuilder.CreateIndex(
                name: "IX_release_candidates_firm_id_client_id_engagement_id",
                table: "release_candidates",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_release_candidate_identity",
                table: "release_candidates",
                columns: new[] { "firm_id", "target_kind", "target_id", "target_revision", "manifest_digest" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_releases_engagements_firm_id_client_id_engagement_id",
                table: "releases",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_releases_practice_clients_firm_id_client_id",
                table: "releases",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_releases_release_candidates_firm_id_release_candidate_id",
                table: "releases",
                columns: new[] { "firm_id", "release_candidate_id" },
                principalTable: "release_candidates",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_releases_users_firm_id_released_by_user_id",
                table: "releases",
                columns: new[] { "firm_id", "released_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_release_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Release events are immutable historical evidence.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_releases_append_only
                  BEFORE UPDATE OR DELETE ON releases
                  FOR EACH ROW EXECUTE FUNCTION prevent_release_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM releases) OR EXISTS (SELECT 1 FROM release_candidates) THEN
                    RAISE EXCEPTION 'Release downgrade would discard immutable release history.';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_releases_append_only ON releases;
                DROP FUNCTION IF EXISTS prevent_release_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_releases_engagements_firm_id_client_id_engagement_id",
                table: "releases");

            migrationBuilder.DropForeignKey(
                name: "FK_releases_practice_clients_firm_id_client_id",
                table: "releases");

            migrationBuilder.DropForeignKey(
                name: "FK_releases_release_candidates_firm_id_release_candidate_id",
                table: "releases");

            migrationBuilder.DropForeignKey(
                name: "FK_releases_users_firm_id_released_by_user_id",
                table: "releases");

            migrationBuilder.DropTable(
                name: "release_candidates");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_releases_firm_id_id",
                table: "releases");

            migrationBuilder.DropIndex(
                name: "IX_releases_firm_id_client_id_engagement_id",
                table: "releases");

            migrationBuilder.DropIndex(
                name: "IX_releases_firm_id_release_candidate_id",
                table: "releases");

            migrationBuilder.DropIndex(
                name: "IX_releases_firm_id_released_by_user_id",
                table: "releases");

            migrationBuilder.DropIndex(
                name: "ux_release_authorized_key",
                table: "releases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_release_values",
                table: "releases");

            migrationBuilder.DropColumn(
                name: "authorized_release_key",
                table: "releases");

            migrationBuilder.DropColumn(
                name: "release_candidate_id",
                table: "releases");

            migrationBuilder.AlterColumn<string>(
                name: "manifest_digest",
                table: "releases",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
