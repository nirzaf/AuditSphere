import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationStart, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { OperationsCommandState, operationStateUrl } from './operations-command-state';
import { OperationInput, decodeOperationInput, operationActions } from './operations-command-contracts';

const actor = '11111111-1111-4111-8111-111111111111', id = '22222222-2222-4222-8222-222222222222';
const epoch = '9007199254740993', hash = 'a'.repeat(64), at = '2026-10-16T00:00:00Z';
const operation = { id, clientId: null, engagementId: null, targetId: id, correlationId: id, kind: 'SYNTHETIC', expectedRevision: epoch, attemptToken: epoch,
  attemptCount: '4', status: 'DEAD_LETTER', isReconciliation: false, hasLease: false, errorCode: null, cancellationDisposition: null, nextAttemptAt: null, completedAt: null, hasResultEvidence: false };
const recoverySession = { id, externalEpoch: epoch, restorePoint: 'Synthetic restore point', reconciliationScope: 'Synthetic scope', findings: '', createdAt: at, approvedRestartAt: null, approvedByUserId: null };
const snapshot = { operatingMode: 'LOCAL_ONLY', deploymentEpoch: epoch, recoveryEpoch: epoch, policyGeneration: epoch, operation, recoverySession };
const inputs = [ { action: 'RETRY', operationId: id }, { action: 'CANCEL', operationId: id, disposition: 'Deliberate cancellation' },
  { action: 'LIFT_QUARANTINE' }, { action: 'APPROVE_RECOVERY_RESTART', recoverySessionId: id, findings: 'Operator reconciliation findings' } ];
function before(input: OperationInput) { return { ...snapshot, operatingMode: input.operationId ? 'LOCAL_ONLY' : 'RECOVERY_QUARANTINE',
  recoveryEpoch: input.action === 'LIFT_QUARANTINE' && !input.recoverySessionId ? '0' : epoch,
  operation: input.operationId ? operation : null, recoverySession: input.action === 'LIFT_QUARANTINE' && !input.recoverySessionId ? null : recoverySession }; }
function state(input: OperationInput) { return { before: before(input), reviewBasis: hash, canRetry: true, canCancel: true, canLiftQuarantine: true, canApproveRecoveryRestart: true, blockers: [], retryBlockers: [], cancelBlockers: [], liftQuarantineBlockers: [], approveRecoveryRestartBlockers: ['An unrelated action may be unavailable'] }; }
function preview(request: { requestId: string; input: unknown }) { const input = decodeOperationInput(request.input, ''); return { requestId: request.requestId, requestHash: hash, reviewBasis: hash, canonicalInput: input,
  before: before(input), canProceed: true, blockers: [], summary: 'Exact local command' }; }
function receipt(p: ReturnType<typeof preview>) { return { id, actorId: actor, actorEpoch: epoch, requestId: p.requestId, requestHash: hash, action: p.canonicalInput.action, createdAt: at, preview: p,
  commandEvent: { id, actorId: actor, actorEpoch: epoch, action: p.canonicalInput.action, operationEventId: p.canonicalInput.operationId ? id : null, createdAt: at,
    before: p.before, after: { ...p.before, operatingMode: 'LOCAL_ONLY',
      operation: p.before.operation ? { ...operation, status: p.canonicalInput.action === 'CANCEL' ? 'CANCELLED_WITH_DISPOSITION' : 'RETRY_WAIT', cancellationDisposition: p.canonicalInput.disposition } : null,
      recoverySession: p.canonicalInput.action === 'APPROVE_RECOVERY_RESTART' ? { ...recoverySession, findings: p.canonicalInput.findings!, approvedByUserId: actor, approvedRestartAt: at } : p.before.recoverySession } } }; }
function deferred() { let resolve!: (value: unknown) => void, reject!: (error: Error) => void; const promise = new Promise<unknown>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }

