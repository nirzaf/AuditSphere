namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class ClientPurchaseInvoiceSql
{
  internal const string Up = """
    CREATE OR REPLACE FUNCTION guard_client_generic_control_post() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_invoice_submissions%ROWTYPE; credit_submission client_sales_credit_note_submissions%ROWTYPE;
      purchase client_purchase_invoice_submissions%ROWTYPE;
    BEGIN
      IF NEW.status='POSTED' THEN
        PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
        IF EXISTS (SELECT 1 FROM client_operational_journal_lines l JOIN client_account_role_configurations cfg
          ON cfg.account_id=l.client_account_id AND cfg.firm_id=l.firm_id AND cfg.client_id=l.client_id
          JOIN client_account_role_decisions rd ON rd.configuration_id=cfg.id AND rd.firm_id=cfg.firm_id AND rd.client_id=cfg.client_id
          WHERE l.firm_id=NEW.firm_id AND l.client_id=NEW.client_id AND l.journal_id=NEW.id AND rd.decision='APPROVE'
            AND cfg.role IN ('AR','AP') AND cfg.effective_from<=NEW.posting_date AND (cfg.effective_to IS NULL OR cfg.effective_to>=NEW.posting_date)) THEN
          SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
            AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
          IF s.id IS NOT NULL THEN
            IF NOT EXISTS (SELECT 1 FROM client_sales_invoice_decisions d WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id
              AND d.submission_id=s.id AND d.decision='APPROVE' AND d.actor_user_id<>s.created_by_user_id
              AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
              RAISE EXCEPTION 'Invoice posting requires its independent invoice decision.' USING ERRCODE='23514';
            END IF;
            PERFORM assert_client_sales_submission(s.id,true,false);
          ELSE
            SELECT * INTO credit_submission FROM client_sales_credit_note_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
              AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
            IF credit_submission.id IS NOT NULL THEN
              IF NOT EXISTS (SELECT 1 FROM client_sales_credit_note_decisions d WHERE d.firm_id=credit_submission.firm_id AND d.client_id=credit_submission.client_id
                AND d.submission_id=credit_submission.id AND d.decision='APPROVE' AND d.actor_user_id<>credit_submission.created_by_user_id
                AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
                RAISE EXCEPTION 'Credit posting requires its independent credit decision.' USING ERRCODE='23514';
              END IF;
              -- Deferred credit submission/decision/open-item checks validate the final transition atomically.
              -- This BEFORE journal trigger must not inspect the still-SUBMITTED stored row.
            ELSE
              SELECT * INTO purchase FROM client_purchase_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
                AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
              IF purchase.id IS NULL THEN
                RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514';
              END IF;
            END IF;
          END IF;
        END IF;
      END IF;
      RETURN NEW;
    END; $fn$;

    CREATE FUNCTION check_client_purchase_invoice_workflow(target_id uuid) RETURNS void LANGUAGE plpgsql AS $fn$
    DECLARE p client_purchase_invoice_submissions%ROWTYPE; d client_purchase_invoice_drafts%ROWTYPE;
      j client_operational_journals%ROWTYPE; decision_row client_purchase_invoice_decisions%ROWTYPE;
      open_count bigint; receipt_count bigint; debit_total numeric; credit_total numeric; line_count bigint;
      duplicate_count bigint; account_row record; manifest jsonb;
    BEGIN
      SELECT * INTO p FROM client_purchase_invoice_submissions WHERE id=target_id;
      IF NOT FOUND THEN RETURN; END IF;
      IF p.manifest_hash<>encode(sha256(convert_to(p.manifest_json,'UTF8')),'hex') THEN
        RAISE EXCEPTION 'Supplier invoice manifest identity is invalid.' USING ERRCODE='23514';
      END IF;
      manifest:=p.manifest_json::jsonb;
      SELECT * INTO d FROM client_purchase_invoice_drafts WHERE firm_id=p.firm_id AND client_id=p.client_id AND id=p.draft_id;
      IF d.id IS NULL OR d.invoice_id<>p.invoice_id OR d.supplier_id<>p.supplier_id OR d.supplier_invoice_reference<>p.supplier_invoice_reference OR
         d.normalized_supplier_reference<>p.normalized_supplier_reference OR manifest->>'Version'<>'client-purchase-submission-v1' OR
         manifest->>'InvoiceId'<>p.invoice_id::text OR manifest->>'DraftId'<>p.draft_id::text OR manifest->>'SupplierId'<>p.supplier_id::text OR
         manifest->>'NormalizedSupplierReference'<>p.normalized_supplier_reference OR manifest->>'Currency'<>d.currency THEN
        RAISE EXCEPTION 'Supplier invoice submission differs from its client draft.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO j FROM client_operational_journals WHERE firm_id=p.firm_id AND client_id=p.client_id AND id=p.journal_id;
      IF j.id IS NULL OR j.revision<p.journal_submitted_revision OR j.currency<>d.currency THEN
        RAISE EXCEPTION 'Supplier invoice journal lineage is invalid.' USING ERRCODE='23514';
      END IF;
      SELECT count(*), coalesce(sum(debit),0), coalesce(sum(credit),0) INTO line_count,debit_total,credit_total
        FROM client_operational_journal_lines WHERE firm_id=p.firm_id AND client_id=p.client_id AND journal_id=p.journal_id;
      IF line_count NOT BETWEEN 2 AND 100 OR line_count<>jsonb_array_length(manifest->'Lines') OR debit_total<>d.gross_amount OR credit_total<>d.gross_amount OR
         EXISTS (SELECT 1 FROM jsonb_array_elements(manifest->'Lines') ml LEFT JOIN client_operational_journal_lines jl
           ON jl.firm_id=p.firm_id AND jl.client_id=p.client_id AND jl.journal_id=p.journal_id AND jl.line_number=(ml->>'LineNumber')::integer
           WHERE jl.id IS NULL OR jl.client_account_id<>(ml->>'AccountId')::uuid OR jl.account_code<>ml->>'AccountCode' OR
             jl.account_name<>ml->>'AccountName' OR jl.debit<>(ml->>'Debit')::numeric OR jl.credit<>(ml->>'Credit')::numeric) THEN
        RAISE EXCEPTION 'Supplier invoice journal does not equal the stated gross amount.' USING ERRCODE='23514';
      END IF;
      SELECT count(*) INTO open_count FROM client_purchase_invoice_open_items WHERE firm_id=p.firm_id AND client_id=p.client_id AND submission_id=p.id;
      SELECT count(*) INTO receipt_count FROM client_operational_posting_receipts WHERE firm_id=p.firm_id AND client_id=p.client_id
        AND journal_id=p.journal_id AND submitted_revision=p.journal_submitted_revision;
      SELECT * INTO decision_row FROM client_purchase_invoice_decisions WHERE firm_id=p.firm_id AND client_id=p.client_id AND submission_id=p.id;
      IF NOT FOUND THEN
        IF j.status<>'SUBMITTED' OR open_count<>0 OR receipt_count<>0 THEN
          RAISE EXCEPTION 'Pending supplier invoice cannot be posted or create an AP open item.' USING ERRCODE='23514';
        END IF;
      ELSIF decision_row.actor_user_id=p.created_by_user_id OR decision_row.intent_hash !~ '^[a-f0-9]{64}$' OR
         decision_row.preview_digest !~ '^[a-f0-9]{64}$' OR length(trim(decision_row.reason))=0 THEN
        RAISE EXCEPTION 'Supplier invoice decision is not independently evidenced.' USING ERRCODE='23514';
      ELSIF decision_row.decision='APPROVE' THEN
        SELECT count(*) INTO duplicate_count FROM client_purchase_invoice_drafts duplicate
          WHERE duplicate.firm_id=p.firm_id AND duplicate.client_id=p.client_id AND duplicate.supplier_id=p.supplier_id
            AND duplicate.normalized_supplier_reference=p.normalized_supplier_reference AND duplicate.invoice_id<>p.invoice_id
            AND duplicate.revision=(SELECT max(latest.revision) FROM client_purchase_invoice_drafts latest
              WHERE latest.firm_id=duplicate.firm_id AND latest.client_id=duplicate.client_id AND latest.invoice_id=duplicate.invoice_id);
        IF j.status<>'POSTED' OR j.posted_by_user_id<>decision_row.actor_user_id OR open_count<>1 OR receipt_count<>1 OR
           duplicate_count>0 AND length(trim(decision_row.duplicate_resolution_reason))=0 OR
           NOT EXISTS (SELECT 1 FROM client_purchase_invoice_open_items o WHERE o.firm_id=p.firm_id AND o.client_id=p.client_id
             AND o.submission_id=p.id AND o.invoice_id=p.invoice_id AND o.journal_id=p.journal_id AND o.supplier_id=p.supplier_id
             AND o.currency=d.currency AND o.original_amount=d.gross_amount AND o.due_date=d.due_date) THEN
          RAISE EXCEPTION 'Approved supplier invoice needs independent review, duplicate rationale, posting receipt and matching AP origin.' USING ERRCODE='23514';
        END IF;
      ELSIF decision_row.decision='RETURN' AND (j.status<>'RETURNED' OR open_count<>0 OR receipt_count<>0) THEN
        RAISE EXCEPTION 'Returned supplier invoice cannot post or create an AP origin.' USING ERRCODE='23514';
      END IF;
    END; $fn$;

    CREATE FUNCTION trigger_check_client_purchase_invoice_workflow() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE target_id uuid;
    BEGIN
      IF TG_TABLE_NAME='client_purchase_invoice_submissions' THEN target_id:=CASE WHEN TG_OP='DELETE' THEN OLD.id ELSE NEW.id END;
      ELSE target_id:=CASE WHEN TG_OP='DELETE' THEN OLD.submission_id ELSE NEW.submission_id END; END IF;
      PERFORM check_client_purchase_invoice_workflow(target_id); RETURN NULL;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER client_purchase_submission_complete AFTER INSERT OR UPDATE ON client_purchase_invoice_submissions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_purchase_invoice_workflow();
    CREATE CONSTRAINT TRIGGER client_purchase_decision_complete AFTER INSERT ON client_purchase_invoice_decisions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_purchase_invoice_workflow();
    CREATE CONSTRAINT TRIGGER client_purchase_open_item_complete AFTER INSERT ON client_purchase_invoice_open_items
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_purchase_invoice_workflow();

    CREATE FUNCTION immutable_client_purchase_invoice() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN RAISE EXCEPTION 'Client purchase invoice history is immutable.' USING ERRCODE='23514'; END; $fn$;
    CREATE TRIGGER immutable_client_purchase_draft BEFORE UPDATE OR DELETE ON client_purchase_invoice_drafts
      FOR EACH ROW EXECUTE FUNCTION immutable_client_purchase_invoice();
    CREATE TRIGGER immutable_client_purchase_submission BEFORE UPDATE OR DELETE ON client_purchase_invoice_submissions
      FOR EACH ROW EXECUTE FUNCTION immutable_client_purchase_invoice();
    CREATE TRIGGER immutable_client_purchase_decision BEFORE UPDATE OR DELETE ON client_purchase_invoice_decisions
      FOR EACH ROW EXECUTE FUNCTION immutable_client_purchase_invoice();
    CREATE TRIGGER immutable_client_purchase_open_item BEFORE UPDATE OR DELETE ON client_purchase_invoice_open_items
      FOR EACH ROW EXECUTE FUNCTION immutable_client_purchase_invoice();
    """;

  internal const string Down = """
    DROP TRIGGER immutable_client_purchase_open_item ON client_purchase_invoice_open_items;
    DROP TRIGGER immutable_client_purchase_decision ON client_purchase_invoice_decisions;
    DROP TRIGGER immutable_client_purchase_submission ON client_purchase_invoice_submissions;
    DROP TRIGGER immutable_client_purchase_draft ON client_purchase_invoice_drafts;
    DROP TRIGGER client_purchase_open_item_complete ON client_purchase_invoice_open_items;
    DROP TRIGGER client_purchase_decision_complete ON client_purchase_invoice_decisions;
    DROP TRIGGER client_purchase_submission_complete ON client_purchase_invoice_submissions;
    DROP FUNCTION immutable_client_purchase_invoice();
    DROP FUNCTION trigger_check_client_purchase_invoice_workflow();
    DROP FUNCTION check_client_purchase_invoice_workflow(uuid);
    CREATE OR REPLACE FUNCTION guard_client_generic_control_post() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_invoice_submissions%ROWTYPE; credit_submission client_sales_credit_note_submissions%ROWTYPE;
    BEGIN
      IF NEW.status='POSTED' THEN
        PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
        IF EXISTS (SELECT 1 FROM client_operational_journal_lines l JOIN client_account_role_configurations cfg
          ON cfg.account_id=l.client_account_id AND cfg.firm_id=l.firm_id AND cfg.client_id=l.client_id
          JOIN client_account_role_decisions rd ON rd.configuration_id=cfg.id AND rd.firm_id=cfg.firm_id AND rd.client_id=cfg.client_id
          WHERE l.firm_id=NEW.firm_id AND l.client_id=NEW.client_id AND l.journal_id=NEW.id AND rd.decision='APPROVE'
            AND cfg.role IN ('AR','AP') AND cfg.effective_from<=NEW.posting_date AND (cfg.effective_to IS NULL OR cfg.effective_to>=NEW.posting_date)) THEN
          SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
            AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
          IF s.id IS NOT NULL THEN
            IF NOT EXISTS (SELECT 1 FROM client_sales_invoice_decisions d WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.submission_id=s.id AND d.decision='APPROVE' AND d.actor_user_id<>s.created_by_user_id AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN RAISE EXCEPTION 'Invoice posting requires independent review.' USING ERRCODE='23514'; END IF;
            PERFORM assert_client_sales_submission(s.id,true,false);
          ELSE
            SELECT * INTO credit_submission FROM client_sales_credit_note_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
            IF credit_submission.id IS NOT NULL THEN
              IF NOT EXISTS (SELECT 1 FROM client_sales_credit_note_decisions d WHERE d.firm_id=credit_submission.firm_id AND d.client_id=credit_submission.client_id AND d.submission_id=credit_submission.id AND d.decision='APPROVE' AND d.actor_user_id<>credit_submission.created_by_user_id AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN RAISE EXCEPTION 'Credit posting requires independent review.' USING ERRCODE='23514'; END IF;
              PERFORM check_client_sales_credit_note_workflow(credit_submission.id);
            ELSE
              RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514';
            END IF;
          END IF;
        END IF;
      END IF;
      RETURN NEW;
    END; $fn$;
    """;
}
