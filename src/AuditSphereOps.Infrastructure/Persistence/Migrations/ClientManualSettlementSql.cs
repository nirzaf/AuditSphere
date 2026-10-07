namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class ClientManualSettlementSql
{
  internal const string Up = """
    CREATE OR REPLACE FUNCTION guard_client_generic_control_post() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_invoice_submissions%ROWTYPE; sales_credit client_sales_credit_note_submissions%ROWTYPE;
      purchase_credit client_purchase_credit_note_submissions%ROWTYPE; purchase client_purchase_invoice_submissions%ROWTYPE;
      settlement client_manual_settlement_origins%ROWTYPE;
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
            SELECT * INTO sales_credit FROM client_sales_credit_note_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
              AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
            IF sales_credit.id IS NOT NULL THEN
              IF NOT EXISTS (SELECT 1 FROM client_sales_credit_note_decisions d WHERE d.firm_id=sales_credit.firm_id AND d.client_id=sales_credit.client_id
                AND d.submission_id=sales_credit.id AND d.decision='APPROVE' AND d.actor_user_id<>sales_credit.created_by_user_id
                AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
                RAISE EXCEPTION 'Credit posting requires its independent credit decision.' USING ERRCODE='23514';
              END IF;
              -- Deferred credit-note constraints validate the final journal transition.
            ELSE
              SELECT * INTO purchase_credit FROM client_purchase_credit_note_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
                AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
              IF purchase_credit.id IS NOT NULL THEN
                IF NOT EXISTS (SELECT 1 FROM client_purchase_credit_note_decisions d WHERE d.firm_id=purchase_credit.firm_id AND d.client_id=purchase_credit.client_id
                  AND d.submission_id=purchase_credit.id AND d.decision='APPROVE' AND d.actor_user_id<>purchase_credit.created_by_user_id
                  AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
                  RAISE EXCEPTION 'Supplier credit posting requires its exact independent decision.' USING ERRCODE='23514';
                END IF;
                -- Deferred supplier-credit constraints validate the final journal transition.
              ELSE
                SELECT * INTO purchase FROM client_purchase_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
                  AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
                IF purchase.id IS NOT NULL THEN
                  -- Deferred supplier-invoice constraints validate its retained decision and posting evidence.
                ELSE
                  SELECT * INTO settlement FROM client_manual_settlement_origins WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND journal_id=NEW.id;
                  IF settlement.id IS NULL OR NOT EXISTS (SELECT 1 FROM client_operational_journal_decisions d
                    WHERE d.firm_id=NEW.firm_id AND d.client_id=NEW.client_id AND d.journal_id=NEW.id AND d.decision='APPROVE'
                      AND d.actor_user_id<>NEW.created_by_user_id AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
                    RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514';
                  END IF;
                  PERFORM check_client_manual_settlement_origin(settlement.id);
                END IF;
              END IF;
            END IF;
          END IF;
        END IF;
      END IF;
      RETURN NEW;
    END; $fn$;

    CREATE FUNCTION check_client_manual_settlement_origin(uuid) RETURNS void LANGUAGE plpgsql AS $fn$
    DECLARE o client_manual_settlement_origins%ROWTYPE; j client_operational_journals%ROWTYPE; expected_role text; party_role text;
    BEGIN
      SELECT * INTO o FROM client_manual_settlement_origins WHERE id=$1;
      IF NOT FOUND THEN RETURN; END IF;
      PERFORM id FROM practice_clients WHERE firm_id=o.firm_id AND id=o.client_id FOR UPDATE;
      SELECT * INTO j FROM client_operational_journals WHERE firm_id=o.firm_id AND client_id=o.client_id AND id=o.journal_id;
      IF NOT FOUND OR j.status NOT IN ('DRAFT','SUBMITTED','RETURNED','APPROVED','POSTED') OR o.amount<=0 OR
         length(trim(o.reference))=0 OR length(trim(o.evidence_reference))=0 THEN
        RAISE EXCEPTION 'Manual settlement origin is incomplete.' USING ERRCODE='23514';
      END IF;
      IF j.status IN ('APPROVED','POSTED') AND NOT EXISTS (SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=j.firm_id AND
          d.client_id=j.client_id AND d.journal_id=j.id AND d.decision='APPROVE' AND d.actor_user_id<>j.created_by_user_id) THEN
        RAISE EXCEPTION 'Approved settlement requires an independent journal approval.' USING ERRCODE='23514';
      END IF;
      expected_role:=CASE WHEN o.source_kind='SALES_RECEIPT' THEN 'AR' WHEN o.source_kind='SUPPLIER_PAYMENT' THEN 'AP' ELSE NULL END;
      party_role:=CASE WHEN o.source_kind='SALES_RECEIPT' THEN 'CUSTOMER' WHEN o.source_kind='SUPPLIER_PAYMENT' THEN 'SUPPLIER' ELSE NULL END;
      IF expected_role IS NULL OR NOT EXISTS (SELECT 1 FROM client_bookkeeping_counterparties p WHERE p.firm_id=o.firm_id AND p.client_id=o.client_id
          AND p.id=o.counterparty_id AND p.role IN (party_role,'BOTH')) OR
         (SELECT count(*) FROM client_operational_journal_lines l WHERE l.firm_id=o.firm_id AND l.client_id=o.client_id AND l.journal_id=o.journal_id)<>2 OR
         (SELECT coalesce(sum(l.debit),0) FROM client_operational_journal_lines l WHERE l.firm_id=o.firm_id AND l.client_id=o.client_id AND l.journal_id=o.journal_id)<>o.amount OR
         (SELECT coalesce(sum(l.credit),0) FROM client_operational_journal_lines l WHERE l.firm_id=o.firm_id AND l.client_id=o.client_id AND l.journal_id=o.journal_id)<>o.amount THEN
        RAISE EXCEPTION 'Manual settlement must have one balanced two-line journal for the client counterparty.' USING ERRCODE='23514';
      END IF;
      IF (SELECT count(*) FROM client_operational_journal_lines l
          JOIN client_account_role_configurations c ON c.firm_id=l.firm_id AND c.client_id=l.client_id AND c.account_id=l.client_account_id
          JOIN client_account_role_decisions d ON d.firm_id=c.firm_id AND d.client_id=c.client_id AND d.configuration_id=c.id
          WHERE l.firm_id=o.firm_id AND l.client_id=o.client_id AND l.journal_id=o.journal_id AND c.role=expected_role AND d.decision='APPROVE'
            AND c.effective_from<=j.posting_date AND (c.effective_to IS NULL OR c.effective_to>=j.posting_date)
            AND ((o.source_kind='SALES_RECEIPT' AND l.debit=0 AND l.credit=o.amount) OR
                 (o.source_kind='SUPPLIER_PAYMENT' AND l.debit=o.amount AND l.credit=0)))<>1 OR
         NOT EXISTS (SELECT 1 FROM client_operational_journal_lines l
          JOIN client_accounts a ON a.firm_id=l.firm_id AND a.client_id=l.client_id AND a.id=l.client_account_id
          WHERE l.firm_id=o.firm_id AND l.client_id=o.client_id AND l.journal_id=o.journal_id AND a.account_type='ASSET'
            AND ((o.source_kind='SALES_RECEIPT' AND l.debit=o.amount AND l.credit=0) OR
                 (o.source_kind='SUPPLIER_PAYMENT' AND l.debit=0 AND l.credit=o.amount))
            AND NOT EXISTS (SELECT 1 FROM client_account_role_configurations c
              JOIN client_account_role_decisions d ON d.firm_id=c.firm_id AND d.client_id=c.client_id AND d.configuration_id=c.id
              WHERE c.firm_id=l.firm_id AND c.client_id=l.client_id AND c.account_id=l.client_account_id AND c.role IN ('AR','AP')
                AND d.decision='APPROVE' AND c.effective_from<=j.posting_date AND (c.effective_to IS NULL OR c.effective_to>=j.posting_date))) THEN
        RAISE EXCEPTION 'Manual settlement must pair its approved AR/AP control with a non-control cash asset for the exact amount.' USING ERRCODE='23514';
      END IF;
    END; $fn$;

    CREATE FUNCTION trigger_check_client_manual_settlement_origin() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE target_id uuid;
    BEGIN
      IF TG_TABLE_NAME='client_manual_settlement_origins' THEN target_id:=NEW.id;
      ELSIF TG_TABLE_NAME='client_operational_journals' THEN
        SELECT id INTO target_id FROM client_manual_settlement_origins WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND journal_id=NEW.id;
      ELSE
        SELECT id INTO target_id FROM client_manual_settlement_origins WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND journal_id=NEW.journal_id;
      END IF;
      IF target_id IS NULL THEN RETURN NULL; END IF;
      PERFORM check_client_manual_settlement_origin(target_id); RETURN NULL;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER client_manual_settlement_origin_complete AFTER INSERT ON client_manual_settlement_origins
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_manual_settlement_origin();
    CREATE CONSTRAINT TRIGGER client_manual_settlement_line_complete AFTER INSERT ON client_operational_journal_lines
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_manual_settlement_origin();
    CREATE CONSTRAINT TRIGGER client_manual_settlement_journal_complete AFTER UPDATE ON client_operational_journals
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_manual_settlement_origin();

    CREATE FUNCTION immutable_client_manual_settlement_origin() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN RAISE EXCEPTION 'Manual settlement evidence is append-only.' USING ERRCODE='23514'; END; $fn$;
    CREATE TRIGGER immutable_client_manual_settlement_origin BEFORE UPDATE OR DELETE ON client_manual_settlement_origins
      FOR EACH ROW EXECUTE FUNCTION immutable_client_manual_settlement_origin();
    CREATE FUNCTION immutable_client_settlement_journal_line() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      IF EXISTS (SELECT 1 FROM client_manual_settlement_origins o WHERE o.firm_id=OLD.firm_id AND o.client_id=OLD.client_id AND o.journal_id=OLD.journal_id) THEN
        RAISE EXCEPTION 'Posted settlement journal lines cannot be edited; use the controlled reversal workflow.' USING ERRCODE='23514';
      END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
    END; $fn$;
    CREATE TRIGGER immutable_client_settlement_journal_line BEFORE UPDATE OR DELETE ON client_operational_journal_lines
      FOR EACH ROW EXECUTE FUNCTION immutable_client_settlement_journal_line();
    """;

  internal const string Down = """
    DROP TRIGGER immutable_client_settlement_journal_line ON client_operational_journal_lines;
    DROP TRIGGER immutable_client_manual_settlement_origin ON client_manual_settlement_origins;
    DROP TRIGGER client_manual_settlement_journal_complete ON client_operational_journals;
    DROP TRIGGER client_manual_settlement_line_complete ON client_operational_journal_lines;
    DROP TRIGGER client_manual_settlement_origin_complete ON client_manual_settlement_origins;
    DROP FUNCTION immutable_client_settlement_journal_line();
    DROP FUNCTION immutable_client_manual_settlement_origin();
    DROP FUNCTION trigger_check_client_manual_settlement_origin();
    DROP FUNCTION check_client_manual_settlement_origin(uuid);
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
            SELECT * INTO sales_credit FROM client_sales_credit_note_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
              AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
            IF sales_credit.id IS NOT NULL THEN
              IF NOT EXISTS (SELECT 1 FROM client_sales_credit_note_decisions d WHERE d.firm_id=sales_credit.firm_id AND d.client_id=sales_credit.client_id
                AND d.submission_id=sales_credit.id AND d.decision='APPROVE' AND d.actor_user_id<>sales_credit.created_by_user_id
                AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
                RAISE EXCEPTION 'Credit posting requires its independent credit decision.' USING ERRCODE='23514';
              END IF;
              -- Deferred credit-note constraints validate the final journal transition.
            ELSE
              SELECT * INTO purchase_credit FROM client_purchase_credit_note_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
                AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
              IF purchase_credit.id IS NOT NULL THEN
                IF NOT EXISTS (SELECT 1 FROM client_purchase_credit_note_decisions d WHERE d.firm_id=purchase_credit.firm_id AND d.client_id=purchase_credit.client_id
                  AND d.submission_id=purchase_credit.id AND d.decision='APPROVE' AND d.actor_user_id<>purchase_credit.created_by_user_id
                  AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
                  RAISE EXCEPTION 'Supplier credit posting requires its exact independent decision.' USING ERRCODE='23514';
                END IF;
                -- Deferred supplier-credit constraints validate the final journal transition.
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
      END IF;
      RETURN NEW;
    END; $fn$;
    """;
}
