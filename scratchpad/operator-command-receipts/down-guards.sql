-- Main migration owner: execute before dropping the two receipt/event tables.
LOCK TABLE firm_safety_states,recovery_sessions,durable_operations,operation_events,operator_command_events,operator_command_receipts IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM operator_command_events) OR EXISTS(SELECT 1 FROM operator_command_receipts) THEN
  RAISE EXCEPTION 'Operator evidence must be retained; downgrade refused.' USING ERRCODE='55000';
 END IF;
END $$;
DROP TRIGGER operator_command_receipt_required ON operator_command_events;
DROP TRIGGER operator_command_receipt_guard ON operator_command_receipts;
DROP TRIGGER operator_command_event_guard ON operator_command_events;
DROP TRIGGER operator_operation_transition ON durable_operations;
DROP TRIGGER operator_recovery_transition ON recovery_sessions;
DROP TRIGGER operator_firm_transition ON firm_safety_states;
DROP FUNCTION require_operator_command_receipt();
DROP FUNCTION guard_operator_command_receipt();
DROP FUNCTION guard_operator_command_event();
DROP FUNCTION retain_operator_command_transition();
DROP FUNCTION operator_command_authorized(uuid);
DROP FUNCTION operator_command_normalize(jsonb);
DROP FUNCTION operator_command_snapshot(firm_safety_states,durable_operations,recovery_sessions);
DROP FUNCTION operator_command_request_hash(uuid,uuid,bigint,uuid,jsonb,jsonb);
DROP FUNCTION operator_command_basis(uuid,uuid,bigint,jsonb);
DROP FUNCTION operator_command_hash_snapshot(jsonb);
DROP FUNCTION operator_command_hash(jsonb);
ALTER TABLE durable_operations DROP CONSTRAINT ck_operation_cancellation;
ALTER TABLE durable_operations ADD CONSTRAINT ck_operation_cancellation
 CHECK(status <> 'CANCELLED_WITH_DISPOSITION' OR length(trim(cancellation_disposition)) > 0);
