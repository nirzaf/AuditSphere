namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class ClientPurchaseCreditNoteSql
{
  internal const string Up = """
    CREATE OR REPLACE FUNCTION guard_client_generic_control_post() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_invoice_submissions%ROWTYPE; sales_credit client_sales_credit_note_submissions%ROWTYPE;
      purchase_credit client_purchase_credit_note_submissions%ROWTYPE; purchase client_purchase_invoice_submissions%ROWTYPE;
    BEGIN
      IF NEW.status='POSTED' THEN
        PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
        IF EXISTS (SELECT 1 FROM client_operational_journal_lines l JOIN client_account_role_configurations cfg
          ON cfg.account_id=l.client_account_id AND cfg.firm_id=l.firm_id AND cfg.client_id=l.client_id
          JOIN client_account_role_decisions rd ON rd.configuration_id=cfg.id AND rd.firm_id=cfg.firm_id AND rd.client_id=cfg.client_id
          WHERE l.firm_id=NEW.firm_id AND l.client_id=NEW.client_id AND l.journal_id=NEW.id AND rd.decision='APPROVE'
            AND cfg.role IN ('AR','AP') AND cfg.effective_from<=NEW.posting_date AND (cfg.effective_to IS NULL OR cfg.effective_to>=NEW.posting_date)) THEN
          SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
          IF s.id IS NOT NULL THEN
            IF NOT EXISTS (SELECT 1 FROM client_sales_invoice_decisions d WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id
              AND d.submission_id=s.id AND d.decision='APPROVE' AND d.actor_user_id<>s.created_by_user_id AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
              RAISE EXCEPTION 'Invoice posting requires its independent invoice decision.' USING ERRCODE='23514';
            END IF;
            PERFORM assert_client_sales_submission(s.id,true,false);
          ELSE
            SELECT * INTO sales_credit FROM client_sales_credit_note_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
            IF sales_credit.id IS NOT NULL THEN
              IF NOT EXISTS (SELECT 1 FROM client_sales_credit_note_decisions d WHERE d.firm_id=sales_credit.firm_id AND d.client_id=sales_credit.client_id
                AND d.submission_id=sales_credit.id AND d.decision='APPROVE' AND d.actor_user_id<>sales_credit.created_by_user_id AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
                RAISE EXCEPTION 'Credit posting requires its independent credit decision.' USING ERRCODE='23514';
              END IF;
            ELSE
              SELECT * INTO purchase_credit FROM client_purchase_credit_note_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
              IF purchase_credit.id IS NOT NULL THEN
                IF NOT EXISTS (SELECT 1 FROM client_purchase_credit_note_decisions d WHERE d.firm_id=purchase_credit.firm_id AND d.client_id=purchase_credit.client_id
                  AND d.submission_id=purchase_credit.id AND d.decision='APPROVE' AND d.actor_user_id<>purchase_credit.created_by_user_id AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
                  RAISE EXCEPTION 'Supplier credit posting requires its exact independent decision.' USING ERRCODE='23514';
                END IF;
              ELSE
                SELECT * INTO purchase FROM client_purchase_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
                IF purchase.id IS NULL THEN RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514'; END IF;
              END IF;
            END IF;
          END IF;
        END IF;
      END IF;
      RETURN NEW;
    END; $fn$;

    CREATE FUNCTION check_client_purchase_credit_note_workflow(target_id uuid) RETURNS void LANGUAGE plpgsql AS $fn$
    DECLARE s client_purchase_credit_note_submissions%ROWTYPE; j client_operational_journals%ROWTYPE;
      decision_row client_purchase_credit_note_decisions%ROWTYPE; manifest jsonb;
      open_count bigint; receipt_count bigint; line_count bigint; debit_total numeric; credit_total numeric;
      original client_purchase_invoice_submissions%ROWTYPE; original_draft client_purchase_invoice_drafts%ROWTYPE;
      original_open client_purchase_invoice_open_items%ROWTYPE; original_manifest jsonb; duplicate_count bigint;
      line_row record; original_amount numeric; cumulative numeric;
    BEGIN
      SELECT * INTO s FROM client_purchase_credit_note_submissions WHERE id=target_id;
      IF NOT FOUND THEN RETURN; END IF;
      IF s.manifest_hash<>encode(sha256(convert_to(s.manifest_json,'UTF8')),'hex') THEN
        RAISE EXCEPTION 'Supplier credit manifest identity is invalid.' USING ERRCODE='23514';
      END IF;
      manifest:=s.manifest_json::jsonb;
      IF manifest->>'Version'<>'client-purchase-credit-v1' OR manifest->>'CreditNoteId'<>s.credit_note_id::text OR
         manifest->>'SupplierId'<>s.supplier_id::text OR manifest->>'Currency'<>s.currency OR (manifest->>'TotalCredit')::numeric<>s.amount OR
         length(trim(manifest->>'Reason'))=0 OR s.journal_submitted_revision<=0 THEN
        RAISE EXCEPTION 'Supplier credit differs from its retained posting intent.' USING ERRCODE='23514';
      END IF;
      IF s.original_invoice_id IS NULL THEN
        IF s.original_submission_id IS NOT NULL OR s.original_open_item_id IS NOT NULL OR length(trim(manifest->>'UnlinkedExceptionRationale'))=0 OR
           EXISTS (SELECT 1 FROM client_purchase_credit_note_lines l WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id AND l.original_line_number IS NOT NULL) THEN
          RAISE EXCEPTION 'Unlinked supplier credit requires an explicit exception rationale and no invented source lines.' USING ERRCODE='23514';
        END IF;
      ELSE
        SELECT * INTO original FROM client_purchase_invoice_submissions WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=s.original_submission_id AND invoice_id=s.original_invoice_id;
        SELECT * INTO original_open FROM client_purchase_invoice_open_items WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=s.original_open_item_id AND invoice_id=s.original_invoice_id;
        SELECT d.* INTO original_draft FROM client_purchase_invoice_drafts d WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.id=original.draft_id AND d.revision=original.draft_revision;
        IF original.id IS NULL OR original.supplier_id<>s.supplier_id OR original_open.id IS NULL OR original_open.supplier_id<>s.supplier_id OR
           original_open.currency<>s.currency OR original.manifest_hash<>(manifest->>'OriginalManifestHash') OR original_draft.id IS NULL OR
           original_draft.snapshot_hash<>encode(sha256(convert_to(original_draft.snapshot_json,'UTF8')),'hex') OR original_draft.tax_amount<>0 OR
           original_draft.gross_amount<>original_draft.net_amount OR
           NOT EXISTS (SELECT 1 FROM client_purchase_invoice_decisions d WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.submission_id=original.id AND d.decision='APPROVE') OR
           NOT EXISTS (SELECT 1 FROM client_operational_journals oj WHERE oj.firm_id=s.firm_id AND oj.client_id=s.client_id AND oj.id=original.journal_id AND oj.status='POSTED') THEN
          RAISE EXCEPTION 'Linked supplier credit requires the exact posted same-supplier untaxed purchase and AP origin.' USING ERRCODE='23514';
        END IF;
        original_manifest:=original.manifest_json::jsonb;
        FOR line_row IN SELECT l.original_line_number,l.amount,l.expense_account_id,l.expense_account_code FROM client_purchase_credit_note_lines l
          WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id LOOP
          SELECT (source_line->>'Gross')::numeric INTO original_amount
            FROM client_purchase_invoice_drafts d CROSS JOIN LATERAL jsonb_array_elements(d.snapshot_json::jsonb->'Lines') source_line
            WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.id=original.draft_id AND (source_line->>'LineNumber')::integer=line_row.original_line_number
              AND (source_line->>'AccountId')::uuid=line_row.expense_account_id AND source_line->>'AccountCode'=line_row.expense_account_code;
          SELECT coalesce(sum(cl.amount),0) INTO cumulative FROM client_purchase_credit_note_lines cl
            JOIN client_purchase_credit_note_submissions cs ON cs.firm_id=cl.firm_id AND cs.client_id=cl.client_id AND cs.id=cl.submission_id
            JOIN client_purchase_credit_note_decisions cd ON cd.firm_id=cs.firm_id AND cd.client_id=cs.client_id AND cd.submission_id=cs.id
            WHERE cs.original_invoice_id=s.original_invoice_id AND cs.id<>s.id AND cl.original_line_number=line_row.original_line_number AND cd.decision='APPROVE';
          IF original_amount IS NULL OR cumulative+line_row.amount>original_amount THEN
            RAISE EXCEPTION 'Cumulative supplier credits cannot exceed an original untaxed purchase line.' USING ERRCODE='23514';
          END IF;
        END LOOP;
      END IF;
      IF (SELECT count(*) FROM client_purchase_credit_note_lines l WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id)=0 OR
         (SELECT coalesce(sum(l.amount),0) FROM client_purchase_credit_note_lines l WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id)<>s.amount OR
         jsonb_array_length(manifest->'Lines')<>(SELECT count(*) FROM client_purchase_credit_note_lines l WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id)+1 THEN
        RAISE EXCEPTION 'Supplier credit line amounts do not equal its exact positive total.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO j FROM client_operational_journals WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=s.journal_id;
      SELECT count(*),coalesce(sum(debit),0),coalesce(sum(credit),0) INTO line_count,debit_total,credit_total FROM client_operational_journal_lines WHERE firm_id=s.firm_id AND client_id=s.client_id AND journal_id=s.journal_id;
      IF j.id IS NULL OR j.currency<>s.currency OR line_count<>jsonb_array_length(manifest->'Lines') OR debit_total<>s.amount OR credit_total<>s.amount OR
         EXISTS (SELECT 1 FROM jsonb_array_elements(manifest->'Lines') ml LEFT JOIN client_operational_journal_lines jl ON jl.firm_id=s.firm_id AND jl.client_id=s.client_id AND jl.journal_id=s.journal_id AND jl.line_number=(ml->>'LineNumber')::integer
           WHERE jl.id IS NULL OR jl.client_account_id<>(ml->>'AccountId')::uuid OR jl.account_code<>ml->>'AccountCode' OR jl.debit<>(ml->>'Debit')::numeric OR jl.credit<>(ml->>'Credit')::numeric) THEN
        RAISE EXCEPTION 'Supplier credit journal differs from its balanced retained manifest.' USING ERRCODE='23514';
      END IF;
      IF line_count<> (SELECT count(*) FROM client_purchase_credit_note_lines l WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id)+1 OR
         EXISTS (SELECT 1 FROM client_purchase_credit_note_lines l LEFT JOIN jsonb_array_elements(manifest->'Lines') ml ON (ml->>'LineNumber')::integer=l.line_number
           WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id AND
             (ml IS NULL OR l.expense_account_id<>(ml->>'AccountId')::uuid OR l.expense_account_code<>ml->>'AccountCode' OR l.amount<>(ml->>'CreditAmount')::numeric OR
              ml->>'Debit'<>'0.000000' OR NULLIF((ml->>'OriginalLineNumber')::integer,0) IS DISTINCT FROM l.original_line_number)) OR
         NOT EXISTS (SELECT 1 FROM client_accounts a JOIN client_account_role_configurations cfg ON cfg.firm_id=a.firm_id AND cfg.client_id=a.client_id AND cfg.account_id=a.id
           JOIN client_account_role_decisions rd ON rd.firm_id=cfg.firm_id AND rd.client_id=cfg.client_id AND rd.configuration_id=cfg.id
           WHERE a.firm_id=s.firm_id AND a.client_id=s.client_id AND a.id=(manifest->>'PayableAccountId')::uuid AND a.chart_version_id=(manifest->>'ChartVersionId')::uuid
             AND a.account_type='LIABILITY' AND cfg.id=(manifest->>'PayableRoleId')::uuid AND cfg.role='AP' AND rd.decision='APPROVE'
             AND cfg.effective_from<=s.posting_date AND (cfg.effective_to IS NULL OR cfg.effective_to>=s.posting_date)) OR
         EXISTS (SELECT 1 FROM client_purchase_credit_note_lines l JOIN client_accounts a ON a.firm_id=l.firm_id AND a.client_id=l.client_id AND a.id=l.expense_account_id
           WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.submission_id=s.id AND
             (a.chart_version_id<>(manifest->>'ChartVersionId')::uuid OR NOT a.is_posting OR a.status<>'ACTIVE' OR a.account_type NOT IN ('EXPENSE','ASSET'))) THEN
        RAISE EXCEPTION 'Supplier credit lines, current AP role, and expense coding must match their retained manifest.' USING ERRCODE='23514';
      END IF;
      SELECT count(*) INTO open_count FROM client_purchase_credit_note_open_items WHERE firm_id=s.firm_id AND client_id=s.client_id AND submission_id=s.id;
      SELECT count(*) INTO receipt_count FROM client_operational_posting_receipts WHERE firm_id=s.firm_id AND client_id=s.client_id AND journal_id=s.journal_id AND submitted_revision=s.journal_submitted_revision;
      SELECT * INTO decision_row FROM client_purchase_credit_note_decisions WHERE firm_id=s.firm_id AND client_id=s.client_id AND submission_id=s.id;
      IF NOT FOUND THEN
        IF j.status<>'SUBMITTED' OR open_count<>0 OR receipt_count<>0 THEN RAISE EXCEPTION 'Pending supplier credit cannot post or create a debit open item.' USING ERRCODE='23514'; END IF;
      ELSIF decision_row.actor_user_id=s.created_by_user_id OR length(trim(decision_row.reason))=0 OR
         decision_row.preview_digest !~ '^[a-f0-9]{64}$' OR (decision_row.review_context_json::jsonb->>'PreviewDigest')<>s.preview_digest THEN
        RAISE EXCEPTION 'Supplier credit decision is not independent.' USING ERRCODE='23514';
      ELSIF decision_row.decision='APPROVE' THEN
        SELECT count(*) INTO duplicate_count FROM client_purchase_credit_note_submissions other WHERE other.firm_id=s.firm_id AND other.client_id=s.client_id AND other.supplier_id=s.supplier_id AND upper(other.credit_note_reference)=upper(s.credit_note_reference) AND other.id<>s.id;
        IF j.status<>'POSTED' OR j.posted_by_user_id<>decision_row.actor_user_id OR open_count<>1 OR receipt_count<>1 OR duplicate_count>0 AND length(trim(decision_row.duplicate_resolution_reason))=0 OR
           NOT EXISTS (SELECT 1 FROM client_purchase_credit_note_open_items o WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.submission_id=s.id AND o.credit_note_id=s.credit_note_id AND o.original_invoice_id IS NOT DISTINCT FROM s.original_invoice_id AND o.journal_id=s.journal_id AND o.supplier_id=s.supplier_id AND o.currency=s.currency AND o.direction='DEBIT' AND o.original_amount=s.amount) OR
           NOT EXISTS (SELECT 1 FROM client_operational_posting_receipts r WHERE r.firm_id=s.firm_id AND r.client_id=s.client_id AND r.command_id=decision_row.command_id
             AND r.journal_id=s.journal_id AND r.actor_user_id=decision_row.actor_user_id AND r.submitted_revision=s.journal_submitted_revision AND r.posted_revision=j.revision
             AND r.intent_hash=decision_row.intent_hash AND r.preview_digest=decision_row.preview_digest AND r.recorded_at=j.posted_at) THEN
          RAISE EXCEPTION 'Approved supplier credit needs independent review, duplicate rationale, posting receipt and matching supplier debit.' USING ERRCODE='23514';
        END IF;
      ELSIF decision_row.decision='RETURN' AND (j.status<>'RETURNED' OR open_count<>0 OR receipt_count<>0) THEN
        RAISE EXCEPTION 'Returned supplier credit cannot post or create a debit open item.' USING ERRCODE='23514';
      END IF;
    END; $fn$;
    CREATE FUNCTION trigger_check_client_purchase_credit_note_workflow() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE target_id uuid;
    BEGIN
      IF TG_TABLE_NAME='client_purchase_credit_note_submissions' THEN target_id:=CASE WHEN TG_OP='DELETE' THEN OLD.id ELSE NEW.id END;
      ELSE target_id:=CASE WHEN TG_OP='DELETE' THEN OLD.submission_id ELSE NEW.submission_id END; END IF;
      PERFORM check_client_purchase_credit_note_workflow(target_id); RETURN NULL;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER client_purchase_credit_submission_complete AFTER INSERT OR UPDATE ON client_purchase_credit_note_submissions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_purchase_credit_note_workflow();
    CREATE CONSTRAINT TRIGGER client_purchase_credit_decision_complete AFTER INSERT ON client_purchase_credit_note_decisions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_purchase_credit_note_workflow();
    CREATE CONSTRAINT TRIGGER client_purchase_credit_line_complete AFTER INSERT ON client_purchase_credit_note_lines
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_purchase_credit_note_workflow();
    CREATE CONSTRAINT TRIGGER client_purchase_credit_open_item_complete AFTER INSERT ON client_purchase_credit_note_open_items
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_purchase_credit_note_workflow();
    CREATE FUNCTION immutable_client_purchase_credit_note() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN RAISE EXCEPTION 'Client supplier credit history is immutable.' USING ERRCODE='23514'; END; $fn$;
    CREATE TRIGGER immutable_client_purchase_credit_submission BEFORE UPDATE OR DELETE ON client_purchase_credit_note_submissions FOR EACH ROW EXECUTE FUNCTION immutable_client_purchase_credit_note();
    CREATE TRIGGER immutable_client_purchase_credit_decision BEFORE UPDATE OR DELETE ON client_purchase_credit_note_decisions FOR EACH ROW EXECUTE FUNCTION immutable_client_purchase_credit_note();
    CREATE TRIGGER immutable_client_purchase_credit_line BEFORE UPDATE OR DELETE ON client_purchase_credit_note_lines FOR EACH ROW EXECUTE FUNCTION immutable_client_purchase_credit_note();
    CREATE TRIGGER immutable_client_purchase_credit_open_item BEFORE UPDATE OR DELETE ON client_purchase_credit_note_open_items FOR EACH ROW EXECUTE FUNCTION immutable_client_purchase_credit_note();
    """;

  internal const string Down = """
    DROP TRIGGER immutable_client_purchase_credit_open_item ON client_purchase_credit_note_open_items;
    DROP TRIGGER immutable_client_purchase_credit_line ON client_purchase_credit_note_lines;
    DROP TRIGGER immutable_client_purchase_credit_decision ON client_purchase_credit_note_decisions;
    DROP TRIGGER immutable_client_purchase_credit_submission ON client_purchase_credit_note_submissions;
    DROP TRIGGER client_purchase_credit_open_item_complete ON client_purchase_credit_note_open_items;
    DROP TRIGGER client_purchase_credit_line_complete ON client_purchase_credit_note_lines;
    DROP TRIGGER client_purchase_credit_decision_complete ON client_purchase_credit_note_decisions;
    DROP TRIGGER client_purchase_credit_submission_complete ON client_purchase_credit_note_submissions;
    DROP FUNCTION immutable_client_purchase_credit_note();
    DROP FUNCTION trigger_check_client_purchase_credit_note_workflow();
    DROP FUNCTION check_client_purchase_credit_note_workflow(uuid);
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
                RAISE EXCEPTION 'Credit posting requires its exact independent credit decision.' USING ERRCODE='23514';
              END IF;
            ELSE
              SELECT * INTO purchase FROM client_purchase_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
                AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
              IF purchase.id IS NULL THEN RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514'; END IF;
            END IF;
          END IF;
        END IF;
      END IF;
      RETURN NEW;
    END; $fn$;
    """;
}
