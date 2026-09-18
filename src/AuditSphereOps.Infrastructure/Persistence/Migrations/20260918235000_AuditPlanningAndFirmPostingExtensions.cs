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
            // ── audit schema tables (§19–§21) ────────────────────────────────

            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS audit;");

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS audit.materiality_assessments (
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

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS audit.audit_risks (
                    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    engagement_id           uuid NOT NULL REFERENCES engagements(id),
                    actor_id                uuid NOT NULL,
                    account_area            varchar(200) NOT NULL,
                    assertion               varchar(100) NOT NULL,
                    drivers                 text NOT NULL,
                    significance_decision   varchar(30) NOT NULL,
                    controls_considered     text,
                    response_description    text NOT NULL,
                    status                  varchar(30) NOT NULL DEFAULT 'IDENTIFIED',
                    created_at              timestamptz NOT NULL DEFAULT now()
                );
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS audit.population_versions (
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

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS audit.workpapers (
                    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    engagement_id    uuid NOT NULL REFERENCES engagements(id),
                    actor_id         uuid NOT NULL,
                    wp_index         varchar(30) NOT NULL,
                    title            varchar(300) NOT NULL,
                    objective        text NOT NULL,
                    template_version varchar(100) NOT NULL,
                    procedure        text NOT NULL,
                    work_performed   text,
                    conclusion       text,
                    revision         bigint NOT NULL DEFAULT 1,
                    status           varchar(30) NOT NULL DEFAULT 'WORKING',
                    submitted_at     timestamptz,
                    created_at       timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT ck_workpaper_revision_positive CHECK (revision > 0),
                    UNIQUE (engagement_id, wp_index)
                );
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS audit.workpaper_submissions (
                    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    workpaper_id  uuid NOT NULL REFERENCES audit.workpapers(id),
                    actor_id      uuid NOT NULL,
                    revision      bigint NOT NULL,
                    conclusion    text NOT NULL,
                    submitted_at  timestamptz NOT NULL DEFAULT now()
                );
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS audit.findings (
                    id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    engagement_id         uuid NOT NULL REFERENCES engagements(id),
                    actor_id              uuid NOT NULL,
                    finding_type          varchar(100) NOT NULL,
                    impact_description    text NOT NULL,
                    corrected             boolean NOT NULL DEFAULT false,
                    monetary_amount       numeric(19,6),
                    management_response   text,
                    status                varchar(30) NOT NULL DEFAULT 'OPEN',
                    created_at            timestamptz NOT NULL DEFAULT now()
                );
                """);

            // ── finance schema extensions for firm posting (§41.5) ───────────

            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS finance;");

            // These tables may already exist from prior migrations via EF.
            // Use CREATE TABLE IF NOT EXISTS so the migration is idempotent.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = 'finance' AND table_name = 'firm_postings'
                    ) THEN
                        CREATE TABLE finance.firm_postings (
                            id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                            firm_id          uuid NOT NULL,
                            actor_id         uuid NOT NULL,
                            fiscal_period_id uuid NOT NULL,
                            source_type      varchar(50) NOT NULL,
                            source_id        uuid NOT NULL,
                            source_revision  bigint NOT NULL DEFAULT 0,
                            posting_purpose  varchar(100) NOT NULL,
                            total_debit      numeric(19,6) NOT NULL,
                            total_credit     numeric(19,6) NOT NULL,
                            reversal_of      uuid REFERENCES finance.firm_postings(id),
                            reason           text,
                            status           varchar(30) NOT NULL DEFAULT 'POSTED',
                            reversed_at      timestamptz,
                            created_at       timestamptz NOT NULL DEFAULT now(),
                            CONSTRAINT ck_firm_posting_balanced CHECK (total_debit = total_credit),
                            UNIQUE (firm_id, source_type, source_id, source_revision, posting_purpose)
                        );
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = 'finance' AND table_name = 'firm_posting_lines'
                    ) THEN
                        CREATE TABLE finance.firm_posting_lines (
                            id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                            posting_id   uuid NOT NULL REFERENCES finance.firm_postings(id),
                            account_code varchar(50) NOT NULL,
                            debit        numeric(19,6) NOT NULL DEFAULT 0,
                            credit       numeric(19,6) NOT NULL DEFAULT 0,
                            CONSTRAINT ck_firm_posting_line_nonneg CHECK (debit >= 0 AND credit >= 0)
                        );
                    END IF;
                END $$;
                """);

            // ── security schema: client_safety_states (§22.4) ───────────────

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = 'security' AND table_name = 'client_safety_states'
                    ) THEN
                        CREATE SCHEMA IF NOT EXISTS security;
                        CREATE TABLE security.client_safety_states (
                            client_id          uuid PRIMARY KEY,
                            input_generation   bigint NOT NULL DEFAULT 1,
                            access_generation  bigint NOT NULL DEFAULT 1,
                            updated_at         timestamptz NOT NULL DEFAULT now()
                        );
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.findings;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.workpaper_submissions;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.workpapers;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.population_versions;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.audit_risks;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.materiality_assessments;");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS audit;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS finance.firm_posting_lines;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS finance.firm_postings;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS security.client_safety_states;");
        }
    }
}
