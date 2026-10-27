using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpeningBridgePriorCloseReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_opening_bridge_scope_id",
                table: "opening_balance_bridges",
                columns: new[] { "firm_id", "client_id", "id" });

            migrationBuilder.CreateTable(
                name: "opening_balance_bridge_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bridge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_period_revision = table.Column<long>(type: "bigint", nullable: false),
                    evidence_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opening_balance_bridge_reviews", x => x.id);
                    table.CheckConstraint("ck_opening_bridge_review", "prior_period_revision >= 2 AND evidence_hash ~ '^[0-9a-f]{64}$' AND length(trim(reason)) BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_opening_balance_bridge_reviews_client_reporting_periods_fir~",
                        columns: x => new { x.firm_id, x.client_id, x.prior_period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opening_balance_bridge_reviews_opening_balance_bridges_firm~",
                        columns: x => new { x.firm_id, x.client_id, x.bridge_id },
                        principalTable: "opening_balance_bridges",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_opening_balance_bridge_reviews_users_firm_id_reviewer_id",
                        columns: x => new { x.firm_id, x.reviewer_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_opening_balance_bridge_reviews_firm_id_client_id_prior_peri~",
                table: "opening_balance_bridge_reviews",
                columns: new[] { "firm_id", "client_id", "prior_period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_opening_balance_bridge_reviews_firm_id_reviewer_id",
                table: "opening_balance_bridge_reviews",
                columns: new[] { "firm_id", "reviewer_id" });

            migrationBuilder.CreateIndex(
                name: "ux_opening_bridge_review_revision",
                table: "opening_balance_bridge_reviews",
                columns: new[] { "firm_id", "client_id", "bridge_id", "prior_period_revision" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_opening_bridge_review() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP <> 'INSERT' THEN
                    RAISE EXCEPTION 'Opening bridge reviews are immutable.' USING ERRCODE = '55000';
                  END IF;
                  IF NOT EXISTS (
                    SELECT 1 FROM opening_balance_bridges b
                    JOIN client_reporting_periods p ON p.firm_id = b.firm_id AND p.client_id = b.client_id
                      AND p.id = b.prior_period_id
                    WHERE b.firm_id = NEW.firm_id AND b.client_id = NEW.client_id AND b.id = NEW.bridge_id
                      AND p.id = NEW.prior_period_id AND p.revision = NEW.prior_period_revision AND p.status = 'CLOSED'
                  ) THEN
                    RAISE EXCEPTION 'Opening review requires its exact closed prior-period revision.' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER opening_bridge_review_guard BEFORE INSERT OR UPDATE OR DELETE
                ON opening_balance_bridge_reviews FOR EACH ROW EXECUTE FUNCTION guard_opening_bridge_review();
                CREATE FUNCTION protect_approved_opening_bridge() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.status = 'APPROVED' THEN
                    RAISE EXCEPTION 'Approved opening evidence is immutable; retain a new review.' USING ERRCODE = '55000';
                  END IF;
                  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END; $$;
                CREATE TRIGGER approved_opening_bridge_immutable BEFORE UPDATE OR DELETE
                ON opening_balance_bridges FOR EACH ROW EXECUTE FUNCTION protect_approved_opening_bridge();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE opening_balance_bridge_reviews IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM opening_balance_bridge_reviews) THEN
                    RAISE EXCEPTION 'Cannot remove retained opening bridge reviews.' USING ERRCODE = '55000';
                  END IF;
                END; $$;
                DROP TRIGGER opening_bridge_review_guard ON opening_balance_bridge_reviews;
                DROP FUNCTION guard_opening_bridge_review();
                DROP TRIGGER approved_opening_bridge_immutable ON opening_balance_bridges;
                DROP FUNCTION protect_approved_opening_bridge();
                """);
            migrationBuilder.DropTable(
                name: "opening_balance_bridge_reviews");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_opening_bridge_scope_id",
                table: "opening_balance_bridges");
        }
    }
}
