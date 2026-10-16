import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationStart, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { PeriodRollforward } from './rollforward';

const a = '11111111-1111-4111-8111-111111111111', b = '22222222-2222-4222-8222-222222222222';
const hash = 'a'.repeat(64), basis = 'b'.repeat(64), epoch = '9007199254740993';
const period = { id: a, periodCode: '2025', startDate: '2025-01-01', endDate: '2025-12-31', basis: 'STATUTORY', currency: 'QAR', revision: 2,
  packages: [{ id: a, currency: 'QAR', hashPrefix: 'aaa' }, { id: b, currency: 'QAR', hashPrefix: 'bbb' }] };
const context = { periods: [period], restatements: [{ id: b, clientId: a, clientName: 'A', periodCode: '2025', revisedBasis: 'IFRS', status: 'SUBMITTED', evidenceReference: 'evidence', canReview: true }] };
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: Error) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
describe('PeriodRollforward reviewed maintenance safety', () => {
  let page: PeriodRollforward, events: Subject<unknown>;
  let api: { resource: ReturnType<typeof vi.fn>; get: ReturnType<typeof vi.fn>; command: ReturnType<typeof vi.fn> };
  let drafts: { save: ReturnType<typeof vi.fn>; readPendingRequest: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };
  const session = { current: signal({ staff: true, userId: a, firmId: a, generation: epoch }), invalidation: signal(0) };
  const data = signal({ clients: [{ id: a, name: 'A' }, { id: b, name: 'B' }], periods: [], restatements: context.restatements });
  const state = (action = 'ROLL_FORWARD', restatementId: string | null = null) => ({ clientId: a, action, periodId: a, restatementId, reviewBasis: basis, canProceed: true, blockers: [] });
  const receipt = (requestId: string, action = 'ROLL_FORWARD') => ({ id: b, clientId: a, actorId: a, actorEpoch: epoch, requestId, requestHash: hash, action, reviewBasis: basis,
    createdAt: '2026-10-16T00:00:00Z', result: { periodId: action === 'ROLL_FORWARD' ? b : a, restatementId: action === 'ROLL_FORWARD' ? null : b, status: action === 'ROLL_FORWARD' ? 'DRAFT' : action === 'CREATE_RESTATEMENT' ? 'SUBMITTED' : 'APPROVED' } });
  beforeEach(() => {
    session.current.set({ staff: true, userId: a, firmId: a, generation: epoch }); session.invalidation.set(0);
    data.set({ clients: [{ id: a, name: 'A' }, { id: b, name: 'B' }], periods: [], restatements: context.restatements });
    events = new Subject();
    api = { resource: vi.fn(() => ({ data, loading: signal(false), error: signal(''), reload: vi.fn() })),
      get: vi.fn(async (url: string) => url.includes('/state?') ? state(new URL('https://local' + url).searchParams.get('action')!, new URL('https://local' + url).searchParams.get('restatementId')) : context),
      command: vi.fn(async (url: string, input) => ({ ok: true, value: url.endsWith('/preview') ? { requestId: input.requestId, requestHash: hash,
        state: state(input.action, input.restatementId), canonicalInput: input, summary: 'Reviewed maintenance effect' } : receipt(input.requestId, input.action) })) };
    drafts = { save: vi.fn(() => true), readPendingRequest: vi.fn(() => ({ state: 'absent' })), clear: vi.fn(() => true) };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api }, { provide: SessionService, useValue: session }, { provide: Router, useValue: { events } }, { provide: TabDrafts, useValue: drafts }] });
    page = TestBed.runInInjectionContext(() => new PeriodRollforward()); TestBed.tick();
  });
  afterEach(() => TestBed.resetTestingModule());
  async function fields() { await page.loadClient(a); page.f.closing = '0'; page.f.opening = '0'; page.f.hash = hash; page.f.evidence = 'source'; }
  async function preview() { await fields(); await page.prepare(); expect(page.flow.preview()).not.toBeNull(); }
  for (const failure of [false, true]) {
    it(`discards late A-B-A context ${failure ? 'error' : 'success'}`, async () => {
      const first = deferred<typeof context>(), middle = deferred<typeof context>(), last = deferred<typeof context>();
      api.get.mockReturnValueOnce(first.promise).mockReturnValueOnce(middle.promise).mockReturnValueOnce(last.promise);
      const one = page.loadClient(a), two = page.loadClient(b), three = page.loadClient(a);
      last.resolve(context); await three;
      if (failure) first.reject(new Error('obsolete')); else first.resolve({ ...context, periods: [{ ...period, periodCode: 'obsolete' }] });
      middle.resolve({ periods: [], restatements: [] }); await Promise.all([one, two]);
      expect(page.closed()[0].periodCode).toBe('2025'); expect(page.closedError()).toBe('');
    });
    it(`discards late A-B-A execution ${failure ? 'error' : 'success'} and retains checkpoint`, async () => {
      await preview(); const id = page.flow.preview()!.requestId, result = deferred<unknown>(); api.command.mockReturnValueOnce(result.promise);
      page.flow.reviewed.set(true); const executing = page.execute();
      await page.loadClient(b); await page.loadClient(a);
      if (failure) result.reject(new Error('obsolete')); else result.resolve({ ok: true, value: receipt(id) });
      await executing; expect(page.flow.receipt()).toBeNull(); expect(drafts.clear).not.toHaveBeenCalled();
    });
    it(`discards late A-B-A preview ${failure ? 'error' : 'success'}`, async () => {
      await fields(); const delayed = deferred<unknown>(); api.command.mockReturnValueOnce(delayed.promise);
      const preparing = page.prepare(); await Promise.resolve();
      const input = api.command.mock.calls[0][1];
      page.f.evidence = 'B'; page.f.evidence = 'source';
      if (failure) delayed.reject(new Error('obsolete')); else delayed.resolve({ ok: true, value: { requestId: input.requestId, requestHash: hash, state: state(), canonicalInput: input, summary: 'obsolete' } });
      await preparing; expect(page.flow.preview()).toBeNull(); expect(page.cmd.message()).toBe('');
    });
  }
  it('binds execution to the canonical preview hash, actor epoch and fresh assent', async () => {
    await preview(); await page.execute(); expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(0);
    page.flow.reviewed.set(true); await page.execute();
    const execution = api.command.mock.calls.find(c => c[0].endsWith('/actions'))!;
    expect(execution[1].expectedRequestHash).toBe(hash); expect(execution[1].reviewed).toBe(true);
    expect(page.flow.receipt()?.actorEpoch).toBe(epoch); expect(page.flow.reviewed()).toBe(false);
    expect(drafts.save.mock.calls[0][1]).toEqual({ requestId: execution[1].requestId, requestHash: hash });
  });
  it('accepts server six-place canonical amounts and executes exactly the displayed payload and hash', async () => {
    await fields(); page.f.closing = '100'; page.f.opening = '100';
    api.command.mockImplementationOnce(async (_url, input) => ({ ok: true, value: { requestId: input.requestId, requestHash: hash, state: state(), summary: 'Create opening bridge',
      canonicalInput: { ...input, rollForward: { ...input.rollForward, priorClosingAmount: '100.000000', currentOpeningAmount: '100.000000' } } } }));
    await page.prepare(); expect(page.flow.preview()?.canonicalInput.rollForward?.currentOpeningAmount).toBe('100.000000');
    expect(page.flow.reviewed()).toBe(false); page.flow.reviewed.set(true); await page.execute();
    const sent = api.command.mock.calls.find(c => c[0].endsWith('/actions'))![1];
    expect(sent.rollForward.priorClosingAmount).toBe('100.000000'); expect(sent.rollForward.currentOpeningAmount).toBe('100.000000');
    expect(sent.expectedRequestHash).toBe(hash);
  });
  it('rejects a canonical amount differing by one millionth before assent or execution', async () => {
    await fields();
    api.command.mockImplementationOnce(async (_url, input) => ({ ok: true, value: { requestId: input.requestId, requestHash: hash, state: state(), summary: 'changed',
      canonicalInput: { ...input, rollForward: { ...input.rollForward, priorClosingAmount: '0.000001', currentOpeningAmount: '0.000000' } } } }));
    await page.prepare(); expect(page.flow.preview()).toBeNull(); expect(page.flow.reviewed()).toBe(false);
    page.flow.reviewed.set(true); await page.execute(); expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(0);
  });
  it('guards duplicate programmatic execution while busy and uncertain', async () => {
    await preview(); const result = deferred<unknown>(); api.command.mockReturnValueOnce(result.promise);
    page.flow.reviewed.set(true); const first = page.execute(); void page.execute();
    expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(1);
    result.resolve({ ok: false, unknown: true, code: 'outcome.unknown', message: 'unknown' }); await first; await page.execute(); await page.prepare();
    expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(1); expect(page.cmd.uncertain()).toBe(true);
  });
  it('requires checkpoint storage before dispatch', async () => {
    await preview(); drafts.save.mockReturnValue(false); page.flow.reviewed.set(true); await page.execute();
    expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(0);
  });
  it('restores only the exact request reference after a fresh authorized client context read', async () => {
    drafts.readPendingRequest.mockImplementation(scope => scope.entity.endsWith('/ROLL_FORWARD') ? { state: 'ready', draft: { value: { requestId: b, requestHash: hash }, submissionPending: true } } : { state: 'absent' });
    await page.loadClient(a); expect(page.cmd.uncertain()).toBe(true); expect(page.flow.reviewed()).toBe(false); expect(page.flow.preview()).toBeNull();
    api.get.mockResolvedValueOnce({ found: true, receipt: receipt(b) });
    await page.flow.reconcile('ROLL_FORWARD'); expect(page.cmd.uncertain()).toBe(false); expect(page.flow.receipt()?.requestId).toBe(b);
    expect(api.command).not.toHaveBeenCalled();
  });
  for (const mismatch of ['actorId', 'actorEpoch', 'clientId', 'action', 'requestId', 'requestHash'] as const) {
    it(`keeps uncertainty when recovered receipt mismatches ${mismatch}`, async () => {
      await preview(); const id = page.flow.preview()!.requestId;
      page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
      api.get.mockResolvedValueOnce({ found: true, receipt: { ...receipt(id), [mismatch]: mismatch === 'actorEpoch' ? '9007199254740992' : mismatch === 'action' ? 'CREATE_RESTATEMENT' : mismatch === 'requestHash' ? basis : b } });
      await page.flow.reconcile('ROLL_FORWARD'); expect(page.cmd.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
    });
  }
  it('retains absent request identity and allows only fresh same-hash same-request review', async () => {
    await preview(); const id = page.flow.preview()!.requestId;
    page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
    api.get.mockResolvedValueOnce({ found: false, receipt: null }); await page.flow.reconcile('ROLL_FORWARD');
    expect(page.cmd.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
    const before = api.command.mock.calls.length; await page.prepare();
    expect(page.flow.preview()?.requestId).toBe(id); expect(page.flow.reviewed()).toBe(false);
    expect(api.command.mock.calls.slice(before).every(c => c[0].endsWith('/preview'))).toBe(true);
    page.flow.reviewed.set(true); await page.execute();
    expect(api.command.mock.calls.at(-1)![1].requestId).toBe(id);
  });
  it('refuses a changed preview hash for an absent retained request without creating another identity', async () => {
    await preview(); const id = page.flow.preview()!.requestId;
    page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
    api.get.mockResolvedValueOnce({ found: false, receipt: null }); await page.flow.reconcile('ROLL_FORWARD');
    api.command.mockImplementationOnce(async (_url, input) => ({ ok: true, value: { requestId: input.requestId, requestHash: basis, state: state(), canonicalInput: input, summary: 'changed' } }));
    await page.prepare(); expect(api.command.mock.calls.at(-1)![1].requestId).toBe(id);
    expect(page.flow.preview()).toBeNull(); expect(page.cmd.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
  });
  it('accepts a delayed exact receipt after a point-in-time missing lookup without another POST', async () => {
    await preview(); const id = page.flow.preview()!.requestId;
    page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
    const count = api.command.mock.calls.length;
    api.get.mockResolvedValueOnce({ found: false, receipt: null }); await page.flow.reconcile('ROLL_FORWARD');
    api.get.mockResolvedValueOnce({ found: true, receipt: receipt(id) }); await page.flow.reconcile('ROLL_FORWARD');
    expect(page.cmd.uncertain()).toBe(false); expect(page.flow.receipt()?.requestId).toBe(id); expect(api.command).toHaveBeenCalledTimes(count);
  });
  for (const failure of [false, true]) {
    it(`drops A-B-A late receipt lookup ${failure ? 'error' : 'success'}`, async () => {
      await preview(); const id = page.flow.preview()!.requestId;
      page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
      const read = deferred<unknown>(); api.get.mockReturnValueOnce(read.promise); const lookup = page.flow.reconcile('ROLL_FORWARD');
      page.f.evidence = 'B'; page.f.evidence = 'source';
      if (failure) read.reject(new Error('obsolete')); else read.resolve({ found: true, receipt: receipt(id) });
      await lookup; expect(page.flow.receipt()).toBeNull(); expect(drafts.clear).not.toHaveBeenCalled();
      expect(page.cmd.uncertain()).toBe(true);
    });
  }
  it('never clears uncertain on arbitrary context reads', async () => {
    await preview(); page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
    await page.loadClient(a); expect(page.cmd.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
  });
  it('fences route, session and destroyed lifetime', async () => {
    const read = deferred<typeof context>(); api.get.mockReturnValueOnce(read.promise); const loading = page.loadClient(a);
    events.next(new NavigationStart(1, '/elsewhere')); read.resolve(context); await loading; expect(page.closed()).toEqual([]);
    const next = deferred<typeof context>(); api.get.mockReturnValueOnce(next.promise); const again = page.loadClient(a);
    session.invalidation.update(n => n + 1); TestBed.tick(); next.resolve(context); await again; expect(page.closed()).toEqual([]);
    const final = deferred<typeof context>(); api.get.mockReturnValueOnce(final.promise); const last = page.loadClient(a);
    TestBed.resetTestingModule(); final.resolve(context); await last; expect(page.closed()).toEqual([]);
  });
  it('invalidates preview and assent on every editable field, source and client change', async () => {
    for (const key of ['client', 'prior', 'code', 'start', 'end', 'basis', 'currency', 'package', 'closing', 'opening', 'hash', 'evidence'] as const) {
      await preview(); page.flow.reviewed.set(true); page.f[key] = page.f[key] + 'changed';
      expect(page.flow.reviewed()).toBe(false); expect(page.flow.preview()).toBeNull();
    }
    await preview(); page.flow.reviewed.set(true); data.set({ ...data() }); TestBed.tick();
    expect(page.flow.reviewed()).toBe(false); expect(page.f.package).toBe(''); expect(page.prior()).toBeNull();
  });
});