describe('Reviewed operator commands and original receipts', () => {
  let flow: OperationsCommandState, api: { get: ReturnType<typeof vi.fn>; command: ReturnType<typeof vi.fn> }, saved: unknown;
  let reload: ReturnType<typeof vi.fn<() => void>>, events: Subject<NavigationStart>, p: ReturnType<typeof preview>, chosen: OperationInput;
  const context = signal<object | null>({}), session = { current: signal({ userId: actor, firmId: id, generation: epoch, staff: true }), invalidation: signal(0) };
  beforeEach(() => {
    context.set({}); session.current.set({ userId: actor, firmId: id, generation: epoch, staff: true }); session.invalidation.set(0); saved = null; events = new Subject(); reload = vi.fn<() => void>();
    chosen = decodeOperationInput(inputs[0], '');
    api = { get: vi.fn(async (url, decoder) => decoder(url.includes('/receipts/') ? { found: true, receipt: receipt(p) } : state(chosen), '')),
      command: vi.fn(async (url, body) => { if (url.endsWith('/preview')) { p = preview(body); return { ok: true, value: p }; } return { ok: true, value: receipt(p) }; }) };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api }, { provide: SessionService, useValue: session }, { provide: Router, useValue: { events } },
      { provide: TabDrafts, useValue: { readPendingRequest: () => saved ? { state: 'ready', draft: { value: saved, submissionPending: true } } : { state: 'absent' },
        save: (_scope: unknown, reference: unknown) => { saved = reference; return true; }, clear: () => { saved = null; return true; } } }] });
    flow = TestBed.runInInjectionContext(() => new OperationsCommandState(() => context(), reload)); TestBed.tick();
  });
  afterEach(() => TestBed.resetTestingModule());
  it('builds nullable state queries without inventing current recovery-session identity', () => {
    expect(operationStateUrl()).toBe('/api/ui/operations/commands/state');
    expect(operationStateUrl(id)).toBe(`/api/ui/operations/commands/state?operationId=${id}`);
    expect(operationStateUrl(null, id)).toBe(`/api/ui/operations/commands/state?recoverySessionId=${id}`);
  });
  for (const [index, action] of operationActions.entries()) {
    it(`${action}: requires fresh exact assent and dispatches once despite common state blockers`, async () => {
      chosen = decodeOperationInput(inputs[index], ''); await flow.prepare(() => inputs[index]); await flow.execute();
      expect(api.command).toHaveBeenCalledTimes(1); flow.reviewed.set(true); await flow.execute(); await flow.execute();
      expect(api.command).toHaveBeenCalledTimes(2); expect(flow.receipt()?.action).toBe(action); expect(flow.uncertain()).toBe(false);
      expect(api.command.mock.calls[1][1].expectedRequestHash).toBe(hash); expect(api.command.mock.calls[1][1].reviewed).toBe(true); expect(reload).toHaveBeenCalledTimes(1);
    });
    it(`${action}: restores reference only and recovers the original event after unrelated worker advancement`, async () => {
      chosen = decodeOperationInput(inputs[index], ''); await flow.prepare(() => inputs[index]); flow.reviewed.set(true);
      api.command.mockResolvedValueOnce({ ok: false, unknown: true, status: 503, message: 'Lost response' }); await flow.execute();
      expect(Object.keys(saved as object).sort()).toEqual(['requestHash', 'requestId']);
      context.set({ workerStatus: 'COMPLETED', attemptToken: '9999999999999999' }); TestBed.tick(); expect(flow.uncertain()).toBe(true);
      const restored = TestBed.runInInjectionContext(() => new OperationsCommandState(() => context(), reload)); TestBed.tick();
      expect(restored.preview()).toBeNull(); expect(restored.reviewed()).toBe(false); await restored.reconcile();
      expect(restored.receipt()?.commandEvent.before.operation?.attemptToken ?? epoch).toBe(epoch); expect(restored.uncertain()).toBe(false);
      expect(api.command).toHaveBeenCalledTimes(2); expect(api.get.mock.calls.at(-1)![0]).toContain('/receipts/');
    });
  }
  it('retains point-in-time absence and requires the same request hash and fresh assent', async () => {
    await flow.prepare(() => inputs[0]); flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'Lost' }); await flow.execute();
    const original = saved; api.get.mockResolvedValueOnce({ found: false, receipt: null }); await flow.reconcile();
    expect(flow.uncertain()).toBe(true); expect(flow.absent()).toBe(true); expect(saved).toBe(original);
    expect(flow.canPrepare('RETRY', id)).toBe(true); expect(flow.canPrepare('CANCEL', id)).toBe(false);
    await flow.prepare(() => inputs[0]); expect(p.requestId).toBe((original as { requestId: string }).requestId); expect(flow.reviewed()).toBe(false);
    await flow.execute(); expect(api.command).toHaveBeenCalledTimes(3);
  });
  for (const failure of [false, true]) it(`discards A-B-A late preview ${failure ? 'error' : 'success'}`, async () => {
    const late = deferred(); api.command.mockReturnValueOnce(late.promise); const preparing = flow.prepare(() => inputs[0]); await Promise.resolve();
    const request = api.command.mock.calls[0][1]; flow.inputChanged(); flow.inputChanged();
    if (failure) late.reject(new Error('late')); else late.resolve({ ok: true, value: preview(request) }); await preparing;
    expect(flow.preview()).toBeNull(); expect(flow.reviewed()).toBe(false);
  });
  for (const failure of [false, true]) it(`discards A-B-A late execution ${failure ? 'error' : 'success'} and retains uncertainty`, async () => {
    await flow.prepare(() => inputs[0]); flow.reviewed.set(true); const late = deferred(); api.command.mockReturnValueOnce(late.promise); const executing = flow.execute();
    context.set(null); TestBed.tick(); context.set({}); TestBed.tick();
    if (failure) late.reject(new Error('late')); else late.resolve({ ok: true, value: receipt(p) }); await executing;
    expect(flow.receipt()).toBeNull(); expect(flow.uncertain()).toBe(true); expect(saved).not.toBeNull();
  });
  it('consumes assent on edits, rejects changed input, and blocks duplicate dispatched commands', async () => {
    let input: OperationInput = { action: 'CANCEL', operationId: id, recoverySessionId: null, disposition: 'Deliberate cancellation', findings: null };
    chosen = decodeOperationInput(input, ''); await flow.prepare(() => input); flow.reviewed.set(true);
    input = { action: 'CANCEL', operationId: id, recoverySessionId: null, disposition: 'Changed cancellation', findings: null };
    await flow.execute(); expect(api.command).toHaveBeenCalledTimes(1);
    flow.inputChanged(); expect(flow.reviewed()).toBe(false); expect(flow.preview()).toBeNull();
    chosen = decodeOperationInput(inputs[0], ''); await flow.prepare(() => inputs[0]); flow.reviewed.set(true);
    const late = deferred(); api.command.mockReturnValueOnce(late.promise); const executing = flow.execute(); await flow.execute(); expect(api.command).toHaveBeenCalledTimes(3);
    late.resolve({ ok: false, unknown: true, message: 'Lost' }); await executing; await flow.execute(); expect(api.command).toHaveBeenCalledTimes(3);
  });
  for (const fence of ['route', 'session', 'destroy']) it(`fences a late receipt across ${fence}`, async () => {
    await flow.prepare(() => inputs[0]); flow.reviewed.set(true); const late = deferred(); api.command.mockReturnValueOnce(late.promise); const executing = flow.execute();
    if (fence === 'route') events.next(new NavigationStart(1, '/elsewhere'));
    if (fence === 'session') { session.invalidation.update(x => x + 1); TestBed.tick(); }
    if (fence === 'destroy') TestBed.resetTestingModule();
    late.resolve({ ok: true, value: receipt(p) }); await executing; expect(flow.receipt()).toBeNull(); expect(reload).not.toHaveBeenCalled();
  });
  it('rejects a recovered receipt owned by another actor or exact epoch', async () => {
    await flow.prepare(() => inputs[0]); flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'Lost' }); await flow.execute();
    const wrong = receipt(p); wrong.actorId = id; wrong.commandEvent.actorId = id; api.get.mockResolvedValueOnce({ found: true, receipt: wrong }); await flow.reconcile();
    expect(flow.uncertain()).toBe(true); expect(flow.receipt()).toBeNull(); expect(saved).not.toBeNull();
  });
  it('uses action-specific state eligibility and does not infer permission from other flags', async () => {
    api.get.mockResolvedValueOnce({ ...state(chosen), canRetry: false, retryBlockers: ['The operation holds a lease.'] });
    await flow.prepare(() => inputs[0]); expect(api.command).not.toHaveBeenCalled(); expect(flow.message()).toContain('holds a lease');
  });
  it('reauthorizes after state/preview denial without dispatching actions', async () => {
    api.get.mockRejectedValueOnce(new Error('Denied')); await flow.prepare(() => inputs[0]); expect(reload).toHaveBeenCalledTimes(1);
    api.command.mockResolvedValueOnce({ ok: false, unknown: false, status: 403, message: 'Denied' });
    await flow.prepare(() => inputs[0]); expect(reload).toHaveBeenCalledTimes(2); expect(flow.preview()).toBeNull();
    expect(api.command).toHaveBeenCalledTimes(1);
  });
  it('does not dispatch when checkpoint storage refuses the minimal reference', async () => {
    await flow.prepare(() => inputs[0]); flow.reviewed.set(true);
    vi.spyOn(TestBed.inject(TabDrafts), 'save').mockReturnValue(false); await flow.execute();
    expect(api.command).toHaveBeenCalledTimes(1); expect(flow.pending()).toBeNull(); expect(flow.message()).toContain('No command was sent');
  });
  it('does not substitute a changed hash after an absent lookup', async () => {
    await flow.prepare(() => inputs[0]); flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'Lost' }); await flow.execute();
    api.get.mockResolvedValueOnce({ found: false, receipt: null }); await flow.reconcile(); const original = saved;
    api.command.mockImplementationOnce(async (_url, body) => ({ ok: true, value: { ...preview(body), requestHash: 'b'.repeat(64) } }));
    await flow.prepare(() => inputs[0]); expect(flow.preview()).toBeNull(); expect(saved).toBe(original); expect(flow.uncertain()).toBe(true);
  });

});
