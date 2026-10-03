using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeBudgetPreparationReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_preparations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_preparations", x => x.id);
                    table.CheckConstraint("ck_budget_preparation", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(preview_json) BETWEEN 1 AND 200000");
                    table.ForeignKey(
                        name: "FK_budget_preparations_engagement_budgets_budget_id",
                        column: x => x.budget_id,
                        principalTable: "engagement_budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_budget_preparations_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_budget_preparations_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_budget_preparations_budget_id",
                table: "budget_preparations",
                column: "budget_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_preparations_engagement_id",
                table: "budget_preparations",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "IX_budget_preparations_firm_id_actor_id_request_id",
                table: "budget_preparations",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE TRIGGER budget_preparation_immutable BEFORE UPDATE OR DELETE ON budget_preparations
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_budget_preparation() RETURNS trigger AS $$
                DECLARE p jsonb := NEW.preview_json::jsonb;
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM engagement_budgets b JOIN engagements e ON e.id=b.engagement_id
                    JOIN users u ON u.id=NEW.actor_id AND u.firm_id=NEW.firm_id
                    WHERE b.id=NEW.budget_id AND b.firm_id=NEW.firm_id AND e.firm_id=NEW.firm_id
                      AND e.id=NEW.engagement_id AND b.created_by_user_id=NEW.actor_id
                      AND b.created_at=NEW.created_at AND b.status='DRAFT'
                      AND b.currency=p->'Fields'->>'Currency'
                      AND b.version=(p->'Fields'->>'ExpectedVersion')::bigint+1
                      AND p->>'RequestHash'=NEW.request_hash AND p->>'ReviewBasis'=NEW.review_basis
                      AND (p->>'RequestId')::uuid=NEW.request_id AND (p->>'EngagementId')::uuid=NEW.engagement_id
                      AND u.session_epoch=NEW.actor_epoch AND NOT u.disabled AND lower(u.user_kind)<>'client'
                      AND EXISTS(SELECT 1 FROM role_grants r WHERE r.firm_id=NEW.firm_id AND r.user_id=NEW.actor_id
                        AND lower(r.role) IN ('partner','manager') AND r.revoked_at IS NULL
                        AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp())
                        AND ((r.engagement_id=e.id AND (r.client_id IS NULL OR r.client_id=e.practice_client_id))
                          OR (r.engagement_id IS NULL AND (r.client_id IS NULL OR r.client_id=e.practice_client_id))))
                  ) OR jsonb_array_length(p->'Fields'->'Lines')<>jsonb_array_length(p->'Lines')
                  OR EXISTS(SELECT 1 FROM jsonb_array_elements(p->'Fields'->'Lines') WITH ORDINALITY f(j,n) WHERE
                    f.j->>'Role' IS DISTINCT FROM p->'Lines'->((f.n-1)::integer)->>'Role' OR
                    f.j->>'Activity' IS DISTINCT FROM p->'Lines'->((f.n-1)::integer)->>'Activity' OR
                    coalesce(f.j->>'Phase','UNASSIGNED') IS DISTINCT FROM p->'Lines'->((f.n-1)::integer)->>'Phase' OR
                    f.j->>'RiskArea' IS DISTINCT FROM p->'Lines'->((f.n-1)::integer)->>'RiskArea' OR
                    f.j->>'ForecastMinutes' IS DISTINCT FROM p->'Lines'->((f.n-1)::integer)->>'ForecastMinutes')
                  OR (p->>'ForecastCost')::numeric<>(SELECT sum(forecast_cost) FROM budget_lines WHERE engagement_budget_id=NEW.budget_id)
                  OR jsonb_array_length(p->'Lines')<>(SELECT count(*) FROM budget_lines WHERE engagement_budget_id=NEW.budget_id)
                  OR EXISTS(SELECT 1 FROM jsonb_array_elements(p->'Lines') j WHERE NOT EXISTS(
                    SELECT 1 FROM budget_lines l JOIN rate_card_versions c ON c.id=l.rate_card_version_id
                    WHERE l.engagement_budget_id=NEW.budget_id AND l.firm_id=NEW.firm_id
                      AND c.firm_id=NEW.firm_id AND c.status='APPROVED' AND c.currency=p->'Fields'->>'Currency'
                      AND c.role=l.role AND c.activity=l.activity AND c.rate_per_hour=l.rate_per_hour
                      AND l.role=j->>'Role' AND l.activity=j->>'Activity' AND l.phase=j->>'Phase'
                      AND l.risk_area IS NOT DISTINCT FROM j->>'RiskArea'
                      AND l.forecast_minutes=(j->>'ForecastMinutes')::integer AND c.id=(j->>'RateCardId')::uuid
                      AND l.rate_per_hour=(j->>'RatePerHour')::numeric AND l.forecast_cost=(j->>'ForecastCost')::numeric
                  )) THEN RAISE EXCEPTION 'Budget preparation requires exact current reviewed publication' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER budget_preparation_publication_guard AFTER INSERT ON budget_preparations
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_budget_preparation();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM budget_preparations) THEN RAISE EXCEPTION 'Retained budget preparation evidence prevents rollback'; END IF;
                END $$;
                DROP FUNCTION enforce_budget_preparation() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "budget_preparations");
        }
    }
}
