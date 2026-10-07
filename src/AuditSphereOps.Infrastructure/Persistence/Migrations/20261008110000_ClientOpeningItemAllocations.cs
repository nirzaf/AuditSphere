using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

/// <summary>Extends approved settlement targets to integrity-checked opening invoice details.</summary>
public partial class ClientOpeningItemAllocations : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.DropCheckConstraint(name: "ck_client_open_item_allocation_line", table: "client_open_item_allocation_lines");
    migrationBuilder.AddCheckConstraint(name: "ck_client_open_item_allocation_line", table: "client_open_item_allocation_lines",
      sql: "line_number>0 AND target_kind IN ('SALES_INVOICE','PURCHASE_INVOICE','OPENING_AR_INVOICE','OPENING_AP_INVOICE') AND target_open_item_id<>'00000000-0000-0000-0000-000000000000'::uuid AND amount>0");
    migrationBuilder.Sql(ClientOpenItemAllocationSql.Down);
    migrationBuilder.Sql("""
    CREATE OR REPLACE FUNCTION check_client_open_item_allocation(uuid) RETURNS void LANGUAGE plpgsql AS $fn$
    DECLARE s client_open_item_allocation_submissions%ROWTYPE; source_party uuid; source_currency text; source_amount numeric; allocation_line record;
      target_amount numeric; net_applied numeric; previous client_open_item_allocation_lines%ROWTYPE; previous_submission client_open_item_allocation_submissions%ROWTYPE;
      reversed_amount numeric;
    BEGIN
      SELECT * INTO s FROM client_open_item_allocation_submissions WHERE id=$1;
      IF NOT FOUND THEN RETURN; END IF;
      PERFORM id FROM practice_clients WHERE firm_id=s.firm_id AND id=s.client_id FOR UPDATE;
      IF s.manifest_hash<>encode(sha256(convert_to(s.manifest_json,'UTF8')),'hex') OR
         NOT EXISTS (SELECT 1 FROM client_open_item_allocation_lines WHERE firm_id=s.firm_id AND client_id=s.client_id AND submission_id=s.id) THEN
        RAISE EXCEPTION 'Allocation manifest or lines are incomplete.' USING ERRCODE='23514';
      END IF;
      IF s.source_kind='SALES_CREDIT' THEN
        SELECT o.customer_id,o.currency,o.original_amount INTO source_party,source_currency,source_amount
          FROM client_sales_credit_note_open_items o JOIN client_operational_journals j
            ON j.firm_id=o.firm_id AND j.client_id=o.client_id AND j.id=o.journal_id
          JOIN client_sales_credit_note_decisions d ON d.firm_id=o.firm_id AND d.client_id=o.client_id AND d.submission_id=o.submission_id
          WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.id=s.source_item_id AND d.decision='APPROVE' AND j.status='POSTED' AND o.direction='CREDIT';
      ELSIF s.source_kind='PURCHASE_CREDIT' THEN
        SELECT o.supplier_id,o.currency,o.original_amount INTO source_party,source_currency,source_amount
          FROM client_purchase_credit_note_open_items o JOIN client_operational_journals j
            ON j.firm_id=o.firm_id AND j.client_id=o.client_id AND j.id=o.journal_id
          JOIN client_purchase_credit_note_decisions d ON d.firm_id=o.firm_id AND d.client_id=o.client_id AND d.submission_id=o.submission_id
          WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.id=s.source_item_id AND d.decision='APPROVE' AND j.status='POSTED' AND o.direction='DEBIT';
      ELSIF s.source_kind IN ('SALES_RECEIPT','SUPPLIER_PAYMENT') AND EXISTS (
        SELECT 1 FROM client_manual_settlement_origins o WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.id=s.source_item_id
      ) THEN
        SELECT o.counterparty_id,j.currency,o.amount INTO source_party,source_currency,source_amount
          FROM client_manual_settlement_origins o
          JOIN client_operational_journals j ON j.firm_id=o.firm_id AND j.client_id=o.client_id AND j.id=o.journal_id
          JOIN client_operational_journal_decisions d ON d.firm_id=j.firm_id AND d.client_id=j.client_id AND d.journal_id=j.id
          WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.id=s.source_item_id AND o.source_kind=s.source_kind
            AND j.status='POSTED' AND d.decision='APPROVE';
      ELSE
        SELECT p.id,l.original_currency,CASE WHEN s.source_kind='SALES_RECEIPT' THEN l.credit ELSE l.debit END
          INTO source_party,source_currency,source_amount
          FROM general_ledger_lines l
          JOIN general_ledger_transactions t ON t.firm_id=l.firm_id AND t.client_id=l.client_id AND t.engagement_id=l.engagement_id
            AND t.import_batch_id=l.import_batch_id AND t.id=l.transaction_id
          JOIN source_import_batches b ON b.firm_id=l.firm_id AND b.client_id=l.client_id AND b.engagement_id=l.engagement_id AND b.id=l.import_batch_id
          JOIN client_account_role_configurations c ON c.firm_id=l.firm_id AND c.client_id=l.client_id AND c.account_id=l.client_account_id
          JOIN client_account_role_decisions cd ON cd.firm_id=c.firm_id AND cd.client_id=c.client_id AND cd.configuration_id=c.id
          JOIN client_bookkeeping_counterparties p ON p.firm_id=l.firm_id AND p.client_id=l.client_id
            AND p.role=CASE WHEN s.source_kind='SALES_RECEIPT' THEN 'CUSTOMER' ELSE 'SUPPLIER' END
            AND (p.external_reference=l.party_identifier OR p.normalized_external_identity=l.party_identifier OR
              lower(p.id::text)=lower(l.party_identifier) OR replace(p.id::text,'-','')=lower(l.party_identifier))
          WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.id=s.source_item_id AND b.source_kind='GL' AND b.status='SEALED'
            AND b.expected_transaction_count=b.accepted_transaction_count AND b.expected_line_count=b.accepted_line_count
            AND length(b.normalized_dataset_digest)=64 AND t.currency=b.currency AND l.original_currency=t.currency
            AND cd.decision='APPROVE' AND c.effective_from<=t.posting_date AND (c.effective_to IS NULL OR c.effective_to>=t.posting_date)
            AND ((s.source_kind='SALES_RECEIPT' AND c.role='AR' AND l.debit=0 AND l.credit>0) OR
              (s.source_kind='SUPPLIER_PAYMENT' AND c.role='AP' AND l.debit>0 AND l.credit=0))
            AND l.party_identifier<>'' AND
            (SELECT coalesce(sum(x.debit),0) FROM general_ledger_lines x
              JOIN client_accounts a ON a.firm_id=x.firm_id AND a.client_id=x.client_id AND a.id=x.client_account_id
              WHERE x.firm_id=l.firm_id AND x.client_id=l.client_id AND x.engagement_id=l.engagement_id AND x.import_batch_id=l.import_batch_id
                AND x.transaction_id=l.transaction_id AND a.account_type='ASSET' AND x.original_currency=l.original_currency
                AND NOT EXISTS (SELECT 1 FROM client_account_role_configurations xc JOIN client_account_role_decisions xd ON xd.firm_id=xc.firm_id AND xd.client_id=xc.client_id AND xd.configuration_id=xc.id WHERE xc.firm_id=x.firm_id AND xc.client_id=x.client_id AND xc.account_id=x.client_account_id AND xd.decision='APPROVE' AND xc.role IN ('AR','AP') AND xc.effective_from<=t.posting_date AND (xc.effective_to IS NULL OR xc.effective_to>=t.posting_date)))=
              CASE WHEN s.source_kind='SALES_RECEIPT' THEN l.credit ELSE 0 END
            AND (SELECT coalesce(sum(x.credit),0) FROM general_ledger_lines x
              JOIN client_accounts a ON a.firm_id=x.firm_id AND a.client_id=x.client_id AND a.id=x.client_account_id
              WHERE x.firm_id=l.firm_id AND x.client_id=l.client_id AND x.engagement_id=l.engagement_id AND x.import_batch_id=l.import_batch_id
                AND x.transaction_id=l.transaction_id AND a.account_type='ASSET' AND x.original_currency=l.original_currency
                AND NOT EXISTS (SELECT 1 FROM client_account_role_configurations xc JOIN client_account_role_decisions xd ON xd.firm_id=xc.firm_id AND xd.client_id=xc.client_id AND xd.configuration_id=xc.id WHERE xc.firm_id=x.firm_id AND xc.client_id=x.client_id AND xc.account_id=x.client_account_id AND xd.decision='APPROVE' AND xc.role IN ('AR','AP') AND xc.effective_from<=t.posting_date AND (xc.effective_to IS NULL OR xc.effective_to>=t.posting_date)))=
              CASE WHEN s.source_kind='SUPPLIER_PAYMENT' THEN l.debit ELSE 0 END
            AND (SELECT coalesce(sum(x.debit),0) FROM general_ledger_lines x WHERE x.firm_id=l.firm_id AND x.client_id=l.client_id AND x.engagement_id=l.engagement_id AND x.import_batch_id=l.import_batch_id AND x.transaction_id=l.transaction_id)=
              (SELECT coalesce(sum(x.credit),0) FROM general_ledger_lines x WHERE x.firm_id=l.firm_id AND x.client_id=l.client_id AND x.engagement_id=l.engagement_id AND x.import_batch_id=l.import_batch_id AND x.transaction_id=l.transaction_id)
            AND (SELECT count(*) FROM general_ledger_lines x JOIN client_account_role_configurations xc
                ON xc.firm_id=x.firm_id AND xc.client_id=x.client_id AND xc.account_id=x.client_account_id
              JOIN client_account_role_decisions xd ON xd.firm_id=xc.firm_id AND xd.client_id=xc.client_id AND xd.configuration_id=xc.id
              WHERE x.firm_id=l.firm_id AND x.client_id=l.client_id AND x.engagement_id=l.engagement_id AND x.import_batch_id=l.import_batch_id AND x.transaction_id=l.transaction_id
                AND xd.decision='APPROVE' AND xc.effective_from<=t.posting_date AND (xc.effective_to IS NULL OR xc.effective_to>=t.posting_date)
                AND ((xc.role='AR' AND x.debit=0 AND x.credit>0) OR (xc.role='AP' AND x.debit>0 AND x.credit=0)))=1;
      END IF;
      IF source_party IS NULL OR source_party<>s.counterparty_id OR source_currency<>s.currency OR source_amount<>s.source_amount THEN
        RAISE EXCEPTION 'Allocation source is not a posted client credit for the exact party and currency.' USING ERRCODE='23514';
      END IF;
      FOR allocation_line IN SELECT * FROM client_open_item_allocation_lines WHERE firm_id=s.firm_id AND client_id=s.client_id AND submission_id=s.id LOOP
        IF allocation_line.target_kind='SALES_INVOICE' THEN
          SELECT o.original_amount INTO target_amount FROM client_sales_invoice_open_items o
            JOIN client_sales_invoice_decisions d ON d.firm_id=o.firm_id AND d.client_id=o.client_id AND d.submission_id=o.submission_id
            JOIN client_operational_journals j ON j.firm_id=o.firm_id AND j.client_id=o.client_id AND j.id=o.journal_id
            WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.id=allocation_line.target_open_item_id AND o.customer_id=s.counterparty_id
              AND o.currency=s.currency AND d.decision='APPROVE' AND j.status='POSTED';
        ELSIF allocation_line.target_kind IN ('OPENING_AR_INVOICE','OPENING_AP_INVOICE') THEN
          SELECT (item->>'Amount')::numeric INTO target_amount
            FROM client_operational_opening_balances o
            CROSS JOIN LATERAL jsonb_array_elements(o.manifest_json::jsonb->'OpenItems') item
            WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.approved_by_user_id IS NOT NULL
              AND o.currency=s.currency AND item->>'Id'=allocation_line.target_open_item_id::text
              AND item->>'CounterpartyId'=s.counterparty_id::text
              AND item->>'Role'=CASE WHEN allocation_line.target_kind='OPENING_AR_INVOICE' THEN 'AR' ELSE 'AP' END
              AND NOT EXISTS (SELECT 1 FROM client_operational_opening_balances newer
                WHERE newer.firm_id=o.firm_id AND newer.client_id=o.client_id AND newer.approved_by_user_id IS NOT NULL
                  AND newer.as_of_date>o.as_of_date);
        ELSE
          SELECT o.original_amount INTO target_amount FROM client_purchase_invoice_open_items o
            JOIN client_purchase_invoice_decisions d ON d.firm_id=o.firm_id AND d.client_id=o.client_id AND d.submission_id=o.submission_id
            JOIN client_operational_journals j ON j.firm_id=o.firm_id AND j.client_id=o.client_id AND j.id=o.journal_id
            WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.id=allocation_line.target_open_item_id AND o.supplier_id=s.counterparty_id
              AND o.currency=s.currency AND d.decision='APPROVE' AND j.status='POSTED';
        END IF;
        IF target_amount IS NULL THEN RAISE EXCEPTION 'Allocation target is not a matching posted client invoice.' USING ERRCODE='23514'; END IF;
        IF s.disposition='ALLOCATE' AND allocation_line.reverses_allocation_line_id IS NOT NULL THEN
          RAISE EXCEPTION 'New allocation cannot name a reversal line.' USING ERRCODE='23514';
        ELSIF s.disposition='UNALLOCATE' THEN
          SELECT * INTO previous FROM client_open_item_allocation_lines WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=allocation_line.reverses_allocation_line_id;
          SELECT * INTO previous_submission FROM client_open_item_allocation_submissions WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=previous.submission_id;
          IF previous.id IS NULL OR previous_submission.disposition<>'ALLOCATE' OR previous_submission.source_kind<>s.source_kind OR
             previous_submission.source_item_id<>s.source_item_id OR previous.target_kind<>allocation_line.target_kind OR previous.target_open_item_id<>allocation_line.target_open_item_id OR
             NOT EXISTS (SELECT 1 FROM client_open_item_allocation_decisions d WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.submission_id=previous_submission.id AND d.decision='APPROVE') THEN
            RAISE EXCEPTION 'Unallocation must reverse an exact approved allocation from the same source.' USING ERRCODE='23514';
          END IF;
          SELECT coalesce(sum(rl.amount),0) INTO reversed_amount FROM client_open_item_allocation_lines rl
            JOIN client_open_item_allocation_submissions rs ON rs.firm_id=rl.firm_id AND rs.client_id=rl.client_id AND rs.id=rl.submission_id
            JOIN client_open_item_allocation_decisions rd ON rd.firm_id=rs.firm_id AND rd.client_id=rs.client_id AND rd.submission_id=rs.id
            WHERE rl.firm_id=s.firm_id AND rl.client_id=s.client_id AND rl.reverses_allocation_line_id=previous.id AND rs.disposition='UNALLOCATE' AND rd.decision='APPROVE';
          IF reversed_amount+allocation_line.amount>previous.amount THEN RAISE EXCEPTION 'Unallocation exceeds the remaining amount of its original allocation.' USING ERRCODE='23514'; END IF;
        END IF;
        SELECT coalesce(sum(CASE WHEN a.disposition='ALLOCATE' THEN al.amount ELSE -al.amount END),0) INTO net_applied
          FROM client_open_item_allocation_lines al
          JOIN client_open_item_allocation_submissions a ON a.firm_id=al.firm_id AND a.client_id=al.client_id AND a.id=al.submission_id
          JOIN client_open_item_allocation_decisions ad ON ad.firm_id=a.firm_id AND ad.client_id=a.client_id AND ad.submission_id=a.id
          WHERE al.firm_id=s.firm_id AND al.client_id=s.client_id AND al.target_kind=allocation_line.target_kind AND al.target_open_item_id=allocation_line.target_open_item_id AND ad.decision='APPROVE';
        IF net_applied>target_amount THEN RAISE EXCEPTION 'Approved allocations exceed the target invoice balance.' USING ERRCODE='23514'; END IF;
      END LOOP;
      IF EXISTS (SELECT 1 FROM client_open_item_allocation_decisions d WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.submission_id=s.id AND d.actor_user_id=s.created_by_user_id) THEN
        RAISE EXCEPTION 'Allocation requires an independent reviewer.' USING ERRCODE='23514';
      END IF;
      IF EXISTS (SELECT 1 FROM client_open_item_allocation_decisions d WHERE d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.submission_id=s.id AND d.decision='APPROVE') THEN
        IF s.disposition='ALLOCATE' THEN
          SELECT coalesce(sum(CASE WHEN a.disposition='ALLOCATE' THEN al.amount ELSE -al.amount END),0) INTO net_applied
            FROM client_open_item_allocation_lines al JOIN client_open_item_allocation_submissions a ON a.firm_id=al.firm_id AND a.client_id=al.client_id AND a.id=al.submission_id
            JOIN client_open_item_allocation_decisions ad ON ad.firm_id=a.firm_id AND ad.client_id=a.client_id AND ad.submission_id=a.id
          WHERE a.firm_id=s.firm_id AND a.client_id=s.client_id AND a.source_kind=s.source_kind AND a.source_item_id=s.source_item_id AND ad.decision='APPROVE';
          IF net_applied>source_amount THEN RAISE EXCEPTION 'Approved allocations exceed the source credit balance.' USING ERRCODE='23514'; END IF;
        END IF;
      END IF;
    END; $fn$;

    CREATE FUNCTION trigger_check_client_open_item_allocation() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE target_id uuid;
    BEGIN
      IF TG_TABLE_NAME='client_open_item_allocation_submissions' THEN target_id:=NEW.id;
      ELSIF TG_TABLE_NAME='client_open_item_allocation_lines' THEN target_id:=NEW.submission_id;
      ELSE target_id:=NEW.submission_id; END IF;
      PERFORM check_client_open_item_allocation(target_id); RETURN NULL;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER client_open_item_allocation_submission_complete AFTER INSERT ON client_open_item_allocation_submissions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_open_item_allocation();
    CREATE CONSTRAINT TRIGGER client_open_item_allocation_line_complete AFTER INSERT ON client_open_item_allocation_lines
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_open_item_allocation();
    CREATE CONSTRAINT TRIGGER client_open_item_allocation_decision_complete AFTER INSERT ON client_open_item_allocation_decisions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION trigger_check_client_open_item_allocation();

    CREATE FUNCTION immutable_client_open_item_allocation() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN RAISE EXCEPTION 'Client open-item allocation history is append-only.' USING ERRCODE='23514'; END; $fn$;
    CREATE TRIGGER immutable_client_open_item_allocation_submission BEFORE UPDATE OR DELETE ON client_open_item_allocation_submissions
      FOR EACH ROW EXECUTE FUNCTION immutable_client_open_item_allocation();
    CREATE TRIGGER immutable_client_open_item_allocation_line BEFORE UPDATE OR DELETE ON client_open_item_allocation_lines
      FOR EACH ROW EXECUTE FUNCTION immutable_client_open_item_allocation();
    CREATE TRIGGER immutable_client_open_item_allocation_decision BEFORE UPDATE OR DELETE ON client_open_item_allocation_decisions
      FOR EACH ROW EXECUTE FUNCTION immutable_client_open_item_allocation();
    """);
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS (SELECT 1 FROM client_open_item_allocation_lines WHERE target_kind IN ('OPENING_AR_INVOICE','OPENING_AP_INVOICE')) THEN
          RAISE EXCEPTION 'Opening-item allocations exist; reverse them before rolling back this migration.' USING ERRCODE='23514';
        END IF;
      END; $guard$;
    """);
    migrationBuilder.Sql(ClientOpenItemAllocationSql.Down);
    migrationBuilder.Sql(ClientOpenItemAllocationSql.Up);
    migrationBuilder.DropCheckConstraint(name: "ck_client_open_item_allocation_line", table: "client_open_item_allocation_lines");
    migrationBuilder.AddCheckConstraint(name: "ck_client_open_item_allocation_line", table: "client_open_item_allocation_lines",
      sql: "line_number>0 AND target_kind IN ('SALES_INVOICE','PURCHASE_INVOICE') AND target_open_item_id<>'00000000-0000-0000-0000-000000000000'::uuid AND amount>0");
  }
}
