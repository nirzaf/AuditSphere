import { decode } from '../../core/decode';
import { decodeOperationInput, decodeOperationLookup, decodeOperationPreview, decodeOperationReceipt, decodeOperationSnapshot } from './operations-command-contracts';

const id = '11111111-1111-4111-8111-111111111111', other = '22222222-2222-4222-8222-222222222222';
const hash = 'a'.repeat(64), epoch = '9007199254740993', at = '2026-10-16T00:00:00Z';
const operation = { id, clientId: null, engagementId: null, targetId: other, correlationId: other, kind: 'SYNTHETIC', expectedRevision: epoch,
  attemptToken: epoch, attemptCount: '4', status: 'DEAD_LETTER', isReconciliation: false, hasLease: false, errorCode: 'synthetic.failed',
  cancellationDisposition: null, nextAttemptAt: null, completedAt: null, hasResultEvidence: false };
const before = { operatingMode: 'LOCAL_ONLY', deploymentEpoch: epoch, recoveryEpoch: '0', policyGeneration: epoch, operation, recoverySession: null };
const preview = { requestId: other, requestHash: hash, reviewBasis: hash, canonicalInput: { action: 'RETRY', operationId: id }, before,
  canProceed: true, blockers: [], summary: 'Re-arm this exact operation, without asserting a provider effect.' };
const receipt = { id: other, actorId: id, actorEpoch: epoch, requestId: other, requestHash: hash, action: 'RETRY', createdAt: at, preview,
  commandEvent: { id: other, actorId: id, actorEpoch: epoch, action: 'RETRY', operationEventId: other, createdAt: at, before,
    after: { ...before, operation: { ...operation, status: 'RETRY_WAIT', attemptCount: '0' } } } };

describe('Reviewed Operations contracts', () => {
  it('preserves exact counters beyond JavaScript integer precision and refuses numeric counters', () => {
    expect(decode(decodeOperationSnapshot, before).deploymentEpoch).toBe(epoch);
    expect(decode(decodeOperationSnapshot, before).operation?.attemptToken).toBe(epoch);
    expect(() => decode(decodeOperationSnapshot, { ...before, deploymentEpoch: 9007199254740992 })).toThrow();
  });
  it('requires positive exact strings for deployment, policy, expected revision and recovery-session epochs', () => {
    for (const field of ['deploymentEpoch', 'policyGeneration']) {
      expect(() => decode(decodeOperationSnapshot, { ...before, [field]: '0' })).toThrow();
      expect(decode(decodeOperationSnapshot, { ...before, [field]: '1' })[field as 'deploymentEpoch' | 'policyGeneration']).toBe('1');
      expect(() => decode(decodeOperationSnapshot, { ...before, [field]: '9223372036854775808' })).toThrow();
    }
    expect(() => decode(decodeOperationSnapshot, { ...before, operation: { ...operation, expectedRevision: '0' } })).toThrow();
    expect(decode(decodeOperationSnapshot, { ...before, operation: { ...operation, expectedRevision: '1' } }).operation?.expectedRevision).toBe('1');
    const session = { id, externalEpoch: '1', restorePoint: 'Synthetic restore', reconciliationScope: 'Synthetic scope', findings: '',
      createdAt: at, approvedRestartAt: null, approvedByUserId: null };
    expect(decode(decodeOperationSnapshot, { ...before, recoverySession: session }).recoverySession?.externalEpoch).toBe('1');
    expect(() => decode(decodeOperationSnapshot, { ...before, recoverySession: { ...session, externalEpoch: '0' } })).toThrow();
    expect(decode(decodeOperationSnapshot, { ...before, deploymentEpoch: '9223372036854775807' }).deploymentEpoch).toBe('9223372036854775807');
  });
  it('requires positive receipt/event actor epochs, including when both crosslinks agree on zero', () => {
    expect(() => decode(decodeOperationReceipt, { ...receipt, actorEpoch: '0', commandEvent: { ...receipt.commandEvent, actorEpoch: '0' } })).toThrow();
    expect(() => decode(decodeOperationReceipt, { ...receipt, commandEvent: { ...receipt.commandEvent, actorEpoch: '0' } })).toThrow();
    expect(decode(decodeOperationReceipt, { ...receipt, actorEpoch: '1', commandEvent: { ...receipt.commandEvent, actorEpoch: '1' } }).actorEpoch).toBe('1');
  });
  it('retains zero for recovery epoch, attempt token and attempt count without numeric conversion', () => {
    const decoded = decode(decodeOperationSnapshot, { ...before, recoveryEpoch: '0', operation: { ...operation, attemptToken: '0', attemptCount: '0' } });
    expect(decoded.recoveryEpoch).toBe('0'); expect(decoded.operation?.attemptToken).toBe('0'); expect(decoded.operation?.attemptCount).toBe('0');
    for (const value of ['-1', '01', '1.0', '9223372036854775808']) expect(() => decode(decodeOperationSnapshot, { ...before, recoveryEpoch: value })).toThrow();
  });
  it('accepts only each action’s own fields and bounds required operator text', () => {
    expect(decode(decodeOperationInput, { action: 'CANCEL', operationId: id, disposition: '  Deliberate cancellation  ' }).disposition).toBe('Deliberate cancellation');
    expect(() => decode(decodeOperationInput, { action: 'RETRY', operationId: id, findings: 'Unrelated' })).toThrow();
    expect(() => decode(decodeOperationInput, { action: 'CANCEL', operationId: id, disposition: ' ' })).toThrow();
    expect(() => decode(decodeOperationInput, { action: 'LIFT_QUARANTINE', operationId: id })).toThrow();
    expect(() => decode(decodeOperationInput, { action: 'RETRY', operationId: id, payload: 'Sensitive' })).toThrow();
    expect(() => decode(decodeOperationInput, { action: 'APPROVE_RECOVERY_RESTART', recoverySessionId: id, findings: 'x'.repeat(10001) })).toThrow();
  });
  it('binds preview and receipt to the exact operation, request, actor, epoch and local command event', () => {
    expect(decode(decodeOperationReceipt, receipt).commandEvent.after.operation?.status).toBe('RETRY_WAIT');
    expect(() => decode(decodeOperationPreview, { ...preview, canonicalInput: { action: 'RETRY', operationId: other } })).toThrow();
    for (const change of [{ requestId: id }, { requestHash: 'b'.repeat(64) }, { actorEpoch: '1' }, { action: 'CANCEL' }]) {
      expect(() => decode(decodeOperationReceipt, { ...receipt, ...change })).toThrow();
    }
    expect(() => decode(decodeOperationReceipt, { ...receipt, commandEvent: { ...receipt.commandEvent, actorId: other } })).toThrow();
    expect(() => decode(decodeOperationReceipt, { ...receipt, commandEvent: { ...receipt.commandEvent, after: { ...before, operation: { ...operation, targetId: id } } } })).toThrow();
    expect(() => decode(decodeOperationReceipt, { ...receipt, commandEvent: { ...receipt.commandEvent, before: { ...before, policyGeneration: '1' } } })).toThrow();
  });
  it('requires internally consistent found/receipt lookup without consulting current queue state', () => {
    expect(decode(decodeOperationLookup, { found: true, receipt }).receipt?.requestId).toBe(other);
    expect(decode(decodeOperationLookup, { found: false, receipt: null }).found).toBe(false);
    expect(() => decode(decodeOperationLookup, { found: false, receipt })).toThrow();
    expect(() => decode(decodeOperationLookup, { found: true, receipt: null })).toThrow();
  });
});
