namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

/// <summary>Revalidates exact invoice journal content while ignoring database-assigned posting metadata.</summary>
internal static class ClientOperationalPostingSnapshotGuardSql
{
  internal const string Up = """
CREATE OR REPLACE FUNCTION check_client_sales_invoice_workflow() RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE invoice_key uuid; journal_key uuid; scope_firm uuid; scope_client uuid;
  s client_sales_invoice_submissions%ROWTYPE; decision client_sales_invoice_decisions%ROWTYPE;
  draft client_sales_invoice_drafts%ROWTYPE; journal client_operational_journals%ROWTYPE; captured jsonb;
BEGIN
  IF TG_TABLE_NAME='client_sales_invoice_submissions' THEN
    invoice_key:=NEW.invoice_id; scope_firm:=NEW.firm_id; scope_client:=NEW.client_id;
    PERFORM assert_client_sales_submission(NEW.id,true,true);
  ELSIF TG_TABLE_NAME='client_sales_invoice_drafts' THEN
    invoice_key:=NEW.invoice_id; scope_firm:=NEW.firm_id; scope_client:=NEW.client_id;
  ELSIF TG_TABLE_NAME='client_sales_invoice_decisions' THEN
    SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.submission_id;
    invoice_key:=s.invoice_id; scope_firm:=s.firm_id; scope_client:=s.client_id;
  ELSIF TG_TABLE_NAME='client_sales_invoice_open_items' THEN
    SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.submission_id;
    invoice_key:=s.invoice_id; scope_firm:=s.firm_id; scope_client:=s.client_id;
    IF NEW.invoice_id<>s.invoice_id OR NEW.journal_id<>s.journal_id THEN
      RAISE EXCEPTION 'Invoice open item cannot change documentary or ledger identity.' USING ERRCODE='23514';
    END IF;
  ELSE
    IF TG_TABLE_NAME='client_operational_journals' THEN journal_key:=CASE WHEN TG_OP='DELETE' THEN OLD.id ELSE NEW.id END;
    ELSE journal_key:=CASE WHEN TG_OP='DELETE' THEN OLD.journal_id ELSE NEW.journal_id END; END IF;
    SELECT * INTO s FROM client_sales_invoice_submissions WHERE journal_id=journal_key ORDER BY draft_revision DESC LIMIT 1;
    IF s.id IS NULL THEN
      IF EXISTS (SELECT 1 FROM client_operational_journals j WHERE j.id=journal_key AND j.status IN ('SUBMITTED','POSTED')
        AND j.journal_number LIKE 'CLIENT-SALES-%') THEN
        RAISE EXCEPTION 'A client sales journal requires a proved invoice submission in the same transaction.' USING ERRCODE='23514';
      END IF;
      RETURN NULL;
    END IF;
    invoice_key:=s.invoice_id; scope_firm:=s.firm_id; scope_client:=s.client_id;
  END IF;
  SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=scope_firm AND client_id=scope_client
    AND invoice_id=invoice_key ORDER BY draft_revision DESC LIMIT 1;
  IF s.id IS NULL THEN RETURN NULL; END IF;
  PERFORM assert_client_sales_submission(s.id,false,false);
  SELECT * INTO draft FROM client_sales_invoice_drafts WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=s.draft_id;
  SELECT * INTO journal FROM client_operational_journals WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=s.journal_id;
  SELECT * INTO decision FROM client_sales_invoice_decisions WHERE firm_id=s.firm_id AND client_id=s.client_id AND submission_id=s.id;
  SELECT snapshot_json INTO captured FROM client_operational_journal_snapshots WHERE firm_id=s.firm_id AND client_id=s.client_id
    AND journal_id=s.journal_id AND journal_revision=s.journal_submitted_revision;
  IF journal.id IS NULL OR journal.status NOT IN ('SUBMITTED','RETURNED','POSTED')
    OR (to_jsonb(journal)-'status'-'revision'-'posted_by_user_id'-'posted_at'-'posting_sequence') IS DISTINCT FROM
      ((captured->'journal')-'status'-'revision'-'posted_by_user_id'-'posted_at'-'posting_sequence')
    OR (SELECT jsonb_agg(to_jsonb(l) ORDER BY l.line_number) FROM client_operational_journal_lines l
      WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.journal_id=s.journal_id) IS DISTINCT FROM captured->'lines'
    OR (journal.status='SUBMITTED' AND (journal.revision<>s.journal_submitted_revision OR decision.id IS NOT NULL))
    OR (journal.status IN ('RETURNED','POSTED') AND (journal.revision<>s.journal_submitted_revision+1 OR decision.id IS NULL
      OR decision.decision IS DISTINCT FROM CASE WHEN journal.status='POSTED' THEN 'APPROVE' ELSE 'RETURN' END)) THEN
    RAISE EXCEPTION 'Invoice lifecycle requires the exact retained journal content and reviewed transition.' USING ERRCODE='23514';
  END IF;
  IF decision.id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=s.firm_id
    AND d.client_id=s.client_id AND d.journal_id=s.journal_id AND d.journal_revision=s.journal_submitted_revision
    AND d.decision=decision.decision AND d.actor_user_id=decision.actor_user_id AND d.reason=decision.reason
    AND d.created_at=decision.created_at AND d.actor_user_id<>s.created_by_user_id) THEN
    RAISE EXCEPTION 'Invoice and native decisions must record the same independent exact review.' USING ERRCODE='23514';
  END IF;
  IF journal.status='SUBMITTED' AND EXISTS (SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=s.firm_id
    AND d.client_id=s.client_id AND d.journal_id=s.journal_id AND d.journal_revision=s.journal_submitted_revision) THEN
    RAISE EXCEPTION 'An invoice review cannot bypass its documentary decision and transition.' USING ERRCODE='23514';
  END IF;
  IF journal.status='POSTED' THEN
    IF journal.posted_by_user_id IS DISTINCT FROM decision.actor_user_id OR journal.posted_at IS DISTINCT FROM decision.created_at
      OR NOT EXISTS (SELECT 1 FROM client_operational_posting_receipts r WHERE r.firm_id=s.firm_id AND r.client_id=s.client_id
        AND r.journal_id=s.journal_id AND r.command_id=decision.command_id AND r.actor_user_id=decision.actor_user_id
        AND r.submitted_revision=s.journal_submitted_revision AND r.posted_revision=journal.revision
        AND r.intent_hash=decision.intent_hash AND r.preview_digest=decision.preview_digest AND r.recorded_at=journal.posted_at)
      OR NOT EXISTS (SELECT 1 FROM client_sales_invoice_open_items o WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id
        AND o.invoice_id=s.invoice_id AND o.submission_id=s.id AND o.journal_id=s.journal_id AND o.customer_id=draft.customer_id
        AND o.currency=draft.currency AND o.original_amount=draft.gross_amount
        AND o.due_date=(draft.snapshot_json::jsonb->>'DueDate')::date AND o.posted_at=journal.posted_at) THEN
      RAISE EXCEPTION 'Invoice posting, exact receipt and original AR open item must commit atomically.' USING ERRCODE='23514';
    END IF;
  ELSIF EXISTS (SELECT 1 FROM client_sales_invoice_open_items o WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.invoice_id=s.invoice_id)
    OR EXISTS (SELECT 1 FROM client_operational_posting_receipts r WHERE r.firm_id=s.firm_id AND r.client_id=s.client_id AND r.journal_id=s.journal_id) THEN
    RAISE EXCEPTION 'Unposted invoices cannot claim an open item or posting receipt.' USING ERRCODE='23514';
  END IF;
  RETURN NULL;
END; $fn$;
""";

  internal static readonly string Down = Up.Replace("-'posting_sequence'", "", StringComparison.Ordinal);
}
