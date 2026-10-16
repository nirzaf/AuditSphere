import { Decoder, arr, bool, decode, guid, instant, nullable, obj, oneOf, sha256, str } from '../../core/decode';
const exactEpoch: Decoder<string> = (raw, path) => {
  if (typeof raw !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(raw) || raw.length === 19 && raw > '9223372036854775807') throw new Error(`${path}: expected exact counter string`); return raw;
};

const positiveEpoch: Decoder<string> = (raw, path) => {
  const value = exactEpoch(raw, path);
  if (value === '0') throw new Error(`${path}: expected positive exact counter string`);
  return value;
};

export const operationActions = ['RETRY', 'CANCEL', 'LIFT_QUARANTINE', 'APPROVE_RECOVERY_RESTART'] as const;
export type OperationAction = typeof operationActions[number];
const action = oneOf(...operationActions);
const inputShape = obj({ action, operationId: nullable(guid), recoverySessionId: nullable(guid), disposition: nullable(str(2000)), findings: nullable(str(10000)) });
export const decodeOperationInput: Decoder<ReturnType<typeof inputShape>> = (raw, path) => {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw) || Object.keys(raw).some(k => !['action', 'operationId', 'recoverySessionId', 'disposition', 'findings'].includes(k))) throw new Error('Unexpected operation input');
  const input = inputShape({ operationId: null, recoverySessionId: null, disposition: null, findings: null, ...raw }, path);
  if (input.action === 'RETRY' && (!input.operationId || input.recoverySessionId || input.disposition !== null || input.findings !== null) ||
    input.action === 'CANCEL' && (!input.operationId || input.recoverySessionId || !input.disposition?.trim() || input.findings !== null) ||
    input.action === 'LIFT_QUARANTINE' && (input.operationId || input.disposition !== null || input.findings !== null) ||
    input.action === 'APPROVE_RECOVERY_RESTART' && (input.operationId || !input.recoverySessionId || input.disposition !== null || !input.findings?.trim())) throw new Error('Invalid action fields');
  if ([input.operationId, input.recoverySessionId].some(id => id === '00000000-0000-0000-0000-000000000000') ||
    [input.disposition, input.findings].some(value => value !== null && /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/.test(value))) throw new Error('Invalid operator input');
  return { ...input, disposition: input.disposition?.trim() ?? null, findings: input.findings?.trim() ?? null };
};
export type OperationInput = ReturnType<typeof decodeOperationInput>;
export const decodeOperatorOperation = obj({ id: guid, clientId: nullable(guid), engagementId: nullable(guid), targetId: guid, correlationId: guid, kind: str(200),
  expectedRevision: positiveEpoch, attemptToken: exactEpoch, attemptCount: exactEpoch, status: str(100), isReconciliation: bool, hasLease: bool,
  errorCode: nullable(str(1000)), cancellationDisposition: nullable(str(2000)), nextAttemptAt: nullable(instant), completedAt: nullable(instant), hasResultEvidence: bool });
export const decodeOperatorRecovery = obj({ id: guid, externalEpoch: positiveEpoch, restorePoint: str(500), reconciliationScope: str(2000), findings: str(10000),
  createdAt: instant, approvedRestartAt: nullable(instant), approvedByUserId: nullable(guid) });
export const decodeOperationSnapshot = obj({ operatingMode: str(100), deploymentEpoch: positiveEpoch, recoveryEpoch: exactEpoch, policyGeneration: positiveEpoch,
  operation: nullable(decodeOperatorOperation), recoverySession: nullable(decodeOperatorRecovery) });
export type OperationSnapshot = ReturnType<typeof decodeOperationSnapshot>;
export const decodeOperationState = obj({ before: decodeOperationSnapshot, reviewBasis: sha256, canRetry: bool, canCancel: bool, canLiftQuarantine: bool,
  canApproveRecoveryRestart: bool, blockers: arr(str(2000), 100), retryBlockers: arr(str(2000), 100), cancelBlockers: arr(str(2000), 100),
  liftQuarantineBlockers: arr(str(2000), 100), approveRecoveryRestartBlockers: arr(str(2000), 100) });
