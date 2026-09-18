using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AuditSphereDbContext))]
    [Migration("20260918235000_AuditPlanningAndFirmPostingExtensions")]
    public partial class AuditPlanningAndFirmPostingExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── materiality_assessments (§19.2) ─────────────────────────────

            migrationBuilder.Sql("""
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
                """);

            // ── audit_risks extensions (§19.3) ────────────────────────────────

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'audit_risks' AND column_name = 'firm_id') THEN
                    ALTER TABLE audit_risks ALTER COLUMN firm_id DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'audit_risks' AND column_name = 'client_id') THEN
                    ALTER TABLE audit_risks ALTER COLUMN client_id DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'audit_risks' AND column_name = 'description') THEN
                    ALTER TABLE audit_risks ALTER COLUMN description DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'audit_risks' AND column_name = 'severity') THEN
                    ALTER TABLE audit_risks ALTER COLUMN severity DROP NOT NULL;
                  END IF;
                END $$;

                ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS actor_id uuid;
                ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS account_area varchar(200);
                ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS drivers text;
                ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS significance_decision varchar(30);
                ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS controls_considered text;
                ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS response_description text;
                ALTER TABLE audit_risks ADD COLUMN IF NOT EXISTS status varchar(30) DEFAULT 'IDENTIFIED';
                """);

            // ── population_versions (§20.1) ─────────────────────────────────

            migrationBuilder.Sql("""
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
                """);

            // ── workpapers & submissions (§21.1) ────────────────────────────

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'workpapers' AND column_name = 'firm_id') THEN
                    ALTER TABLE workpapers ALTER COLUMN firm_id DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'workpapers' AND column_name = 'client_id') THEN
                    ALTER TABLE workpapers ALTER COLUMN client_id DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'workpapers' AND column_name = 'procedure_id') THEN
                    ALTER TABLE workpapers ALTER COLUMN procedure_id DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'workpapers' AND column_name = 'state') THEN
                    ALTER TABLE workpapers ALTER COLUMN state DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'workpapers' AND column_name = 'generation') THEN
                    ALTER TABLE workpapers ALTER COLUMN generation DROP NOT NULL;
                  END IF;
                END $$;

                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS actor_id uuid;
                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS wp_index varchar(30);
                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS objective text;
                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS template_version varchar(100);
                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS procedure text;
                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS work_performed text;
                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS conclusion text;
                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS status varchar(30) DEFAULT 'WORKING';
                ALTER TABLE workpapers ADD COLUMN IF NOT EXISTS submitted_at timestamptz;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS workpaper_submissions (
                    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    workpaper_id  uuid NOT NULL REFERENCES workpapers(id),
                    actor_id      uuid NOT NULL,
                    revision      bigint NOT NULL,
                    conclusion    text NOT NULL,
                    submitted_at  timestamptz NOT NULL DEFAULT now()
                );
                """);

            // ── findings extensions (§23) ───────────────────────────────────

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'findings' AND column_name = 'firm_id') THEN
                    ALTER TABLE findings ALTER COLUMN firm_id DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'findings' AND column_name = 'client_id') THEN
                    ALTER TABLE findings ALTER COLUMN client_id DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'findings' AND column_name = 'title') THEN
                    ALTER TABLE findings ALTER COLUMN title DROP NOT NULL;
                  END IF;
                  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'findings' AND column_name = 'severity') THEN
                    ALTER TABLE findings ALTER COLUMN severity DROP NOT NULL;
                  END IF;
                END $$;

                ALTER TABLE findings ADD COLUMN IF NOT EXISTS actor_id uuid;
                ALTER TABLE findings ADD COLUMN IF NOT EXISTS finding_type varchar(100);
                ALTER TABLE findings ADD COLUMN IF NOT EXISTS impact_description text;
                ALTER TABLE findings ADD COLUMN IF NOT EXISTS corrected boolean NOT NULL DEFAULT false;
                ALTER TABLE findings ADD COLUMN IF NOT EXISTS monetary_amount numeric(19,6);
                ALTER TABLE findings ADD COLUMN IF NOT EXISTS management_response text;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS workpaper_submissions;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS population_versions;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS materiality_assessments;");
        }
    }
}
