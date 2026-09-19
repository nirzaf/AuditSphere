using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditPlanningScopeIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preflight: this migration tightens scope integrity on tables whose legacy shape cannot
            // prove it. Rows written before the (firm, client, engagement) triple was enforced may
            // carry an unverifiable scope, and the new NOT NULL columns cannot be backfilled without
            // inventing an actor or a client. Refuse before modifying anything, exactly as the
            // durable-outbox and release-gate migrations refuse ambiguous legacy history.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                  candidate text;
                  rows_present bigint;
                  blocking text := '';
                BEGIN
                  FOREACH candidate IN ARRAY ARRAY[
                    'audit_risks', 'workpapers', 'findings', 'materiality_assessments',
                    'population_versions', 'workpaper_submissions', 'source_receipts',
                    'evidence_links', 'engagement_assignments', 'mapping_rules', 'review_points',
                    'archives', 'record_states', 'acceptance_decisions', 'evaluation_responses',
                    'eqr_cases', 'written_representations', 'specialist_clearances', 'audit_procedures'
                  ] LOOP
                    IF EXISTS (SELECT 1 FROM information_schema.tables
                               WHERE table_name = candidate AND table_schema = current_schema()) THEN
                      EXECUTE format('SELECT count(*) FROM %I', candidate) INTO rows_present;
                      IF rows_present > 0 THEN
                        blocking := blocking || candidate || '=' || rows_present || ' ';
                      END IF;
                    END IF;
                  END LOOP;
                  IF blocking <> '' THEN
                    RAISE EXCEPTION 'Audit scope migration needs an explicit disposition for existing planning history before scope foreign keys can be enforced: %', blocking
                      USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);

            // These three tables were created outside the EF model by an earlier migration, so they
            // are absent from the model snapshot. They are provably empty (preflight above); drop and
            // let the model-declared statements below recreate them, which also removes the single
            // column engagements(id) references that never established matching client scope (§42.3).
            migrationBuilder.Sql("DROP TABLE IF EXISTS workpaper_submissions;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS population_versions;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS materiality_assessments;");
            // Redundant with the model-declared ck_workpaper_values, which folds the same rule in.
            migrationBuilder.Sql("ALTER TABLE workpapers DROP CONSTRAINT IF EXISTS ck_workpaper_revision_positive;");

            migrationBuilder.DropForeignKey(
                name: "FK_engagement_assignments_engagements_engagement_id",
                table: "engagement_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_engagement_assignments_users_user_id",
                table: "engagement_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_eqr_cases_engagements_engagement_id",
                table: "eqr_cases");

            migrationBuilder.DropForeignKey(
                name: "FK_eqr_cases_users_eqr_partner_user_id",
                table: "eqr_cases");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_links_source_receipts_source_receipt_id",
                table: "evidence_links");

            migrationBuilder.DropForeignKey(
                name: "FK_question_definitions_questionnaire_templates_template_id",
                table: "question_definitions");

            migrationBuilder.DropForeignKey(
                name: "FK_source_receipts_engagements_engagement_id",
                table: "source_receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_specialist_clearances_practice_clients_practice_client_id",
                table: "specialist_clearances");

            migrationBuilder.DropForeignKey(
                name: "FK_written_representations_engagements_engagement_id",
                table: "written_representations");

            migrationBuilder.DropIndex(
                name: "IX_written_representations_engagement_id_code",
                table: "written_representations");

            migrationBuilder.DropIndex(
                name: "IX_specialist_clearances_practice_client_id_area",
                table: "specialist_clearances");

            migrationBuilder.DropIndex(
                name: "IX_source_receipts_engagement_id_receipt_token",
                table: "source_receipts");

            migrationBuilder.DropIndex(
                name: "IX_evidence_links_engagement_id_source_receipt_id",
                table: "evidence_links");

            migrationBuilder.DropIndex(
                name: "IX_evidence_links_source_receipt_id",
                table: "evidence_links");

            migrationBuilder.DropIndex(
                name: "IX_eqr_cases_engagement_id",
                table: "eqr_cases");

            migrationBuilder.DropIndex(
                name: "IX_eqr_cases_eqr_partner_user_id",
                table: "eqr_cases");

            migrationBuilder.DropIndex(
                name: "IX_engagement_assignments_engagement_id_user_id_role",
                table: "engagement_assignments");

            migrationBuilder.DropIndex(
                name: "IX_engagement_assignments_user_id",
                table: "engagement_assignments");

            migrationBuilder.DropColumn(
                name: "generation",
                table: "workpapers");

            migrationBuilder.DropColumn(
                name: "severity",
                table: "findings");

            // EF's rename heuristic paired the vestigial free-text columns with the model's new ones
            // (workpapers.state -> procedure, findings.title -> impact_description). They are not
            // renames: the legacy columns are removed and the authoritative columns already exist.
            migrationBuilder.Sql("ALTER TABLE workpapers DROP COLUMN IF EXISTS state;");
            migrationBuilder.Sql("ALTER TABLE findings DROP COLUMN IF EXISTS title;");

            // A foreign key on engagement id alone never established matching client or firm scope
            // (§27.4, §42.3); the composite keys added below replace them outright.
            migrationBuilder.Sql("ALTER TABLE audit_risks DROP CONSTRAINT IF EXISTS audit_risks_engagement_id_fkey;");
            migrationBuilder.Sql("ALTER TABLE workpapers DROP CONSTRAINT IF EXISTS workpapers_engagement_id_fkey;");
            migrationBuilder.Sql("ALTER TABLE findings DROP CONSTRAINT IF EXISTS findings_engagement_id_fkey;");

            // An earlier migration relaxed these to nullable while the EF model still declared them
            // required, so EF had no diff to emit and the database quietly drifted from the model.
            // Scope columns are mandatory for every professional record (§42.3, §27.1).
            migrationBuilder.Sql("""
                ALTER TABLE audit_risks ALTER COLUMN firm_id SET NOT NULL;
                ALTER TABLE audit_risks ALTER COLUMN client_id SET NOT NULL;
                ALTER TABLE audit_risks ALTER COLUMN description SET NOT NULL;
                ALTER TABLE audit_risks ALTER COLUMN severity SET NOT NULL;
                ALTER TABLE workpapers ALTER COLUMN firm_id SET NOT NULL;
                ALTER TABLE workpapers ALTER COLUMN client_id SET NOT NULL;
                ALTER TABLE findings ALTER COLUMN firm_id SET NOT NULL;
                ALTER TABLE findings ALTER COLUMN client_id SET NOT NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "written_representations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "written_representations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "workpapers",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "procedure_id",
                table: "workpapers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.Sql("ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS actor_id uuid NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS conclusion text;");

            migrationBuilder.Sql("ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS objective text NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS status character varying(30) NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS submitted_at timestamp with time zone;");

            migrationBuilder.Sql("ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS template_version character varying(100) NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS work_performed text;");

            migrationBuilder.Sql("ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS wp_index character varying(30) NOT NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "specialist_clearances",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "area",
                table: "specialist_clearances",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                table: "source_receipts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "sha256_digest",
                table: "source_receipts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "receipt_token",
                table: "source_receipts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "original_file_name",
                table: "source_receipts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.Sql("ALTER TABLE source_receipts ADD COLUMN IF NOT EXISTS client_id uuid NOT NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "target_kind",
                table: "review_points",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "comment",
                table: "review_points",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "observed_label",
                table: "record_states",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "local_state",
                table: "record_states",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "artifact_kind",
                table: "record_states",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "source_pattern",
                table: "mapping_rules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "mapping_code",
                table: "mapping_rules",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.Sql("ALTER TABLE mapping_rules ADD COLUMN IF NOT EXISTS client_id uuid NOT NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "findings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.Sql("ALTER TABLE findings ADD COLUMN IF NOT EXISTS actor_id uuid NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE findings ADD COLUMN IF NOT EXISTS corrected boolean NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE findings ADD COLUMN IF NOT EXISTS finding_type character varying(100) NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE findings ADD COLUMN IF NOT EXISTS management_response text;");

            migrationBuilder.Sql("ALTER TABLE findings ADD COLUMN IF NOT EXISTS monetary_amount numeric(19,6);");

            migrationBuilder.AlterColumn<string>(
                name: "purpose",
                table: "evidence_links",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "assertion",
                table: "evidence_links",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.Sql("ALTER TABLE evidence_links ADD COLUMN IF NOT EXISTS client_id uuid NOT NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "question_id",
                table: "evaluation_responses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "bank",
                table: "evaluation_responses",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "answer",
                table: "evaluation_responses",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "eqr_cases",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "engagement_assignments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.Sql("ALTER TABLE engagement_assignments ADD COLUMN IF NOT EXISTS client_id uuid NOT NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "severity",
                table: "audit_risks",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "assertion",
                table: "audit_risks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.Sql("ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS account_area character varying(200) NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS actor_id uuid NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS controls_considered text;");

            migrationBuilder.Sql("ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS drivers text NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS response_description text NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS significance_decision character varying(30) NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS status character varying(30) NOT NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "archives",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "profile_id",
                table: "archives",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "service_route",
                table: "acceptance_decisions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "decision",
                table: "acceptance_decisions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_workpapers_firm_id_id",
                table: "workpapers",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_workpapers_scope_id",
                table: "workpapers",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_source_receipts_firm_id_client_id_engagement_id_id",
                table: "source_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_findings_firm_id_id",
                table: "findings",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_findings_scope_id",
                table: "findings",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_audit_risks_firm_id_id",
                table: "audit_risks",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_audit_risks_scope_id",
                table: "audit_risks",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_audit_procedures_firm_id_id",
                table: "audit_procedures",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_audit_procedures_scope_id",
                table: "audit_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "materiality_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    benchmark_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    benchmark_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    benchmark_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    rate_applied = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    overall_materiality = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    performance_materiality = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    clearly_trivial_threshold = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    qualitative_considerations = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materiality_assessments", x => x.id);
                    table.UniqueConstraint("AK_materiality_assessments_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_materiality_assessments_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_materiality_values", "length(trim(benchmark_source)) > 0 AND length(trim(benchmark_version)) > 0 AND length(trim(rationale)) > 0 AND benchmark_amount > 0 AND rate_applied > 0 AND rate_applied <= 1 AND overall_materiality > 0 AND performance_materiality < overall_materiality AND clearly_trivial_threshold < performance_materiality AND status IN ('DRAFT','APPROVED')");
                    table.ForeignKey(
                        name: "FK_materiality_assessments_engagements_firm_id_client_id_engag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_materiality_assessments_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "population_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    assertion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_receipt_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    extraction_parameters = table.Column<string>(type: "text", nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    monetary_control_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    exclusions = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_population_versions", x => x.id);
                    table.UniqueConstraint("AK_population_versions_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_population_versions_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_population_values", "length(trim(purpose)) > 0 AND length(trim(assertion)) > 0 AND length(trim(source_receipt_ref)) > 0 AND length(trim(extraction_parameters)) > 0 AND row_count >= 0 AND monetary_control_total >= 0 AND currency ~ '^[A-Z]{3}$' AND status IN ('PENDING_APPROVAL','APPROVED','REJECTED')");
                    table.ForeignKey(
                        name: "FK_population_versions_engagements_firm_id_client_id_engagemen~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_population_versions_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workpaper_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workpaper_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    work_performed = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    conclusion = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workpaper_submissions", x => x.id);
                    table.UniqueConstraint("AK_workpaper_submissions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_workpaper_submission_values", "revision > 0 AND length(trim(conclusion)) > 0 AND length(trim(work_performed)) > 0");
                    table.ForeignKey(
                        name: "FK_workpaper_submissions_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workpaper_submissions_workpapers_firm_id_client_id_engageme~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workpaper_id },
                        principalTable: "workpapers",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_written_representations_firm_id_client_id_engagement_id",
                table: "written_representations",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_written_representation_code",
                table: "written_representations",
                columns: new[] { "firm_id", "engagement_id", "code" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_written_representation_values",
                table: "written_representations",
                sql: "length(trim(code)) > 0 AND length(trim(title)) > 0 AND length(trim(narrative)) > 0 AND ((obtained AND obtained_at IS NOT NULL) OR NOT obtained)");

            migrationBuilder.CreateIndex(
                name: "IX_workpapers_firm_id_actor_id",
                table: "workpapers",
                columns: new[] { "firm_id", "actor_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workpapers_firm_id_client_id_engagement_id_procedure_id",
                table: "workpapers",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workpapers_scope_status",
                table: "workpapers",
                columns: new[] { "firm_id", "engagement_id", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_workpaper_values",
                table: "workpapers",
                sql: "revision > 0 AND length(trim(wp_index)) > 0 AND length(trim(title)) > 0 AND length(trim(objective)) > 0 AND length(trim(template_version)) > 0 AND length(trim(procedure)) > 0 AND status IN ('WORKING','SUBMITTED_SNAPSHOT') AND ((status = 'SUBMITTED_SNAPSHOT' AND submitted_at IS NOT NULL AND length(trim(conclusion)) > 0)   OR (status = 'WORKING' AND submitted_at IS NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_clearance_client_area",
                table: "specialist_clearances",
                columns: new[] { "firm_id", "practice_client_id", "area" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_specialist_clearance_values",
                table: "specialist_clearances",
                sql: "length(trim(area)) > 0 AND length(trim(specialist_name)) > 0 AND status IN ('PENDING','CLEARED','HOLD','CONDITIONS') AND ((status = 'CLEARED' AND cleared_at IS NOT NULL) OR status <> 'CLEARED') AND ((status = 'CONDITIONS' AND length(trim(conditions)) > 0) OR status <> 'CONDITIONS')");

            migrationBuilder.CreateIndex(
                name: "IX_source_receipts_firm_id_acquired_by_user_id",
                table: "source_receipts",
                columns: new[] { "firm_id", "acquired_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_source_receipt_scope_token",
                table: "source_receipts",
                columns: new[] { "firm_id", "engagement_id", "receipt_token" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_source_receipt_values",
                table: "source_receipts",
                sql: "source_type IN ('PBC_UPLOAD','DIRECT_FEED','CSV_IMPORT') AND length(trim(receipt_token)) > 0 AND sha256_digest ~ '^[0-9a-f]{64}$' AND byte_count > 0 AND length(trim(original_file_name)) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_review_points_firm_id_client_id_engagement_id",
                table: "review_points",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_review_points_firm_id_raised_by_user_id",
                table: "review_points",
                columns: new[] { "firm_id", "raised_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_review_points_scope_cleared",
                table: "review_points",
                columns: new[] { "firm_id", "engagement_id", "target_id", "cleared" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_review_point_values",
                table: "review_points",
                sql: "length(trim(target_kind)) > 0 AND target_revision >= 1 AND length(trim(comment)) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_record_states_firm_id_client_id_engagement_id",
                table: "record_states",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_record_state_artifact",
                table: "record_states",
                columns: new[] { "firm_id", "engagement_id", "artifact_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_record_state_values",
                table: "record_states",
                sql: "length(trim(artifact_kind)) > 0 AND local_state IN ('Active','Locked','Archived')");

            migrationBuilder.CreateIndex(
                name: "IX_mapping_rules_firm_id_client_id_engagement_id",
                table: "mapping_rules",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_mapping_rule_code_revision",
                table: "mapping_rules",
                columns: new[] { "firm_id", "engagement_id", "mapping_code", "revision" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_mapping_rule_values",
                table: "mapping_rules",
                sql: "revision >= 1 AND length(trim(mapping_code)) > 0 AND length(trim(source_pattern)) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_findings_firm_id_actor_id",
                table: "findings",
                columns: new[] { "firm_id", "actor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_findings_scope_created",
                table: "findings",
                columns: new[] { "firm_id", "client_id", "engagement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_findings_scope_status",
                table: "findings",
                columns: new[] { "firm_id", "engagement_id", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_finding_values",
                table: "findings",
                sql: "length(trim(finding_type)) > 0 AND length(trim(impact_description)) > 0 AND (monetary_amount IS NULL OR monetary_amount >= 0) AND status IN ('OPEN','CORRECTED','EVALUATED') AND ((corrected AND status <> 'OPEN') OR NOT corrected)");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_links_firm_id_client_id_engagement_id_source_recei~",
                table: "evidence_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_receipt_id" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_links_firm_id_client_id_engagement_id_workpaper_id",
                table: "evidence_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workpaper_id" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_scope_receipt",
                table: "evidence_links",
                columns: new[] { "firm_id", "engagement_id", "source_receipt_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidence_link_values",
                table: "evidence_links",
                sql: "length(trim(purpose)) > 0 AND length(trim(assertion)) > 0 AND length(trim(relevance_reliability_assessment)) > 0");

            migrationBuilder.CreateIndex(
                name: "ux_evaluation_response_question",
                table: "evaluation_responses",
                columns: new[] { "firm_id", "practice_client_id", "bank", "question_id", "revision" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_evaluation_response_values",
                table: "evaluation_responses",
                sql: "bank IN ('CE','RV') AND revision >= 1 AND length(trim(question_id)) > 0 AND length(trim(answer)) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_eqr_cases_firm_id_client_id_engagement_id",
                table: "eqr_cases",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_eqr_cases_firm_id_eqr_partner_user_id",
                table: "eqr_cases",
                columns: new[] { "firm_id", "eqr_partner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_eqr_case_engagement",
                table: "eqr_cases",
                columns: new[] { "firm_id", "engagement_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_eqr_case_values",
                table: "eqr_cases",
                sql: "status IN ('PENDING','IN_PROGRESS','CONCURRED','CHANGES_REQUESTED') AND ((status = 'CONCURRED' AND concurrence_date IS NOT NULL AND completed_at IS NOT NULL)   OR status <> 'CONCURRED')");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_assignments_firm_id_client_id_engagement_id",
                table: "engagement_assignments",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_assignments_firm_id_user_id",
                table: "engagement_assignments",
                columns: new[] { "firm_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_engagement_assignments_scope",
                table: "engagement_assignments",
                columns: new[] { "firm_id", "engagement_id", "user_id", "role" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_engagement_assignment_values",
                table: "engagement_assignments",
                sql: "length(trim(role)) > 0 AND allocated_hours >= 0 AND ((start_date IS NULL OR end_date IS NULL) OR start_date <= end_date)");

            migrationBuilder.CreateIndex(
                name: "IX_audit_risks_firm_id_actor_id",
                table: "audit_risks",
                columns: new[] { "firm_id", "actor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_risks_scope_created",
                table: "audit_risks",
                columns: new[] { "firm_id", "client_id", "engagement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_risks_scope_status",
                table: "audit_risks",
                columns: new[] { "firm_id", "engagement_id", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_risk_values",
                table: "audit_risks",
                sql: "length(trim(account_area)) > 0 AND length(trim(assertion)) > 0 AND length(trim(description)) > 0 AND length(trim(drivers)) > 0 AND length(trim(response_description)) > 0 AND significance_decision IN ('SIGNIFICANT','NORMAL') AND status IN ('IDENTIFIED','ASSESSED','RESPONDED') AND severity = CASE WHEN significance_decision = 'SIGNIFICANT' THEN 'Significant' ELSE 'Normal' END");

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedures_firm_id_client_id_engagement_id_risk_id",
                table: "audit_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "risk_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures",
                sql: "length(trim(title)) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_archives_firm_id_client_id_engagement_id",
                table: "archives",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_archives_scope_status",
                table: "archives",
                columns: new[] { "firm_id", "engagement_id", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_values",
                table: "archives",
                sql: "length(trim(profile_id)) > 0 AND length(trim(status)) > 0");

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_client_generation",
                table: "acceptance_decisions",
                columns: new[] { "firm_id", "practice_client_id", "generation" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_acceptance_decision_values",
                table: "acceptance_decisions",
                sql: "generation >= 1 AND length(trim(service_route)) > 0 AND decision IN ('Pending','Accepted','AcceptedWithConditions','Declined') AND ((decision IN ('Accepted','AcceptedWithConditions','Declined') AND decided_at IS NOT NULL        AND decided_by_user_id IS NOT NULL) OR decision = 'Pending')");

            migrationBuilder.CreateIndex(
                name: "IX_materiality_assessments_firm_id_actor_id",
                table: "materiality_assessments",
                columns: new[] { "firm_id", "actor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_materiality_scope_status_created",
                table: "materiality_assessments",
                columns: new[] { "firm_id", "engagement_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_population_scope_status",
                table: "population_versions",
                columns: new[] { "firm_id", "engagement_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_population_versions_firm_id_actor_id",
                table: "population_versions",
                columns: new[] { "firm_id", "actor_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workpaper_submissions_firm_id_actor_id",
                table: "workpaper_submissions",
                columns: new[] { "firm_id", "actor_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workpaper_submissions_firm_id_client_id_engagement_id_workp~",
                table: "workpaper_submissions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workpaper_id" });

            migrationBuilder.CreateIndex(
                name: "ux_workpaper_submission_revision",
                table: "workpaper_submissions",
                columns: new[] { "firm_id", "workpaper_id", "revision" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_acceptance_decisions_practice_clients_firm_id_practice_clie~",
                table: "acceptance_decisions",
                columns: new[] { "firm_id", "practice_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_archives_engagements_firm_id_client_id_engagement_id",
                table: "archives",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_procedures_audit_risks_firm_id_client_id_engagement_i~",
                table: "audit_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "risk_id" },
                principalTable: "audit_risks",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_procedures_engagements_firm_id_client_id_engagement_id",
                table: "audit_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_risks_engagements_firm_id_client_id_engagement_id",
                table: "audit_risks",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_risks_users_firm_id_actor_id",
                table: "audit_risks",
                columns: new[] { "firm_id", "actor_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_engagement_assignments_engagements_firm_id_client_id_engage~",
                table: "engagement_assignments",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_engagement_assignments_users_firm_id_user_id",
                table: "engagement_assignments",
                columns: new[] { "firm_id", "user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_eqr_cases_engagements_firm_id_client_id_engagement_id",
                table: "eqr_cases",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_eqr_cases_users_firm_id_eqr_partner_user_id",
                table: "eqr_cases",
                columns: new[] { "firm_id", "eqr_partner_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_evaluation_responses_practice_clients_firm_id_practice_clie~",
                table: "evaluation_responses",
                columns: new[] { "firm_id", "practice_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_links_engagements_firm_id_client_id_engagement_id",
                table: "evidence_links",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_links_source_receipts_firm_id_client_id_engagement~",
                table: "evidence_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_receipt_id" },
                principalTable: "source_receipts",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_links_workpapers_firm_id_client_id_engagement_id_w~",
                table: "evidence_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workpaper_id" },
                principalTable: "workpapers",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_findings_engagements_firm_id_client_id_engagement_id",
                table: "findings",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_findings_users_firm_id_actor_id",
                table: "findings",
                columns: new[] { "firm_id", "actor_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mapping_rules_engagements_firm_id_client_id_engagement_id",
                table: "mapping_rules",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_question_definitions_questionnaire_templates_template_id",
                table: "question_definitions",
                column: "template_id",
                principalTable: "questionnaire_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_record_states_engagements_firm_id_client_id_engagement_id",
                table: "record_states",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_review_points_engagements_firm_id_client_id_engagement_id",
                table: "review_points",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_review_points_users_firm_id_raised_by_user_id",
                table: "review_points",
                columns: new[] { "firm_id", "raised_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_source_receipts_engagements_firm_id_client_id_engagement_id",
                table: "source_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_source_receipts_users_firm_id_acquired_by_user_id",
                table: "source_receipts",
                columns: new[] { "firm_id", "acquired_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_specialist_clearances_practice_clients_firm_id_practice_cli~",
                table: "specialist_clearances",
                columns: new[] { "firm_id", "practice_client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workpapers_audit_procedures_firm_id_client_id_engagement_id~",
                table: "workpapers",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" },
                principalTable: "audit_procedures",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workpapers_engagements_firm_id_client_id_engagement_id",
                table: "workpapers",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_workpapers_users_firm_id_actor_id",
                table: "workpapers",
                columns: new[] { "firm_id", "actor_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_written_representations_engagements_firm_id_client_id_engag~",
                table: "written_representations",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // Frozen planning evidence is append-only at the database boundary, not merely by
            // application convention (§27.5, NT-12). A finding stays mutable: filing a management
            // response is a working-record correction, and the immutable record it feeds is the
            // workpaper submission.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_materiality_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Materiality assessments are immutable planning evidence.'
                    USING ERRCODE = '55000';
                END $$;

                CREATE OR REPLACE FUNCTION prevent_population_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Population versions are immutable extraction evidence.'
                    USING ERRCODE = '55000';
                END $$;

                CREATE OR REPLACE FUNCTION prevent_workpaper_submission_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Workpaper submissions are frozen snapshots and cannot change.'
                    USING ERRCODE = '55000';
                END $$;

                CREATE OR REPLACE FUNCTION freeze_submitted_workpaper()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.status = 'SUBMITTED_SNAPSHOT' THEN
                    RAISE EXCEPTION 'A submitted workpaper is frozen; a corrected conclusion requires a new revision.'
                      USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END $$;

                CREATE OR REPLACE FUNCTION freeze_deleted_workpaper()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.status = 'SUBMITTED_SNAPSHOT' THEN
                    RAISE EXCEPTION 'A submitted workpaper is frozen evidence and cannot be deleted.'
                      USING ERRCODE = '55000';
                  END IF;
                  RETURN OLD;
                END $$;

                CREATE TRIGGER trg_materiality_assessments_append_only
                  BEFORE UPDATE OR DELETE ON materiality_assessments
                  FOR EACH ROW EXECUTE FUNCTION prevent_materiality_mutation();

                CREATE TRIGGER trg_population_versions_append_only
                  BEFORE UPDATE OR DELETE ON population_versions
                  FOR EACH ROW EXECUTE FUNCTION prevent_population_mutation();

                CREATE TRIGGER trg_workpaper_submissions_append_only
                  BEFORE UPDATE OR DELETE ON workpaper_submissions
                  FOR EACH ROW EXECUTE FUNCTION prevent_workpaper_submission_mutation();

                CREATE TRIGGER trg_workpapers_submitted_freeze
                  BEFORE UPDATE ON workpapers
                  FOR EACH ROW EXECUTE FUNCTION freeze_submitted_workpaper();

                CREATE TRIGGER trg_workpapers_submitted_delete_freeze
                  BEFORE DELETE ON workpapers
                  FOR EACH ROW EXECUTE FUNCTION freeze_deleted_workpaper();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the immutability controls first so the reverse statements can run.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_workpapers_submitted_delete_freeze ON workpapers;
                DROP TRIGGER IF EXISTS trg_workpapers_submitted_freeze ON workpapers;
                DROP TRIGGER IF EXISTS trg_workpaper_submissions_append_only ON workpaper_submissions;
                DROP TRIGGER IF EXISTS trg_population_versions_append_only ON population_versions;
                DROP TRIGGER IF EXISTS trg_materiality_assessments_append_only ON materiality_assessments;
                DROP FUNCTION IF EXISTS freeze_deleted_workpaper();
                DROP FUNCTION IF EXISTS freeze_submitted_workpaper();
                DROP FUNCTION IF EXISTS prevent_workpaper_submission_mutation();
                DROP FUNCTION IF EXISTS prevent_population_mutation();
                DROP FUNCTION IF EXISTS prevent_materiality_mutation();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_acceptance_decisions_practice_clients_firm_id_practice_clie~",
                table: "acceptance_decisions");

            migrationBuilder.DropForeignKey(
                name: "FK_archives_engagements_firm_id_client_id_engagement_id",
                table: "archives");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_procedures_audit_risks_firm_id_client_id_engagement_i~",
                table: "audit_procedures");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_procedures_engagements_firm_id_client_id_engagement_id",
                table: "audit_procedures");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_risks_engagements_firm_id_client_id_engagement_id",
                table: "audit_risks");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_risks_users_firm_id_actor_id",
                table: "audit_risks");

            migrationBuilder.DropForeignKey(
                name: "FK_engagement_assignments_engagements_firm_id_client_id_engage~",
                table: "engagement_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_engagement_assignments_users_firm_id_user_id",
                table: "engagement_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_eqr_cases_engagements_firm_id_client_id_engagement_id",
                table: "eqr_cases");

            migrationBuilder.DropForeignKey(
                name: "FK_eqr_cases_users_firm_id_eqr_partner_user_id",
                table: "eqr_cases");

            migrationBuilder.DropForeignKey(
                name: "FK_evaluation_responses_practice_clients_firm_id_practice_clie~",
                table: "evaluation_responses");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_links_engagements_firm_id_client_id_engagement_id",
                table: "evidence_links");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_links_source_receipts_firm_id_client_id_engagement~",
                table: "evidence_links");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_links_workpapers_firm_id_client_id_engagement_id_w~",
                table: "evidence_links");

            migrationBuilder.DropForeignKey(
                name: "FK_findings_engagements_firm_id_client_id_engagement_id",
                table: "findings");

            migrationBuilder.DropForeignKey(
                name: "FK_findings_users_firm_id_actor_id",
                table: "findings");

            migrationBuilder.DropForeignKey(
                name: "FK_mapping_rules_engagements_firm_id_client_id_engagement_id",
                table: "mapping_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_question_definitions_questionnaire_templates_template_id",
                table: "question_definitions");

            migrationBuilder.DropForeignKey(
                name: "FK_record_states_engagements_firm_id_client_id_engagement_id",
                table: "record_states");

            migrationBuilder.DropForeignKey(
                name: "FK_review_points_engagements_firm_id_client_id_engagement_id",
                table: "review_points");

            migrationBuilder.DropForeignKey(
                name: "FK_review_points_users_firm_id_raised_by_user_id",
                table: "review_points");

            migrationBuilder.DropForeignKey(
                name: "FK_source_receipts_engagements_firm_id_client_id_engagement_id",
                table: "source_receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_source_receipts_users_firm_id_acquired_by_user_id",
                table: "source_receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_specialist_clearances_practice_clients_firm_id_practice_cli~",
                table: "specialist_clearances");

            migrationBuilder.DropForeignKey(
                name: "FK_workpapers_audit_procedures_firm_id_client_id_engagement_id~",
                table: "workpapers");

            migrationBuilder.DropForeignKey(
                name: "FK_workpapers_engagements_firm_id_client_id_engagement_id",
                table: "workpapers");

            migrationBuilder.DropForeignKey(
                name: "FK_workpapers_users_firm_id_actor_id",
                table: "workpapers");

            migrationBuilder.DropForeignKey(
                name: "FK_written_representations_engagements_firm_id_client_id_engag~",
                table: "written_representations");

            migrationBuilder.DropTable(
                name: "materiality_assessments");

            migrationBuilder.DropTable(
                name: "population_versions");

            migrationBuilder.DropTable(
                name: "workpaper_submissions");

            migrationBuilder.DropIndex(
                name: "IX_written_representations_firm_id_client_id_engagement_id",
                table: "written_representations");

            migrationBuilder.DropIndex(
                name: "ux_written_representation_code",
                table: "written_representations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_written_representation_values",
                table: "written_representations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_workpapers_firm_id_id",
                table: "workpapers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_workpapers_scope_id",
                table: "workpapers");

            migrationBuilder.DropIndex(
                name: "IX_workpapers_firm_id_actor_id",
                table: "workpapers");

            migrationBuilder.DropIndex(
                name: "IX_workpapers_firm_id_client_id_engagement_id_procedure_id",
                table: "workpapers");

            migrationBuilder.DropIndex(
                name: "ix_workpapers_scope_status",
                table: "workpapers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workpaper_values",
                table: "workpapers");

            migrationBuilder.DropIndex(
                name: "ix_clearance_client_area",
                table: "specialist_clearances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_specialist_clearance_values",
                table: "specialist_clearances");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_source_receipts_firm_id_client_id_engagement_id_id",
                table: "source_receipts");

            migrationBuilder.DropIndex(
                name: "IX_source_receipts_firm_id_acquired_by_user_id",
                table: "source_receipts");

            migrationBuilder.DropIndex(
                name: "ux_source_receipt_scope_token",
                table: "source_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_source_receipt_values",
                table: "source_receipts");

            migrationBuilder.DropIndex(
                name: "IX_review_points_firm_id_client_id_engagement_id",
                table: "review_points");

            migrationBuilder.DropIndex(
                name: "IX_review_points_firm_id_raised_by_user_id",
                table: "review_points");

            migrationBuilder.DropIndex(
                name: "ix_review_points_scope_cleared",
                table: "review_points");

            migrationBuilder.DropCheckConstraint(
                name: "ck_review_point_values",
                table: "review_points");

            migrationBuilder.DropIndex(
                name: "IX_record_states_firm_id_client_id_engagement_id",
                table: "record_states");

            migrationBuilder.DropIndex(
                name: "ux_record_state_artifact",
                table: "record_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_record_state_values",
                table: "record_states");

            migrationBuilder.DropIndex(
                name: "IX_mapping_rules_firm_id_client_id_engagement_id",
                table: "mapping_rules");

            migrationBuilder.DropIndex(
                name: "ux_mapping_rule_code_revision",
                table: "mapping_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mapping_rule_values",
                table: "mapping_rules");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_findings_firm_id_id",
                table: "findings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_findings_scope_id",
                table: "findings");

            migrationBuilder.DropIndex(
                name: "IX_findings_firm_id_actor_id",
                table: "findings");

            migrationBuilder.DropIndex(
                name: "ix_findings_scope_created",
                table: "findings");

            migrationBuilder.DropIndex(
                name: "ix_findings_scope_status",
                table: "findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_finding_values",
                table: "findings");

            migrationBuilder.DropIndex(
                name: "IX_evidence_links_firm_id_client_id_engagement_id_source_recei~",
                table: "evidence_links");

            migrationBuilder.DropIndex(
                name: "IX_evidence_links_firm_id_client_id_engagement_id_workpaper_id",
                table: "evidence_links");

            migrationBuilder.DropIndex(
                name: "ix_evidence_scope_receipt",
                table: "evidence_links");

            migrationBuilder.DropCheckConstraint(
                name: "ck_evidence_link_values",
                table: "evidence_links");

            migrationBuilder.DropIndex(
                name: "ux_evaluation_response_question",
                table: "evaluation_responses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_evaluation_response_values",
                table: "evaluation_responses");

            migrationBuilder.DropIndex(
                name: "IX_eqr_cases_firm_id_client_id_engagement_id",
                table: "eqr_cases");

            migrationBuilder.DropIndex(
                name: "IX_eqr_cases_firm_id_eqr_partner_user_id",
                table: "eqr_cases");

            migrationBuilder.DropIndex(
                name: "ux_eqr_case_engagement",
                table: "eqr_cases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_eqr_case_values",
                table: "eqr_cases");

            migrationBuilder.DropIndex(
                name: "IX_engagement_assignments_firm_id_client_id_engagement_id",
                table: "engagement_assignments");

            migrationBuilder.DropIndex(
                name: "IX_engagement_assignments_firm_id_user_id",
                table: "engagement_assignments");

            migrationBuilder.DropIndex(
                name: "ix_engagement_assignments_scope",
                table: "engagement_assignments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_engagement_assignment_values",
                table: "engagement_assignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_audit_risks_firm_id_id",
                table: "audit_risks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_audit_risks_scope_id",
                table: "audit_risks");

            migrationBuilder.DropIndex(
                name: "IX_audit_risks_firm_id_actor_id",
                table: "audit_risks");

            migrationBuilder.DropIndex(
                name: "ix_audit_risks_scope_created",
                table: "audit_risks");

            migrationBuilder.DropIndex(
                name: "ix_audit_risks_scope_status",
                table: "audit_risks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_risk_values",
                table: "audit_risks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_audit_procedures_firm_id_id",
                table: "audit_procedures");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_audit_procedures_scope_id",
                table: "audit_procedures");

            migrationBuilder.DropIndex(
                name: "IX_audit_procedures_firm_id_client_id_engagement_id_risk_id",
                table: "audit_procedures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures");

            migrationBuilder.DropIndex(
                name: "IX_archives_firm_id_client_id_engagement_id",
                table: "archives");

            migrationBuilder.DropIndex(
                name: "ix_archives_scope_status",
                table: "archives");

            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_values",
                table: "archives");

            migrationBuilder.DropIndex(
                name: "ix_acceptance_client_generation",
                table: "acceptance_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_acceptance_decision_values",
                table: "acceptance_decisions");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "source_receipts");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "mapping_rules");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "evidence_links");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "engagement_assignments");

            // Restore the pre-migration legacy columns in place; renaming the authoritative columns
            // away would leave the earlier migration's expected shape incomplete.
            migrationBuilder.Sql("ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS state text;");
            migrationBuilder.Sql("ALTER TABLE findings ADD COLUMN IF NOT EXISTS title text;");
            migrationBuilder.Sql("ALTER TABLE audit_risks DROP CONSTRAINT IF EXISTS audit_risks_engagement_id_fkey; ALTER TABLE audit_risks ADD CONSTRAINT audit_risks_engagement_id_fkey FOREIGN KEY (engagement_id) REFERENCES engagements(id);");
            migrationBuilder.Sql("ALTER TABLE workpapers DROP CONSTRAINT IF EXISTS workpapers_engagement_id_fkey; ALTER TABLE workpapers ADD CONSTRAINT workpapers_engagement_id_fkey FOREIGN KEY (engagement_id) REFERENCES engagements(id);");
            migrationBuilder.Sql("ALTER TABLE findings DROP CONSTRAINT IF EXISTS findings_engagement_id_fkey; ALTER TABLE findings ADD CONSTRAINT findings_engagement_id_fkey FOREIGN KEY (engagement_id) REFERENCES engagements(id);");
            migrationBuilder.Sql("""
                ALTER TABLE audit_risks ALTER COLUMN firm_id DROP NOT NULL;
                ALTER TABLE audit_risks ALTER COLUMN client_id DROP NOT NULL;
                ALTER TABLE audit_risks ALTER COLUMN description DROP NOT NULL;
                ALTER TABLE audit_risks ALTER COLUMN severity DROP NOT NULL;
                ALTER TABLE workpapers ALTER COLUMN firm_id DROP NOT NULL;
                ALTER TABLE workpapers ALTER COLUMN client_id DROP NOT NULL;
                ALTER TABLE findings ALTER COLUMN firm_id DROP NOT NULL;
                ALTER TABLE findings ALTER COLUMN client_id DROP NOT NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "written_representations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "written_representations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "workpapers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<Guid>(
                name: "procedure_id",
                table: "workpapers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "generation",
                table: "workpapers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "specialist_clearances",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "area",
                table: "specialist_clearances",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                table: "source_receipts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "sha256_digest",
                table: "source_receipts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "receipt_token",
                table: "source_receipts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "original_file_name",
                table: "source_receipts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "target_kind",
                table: "review_points",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "comment",
                table: "review_points",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20000)",
                oldMaxLength: 20000);

            migrationBuilder.AlterColumn<string>(
                name: "observed_label",
                table: "record_states",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "local_state",
                table: "record_states",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "artifact_kind",
                table: "record_states",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "source_pattern",
                table: "mapping_rules",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "mapping_code",
                table: "mapping_rules",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "findings",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<string>(
                name: "severity",
                table: "findings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "purpose",
                table: "evidence_links",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "assertion",
                table: "evidence_links",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "question_id",
                table: "evaluation_responses",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "bank",
                table: "evaluation_responses",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "answer",
                table: "evaluation_responses",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "eqr_cases",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "engagement_assignments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "severity",
                table: "audit_risks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "assertion",
                table: "audit_risks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "archives",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "profile_id",
                table: "archives",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "service_route",
                table: "acceptance_decisions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "decision",
                table: "acceptance_decisions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.CreateIndex(
                name: "IX_written_representations_engagement_id_code",
                table: "written_representations",
                columns: new[] { "engagement_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_specialist_clearances_practice_client_id_area",
                table: "specialist_clearances",
                columns: new[] { "practice_client_id", "area" });

            migrationBuilder.CreateIndex(
                name: "IX_source_receipts_engagement_id_receipt_token",
                table: "source_receipts",
                columns: new[] { "engagement_id", "receipt_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_links_engagement_id_source_receipt_id",
                table: "evidence_links",
                columns: new[] { "engagement_id", "source_receipt_id" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_links_source_receipt_id",
                table: "evidence_links",
                column: "source_receipt_id");

            migrationBuilder.CreateIndex(
                name: "IX_eqr_cases_engagement_id",
                table: "eqr_cases",
                column: "engagement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_eqr_cases_eqr_partner_user_id",
                table: "eqr_cases",
                column: "eqr_partner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_assignments_engagement_id_user_id_role",
                table: "engagement_assignments",
                columns: new[] { "engagement_id", "user_id", "role" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_assignments_user_id",
                table: "engagement_assignments",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_engagement_assignments_engagements_engagement_id",
                table: "engagement_assignments",
                column: "engagement_id",
                principalTable: "engagements",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_engagement_assignments_users_user_id",
                table: "engagement_assignments",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_eqr_cases_engagements_engagement_id",
                table: "eqr_cases",
                column: "engagement_id",
                principalTable: "engagements",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_eqr_cases_users_eqr_partner_user_id",
                table: "eqr_cases",
                column: "eqr_partner_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_links_source_receipts_source_receipt_id",
                table: "evidence_links",
                column: "source_receipt_id",
                principalTable: "source_receipts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_question_definitions_questionnaire_templates_template_id",
                table: "question_definitions",
                column: "template_id",
                principalTable: "questionnaire_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_source_receipts_engagements_engagement_id",
                table: "source_receipts",
                column: "engagement_id",
                principalTable: "engagements",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_specialist_clearances_practice_clients_practice_client_id",
                table: "specialist_clearances",
                column: "practice_client_id",
                principalTable: "practice_clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_written_representations_engagements_engagement_id",
                table: "written_representations",
                column: "engagement_id",
                principalTable: "engagements",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Restore the pre-migration shape of the three model-external tables exactly as the
            // earlier migration created them, so a downgrade leaves a usable database instead of
            // silently discarding the planning tables.
            migrationBuilder.Sql("""
                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS generation bigint;

                CREATE TABLE IF NOT EXISTS materiality_assessments (
                    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    engagement_id               uuid NOT NULL REFERENCES engagements(id),
                    actor_id                    uuid NOT NULL,
                    benchmark_source            varchar(200) NOT NULL,
                    benchmark_version           varchar(100) NOT NULL,
                    rationale                   text NOT NULL,
                    benchmark_amount            numeric(19,6) NOT NULL,
                    rate_applied                numeric(9,6) NOT NULL,
                    overall_materiality         numeric(19,6) NOT NULL,
                    performance_materiality     numeric(19,6) NOT NULL,
                    clearly_trivial_threshold   numeric(19,6) NOT NULL,
                    qualitative_considerations  text,
                    status                      varchar(30) NOT NULL DEFAULT 'DRAFT',
                    created_at                  timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT ck_materiality_thresholds CHECK (
                        performance_materiality < overall_materiality AND
                        clearly_trivial_threshold < performance_materiality
                    )
                );

                CREATE TABLE IF NOT EXISTS population_versions (
                    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    engagement_id           uuid NOT NULL REFERENCES engagements(id),
                    actor_id                uuid NOT NULL,
                    purpose                 varchar(300) NOT NULL,
                    assertion               varchar(100) NOT NULL,
                    source_receipt_ref      varchar(200) NOT NULL,
                    extraction_parameters   text NOT NULL,
                    row_count               integer NOT NULL CHECK (row_count >= 0),
                    monetary_control_total  numeric(19,6) NOT NULL CHECK (monetary_control_total >= 0),
                    currency                char(3) NOT NULL,
                    exclusions              text,
                    status                  varchar(30) NOT NULL DEFAULT 'PENDING_APPROVAL',
                    created_at              timestamptz NOT NULL DEFAULT now()
                );

                CREATE TABLE IF NOT EXISTS workpaper_submissions (
                    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    workpaper_id  uuid NOT NULL REFERENCES workpapers(id),
                    actor_id      uuid NOT NULL,
                    revision      bigint NOT NULL,
                    conclusion    text NOT NULL,
                    submitted_at  timestamptz NOT NULL DEFAULT now()
                );

                ALTER TABLE workpapers ADD CONSTRAINT ck_workpaper_revision_positive CHECK (revision > 0);
                """);
        }
    }
}