export function matchesOperationTarget(input: OperationInput, snapshot: OperationSnapshot): boolean {
  return input.operationId ? snapshot.operation?.id === input.operationId : input.recoverySessionId ? snapshot.recoverySession?.id === input.recoverySessionId : snapshot.operation === null;
}
const previewShape = obj({ requestId: guid, requestHash: sha256, reviewBasis: sha256, canonicalInput: decodeOperationInput, before: decodeOperationSnapshot,
  canProceed: bool, blockers: arr(str(2000), 100), summary: str(8000) });
export const decodeOperationPreview: Decoder<ReturnType<typeof previewShape>> = (raw, path) => {
  const preview = previewShape(raw, path); if (!matchesOperationTarget(preview.canonicalInput, preview.before)) throw new Error('Wrong preview target'); return preview;
};
const eventShape = obj({ id: guid, actorId: guid, actorEpoch: positiveEpoch, action, operationEventId: nullable(guid), createdAt: instant,
  before: decodeOperationSnapshot, after: decodeOperationSnapshot });
const receiptShape = obj({ id: guid, actorId: guid, actorEpoch: positiveEpoch, requestId: guid, requestHash: sha256, action, createdAt: instant,
  preview: decodeOperationPreview, commandEvent: eventShape });
export const decodeOperationReceipt: Decoder<ReturnType<typeof receiptShape>> = (raw, path) => {
  const receipt = receiptShape(raw, path), preview = receipt.preview, event = receipt.commandEvent;
  if (!preview.canProceed || receipt.requestId !== preview.requestId || receipt.requestHash !== preview.requestHash || receipt.action !== preview.canonicalInput.action ||
    event.actorId !== receipt.actorId || event.actorEpoch !== receipt.actorEpoch || event.action !== receipt.action ||
    JSON.stringify(event.before) !== JSON.stringify(preview.before) || !matchesOperationTarget(preview.canonicalInput, event.after) ||
    (preview.canonicalInput.operationId !== null && event.operationEventId === null)) throw new Error('Inconsistent operation receipt');
  const before = event.before.operation, after = event.after.operation;
  if (before && after && (before.id !== after.id || before.clientId !== after.clientId || before.engagementId !== after.engagementId ||
    before.targetId !== after.targetId || before.correlationId !== after.correlationId || before.kind !== after.kind)) throw new Error('Changed operation identity');
  const priorSession = event.before.recoverySession, afterSession = event.after.recoverySession;
  if (priorSession && (!afterSession || priorSession.id !== afterSession.id || priorSession.externalEpoch !== afterSession.externalEpoch)) throw new Error('Changed recovery session identity');
  const input = preview.canonicalInput;
  if (input.action === 'RETRY' && after?.status !== 'RETRY_WAIT' ||
    input.action === 'CANCEL' && (after?.status !== 'CANCELLED_WITH_DISPOSITION' || after.cancellationDisposition !== input.disposition) ||
    (input.action === 'LIFT_QUARANTINE' || input.action === 'APPROVE_RECOVERY_RESTART') && event.after.operatingMode !== 'LOCAL_ONLY' ||
    input.action === 'APPROVE_RECOVERY_RESTART' && (!afterSession?.approvedRestartAt || afterSession.approvedByUserId !== receipt.actorId || afterSession.findings !== input.findings)) throw new Error('Receipt effect differs from reviewed action');
  return receipt;
};
const lookupShape = obj({ found: bool, receipt: nullable(decodeOperationReceipt) });
export const decodeOperationLookup: Decoder<ReturnType<typeof lookupShape>> = (raw, path) => {
  const lookup = lookupShape(raw, path); if (lookup.found !== !!lookup.receipt) throw new Error('Inconsistent receipt lookup'); return lookup;
};
export type OperationPreview = ReturnType<typeof decodeOperationPreview>;
export type OperationReceipt = ReturnType<typeof decodeOperationReceipt>;
export const operationInput = (value: unknown) => decode(decodeOperationInput, value);
