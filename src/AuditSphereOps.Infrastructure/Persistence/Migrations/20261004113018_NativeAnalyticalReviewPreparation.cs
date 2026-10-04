using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeAnalyticalReviewPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_analytical_reviews_firm_id_client_id_engagement_id",
                table: "analytical_reviews");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_analytical_reviews_firm_id_client_id_engagement_id_id",
                table: "analytical_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "accounting_analysis_preparations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: false),
                    context_json = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_analysis_preparations", x => x.id);
                    table.CheckConstraint("ck_accounting_analysis_preparation", "actor_epoch >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(input_json) BETWEEN 1 AND 20000 AND length(context_json) BETWEEN 1 AND 10000 AND length(result_json) BETWEEN 1 AND 100000 AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_accounting_analysis_preparations_analytical_reviews_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.evidence_id },
                        principalTable: "analytical_reviews",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_analysis_preparations_engagements_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_analysis_preparations_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_analysis_preparations_firm_id_actor_id_request_id",
                table: "accounting_analysis_preparations",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_analysis_preparations_firm_id_client_id_engageme~",
                table: "accounting_analysis_preparations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "evidence_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_analysis_preparations_firm_id_engagement_id_evid~",
                table: "accounting_analysis_preparations",
                columns: new[] { "firm_id", "engagement_id", "evidence_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER accounting_analysis_preparations_append_only BEFORE UPDATE OR DELETE ON accounting_analysis_preparations
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_native_analytical_preparation() RETURNS trigger AS $$
                DECLARE target analytical_reviews%ROWTYPE; current_user_epoch bigint;
                BEGIN
                  SELECT * INTO target FROM analytical_reviews WHERE id=NEW.evidence_id AND firm_id=NEW.firm_id
                    AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id FOR SHARE;
                  SELECT session_epoch INTO current_user_epoch FROM users WHERE id=NEW.actor_id AND firm_id=NEW.firm_id
                    AND disabled=false FOR SHARE;
                  IF target.id IS NULL OR current_user_epoch IS DISTINCT FROM NEW.actor_epoch
                    OR target.created_by_user_id IS DISTINCT FROM NEW.actor_id
                    OR target.status NOT IN ('DRAFT','INSUFFICIENT_DATA') OR target.reviewed_by_user_id IS NOT NULL OR target.reviewed_at IS NOT NULL
                    OR target.period_id IS DISTINCT FROM (NEW.input_json::jsonb->>'PeriodId')::uuid
                    OR target.period_id IS DISTINCT FROM (NEW.context_json::jsonb->>'PeriodId')::uuid
                    OR target.comparison_period_id IS DISTINCT FROM NULLIF(NEW.input_json::jsonb->>'ComparisonPeriodId','')::uuid
                    OR target.input_generation IS DISTINCT FROM (NEW.context_json::jsonb->>'InputGeneration')::bigint
                    OR target.area IS DISTINCT FROM upper(trim(NEW.input_json::jsonb->>'Area'))
                    OR target.measure IS DISTINCT FROM trim(NEW.input_json::jsonb->>'Measure')
                    OR target.current_amount IS DISTINCT FROM (NEW.input_json::jsonb->>'CurrentAmount')::numeric
                    OR target.prior_amount IS DISTINCT FROM (NEW.input_json::jsonb->>'PriorAmount')::numeric
                    OR target.budget_amount IS DISTINCT FROM NULLIF(NEW.input_json::jsonb->>'BudgetAmount','')::numeric
                    OR target.denominator_basis IS DISTINCT FROM trim(NEW.input_json::jsonb->>'DenominatorBasis')
                    OR target.formula_version IS DISTINCT FROM trim(NEW.input_json::jsonb->>'FormulaVersion')
                    OR target.explanation IS DISTINCT FROM trim(NEW.input_json::jsonb->>'Explanation')
                    OR target.seasonality_explanation IS DISTINCT FROM trim(NEW.input_json::jsonb->>'SeasonalityExplanation')
                    OR (NEW.context_json::jsonb->>'EngagementId')::uuid IS DISTINCT FROM NEW.engagement_id
                    OR (NEW.context_json::jsonb->>'ClientId')::uuid IS DISTINCT FROM NEW.client_id
                    OR NEW.context_json::jsonb->>'ReviewBasis' IS DISTINCT FROM NEW.review_basis
                    OR (NEW.result_json::jsonb->>'EvidenceId')::uuid IS DISTINCT FROM NEW.evidence_id
                    OR NEW.result_json::jsonb->>'Kind' IS DISTINCT FROM 'ANALYTICAL'
                    OR NEW.result_json::jsonb->>'Status' IS DISTINCT FROM target.status
                    OR NEW.result_json::jsonb->>'Area' IS DISTINCT FROM target.area
                    OR NEW.result_json::jsonb->>'Measure' IS DISTINCT FROM target.measure
                    OR (NEW.result_json::jsonb->>'ActorId')::uuid IS DISTINCT FROM NEW.actor_id THEN
                    RAISE EXCEPTION 'Native analytical preparation requires exact actor, period and retained result';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER accounting_analysis_preparations_scope_guard BEFORE INSERT ON accounting_analysis_preparations
                  FOR EACH ROW EXECUTE FUNCTION enforce_native_analytical_preparation();
                CREATE FUNCTION protect_native_analytical_inputs() RETURNS trigger AS $$
                BEGIN
                  IF EXISTS(SELECT 1 FROM accounting_analysis_preparations WHERE firm_id=OLD.firm_id AND evidence_id=OLD.id) THEN
                    IF TG_OP='DELETE' OR (to_jsonb(NEW)-ARRAY['status','reviewed_by_user_id','reviewed_at','review_conclusion'])
                      IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','reviewed_by_user_id','reviewed_at','review_conclusion']) THEN
                      RAISE EXCEPTION 'Retained analytical preparation inputs are immutable; create a new revision';
                    END IF;
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER native_analytical_inputs_frozen BEFORE UPDATE OR DELETE ON analytical_reviews
                  FOR EACH ROW EXECUTE FUNCTION protect_native_analytical_inputs();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM accounting_analysis_preparations) THEN
                    RAISE EXCEPTION 'Retained analytical preparations prevent rollback';
                  END IF;
                END $$;
                DROP TRIGGER native_analytical_inputs_frozen ON analytical_reviews;
                DROP FUNCTION protect_native_analytical_inputs();
                DROP TRIGGER accounting_analysis_preparations_scope_guard ON accounting_analysis_preparations;
                DROP FUNCTION enforce_native_analytical_preparation();
                DROP TRIGGER accounting_analysis_preparations_append_only ON accounting_analysis_preparations;
                """);
            migrationBuilder.DropTable(
                name: "accounting_analysis_preparations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_analytical_reviews_firm_id_client_id_engagement_id_id",
                table: "analytical_reviews");

            migrationBuilder.CreateIndex(
                name: "IX_analytical_reviews_firm_id_client_id_engagement_id",
                table: "analytical_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id" });
        }
    }
}
