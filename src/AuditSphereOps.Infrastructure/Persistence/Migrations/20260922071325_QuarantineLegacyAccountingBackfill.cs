using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuarantineLegacyAccountingBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_backfill_quarantines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    context_kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    candidate_count = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_backfill_quarantines", x => x.id);
                    table.CheckConstraint("ck_accounting_backfill_quarantine_values", "length(trim(target_kind)) > 0 AND length(trim(context_kind)) > 0 AND candidate_count >= 0 AND length(trim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_accounting_backfill_quarantines_practice_clients_firm_id_cl~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_backfill_quarantines_firm_id_client_id",
                table: "accounting_backfill_quarantines",
                columns: new[] { "firm_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ux_accounting_backfill_quarantine_target",
                table: "accounting_backfill_quarantines",
                columns: new[] { "firm_id", "target_kind", "target_id", "context_kind" },
                unique: true);

            migrationBuilder.Sql("""
                WITH candidates AS (
                  SELECT d.id, d.firm_id, d.client_id,
                    count(p.id)::int AS candidate_count,
                    (array_agg(p.id ORDER BY p.id) FILTER (WHERE p.id IS NOT NULL))[1] AS period_id,
                    (array_agg(p.basis ORDER BY p.id) FILTER (WHERE p.id IS NOT NULL))[1] AS period_basis
                  FROM trial_balance_datasets d
                  JOIN engagements e ON e.firm_id = d.firm_id AND e.id = d.engagement_id
                    AND e.practice_client_id = d.client_id
                  LEFT JOIN client_reporting_periods p ON p.firm_id = d.firm_id AND p.client_id = d.client_id
                    AND to_char(p.start_date, 'YYYY-MM-DD') = e.period_start
                    AND to_char(p.end_date, 'YYYY-MM-DD') = e.period_end
                    AND p.currency = d.currency
                    AND (d.basis IS NULL OR upper(trim(p.basis)) = upper(trim(d.basis)))
                  WHERE d.period_id IS NULL
                  GROUP BY d.id, d.firm_id, d.client_id
                )
                UPDATE trial_balance_datasets d
                SET period_id = c.period_id, basis = coalesce(d.basis, c.period_basis)
                FROM candidates c
                WHERE d.id = c.id AND d.firm_id = c.firm_id AND c.candidate_count = 1;

                WITH candidates AS (
                  SELECT d.id, d.firm_id, d.client_id, count(p.id)::int AS candidate_count
                  FROM trial_balance_datasets d
                  JOIN engagements e ON e.firm_id = d.firm_id AND e.id = d.engagement_id
                    AND e.practice_client_id = d.client_id
                  LEFT JOIN client_reporting_periods p ON p.firm_id = d.firm_id AND p.client_id = d.client_id
                    AND to_char(p.start_date, 'YYYY-MM-DD') = e.period_start
                    AND to_char(p.end_date, 'YYYY-MM-DD') = e.period_end
                    AND p.currency = d.currency
                    AND (d.basis IS NULL OR upper(trim(p.basis)) = upper(trim(d.basis)))
                  WHERE d.period_id IS NULL
                  GROUP BY d.id, d.firm_id, d.client_id
                )
                INSERT INTO accounting_backfill_quarantines
                  (id, firm_id, client_id, target_kind, target_id, context_kind, candidate_count, reason, created_at)
                SELECT gen_random_uuid(), firm_id, client_id, 'TRIAL_BALANCE_DATASET', id, 'REPORTING_PERIOD',
                  candidate_count,
                  CASE WHEN candidate_count = 0
                    THEN 'No reporting period exactly matches the legacy engagement dates, client and currency.'
                    ELSE 'Multiple reporting periods match the legacy engagement dates, client and currency.' END,
                  statement_timestamp()
                FROM candidates
                ON CONFLICT (firm_id, target_kind, target_id, context_kind) DO NOTHING;

                WITH candidates AS (
                  SELECT m.id, m.firm_id, m.client_id,
                    count(c.id)::int AS candidate_count,
                    (array_agg(c.id ORDER BY c.id) FILTER (WHERE c.id IS NOT NULL))[1] AS chart_id
                  FROM mapping_versions m
                  JOIN trial_balance_datasets d ON d.firm_id = m.firm_id AND d.client_id = m.client_id
                    AND d.engagement_id = m.engagement_id AND d.id = m.dataset_id
                  LEFT JOIN client_chart_versions c ON c.firm_id = m.firm_id AND c.client_id = m.client_id
                    AND c.status = 'APPROVED'
                    AND to_char(c.effective_from, 'YYYY-MM-DD') <= m.period_end
                    AND (c.effective_to IS NULL OR to_char(c.effective_to, 'YYYY-MM-DD') >= m.period_start)
                  WHERE m.client_chart_version_id IS NULL
                  GROUP BY m.id, m.firm_id, m.client_id
                )
                UPDATE mapping_versions m
                SET client_chart_version_id = c.chart_id
                FROM candidates c
                WHERE m.id = c.id AND m.firm_id = c.firm_id AND c.candidate_count = 1;

                WITH candidates AS (
                  SELECT m.id, m.firm_id, m.client_id, count(c.id)::int AS candidate_count
                  FROM mapping_versions m
                  JOIN trial_balance_datasets d ON d.firm_id = m.firm_id AND d.client_id = m.client_id
                    AND d.engagement_id = m.engagement_id AND d.id = m.dataset_id
                  LEFT JOIN client_chart_versions c ON c.firm_id = m.firm_id AND c.client_id = m.client_id
                    AND c.status = 'APPROVED'
                    AND to_char(c.effective_from, 'YYYY-MM-DD') <= m.period_end
                    AND (c.effective_to IS NULL OR to_char(c.effective_to, 'YYYY-MM-DD') >= m.period_start)
                  WHERE m.client_chart_version_id IS NULL
                  GROUP BY m.id, m.firm_id, m.client_id
                )
                INSERT INTO accounting_backfill_quarantines
                  (id, firm_id, client_id, target_kind, target_id, context_kind, candidate_count, reason, created_at)
                SELECT gen_random_uuid(), firm_id, client_id, 'MAPPING_VERSION', id, 'CLIENT_CHART_VERSION',
                  candidate_count,
                  CASE WHEN candidate_count = 0
                    THEN 'No approved client chart is effective for the legacy mapping period.'
                    ELSE 'Multiple approved client charts are effective for the legacy mapping period.' END,
                  statement_timestamp()
                FROM candidates
                ON CONFLICT (firm_id, target_kind, target_id, context_kind) DO NOTHING;

                CREATE OR REPLACE FUNCTION prevent_accounting_backfill_quarantine_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Accounting backfill quarantine evidence is append-only.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_accounting_backfill_quarantine_append_only
                  BEFORE UPDATE OR DELETE ON accounting_backfill_quarantines
                  FOR EACH ROW EXECUTE FUNCTION prevent_accounting_backfill_quarantine_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_backfill_quarantines");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_accounting_backfill_quarantine_mutation();");
        }
    }
}
