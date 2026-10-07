namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class ClientSalesCreditNoteSql
{
  internal const string Up = """
    CREATE OR REPLACE FUNCTION guard_client_generic_control_post() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_invoice_submissions%ROWTYPE; credit_submission client_sales_credit_note_submissions%ROWTYPE;
    BEGIN
      IF NEW.status='POSTED' THEN
        PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
        IF EXISTS (SELECT 1 FROM client_operational_journal_lines l JOIN client_account_role_configurations c
          ON c.account_id=l.client_account_id AND c.firm_id=l.firm_id AND c.client_id=l.client_id
          JOIN client_account_role_decisions d ON d.configuration_id=c.id AND d.firm_id=c.firm_id AND d.client_id=c.client_id
          WHERE l.firm_id=NEW.firm_id AND l.client_id=NEW.client_id AND l.journal_id=NEW.id AND d.decision='APPROVE'
            AND c.role IN ('AR','AP') AND c.effective_from<=NEW.posting_date AND (c.effective_to IS NULL OR c.effective_to>=NEW.posting_date)) THEN
          SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
            AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
          IF s.id IS NOT NULL THEN
            IF NOT EXISTS (SELECT 1 FROM client_sales_invoice_decisions d WHERE d.firm_id=s.firm_id
              AND d.client_id=s.client_id AND d.submission_id=s.id AND d.decision='APPROVE' AND d.actor_user_id<>s.created_by_user_id
              AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
              RAISE EXCEPTION 'Invoice posting requires its independent invoice decision.' USING ERRCODE='23514';
            END IF;
            PERFORM assert_client_sales_submission(s.id,true,false);
          ELSE
            SELECT * INTO credit_submission FROM client_sales_credit_note_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
              AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
            IF credit_submission.id IS NULL THEN
              RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514';
            END IF;
          END IF;
        END IF;
      END IF;
      RETURN NEW;
    END; $fn$;

    CREATE FUNCTION check_client_sales_credit_note_workflow(target_id uuid) RETURNS void LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_credit_note_submissions%ROWTYPE; j client_operational_journals%ROWTYPE;
      original client_sales_invoice_submissions%ROWTYPE; decision_row client_sales_credit_note_decisions%ROWTYPE;
      line_total numeric; original_line_amount numeric; line_row record; open_count bigint; receipt_count bigint;
    BEGIN
      SELECT * INTO s FROM client_sales_credit_note_submissions WHERE id=target_id;
      IF NOT FOUND THEN RETURN; END IF;
      IF s.manifest_hash<>encode(sha256(convert_to(s.manifest_json,'UTF8')),'hex') OR
         s.amount<>(SELECT coalesce(sum(l.amount),0) FROM client_sales_credit_note_lines l WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id) OR
         (SELECT count(*) FROM client_sales_credit_note_lines l WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id)=0 THEN
        RAISE EXCEPTION 'Credit-note amount or retained manifest is inconsistent.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO original FROM client_sales_invoice_submissions WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=s.original_submission_id;
      IF original.id IS NULL OR original.invoice_id<>s.original_invoice_id OR
         NOT EXISTS (SELECT 1 FROM client_sales_invoice_decisions d WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.submission_id=original.id AND d.decision='APPROVE') OR
         NOT EXISTS (SELECT 1 FROM client_sales_invoice_open_items o WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.id=s.original_open_item_id
           AND o.submission_id=original.id AND o.invoice_id=s.original_invoice_id AND o.customer_id=s.customer_id AND o.currency=s.currency) THEN
        RAISE EXCEPTION 'Credit note must reference the exact posted client invoice and receivable.' USING ERRCODE='23514';
      END IF;
      FOR line_row IN SELECT l.original_line_number FROM client_sales_credit_note_lines l
        WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id LOOP
        SELECT (source_line->>'Net')::numeric INTO original_line_amount
          FROM client_sales_invoice_drafts draft CROSS JOIN LATERAL jsonb_array_elements(draft.snapshot_json::jsonb->'Lines') source_line
          WHERE draft.firm_id=s.firm_id AND draft.client_id=s.client_id AND draft.id=original.draft_id
            AND (source_line->>'LineNumber')::integer=line_row.original_line_number
            AND (source_line->>'AccountId')::uuid=(SELECT l.revenue_account_id FROM client_sales_credit_note_lines l
              WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id AND l.original_line_number=line_row.original_line_number)
            AND source_line->>'AccountCode'=(SELECT l.revenue_account_code FROM client_sales_credit_note_lines l
              WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id AND l.original_line_number=line_row.original_line_number);
        SELECT coalesce(sum(l.amount),0) INTO line_total FROM client_sales_credit_note_lines l
          JOIN client_sales_credit_note_submissions cs ON cs.firm_id=l.firm_id AND cs.client_id=l.client_id AND cs.id=l.submission_id
          JOIN client_sales_credit_note_decisions cd ON cd.firm_id=cs.firm_id AND cd.client_id=cs.client_id AND cd.submission_id=cs.id
          WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND cs.original_submission_id=s.original_submission_id
            AND l.original_line_number=line_row.original_line_number AND cd.decision='APPROVE';
        IF original_line_amount IS NULL OR line_total>original_line_amount THEN
          RAISE EXCEPTION 'Cumulative approved credit exceeds its original invoice line.' USING ERRCODE='23514';
        END IF;
      END LOOP;
      SELECT * INTO j FROM client_operational_journals WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=s.journal_id;
      IF j.id IS NULL OR j.revision<s.journal_submitted_revision OR j.currency<>s.currency THEN
        RAISE EXCEPTION 'Credit-note journal lineage is invalid.' USING ERRCODE='23514';
      END IF;
      SELECT count(*) INTO open_count FROM client_sales_credit_note_open_items WHERE firm_id=s.firm_id AND client_id=s.client_id AND submission_id=s.id;
      SELECT count(*) INTO receipt_count FROM client_operational_posting_receipts WHERE firm_id=s.firm_id AND client_id=s.client_id
        AND journal_id=s.journal_id AND submitted_revision=s.journal_submitted_revision;
      SELECT * INTO decision_row FROM client_sales_credit_note_decisions WHERE firm_id=s.firm_id AND client_id=s.client_id AND submission_id=s.id;
      IF NOT FOUND THEN
        IF j.status<>'SUBMITTED' OR open_count<>0 OR receipt_count<>0 THEN
          RAISE EXCEPTION 'Pending credit note cannot be posted or create an open credit.' USING ERRCODE='23514';
        END IF;
      ELSIF decision_row.actor_user_id=s.created_by_user_id OR decision_row.intent_hash !~ '^[a-f0-9]{64}$' OR
            decision_row.preview_digest !~ '^[a-f0-9]{64}$' OR length(trim(decision_row.reason))=0 THEN
        RAISE EXCEPTION 'Credit-note decision is not independently evidenced.' USING ERRCODE='23514';
      ELSIF decision_row.decision='APPROVE' THEN
        IF j.status<>'POSTED' OR j.posted_by_user_id<>decision_row.actor_user_id OR open_count<>1 OR receipt_count<>1 OR
           NOT EXISTS (SELECT 1 FROM client_sales_credit_note_open_items o WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id
             AND o.submission_id=s.id AND o.credit_note_id=s.credit_note_id AND o.original_invoice_id=s.original_invoice_id
             AND o.journal_id=s.journal_id AND o.customer_id=s.customer_id AND o.currency=s.currency AND o.direction='CREDIT' AND o.original_amount=s.amount) THEN
          RAISE EXCEPTION 'Approved credit note requires a posted journal, posting receipt, and matching unapplied customer credit.' USING ERRCODE='23514';
        END IF;
      ELSIF decision_row.decision='RETURN' AND (j.status<>'RETURNED' OR open_count<>0 OR receipt_count<>0) THEN
        RAISE EXCEPTION 'Returned credit note cannot post or create an open credit.' USING ERRCODE='23514';
      END IF;
    END; $fn$;

    CREATE FUNCTION trigger_check_client_sales_credit_note_workflow() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE target_id uuid;
    BEGIN
      IF TG_TABLE_NAME='client_sales_credit_note_submissions' THEN target_id:=CASE WHEN TG_OP='DELETE' THEN OLD.id ELSE NEW.id END;
      ELSE target_id:=CASE WHEN TG_OP='DELETE' THEN OLD.submission_id ELSE NEW.submission_id END; END IF;
      PERFORM check_client_sales_credit_note_workflow(target_id); RETURN NULL;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER client_sales_credit_submission_complete AFTER INSERT OR UPDATE ON client_sales_credit_note_submissions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_sales_credit_note_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_credit_decision_complete AFTER INSERT ON client_sales_credit_note_decisions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_sales_credit_note_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_credit_lines_complete AFTER INSERT ON client_sales_credit_note_lines
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_sales_credit_note_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_credit_open_item_complete AFTER INSERT ON client_sales_credit_note_open_items
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_sales_credit_note_workflow();

    CREATE FUNCTION immutable_client_sales_credit_note() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN RAISE EXCEPTION 'Client sales credit-note history is immutable.' USING ERRCODE='23514'; END; $fn$;
    CREATE TRIGGER immutable_client_sales_credit_submission BEFORE UPDATE OR DELETE ON client_sales_credit_note_submissions
      FOR EACH ROW EXECUTE FUNCTION immutable_client_sales_credit_note();
    CREATE TRIGGER immutable_client_sales_credit_decision BEFORE UPDATE OR DELETE ON client_sales_credit_note_decisions
      FOR EACH ROW EXECUTE FUNCTION immutable_client_sales_credit_note();
    CREATE TRIGGER immutable_client_sales_credit_line BEFORE UPDATE OR DELETE ON client_sales_credit_note_lines
      FOR EACH ROW EXECUTE FUNCTION immutable_client_sales_credit_note();
    CREATE TRIGGER immutable_client_sales_credit_open_item BEFORE UPDATE OR DELETE ON client_sales_credit_note_open_items
      FOR EACH ROW EXECUTE FUNCTION immutable_client_sales_credit_note();
    """;

  internal const string Down = """
    DROP TRIGGER immutable_client_sales_credit_open_item ON client_sales_credit_note_open_items;
    DROP TRIGGER immutable_client_sales_credit_line ON client_sales_credit_note_lines;
    DROP TRIGGER immutable_client_sales_credit_decision ON client_sales_credit_note_decisions;
    DROP TRIGGER immutable_client_sales_credit_submission ON client_sales_credit_note_submissions;
    DROP TRIGGER client_sales_credit_open_item_complete ON client_sales_credit_note_open_items;
    DROP TRIGGER client_sales_credit_lines_complete ON client_sales_credit_note_lines;
    DROP TRIGGER client_sales_credit_decision_complete ON client_sales_credit_note_decisions;
    DROP TRIGGER client_sales_credit_submission_complete ON client_sales_credit_note_submissions;
    DROP FUNCTION immutable_client_sales_credit_note();
    DROP FUNCTION trigger_check_client_sales_credit_note_workflow();
    DROP FUNCTION check_client_sales_credit_note_workflow(uuid);
    CREATE OR REPLACE FUNCTION guard_client_generic_control_post() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_invoice_submissions%ROWTYPE;
    BEGIN
      IF NEW.status='POSTED' THEN
        PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
        IF EXISTS (SELECT 1 FROM client_operational_journal_lines l JOIN client_account_role_configurations c
          ON c.account_id=l.client_account_id AND c.firm_id=l.firm_id AND c.client_id=l.client_id
          JOIN client_account_role_decisions d ON d.configuration_id=c.id AND d.firm_id=c.firm_id AND d.client_id=c.client_id
          WHERE l.firm_id=NEW.firm_id AND l.client_id=NEW.client_id AND l.journal_id=NEW.id AND d.decision='APPROVE'
            AND c.role IN ('AR','AP') AND c.effective_from<=NEW.posting_date AND (c.effective_to IS NULL OR c.effective_to>=NEW.posting_date)) THEN
          SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
            AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
          IF s.id IS NULL OR NOT EXISTS (SELECT 1 FROM client_sales_invoice_decisions d WHERE d.firm_id=s.firm_id
            AND d.client_id=s.client_id AND d.submission_id=s.id AND d.decision='APPROVE' AND d.actor_user_id<>s.created_by_user_id
            AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
            RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514';
          END IF;
          PERFORM assert_client_sales_submission(s.id,true,false);
        END IF;
      END IF;
      RETURN NEW;
    END; $fn$;
    """;
}
