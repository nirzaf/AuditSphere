using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffCertificationReviewEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_staff_certifications_firm_id_id",
                table: "staff_certifications",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "staff_certification_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_name = table.Column<string>(type: "text", nullable: false),
                    source_issuer = table.Column<string>(type: "text", nullable: true),
                    source_expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    source_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    decision = table.Column<string>(type: "text", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_certification_reviews", x => x.id);
                    table.CheckConstraint("ck_staff_certification_review_values", "revision >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_digest ~ '^[0-9a-f]{64}$' AND source_digest ~ '^[0-9a-f]{64}$' AND decision IN ('VERIFIED','REJECTED','REVOKED') AND length(trim(source_name)) > 0 AND length(trim(reason)) > 0 AND (decision <> 'VERIFIED' OR length(trim(evidence_reference)) > 0) AND reviewed_at >= source_recorded_at AND reviewed_by_user_id <> subject_user_id AND reviewed_by_user_id <> source_recorded_by_user_id");
                    table.ForeignKey(
                        name: "FK_staff_certification_reviews_staff_certifications_firm_id_ce~",
                        columns: x => new { x.firm_id, x.certification_id },
                        principalTable: "staff_certifications",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_certification_reviews_users_firm_id_reviewed_by_user_~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_certification_reviews_users_firm_id_source_recorded_b~",
                        columns: x => new { x.firm_id, x.source_recorded_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_certification_reviews_users_firm_id_subject_user_id",
                        columns: x => new { x.firm_id, x.subject_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_staff_certification_reviews_firm_id_certification_id_revisi~",
                table: "staff_certification_reviews",
                columns: new[] { "firm_id", "certification_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_certification_reviews_firm_id_request_id",
                table: "staff_certification_reviews",
                columns: new[] { "firm_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_certification_reviews_firm_id_reviewed_by_user_id",
                table: "staff_certification_reviews",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_certification_reviews_firm_id_source_recorded_by_user~",
                table: "staff_certification_reviews",
                columns: new[] { "firm_id", "source_recorded_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_certification_reviews_firm_id_subject_user_id",
                table: "staff_certification_reviews",
                columns: new[] { "firm_id", "subject_user_id" });

            migrationBuilder.Sql("""
                CREATE FUNCTION guard_staff_certification_review_insert() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE source staff_certifications%ROWTYPE; latest_revision bigint;
                BEGIN
                  PERFORM 1 FROM firm_safety_states WHERE id = NEW.firm_id FOR UPDATE;
                  IF NOT FOUND THEN RAISE EXCEPTION 'Certification review requires the firm safety fence' USING ERRCODE = '23514'; END IF;
                  SELECT * INTO source FROM staff_certifications WHERE firm_id = NEW.firm_id AND id = NEW.certification_id FOR SHARE;
                  IF NOT FOUND OR NEW.subject_user_id IS DISTINCT FROM source.user_id
                    OR NEW.source_name IS DISTINCT FROM source.name
                    OR NEW.source_issuer IS DISTINCT FROM source.issuer
                    OR NEW.source_expires_on IS DISTINCT FROM source.expires_on
                    OR NEW.source_recorded_at IS DISTINCT FROM source.recorded_at
                    OR NEW.source_recorded_by_user_id IS DISTINCT FROM source.recorded_by_user_id
                  THEN RAISE EXCEPTION 'Certification review source snapshot differs' USING ERRCODE = '23514'; END IF;
                  SELECT COALESCE(MAX(revision), 0) INTO latest_revision FROM staff_certification_reviews
                    WHERE firm_id = NEW.firm_id AND certification_id = NEW.certification_id;
                  IF NEW.revision <> latest_revision + 1 OR (NEW.decision = 'REVOKED' AND latest_revision = 0)
                  THEN RAISE EXCEPTION 'Certification review head changed' USING ERRCODE = '23514'; END IF;
                  IF NOT EXISTS (SELECT 1 FROM users WHERE firm_id = NEW.firm_id AND id = NEW.reviewed_by_user_id AND user_kind = 'Staff' AND NOT disabled)
                    OR (NEW.decision = 'VERIFIED' AND NOT EXISTS (SELECT 1 FROM users WHERE firm_id = NEW.firm_id AND id = NEW.subject_user_id AND user_kind = 'Staff' AND NOT disabled))
                  THEN RAISE EXCEPTION 'Certification review requires current staff identities' USING ERRCODE = '23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER staff_certification_review_source_guard BEFORE INSERT ON staff_certification_reviews
                  FOR EACH ROW EXECUTE FUNCTION guard_staff_certification_review_insert();
                CREATE FUNCTION prevent_staff_certification_review_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Certification reviews are append-only' USING ERRCODE = '23514'; END $$;
                CREATE TRIGGER staff_certification_reviews_append_only BEFORE UPDATE OR DELETE ON staff_certification_reviews
                  FOR EACH ROW EXECUTE FUNCTION prevent_staff_certification_review_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER staff_certification_reviews_append_only ON staff_certification_reviews;
                DROP FUNCTION prevent_staff_certification_review_mutation();
                DROP TRIGGER staff_certification_review_source_guard ON staff_certification_reviews;
                DROP FUNCTION guard_staff_certification_review_insert();
                """);
            migrationBuilder.DropTable(
                name: "staff_certification_reviews");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_staff_certifications_firm_id_id",
                table: "staff_certifications");
        }
    }
}
