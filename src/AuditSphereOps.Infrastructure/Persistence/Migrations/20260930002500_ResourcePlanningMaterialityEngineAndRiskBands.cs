using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResourcePlanningMaterialityEngineAndRiskBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_budget_lines_firm_id_engagement_budget_id_role_activity",
                table: "budget_lines");

            migrationBuilder.AddColumn<string>(
                name: "phase",
                table: "work_tasks",
                type: "text",
                nullable: false,
                defaultValue: "UNASSIGNED");

            migrationBuilder.AddColumn<string>(
                name: "risk_area",
                table: "work_tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phase",
                table: "time_entries",
                type: "text",
                nullable: false,
                defaultValue: "UNASSIGNED");

            migrationBuilder.AddColumn<string>(
                name: "risk_area",
                table: "time_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phase",
                table: "budget_lines",
                type: "text",
                nullable: false,
                defaultValue: "UNASSIGNED");

            migrationBuilder.AddColumn<string>(
                name: "risk_area",
                table: "budget_lines",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "engagement_staff_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staffing_level = table.Column<string>(type: "text", nullable: false),
                    role_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_staff_assignments", x => x.id);
                    table.CheckConstraint("ck_engagement_staff_assignment_values", "staffing_level IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE') AND ((revoked_at IS NULL) = (revoked_by_user_id IS NULL))");
                    table.ForeignKey(
                        name: "FK_engagement_staff_assignments_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_staff_assignments_role_grants_role_grant_id",
                        column: x => x.role_grant_id,
                        principalTable: "role_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_staff_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "materiality_calculations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    materiality_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mapping_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mapping_version_number = table.Column<long>(type: "bigint", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_digest = table.Column<string>(type: "text", nullable: false),
                    benchmark_kind = table.Column<string>(type: "text", nullable: false),
                    destination_code = table.Column<string>(type: "text", nullable: true),
                    source_line_count = table.Column<int>(type: "integer", nullable: false),
                    benchmark_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    performance_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    trivial_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    planning_materiality = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    tolerable_error = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    sad_threshold = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    policy_version = table.Column<string>(type: "text", nullable: false),
                    input_hash = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materiality_calculations", x => x.id);
                    table.CheckConstraint("ck_materiality_calculation_values", "benchmark_kind IN ('REVENUE','PROFIT_BEFORE_TAX','TOTAL_ASSETS','NET_ASSETS','TOTAL_EXPENSES','MAPPED_LINE') AND ((benchmark_kind = 'MAPPED_LINE') = (destination_code IS NOT NULL)) AND benchmark_amount > 0 AND planning_materiality > 0 AND tolerable_error > 0 AND tolerable_error < planning_materiality AND sad_threshold > 0 AND sad_threshold < tolerable_error AND source_line_count > 0 AND length(input_hash) = 64 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_materiality_calculations_mapping_versions_mapping_version_id",
                        column: x => x.mapping_version_id,
                        principalTable: "mapping_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_materiality_calculations_materiality_assessments_materialit~",
                        column: x => x.materiality_assessment_id,
                        principalTable: "materiality_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "risk_band_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    likelihood_score = table.Column<int>(type: "integer", nullable: false),
                    magnitude_score = table.Column<int>(type: "integer", nullable: false),
                    significant = table.Column<bool>(type: "boolean", nullable: false),
                    fraud_risk = table.Column<bool>(type: "boolean", nullable: false),
                    band = table.Column<string>(type: "text", nullable: false),
                    rule_version = table.Column<string>(type: "text", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    assessed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_band_assessments", x => x.id);
                    table.CheckConstraint("ck_risk_band_assessment_rule", "likelihood_score BETWEEN 1 AND 3 AND magnitude_score BETWEEN 1 AND 3 AND length(rationale) > 0 AND band = CASE WHEN significant OR fraud_risk THEN 'RED' WHEN likelihood_score * magnitude_score >= 6 THEN 'RED' WHEN likelihood_score * magnitude_score >= 3 THEN 'AMBER' ELSE 'GREEN' END");
                    table.ForeignKey(
                        name: "FK_risk_band_assessments_audit_risks_risk_id",
                        column: x => x.risk_id,
                        principalTable: "audit_risks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_minutes = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_allocations", x => x.id);
                    table.CheckConstraint("ck_staff_allocation_values", "planned_minutes BETWEEN 1 AND 4800 AND extract(isodow from week_start) = 1");
                    table.ForeignKey(
                        name: "FK_staff_allocations_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_allocations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_availabilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    minutes_per_day = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_availabilities", x => x.id);
                    table.CheckConstraint("ck_staff_availability_values", "end_date >= start_date AND minutes_per_day BETWEEN 1 AND 1440 AND kind IN ('LEAVE','TRAINING','PUBLIC_HOLIDAY')");
                    table.ForeignKey(
                        name: "FK_staff_availabilities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_certifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    issuer = table.Column<string>(type: "text", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_certifications", x => x.id);
                    table.CheckConstraint("ck_staff_certification_values", "length(name) > 0");
                    table.ForeignKey(
                        name: "FK_staff_certifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department = table.Column<string>(type: "text", nullable: false),
                    skills = table.Column<string>(type: "text", nullable: false),
                    weekly_capacity_minutes = table.Column<int>(type: "integer", nullable: false),
                    target_utilization_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 5, scale: 2, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_profiles", x => x.id);
                    table.CheckConstraint("ck_staff_profile_values", "weekly_capacity_minutes BETWEEN 0 AND 4800 AND target_utilization_percent BETWEEN 0 AND 100 AND length(department) > 0");
                    table.ForeignKey(
                        name: "FK_staff_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "risk_owner_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_band_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_staffing_level = table.Column<string>(type: "text", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_owner_assignments", x => x.id);
                    table.CheckConstraint("ck_risk_owner_assignment_values", "owner_staffing_level IN ('ENGAGEMENT_PARTNER','AUDIT_MANAGER','SENIOR_AUDITOR','STAFF_ASSOCIATE')");
                    table.ForeignKey(
                        name: "FK_risk_owner_assignments_risk_band_assessments_risk_band_asse~",
                        column: x => x.risk_band_assessment_id,
                        principalTable: "risk_band_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "risk_partner_clearances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_band_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_partner_clearances", x => x.id);
                    table.CheckConstraint("ck_risk_partner_clearance_values", "length(note) > 0");
                    table.ForeignKey(
                        name: "FK_risk_partner_clearances_risk_band_assessments_risk_band_ass~",
                        column: x => x.risk_band_assessment_id,
                        principalTable: "risk_band_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_budget_lines_firm_budget_role_activity_phase_area",
                table: "budget_lines",
                columns: new[] { "firm_id", "engagement_budget_id", "role", "activity", "phase", "risk_area" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_engagement_staff_assignments_engagement_id",
                table: "engagement_staff_assignments",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_staff_assignments_firm_id_engagement_id_user_id",
                table: "engagement_staff_assignments",
                columns: new[] { "firm_id", "engagement_id", "user_id" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_staff_assignments_role_grant_id",
                table: "engagement_staff_assignments",
                column: "role_grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_engagement_staff_assignments_single_partner",
                table: "engagement_staff_assignments",
                columns: new[] { "firm_id", "engagement_id" },
                unique: true,
                filter: "revoked_at IS NULL AND staffing_level = 'ENGAGEMENT_PARTNER'");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_staff_assignments_user_id",
                table: "engagement_staff_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_materiality_calculations_firm_id_engagement_id_created_at",
                table: "materiality_calculations",
                columns: new[] { "firm_id", "engagement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_materiality_calculations_firm_id_materiality_assessment_id",
                table: "materiality_calculations",
                columns: new[] { "firm_id", "materiality_assessment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_materiality_calculations_mapping_version_id",
                table: "materiality_calculations",
                column: "mapping_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_materiality_calculations_materiality_assessment_id",
                table: "materiality_calculations",
                column: "materiality_assessment_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_band_assessments_firm_id_risk_id_assessed_at",
                table: "risk_band_assessments",
                columns: new[] { "firm_id", "risk_id", "assessed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_band_assessments_risk_id",
                table: "risk_band_assessments",
                column: "risk_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_owner_assignments_firm_id_risk_band_assessment_id",
                table: "risk_owner_assignments",
                columns: new[] { "firm_id", "risk_band_assessment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_owner_assignments_risk_band_assessment_id",
                table: "risk_owner_assignments",
                column: "risk_band_assessment_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_partner_clearances_firm_id_risk_band_assessment_id",
                table: "risk_partner_clearances",
                columns: new[] { "firm_id", "risk_band_assessment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_risk_partner_clearances_risk_band_assessment_id",
                table: "risk_partner_clearances",
                column: "risk_band_assessment_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_allocations_engagement_id",
                table: "staff_allocations",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_allocations_firm_id_engagement_id_user_id_week_start",
                table: "staff_allocations",
                columns: new[] { "firm_id", "engagement_id", "user_id", "week_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_allocations_firm_id_week_start",
                table: "staff_allocations",
                columns: new[] { "firm_id", "week_start" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_allocations_user_id",
                table: "staff_allocations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_availabilities_firm_id_user_id_start_date",
                table: "staff_availabilities",
                columns: new[] { "firm_id", "user_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_availabilities_user_id",
                table: "staff_availabilities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_certifications_firm_id_user_id",
                table: "staff_certifications",
                columns: new[] { "firm_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_certifications_user_id",
                table: "staff_certifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_staff_profiles_firm_id_user_id",
                table: "staff_profiles",
                columns: new[] { "firm_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_profiles_user_id",
                table: "staff_profiles",
                column: "user_id");
            migrationBuilder.Sql("""
                ALTER TABLE work_tasks ADD CONSTRAINT ck_work_task_phase CHECK (phase IN ('PLANNING','FIELDWORK','COMPLETION','REPORTING','UNASSIGNED'));
                ALTER TABLE time_entries ADD CONSTRAINT ck_time_entry_phase CHECK (phase IN ('PLANNING','FIELDWORK','COMPLETION','REPORTING','UNASSIGNED'));
                ALTER TABLE budget_lines ADD CONSTRAINT ck_budget_line_phase CHECK (phase IN ('PLANNING','FIELDWORK','COMPLETION','REPORTING','UNASSIGNED'));
                CREATE FUNCTION prevent_planning_evidence_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION '% rows are append-only planning evidence', TG_TABLE_NAME;
                END;
                $$;
                CREATE TRIGGER trg_materiality_calculations_append_only BEFORE UPDATE OR DELETE ON materiality_calculations
                  FOR EACH ROW EXECUTE FUNCTION prevent_planning_evidence_mutation();
                CREATE TRIGGER trg_risk_band_assessments_append_only BEFORE UPDATE OR DELETE ON risk_band_assessments
                  FOR EACH ROW EXECUTE FUNCTION prevent_planning_evidence_mutation();
                CREATE TRIGGER trg_risk_partner_clearances_append_only BEFORE UPDATE OR DELETE ON risk_partner_clearances
                  FOR EACH ROW EXECUTE FUNCTION prevent_planning_evidence_mutation();
                CREATE TRIGGER trg_risk_owner_assignments_append_only BEFORE UPDATE OR DELETE ON risk_owner_assignments
                  FOR EACH ROW EXECUTE FUNCTION prevent_planning_evidence_mutation();
                CREATE FUNCTION enforce_risk_band_significance() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW.significant IS DISTINCT FROM (SELECT significance_decision = 'SIGNIFICANT' FROM audit_risks WHERE id = NEW.risk_id AND firm_id = NEW.firm_id) THEN
                    RAISE EXCEPTION 'the significance input must match the risk''s recorded significance decision';
                  END IF;
                  RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_risk_band_assessments_significance BEFORE INSERT ON risk_band_assessments
                  FOR EACH ROW EXECUTE FUNCTION enforce_risk_band_significance();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_risk_band_assessments_significance ON risk_band_assessments;
                DROP FUNCTION IF EXISTS enforce_risk_band_significance();
                DROP TRIGGER IF EXISTS trg_materiality_calculations_append_only ON materiality_calculations;
                DROP TRIGGER IF EXISTS trg_risk_band_assessments_append_only ON risk_band_assessments;
                DROP TRIGGER IF EXISTS trg_risk_partner_clearances_append_only ON risk_partner_clearances;
                DROP TRIGGER IF EXISTS trg_risk_owner_assignments_append_only ON risk_owner_assignments;
                DROP FUNCTION IF EXISTS prevent_planning_evidence_mutation();
                ALTER TABLE work_tasks DROP CONSTRAINT IF EXISTS ck_work_task_phase;
                ALTER TABLE time_entries DROP CONSTRAINT IF EXISTS ck_time_entry_phase;
                ALTER TABLE budget_lines DROP CONSTRAINT IF EXISTS ck_budget_line_phase;
                """);

            migrationBuilder.DropTable(
                name: "engagement_staff_assignments");

            migrationBuilder.DropTable(
                name: "materiality_calculations");

            migrationBuilder.DropTable(
                name: "risk_owner_assignments");

            migrationBuilder.DropTable(
                name: "risk_partner_clearances");

            migrationBuilder.DropTable(
                name: "staff_allocations");

            migrationBuilder.DropTable(
                name: "staff_availabilities");

            migrationBuilder.DropTable(
                name: "staff_certifications");

            migrationBuilder.DropTable(
                name: "staff_profiles");

            migrationBuilder.DropTable(
                name: "risk_band_assessments");

            migrationBuilder.DropIndex(
                name: "IX_budget_lines_firm_budget_role_activity_phase_area",
                table: "budget_lines");

            migrationBuilder.DropColumn(
                name: "phase",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "risk_area",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "phase",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "risk_area",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "phase",
                table: "budget_lines");

            migrationBuilder.DropColumn(
                name: "risk_area",
                table: "budget_lines");

            migrationBuilder.CreateIndex(
                name: "IX_budget_lines_firm_id_engagement_budget_id_role_activity",
                table: "budget_lines",
                columns: new[] { "firm_id", "engagement_budget_id", "role", "activity" },
                unique: true);
        }
    }
}
