namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class ConsolidationMatchReservationSql
{
  internal const string Preflight = """
    DO $f01$
    BEGIN
      IF EXISTS (
        SELECT 1
        FROM consolidation_journals j
        JOIN consolidation_journal_lines jl
          ON jl.consolidation_journal_id = j.id
        WHERE j.status = 'APPROVED'
          AND EXISTS (
            SELECT 1 FROM consolidation_journal_lines linked
            WHERE linked.firm_id = j.firm_id AND linked.group_id = j.group_id
              AND linked.scope_version_id = j.scope_version_id
              AND linked.consolidation_journal_id = j.id AND linked.intercompany_match_id IS NOT NULL)
          AND (
            j.approved_by_user_id IS NULL OR j.approved_at IS NULL
            OR j.created_by_user_id = j.approved_by_user_id
            OR jl.group_id IS DISTINCT FROM j.group_id
            OR jl.scope_version_id IS DISTINCT FROM j.scope_version_id
            OR jl.currency IS DISTINCT FROM j.currency
            OR jl.debit < 0 OR jl.credit < 0 OR (jl.debit > 0 AND jl.credit > 0)
            OR NOT EXISTS (
              SELECT 1 FROM consolidation_scope_versions s
              WHERE s.firm_id = j.firm_id AND s.group_id = j.group_id AND s.id = j.scope_version_id
                AND s.status = 'APPROVED' AND s.reporting_currency = j.currency
                AND s.approved_by_user_id IS NOT NULL AND s.approved_at IS NOT NULL
                AND s.approved_by_user_id <> s.created_by_user_id)
            OR (jl.intercompany_match_id IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM intercompany_matches m
              WHERE m.firm_id = jl.firm_id AND m.group_id = jl.group_id
                AND m.scope_version_id = jl.scope_version_id AND m.id = jl.intercompany_match_id
                AND m.status = 'APPROVED' AND NOT m.outside_perimeter_review AND m.currency = jl.currency
                AND m.reviewed_by_user_id IS NOT NULL AND m.reviewed_at IS NOT NULL
                AND m.reviewed_by_user_id <> m.created_by_user_id))
          )
      ) THEN
        RAISE EXCEPTION 'F01 preflight blocked: approved linked journals have invalid scope, match, approval, or line evidence; reviewed correction is required.'
          USING ERRCODE = 'P0001';
      END IF;

      IF EXISTS (
        SELECT 1
        FROM consolidation_journals j
        JOIN consolidation_journal_lines jl
          ON jl.consolidation_journal_id = j.id
        WHERE j.status = 'APPROVED'
          AND EXISTS (
            SELECT 1 FROM consolidation_journal_lines linked
            WHERE linked.firm_id = j.firm_id AND linked.group_id = j.group_id
              AND linked.scope_version_id = j.scope_version_id
              AND linked.consolidation_journal_id = j.id AND linked.intercompany_match_id IS NOT NULL)
        GROUP BY j.id, j.total_debits, j.total_credits_abs
        HAVING count(*) < 2 OR sum(jl.debit) IS DISTINCT FROM j.total_debits
          OR sum(jl.credit) IS DISTINCT FROM j.total_credits_abs
          OR j.total_debits IS DISTINCT FROM j.total_credits_abs
      ) THEN
        RAISE EXCEPTION 'F01 preflight blocked: approved linked journal balance evidence is contradictory; reviewed correction is required.'
          USING ERRCODE = 'P0001';
      END IF;

      IF EXISTS (
        SELECT jl.firm_id, jl.group_id, jl.scope_version_id, jl.intercompany_match_id
        FROM consolidation_journals j
        JOIN consolidation_journal_lines jl
          ON jl.firm_id = j.firm_id AND jl.group_id = j.group_id
            AND jl.scope_version_id = j.scope_version_id AND jl.consolidation_journal_id = j.id
        WHERE j.status = 'APPROVED' AND jl.intercompany_match_id IS NOT NULL
        GROUP BY jl.firm_id, jl.group_id, jl.scope_version_id, jl.intercompany_match_id
        HAVING count(DISTINCT j.id) > 1
      ) THEN
        RAISE EXCEPTION 'F01 preflight blocked: an intercompany match has multiple approved journal owners; reviewed correction is required.'
          USING ERRCODE = 'P0001';
      END IF;
    END;
    $f01$;
    """;

  internal const string Backfill = """
    INSERT INTO consolidation_match_reservations
      (firm_id, group_id, scope_version_id, intercompany_match_id, consolidation_journal_id)
    SELECT DISTINCT j.firm_id, j.group_id, j.scope_version_id, jl.intercompany_match_id, j.id
    FROM consolidation_journals j
    JOIN consolidation_journal_lines jl
      ON jl.firm_id = j.firm_id AND jl.group_id = j.group_id
        AND jl.scope_version_id = j.scope_version_id AND jl.consolidation_journal_id = j.id
    WHERE j.status = 'APPROVED' AND jl.intercompany_match_id IS NOT NULL;
    """;
}
