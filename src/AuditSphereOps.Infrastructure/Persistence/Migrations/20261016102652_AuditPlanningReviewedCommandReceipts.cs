using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditPlanningReviewedCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_planning_command_receipts",
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
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_json = table.Column<string>(type: "character varying(120000)", maxLength: 120000, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_planning_command_receipts", x => x.id);
                    table.CheckConstraint("ck_audit_planning_command_receipt_values", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND result_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND action IN ('CALCULATE_MATERIALITY','ROUND_MATERIALITY','APPROVE_MATERIALITY','CREATE_RISK','ASSESS_RISK','ASSIGN_RISK_OWNER','CLEAR_RISK','LINK_PROCEDURE_RISK') AND length(input_json) BETWEEN 1 AND 120000 AND length(preview_json) BETWEEN 1 AND 1000000 AND length(result_json) BETWEEN 1 AND 1000000 AND jsonb_typeof(input_json::jsonb) = 'object' AND jsonb_typeof(preview_json::jsonb) = 'object' AND jsonb_typeof(result_json::jsonb) = 'object' AND (input_json::jsonb->>'Action') IS NOT DISTINCT FROM action AND (result_json::jsonb->>'ResultId')::uuid IS NOT DISTINCT FROM result_id");
                    table.ForeignKey(
                        name: "FK_audit_planning_command_receipts_engagements_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_planning_command_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_planning_command_receipts_firm_id_client_id_engagemen~",
                table: "audit_planning_command_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_planning_engagement_history",
                table: "audit_planning_command_receipts",
                columns: new[] { "firm_id", "engagement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_planning_actor_request",
                table: "audit_planning_command_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "ux_audit_planning_action_result",
                table: "audit_planning_command_receipts",
                columns: new[] { "firm_id", "engagement_id", "action", "result_id" },
                unique: true,
                filter: "action <> 'LINK_PROCEDURE_RISK'");

            migrationBuilder.Sql("""
                CREATE FUNCTION enforce_audit_planning_receipt_publication()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                  p jsonb := NEW.preview_json::jsonb;
                  i jsonb := NEW.input_json::jsonb;
                  r jsonb := NEW.result_json::jsonb;
                  actual jsonb;
                  detail jsonb;
                  calc jsonb;
                  risk jsonb;
                BEGIN
                  IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR jsonb_typeof(i) IS DISTINCT FROM 'object' OR jsonb_typeof(r) IS DISTINCT FROM 'object'
                    OR (p->>'RequestId')::uuid IS DISTINCT FROM NEW.request_id
                    OR p->>'RequestHash' IS DISTINCT FROM NEW.request_hash OR p->>'ReviewBasis' IS DISTINCT FROM NEW.review_basis
                    OR p->'CanonicalInput' IS DISTINCT FROM i OR i->>'Action' IS DISTINCT FROM NEW.action
                    OR (p->'State'->>'EngagementId')::uuid IS DISTINCT FROM NEW.engagement_id
                    OR (p->'State'->>'ClientId')::uuid IS DISTINCT FROM NEW.client_id
                    OR p->'State'->>'Action' IS DISTINCT FROM NEW.action OR p->'State'->>'ReviewBasis' IS DISTINCT FROM NEW.review_basis
                    OR p->'State'->>'CanProceed' IS DISTINCT FROM 'true'
                    OR (r->>'ResultId')::uuid IS DISTINCT FROM NEW.result_id OR r->>'Summary' IS DISTINCT FROM p->>'Summary'
                    OR NOT EXISTS (SELECT 1 FROM users WHERE id=NEW.actor_id AND firm_id=NEW.firm_id AND session_epoch=NEW.actor_epoch AND NOT disabled AND user_kind <> 'Client')
                  THEN RAISE EXCEPTION 'Planning receipt envelope is inconsistent.' USING ERRCODE='23514'; END IF;

                  CASE NEW.action
                    WHEN 'CALCULATE_MATERIALITY', 'ROUND_MATERIALITY', 'APPROVE_MATERIALITY' THEN
                      SELECT to_jsonb(m) INTO actual FROM materiality_assessments m WHERE m.id=NEW.result_id AND m.firm_id=NEW.firm_id AND m.client_id=NEW.client_id AND m.engagement_id=NEW.engagement_id;
                      detail := r->'Materiality';
                      SELECT to_jsonb(c) INTO calc FROM materiality_calculations c WHERE c.id=(detail->>'CalculationId')::uuid AND c.firm_id=NEW.firm_id AND c.client_id=NEW.client_id AND c.engagement_id=NEW.engagement_id;
                      IF actual IS NULL OR calc IS NULL OR (detail->>'AssessmentId')::uuid IS DISTINCT FROM NEW.result_id
                        OR (detail->>'PlanningMateriality')::numeric IS DISTINCT FROM (actual->>'overall_materiality')::numeric
                        OR (detail->>'TolerableError')::numeric IS DISTINCT FROM (actual->>'performance_materiality')::numeric
                        OR (detail->>'SadThreshold')::numeric IS DISTINCT FROM (actual->>'clearly_trivial_threshold')::numeric
                        OR detail->>'Rationale' IS DISTINCT FROM actual->>'rationale'
                        OR (detail->>'PreparedByUserId')::uuid IS DISTINCT FROM (actual->>'actor_id')::uuid
                        OR detail->>'PolicyVersion' IS DISTINCT FROM calc->>'policy_version'
                        OR (detail->>'MappingVersion')::bigint IS DISTINCT FROM (calc->>'mapping_version_number')::bigint
                        OR (p->'State'->'Source'->>'MappingId')::uuid IS DISTINCT FROM (calc->>'mapping_version_id')::uuid
                        OR p->'State'->'Source'->>'DatasetDigest' IS DISTINCT FROM calc->>'dataset_digest'
                        OR ((calc->>'materiality_assessment_id')::uuid <> NEW.result_id AND NOT EXISTS (SELECT 1 FROM materiality_rounding_decisions d WHERE d.firm_id=NEW.firm_id AND d.client_id=NEW.client_id AND d.engagement_id=NEW.engagement_id AND d.effective_assessment_id=NEW.result_id AND d.materiality_calculation_id=(calc->>'id')::uuid AND d.id=(detail->>'RoundingDecisionId')::uuid))
                        OR (detail->>'MappingId')::uuid IS DISTINCT FROM (calc->>'mapping_version_id')::uuid
                        OR (detail->>'DatasetId')::uuid IS DISTINCT FROM (calc->>'dataset_id')::uuid
                        OR detail->>'DatasetDigest' IS DISTINCT FROM calc->>'dataset_digest'
                        OR detail->>'Currency' IS DISTINCT FROM calc->>'currency'
                        OR (detail->>'ComputedPlanningMateriality')::numeric IS DISTINCT FROM (calc->>'planning_materiality')::numeric
                        OR (detail->>'ComputedTolerableError')::numeric IS DISTINCT FROM (calc->>'tolerable_error')::numeric
                        OR (detail->>'ComputedSadThreshold')::numeric IS DISTINCT FROM (calc->>'sad_threshold')::numeric
                        OR (p->'MaterialityOutcome'->>'PlanningMateriality')::numeric IS DISTINCT FROM (actual->>'overall_materiality')::numeric
                        OR (p->'MaterialityOutcome'->>'TolerableError')::numeric IS DISTINCT FROM (actual->>'performance_materiality')::numeric
                        OR (p->'MaterialityOutcome'->>'SadThreshold')::numeric IS DISTINCT FROM (actual->>'clearly_trivial_threshold')::numeric
                      THEN RAISE EXCEPTION 'Planning receipt materiality lineage is inconsistent.' USING ERRCODE='23514'; END IF;
                      IF NEW.action='CALCULATE_MATERIALITY' THEN
                        IF (actual->>'actor_id')::uuid IS DISTINCT FROM NEW.actor_id OR (calc->>'materiality_assessment_id')::uuid IS DISTINCT FROM NEW.result_id
                          OR calc->>'benchmark_kind' IS DISTINCT FROM i->'Calculate'->>'BenchmarkKind'
                          OR (calc->>'rate_percent')::numeric IS DISTINCT FROM (i->'Calculate'->>'RatePercent')::numeric
                          OR (calc->>'performance_percent')::numeric IS DISTINCT FROM (i->'Calculate'->>'PerformancePercent')::numeric
                          OR (calc->>'trivial_percent')::numeric IS DISTINCT FROM (i->'Calculate'->>'TrivialPercent')::numeric
                          OR actual->>'rationale' IS DISTINCT FROM i->'Calculate'->>'Rationale'
                        THEN RAISE EXCEPTION 'Calculation receipt does not describe its actor result.' USING ERRCODE='23514'; END IF;
                      ELSIF NEW.action='ROUND_MATERIALITY' THEN
                        IF (actual->>'actor_id')::uuid IS DISTINCT FROM NEW.actor_id
                          OR (p->'State'->'Materiality'->>'AssessmentId')::uuid IS DISTINCT FROM (i->'Round'->>'AssessmentId')::uuid
                          OR NOT EXISTS (SELECT 1 FROM materiality_rounding_decisions d WHERE d.id=(detail->>'RoundingDecisionId')::uuid AND d.firm_id=NEW.firm_id AND d.client_id=NEW.client_id AND d.engagement_id=NEW.engagement_id
                            AND d.effective_assessment_id=NEW.result_id AND d.decided_by_user_id=NEW.actor_id AND d.materiality_calculation_id=(calc->>'id')::uuid
                            AND d.rationale=i->'Round'->>'Rationale' AND d.adjusted_planning_materiality=(i->'Round'->>'PlanningMateriality')::numeric
                            AND d.adjusted_tolerable_error=(i->'Round'->>'TolerableError')::numeric AND d.adjusted_sad_threshold=(i->'Round'->>'SadThreshold')::numeric)
                        THEN RAISE EXCEPTION 'Rounding receipt does not describe its actor result.' USING ERRCODE='23514'; END IF;
                      ELSE
                        IF (i->'Approve'->>'AssessmentId')::uuid IS DISTINCT FROM NEW.result_id
                          OR (p->'State'->'Materiality'->>'AssessmentId')::uuid IS DISTINCT FROM NEW.result_id
                          OR (p->'State'->'Materiality'->>'CalculationId')::uuid IS DISTINCT FROM (calc->>'id')::uuid
                          OR (r->'Materiality'->>'ApprovedByUserId')::uuid IS DISTINCT FROM NEW.actor_id
                          OR (p->'State'->'Materiality'->>'PlanningMateriality')::numeric IS DISTINCT FROM (actual->>'overall_materiality')::numeric
                          OR (p->'State'->'Materiality'->>'TolerableError')::numeric IS DISTINCT FROM (actual->>'performance_materiality')::numeric
                          OR (p->'State'->'Materiality'->>'SadThreshold')::numeric IS DISTINCT FROM (actual->>'clearly_trivial_threshold')::numeric
                          OR NOT EXISTS (SELECT 1 FROM materiality_approvals a WHERE a.firm_id=NEW.firm_id AND a.client_id=NEW.client_id AND a.engagement_id=NEW.engagement_id
                            AND a.materiality_assessment_id=NEW.result_id AND a.approved_by_user_id=NEW.actor_id AND a.approved_by_user_id <> (actual->>'actor_id')::uuid)
                        THEN RAISE EXCEPTION 'Approval receipt does not describe its actor decision.' USING ERRCODE='23514'; END IF;
                      END IF;
                    WHEN 'CREATE_RISK' THEN
                      SELECT to_jsonb(x) INTO actual FROM audit_risks x WHERE x.id=NEW.result_id AND x.firm_id=NEW.firm_id AND x.client_id=NEW.client_id AND x.engagement_id=NEW.engagement_id;
                      IF actual IS NULL OR (actual->>'actor_id')::uuid IS DISTINCT FROM NEW.actor_id OR (r->'Risk'->>'RiskId')::uuid IS DISTINCT FROM NEW.result_id
                        OR actual->>'account_area' IS DISTINCT FROM i->'CreateRisk'->>'Area' OR actual->>'description' IS DISTINCT FROM i->'CreateRisk'->>'Description'
                        OR actual->>'controls_considered' IS DISTINCT FROM i->'CreateRisk'->>'ControlsConsidered' OR actual->>'drivers' IS DISTINCT FROM i->'CreateRisk'->>'Drivers' OR actual->>'assertion' IS DISTINCT FROM i->'CreateRisk'->>'Assertion'
                        OR actual->>'significance_decision' IS DISTINCT FROM i->'CreateRisk'->>'SignificanceDecision' OR actual->>'response_description' IS DISTINCT FROM i->'CreateRisk'->>'Response'
                      THEN RAISE EXCEPTION 'Risk receipt does not describe its actor result.' USING ERRCODE='23514'; END IF;
                    WHEN 'ASSESS_RISK' THEN
                      SELECT to_jsonb(x) INTO actual FROM risk_band_assessments x WHERE x.id=NEW.result_id AND x.firm_id=NEW.firm_id AND x.client_id=NEW.client_id AND x.engagement_id=NEW.engagement_id;
                      IF actual IS NULL OR (actual->>'assessed_by_user_id')::uuid IS DISTINCT FROM NEW.actor_id
                        OR (actual->>'risk_id')::uuid IS DISTINCT FROM (i->'AssessRisk'->>'RiskId')::uuid
                        OR (r->'Risk'->>'AssessmentId')::uuid IS DISTINCT FROM NEW.result_id OR r->'Risk'->>'Band' IS DISTINCT FROM actual->>'band'
                        OR p->>'RiskBandOutcome' IS DISTINCT FROM actual->>'band' OR actual->>'rationale' IS DISTINCT FROM i->'AssessRisk'->>'Rationale'
                        OR (actual->>'likelihood_score')::int IS DISTINCT FROM (i->'AssessRisk'->>'Likelihood')::int
                        OR (actual->>'magnitude_score')::int IS DISTINCT FROM (i->'AssessRisk'->>'Magnitude')::int
                        OR (actual->>'fraud_risk')::boolean IS DISTINCT FROM (i->'AssessRisk'->>'Fraud')::boolean
                        OR (actual->>'critical_estimate')::boolean IS DISTINCT FROM (i->'AssessRisk'->>'CriticalEstimate')::boolean
                      THEN RAISE EXCEPTION 'Band receipt does not describe its actor result.' USING ERRCODE='23514'; END IF;
                    WHEN 'ASSIGN_RISK_OWNER' THEN
                      SELECT to_jsonb(x) INTO actual FROM risk_owner_assignments x WHERE x.id=NEW.result_id AND x.firm_id=NEW.firm_id AND x.client_id=NEW.client_id AND x.engagement_id=NEW.engagement_id;
                      IF actual IS NULL OR (actual->>'assigned_by_user_id')::uuid IS DISTINCT FROM NEW.actor_id
                        OR (actual->>'risk_id')::uuid IS DISTINCT FROM (i->'AssignRiskOwner'->>'RiskId')::uuid
                        OR (actual->>'risk_band_assessment_id')::uuid IS DISTINCT FROM (i->'AssignRiskOwner'->>'ExpectedAssessmentId')::uuid
                        OR (actual->>'owner_user_id')::uuid IS DISTINCT FROM (i->'AssignRiskOwner'->>'OwnerUserId')::uuid
                        OR (r->'Risk'->>'OwnerAssignmentId')::uuid IS DISTINCT FROM NEW.result_id
                        OR (r->'Risk'->>'OwnerUserId')::uuid IS DISTINCT FROM (actual->>'owner_user_id')::uuid
                        OR (p->'State'->'Risk'->>'AssessmentId')::uuid IS DISTINCT FROM (actual->>'risk_band_assessment_id')::uuid
                      THEN RAISE EXCEPTION 'Owner receipt does not describe its actor result.' USING ERRCODE='23514'; END IF;
                    WHEN 'CLEAR_RISK' THEN
                      SELECT to_jsonb(x) INTO actual FROM risk_partner_clearances x WHERE x.id=NEW.result_id AND x.firm_id=NEW.firm_id AND x.client_id=NEW.client_id AND x.engagement_id=NEW.engagement_id;
                      IF actual IS NULL OR (actual->>'partner_user_id')::uuid IS DISTINCT FROM NEW.actor_id
                        OR (actual->>'risk_id')::uuid IS DISTINCT FROM (i->'ClearRisk'->>'RiskId')::uuid
                        OR (actual->>'risk_band_assessment_id')::uuid IS DISTINCT FROM (i->'ClearRisk'->>'ExpectedAssessmentId')::uuid
                        OR actual->>'note' IS DISTINCT FROM i->'ClearRisk'->>'Note'
                        OR (r->'Risk'->>'ClearanceId')::uuid IS DISTINCT FROM NEW.result_id OR r->'Risk'->>'ClearanceNote' IS DISTINCT FROM actual->>'note'
                        OR (p->'State'->'Risk'->>'AssessmentId')::uuid IS DISTINCT FROM (actual->>'risk_band_assessment_id')::uuid
                        OR NOT EXISTS (SELECT 1 FROM risk_band_assessments b WHERE b.id=(actual->>'risk_band_assessment_id')::uuid AND b.risk_id=(actual->>'risk_id')::uuid AND b.band='RED' AND b.assessed_by_user_id <> NEW.actor_id)
                      THEN RAISE EXCEPTION 'Clearance receipt does not describe its actor decision.' USING ERRCODE='23514'; END IF;
                    WHEN 'LINK_PROCEDURE_RISK' THEN
                      SELECT to_jsonb(x) INTO actual FROM audit_procedures x WHERE x.id=NEW.result_id AND x.firm_id=NEW.firm_id AND x.client_id=NEW.client_id AND x.engagement_id=NEW.engagement_id;
                      IF actual IS NULL OR (i->'LinkProcedureRisk'->>'ProcedureId')::uuid IS DISTINCT FROM NEW.result_id
                        OR (r->'Procedure'->>'ProcedureId')::uuid IS DISTINCT FROM NEW.result_id
                        OR (actual->>'risk_id')::uuid IS DISTINCT FROM (i->'LinkProcedureRisk'->>'RiskId')::uuid
                        OR (r->'Procedure'->>'RiskId')::uuid IS DISTINCT FROM (actual->>'risk_id')::uuid
                        OR (p->'State'->'Procedure'->>'RiskId')::uuid IS DISTINCT FROM (i->'LinkProcedureRisk'->>'ExpectedRiskId')::uuid
                        OR (r->'Procedure'->>'ResultRevision')::bigint IS DISTINCT FROM (actual->>'current_result_revision')::bigint
                      THEN RAISE EXCEPTION 'Link receipt does not describe its scoped procedure result.' USING ERRCODE='23514'; END IF;
                    ELSE RAISE EXCEPTION 'Unsupported planning receipt action.' USING ERRCODE='23514';
                  END CASE;
                  IF NEW.action IN ('CREATE_RISK','ASSESS_RISK','ASSIGN_RISK_OWNER','CLEAR_RISK') THEN
                    detail := r->'Risk';
                    SELECT to_jsonb(x) INTO risk FROM audit_risks x WHERE x.id=(detail->>'RiskId')::uuid AND x.firm_id=NEW.firm_id AND x.client_id=NEW.client_id AND x.engagement_id=NEW.engagement_id;
                    IF risk IS NULL OR detail->>'Area' IS DISTINCT FROM risk->>'account_area' OR detail->>'Assertion' IS DISTINCT FROM risk->>'assertion'
                      OR detail->>'Description' IS DISTINCT FROM risk->>'description' OR detail->>'Drivers' IS DISTINCT FROM risk->>'drivers'
                      OR detail->>'ControlsConsidered' IS DISTINCT FROM risk->>'controls_considered' OR detail->>'Response' IS DISTINCT FROM risk->>'response_description' OR detail->>'SignificanceDecision' IS DISTINCT FROM risk->>'significance_decision'
                      OR (NEW.action <> 'CREATE_RISK' AND (actual->>'risk_id')::uuid IS DISTINCT FROM (risk->>'id')::uuid)
                    THEN RAISE EXCEPTION 'Risk result snapshot is inconsistent with scoped evidence.' USING ERRCODE='23514'; END IF;
                  END IF;
                  IF NEW.action IN ('ASSESS_RISK','ASSIGN_RISK_OWNER','CLEAR_RISK') THEN
                    SELECT to_jsonb(b) INTO calc FROM risk_band_assessments b
                      WHERE b.id=(r->'Risk'->>'AssessmentId')::uuid AND b.firm_id=NEW.firm_id AND b.client_id=NEW.client_id
                      AND b.engagement_id=NEW.engagement_id AND b.risk_id=(r->'Risk'->>'RiskId')::uuid;
                    IF calc IS NULL OR r->'Risk'->>'Band' IS DISTINCT FROM calc->>'band'
                      OR (r->'Risk'->>'Likelihood')::int IS DISTINCT FROM (calc->>'likelihood_score')::int
                      OR (r->'Risk'->>'Magnitude')::int IS DISTINCT FROM (calc->>'magnitude_score')::int
                      OR (r->'Risk'->>'Fraud')::boolean IS DISTINCT FROM (calc->>'fraud_risk')::boolean
                      OR (r->'Risk'->>'CriticalEstimate')::boolean IS DISTINCT FROM (calc->>'critical_estimate')::boolean
                      OR r->'Risk'->>'AssessmentRationale' IS DISTINCT FROM calc->>'rationale'
                      OR (r->'Risk'->>'AssessedByUserId')::uuid IS DISTINCT FROM (calc->>'assessed_by_user_id')::uuid
                      OR (NEW.action IN ('ASSIGN_RISK_OWNER','CLEAR_RISK') AND (actual->>'risk_band_assessment_id')::uuid IS DISTINCT FROM (calc->>'id')::uuid)
                      OR EXISTS (SELECT 1 FROM risk_band_assessments newer WHERE newer.firm_id=NEW.firm_id AND newer.risk_id=(calc->>'risk_id')::uuid
                        AND (newer.assessed_at, newer.id) > ((calc->>'assessed_at')::timestamptz, (calc->>'id')::uuid))
                    THEN RAISE EXCEPTION 'Risk assessment snapshot is not the current scoped evidence.' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN NEW;
                EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range THEN
                  RAISE EXCEPTION 'Malformed planning receipt evidence.' USING ERRCODE='23514';
                END;
                $$;
                CREATE TRIGGER audit_planning_receipt_publication_guard
                BEFORE INSERT ON audit_planning_command_receipts
                FOR EACH ROW EXECUTE FUNCTION enforce_audit_planning_receipt_publication();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_audit_planning_receipt_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Reviewed audit planning receipts are immutable.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER audit_planning_receipt_immutable
                BEFORE UPDATE OR DELETE ON audit_planning_command_receipts
                FOR EACH ROW EXECUTE FUNCTION reject_audit_planning_receipt_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE audit_planning_command_receipts IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM audit_planning_command_receipts) THEN
                    RAISE EXCEPTION 'Cannot remove retained reviewed audit planning receipts.' USING ERRCODE = '55000';
                  END IF;
                END;
                $$;
                DROP TRIGGER audit_planning_receipt_publication_guard ON audit_planning_command_receipts;
                DROP FUNCTION enforce_audit_planning_receipt_publication();
                DROP TRIGGER audit_planning_receipt_immutable ON audit_planning_command_receipts;
                DROP FUNCTION reject_audit_planning_receipt_mutation();
                """);
            migrationBuilder.DropTable(
                name: "audit_planning_command_receipts");
        }
    }
}
