using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseEvidenceAndCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM releases) THEN
                    RAISE EXCEPTION 'Preflight failed: existing releases rows cannot be backfilled with checkpoint_id automatically.'
                      USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_release_values",
                table: "releases");


            migrationBuilder.DropColumn(
                name: "external_checkpoint",
                table: "releases");

            migrationBuilder.AddColumn<Guid>(
                name: "checkpoint_id",
                table: "releases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "protection_attestations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    binding = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    profile_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    profile_version = table.Column<long>(type: "bigint", nullable: false),
                    observed_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    verification_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expiry_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recheck_rule = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_protection_attestations", x => x.id);
                    table.UniqueConstraint("AK_protection_attestations_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_protection_attestations_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_protection_attestation_values", "artifact_hash ~ '^[0-9a-f]{64}$' AND profile_version >= 1 AND length(trim(binding)) > 0 AND length(trim(profile_id)) > 0 AND observed_state IN ('PROTECTED','PENDING','EXPIRED','RECHECK_REQUIRED')");
                    table.ForeignKey(
                        name: "FK_protection_attestations_engagements_firm_id_client_id_engag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_checkpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_revision = table.Column<long>(type: "bigint", nullable: false),
                    authorized_release_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    manifest_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stored_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    read_back_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    verified_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_checkpoints", x => x.id);
                    table.UniqueConstraint("AK_release_checkpoints_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_release_checkpoints_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_release_checkpoint_values", "candidate_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND read_back_digest ~ '^[0-9a-f]{64}$' AND length(trim(authorized_release_key)) > 0 AND length(trim(stored_reference)) > 0 AND verified_status IN ('VERIFIED','PENDING','MISMATCHED','EXPIRED')");
                    table.ForeignKey(
                        name: "FK_release_checkpoints_engagements_firm_id_client_id_engagemen~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_checkpoints_release_candidates_firm_id_release_cand~",
                        columns: x => new { x.firm_id, x.release_candidate_id },
                        principalTable: "release_candidates",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "signature_lineages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pre_sign_artifact_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    signed_artifact_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    signing_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_identity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    verification_outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    verifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signature_lineages", x => x.id);
                    table.UniqueConstraint("AK_signature_lineages_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_signature_lineages_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_signature_lineage_values", "pre_sign_artifact_hash ~ '^[0-9a-f]{64}$' AND signed_artifact_hash ~ '^[0-9a-f]{64}$' AND length(trim(signing_method)) > 0 AND length(trim(request_identity)) > 0 AND verification_outcome IN ('VERIFIED','INVALID','REJECTED')");
                    table.ForeignKey(
                        name: "FK_signature_lineages_engagements_firm_id_client_id_engagement~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_signature_lineages_release_candidates_firm_id_candidate_id",
                        columns: x => new { x.firm_id, x.candidate_id },
                        principalTable: "release_candidates",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_releases_firm_id_checkpoint_id",
                table: "releases",
                columns: new[] { "firm_id", "checkpoint_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_values",
                table: "releases",
                sql: "package_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND length(authorized_release_key) > 0");

            migrationBuilder.CreateIndex(
                name: "ix_protection_attestations_artifact",
                table: "protection_attestations",
                columns: new[] { "firm_id", "engagement_id", "artifact_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_release_checkpoint_candidate_manifest",
                table: "release_checkpoints",
                columns: new[] { "firm_id", "release_candidate_id", "candidate_revision", "manifest_digest" });

            migrationBuilder.CreateIndex(
                name: "ix_signature_lineages_candidate",
                table: "signature_lineages",
                columns: new[] { "firm_id", "candidate_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_releases_release_checkpoints_firm_id_checkpoint_id",
                table: "releases",
                columns: new[] { "firm_id", "checkpoint_id" },
                principalTable: "release_checkpoints",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_release_checkpoint_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Release checkpoints are immutable release evidence.'
                    USING ERRCODE = '55000';
                END $$;

                CREATE OR REPLACE FUNCTION prevent_protection_attestation_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Protection attestations are immutable compliance evidence.'
                    USING ERRCODE = '55000';
                END $$;

                CREATE OR REPLACE FUNCTION prevent_signature_lineage_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Signature lineages are immutable signing evidence.'
                    USING ERRCODE = '55000';
                END $$;

                CREATE TRIGGER trg_release_checkpoints_append_only
                  BEFORE UPDATE OR DELETE ON release_checkpoints
                  FOR EACH ROW EXECUTE FUNCTION prevent_release_checkpoint_mutation();

                CREATE TRIGGER trg_protection_attestations_append_only
                  BEFORE UPDATE OR DELETE ON protection_attestations
                  FOR EACH ROW EXECUTE FUNCTION prevent_protection_attestation_mutation();

                CREATE TRIGGER trg_signature_lineages_append_only
                  BEFORE UPDATE OR DELETE ON signature_lineages
                  FOR EACH ROW EXECUTE FUNCTION prevent_signature_lineage_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM release_checkpoints) OR
                     EXISTS (SELECT 1 FROM protection_attestations) OR
                     EXISTS (SELECT 1 FROM signature_lineages) OR
                     EXISTS (SELECT 1 FROM releases) THEN
                    RAISE EXCEPTION 'Evidence loss refusal: cannot drop release evidence tables while rows exist.'
                      USING ERRCODE = '55000';
                  END IF;
                END $$;

                DROP TRIGGER IF EXISTS trg_release_checkpoints_append_only ON release_checkpoints;
                DROP TRIGGER IF EXISTS trg_protection_attestations_append_only ON protection_attestations;
                DROP TRIGGER IF EXISTS trg_signature_lineages_append_only ON signature_lineages;
                DROP FUNCTION IF EXISTS prevent_release_checkpoint_mutation();
                DROP FUNCTION IF EXISTS prevent_protection_attestation_mutation();
                DROP FUNCTION IF EXISTS prevent_signature_lineage_mutation();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_releases_release_checkpoints_firm_id_checkpoint_id",
                table: "releases");


            migrationBuilder.DropTable(
                name: "protection_attestations");

            migrationBuilder.DropTable(
                name: "release_checkpoints");

            migrationBuilder.DropTable(
                name: "signature_lineages");

            migrationBuilder.DropIndex(
                name: "IX_releases_firm_id_checkpoint_id",
                table: "releases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_release_values",
                table: "releases");

            migrationBuilder.DropColumn(
                name: "checkpoint_id",
                table: "releases");

            migrationBuilder.AddColumn<bool>(
                name: "external_checkpoint",
                table: "releases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_release_values",
                table: "releases",
                sql: "package_revision >= 1 AND manifest_digest ~ '^[0-9a-f]{64}$' AND length(authorized_release_key) > 0 AND external_checkpoint");
        }
    }
}
