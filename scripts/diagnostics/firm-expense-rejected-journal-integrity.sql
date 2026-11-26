-- Read-only rollout diagnostic. Review every returned row before planning remediation.
BEGIN READ ONLY;
SET LOCAL statement_timeout = '5s';

SELECT
  e.firm_id,
  e.id AS expense_id,
  e.status AS expense_status,
  e.journal_id,
  j.status AS journal_status,
  e.posting_id,
  p.period_code,
  CASE
    WHEN j.status = 'POSTED' THEN 'POSTED_REQUIRES_EXISTING_REVERSAL_REVIEW'
    ELSE 'LIVE_JOURNAL_REQUIRES_REVIEWED_REMEDIATION_PLAN'
  END AS disposition
FROM firm_expenses AS e
JOIN firm_journals AS j
  ON j.firm_id = e.firm_id
 AND j.id = e.journal_id
JOIN firm_periods AS p
  ON p.firm_id = j.firm_id
 AND p.id = j.period_id
WHERE e.status = 'REJECTED'
  AND j.status IN ('DRAFT', 'REVIEW_REQUIRED', 'APPROVED', 'POSTED')
ORDER BY e.firm_id, e.expense_date, e.id;

COMMIT;
