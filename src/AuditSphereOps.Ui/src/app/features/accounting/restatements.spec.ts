import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationStart, Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { PeriodRestatements } from './restatements';

const a = '11111111-1111-4111-8111-111111111111', b = '22222222-2222-4222-8222-222222222222';
const hash = 'a'.repeat(64), basis = 'b'.repeat(64), epoch = '9007199254740993';
const period = { id: a, periodCode: '2025', startDate: '2025-01-01', endDate: '2025-12-31', basis: 'STATUTORY', currency: 'QAR', revision: 2,
  packages: [{ id: a, currency: 'QAR', hashPrefix: 'aaa' }, { id: b, currency: 'QAR', hashPrefix: 'bbb' }] };
const context = { periods: [period], restatements: [{ id: b, clientId: a, clientName: 'A', periodCode: '2025', revisedBasis: 'IFRS', status: 'SUBMITTED', evidenceReference: 'evidence', canReview: true }] };
const approvalDetail = { restatementId: b, clientId: a, engagementId: b, periodId: a, periodCode: '2025', periodStart: '2025-01-01', periodEnd: '2025-12-31', currency: 'QAR',
  originalPackageId: a, originalPackageHash: hash, revisedPackageId: b, revisedPackageHash: basis, revisedBasis: 'IFRS', changeType: 'RESTATED_ERROR',
  affectedPeriods: '2025', reason: 'Submitted correction', evidenceReference: 'submitted source', status: 'SUBMITTED', createdByUserId: b, createdAt: '2026-10-16T00:00:00Z' };
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: Error) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
describe('PeriodRestatements reviewed maintenance safety', () => {
  let page: PeriodRestatements, events: Subject<unknown>;
  let api: { resource: ReturnType<typeof vi.fn>; get: ReturnType<typeof vi.fn>; command: ReturnType<typeof vi.fn> };
  let drafts: { save: ReturnType<typeof vi.fn>; readPendingRequest: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };
  const session = { current: signal({ staff: true, userId: a, firmId: a, generation: epoch }), invalidation: signal(0) };
  const data = signal({ clients: [{ id: a, name: 'A' }, { id: b, name: 'B' }], periods: [], restatements: context.restatements });
  const state = (action = 'CREATE_RESTATEMENT', restatementId: string | null = null) => ({ clientId: a, action, periodId: a, restatementId, reviewBasis: basis, canProceed: true, blockers: [] });
  const receipt = (requestId: string, action = 'CREATE_RESTATEMENT') => ({ id: b, clientId: a, actorId: a, actorEpoch: epoch, requestId, requestHash: hash, action, reviewBasis: basis,
    createdAt: '2026-10-16T00:00:00Z', result: { periodId: action === 'ROLL_FORWARD' ? b : a, restatementId: action === 'ROLL_FORWARD' ? null : b, status: action === 'ROLL_FORWARD' ? 'DRAFT' : action === 'CREATE_RESTATEMENT' ? 'SUBMITTED' : 'APPROVED' } });
  beforeEach(() => {
    session.current.set({ staff: true, userId: a, firmId: a, generation: epoch }); session.invalidation.set(0);
    data.set({ clients: [{ id: a, name: 'A' }, { id: b, name: 'B' }], periods: [], restatements: context.restatements });
    events = new Subject();
    api = { resource: vi.fn(() => ({ data, loading: signal(false), error: signal(''), reload: vi.fn() })),
      get: vi.fn(async (url: string) => url.includes('/state?') ? state(new URL('https://local' + url).searchParams.get('action')!, new URL('https://local' + url).searchParams.get('restatementId')) : context),
      command: vi.fn(async (url: string, input) => ({ ok: true, value: url.endsWith('/preview') ? { requestId: input.requestId, requestHash: hash,
        state: state(input.action, input.restatementId), canonicalInput: input, summary: 'Reviewed maintenance effect', approvalDetail: input.action === 'APPROVE_RESTATEMENT' ? approvalDetail : null } : receipt(input.requestId, input.action) })) };
    drafts = { save: vi.fn(() => true), readPendingRequest: vi.fn(() => ({ state: 'absent' })), clear: vi.fn(() => true) };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api }, { provide: SessionService, useValue: session }, { provide: Router, useValue: { events } }, { provide: TabDrafts, useValue: drafts }] });
    page = TestBed.runInInjectionContext(() => new PeriodRestatements()); TestBed.tick();
  });
  afterEach(() => TestBed.resetTestingModule());
  async function fields() { await page.loadClient(a); page.f.original = a; page.f.revised = b; page.f.basis = 'STATUTORY'; page.f.reason = 'reason'; page.f.evidence = 'source'; page.f.type = 'RESTATED_ERROR'; page.f.affected = ''; }
  async function preview() { await fields(); await page.prepareCreate(); expect(page.flow.preview()).not.toBeNull(); }
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
      const preparing = page.prepareCreate(); await Promise.resolve();
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
  it('guards duplicate programmatic execution while busy and uncertain', async () => {
    await preview(); const result = deferred<unknown>(); api.command.mockReturnValueOnce(result.promise);
    page.flow.reviewed.set(true); const first = page.execute(); void page.execute();
    expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(1);
    result.resolve({ ok: false, unknown: true, code: 'outcome.unknown', message: 'unknown' }); await first; await page.execute(); await page.prepareCreate();
    expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(1); expect(page.cmd.uncertain()).toBe(true);
  });
  it('requires checkpoint storage before dispatch', async () => {
    await preview(); drafts.save.mockReturnValue(false); page.flow.reviewed.set(true); await page.execute();
    expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(0);
  });
  it('keeps exact unknown-request recovery after clearing drafts on failed context refresh', async () => {
    await preview(); const requestId = page.flow.preview()!.requestId;
    page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
    const writes = api.command.mock.calls.length;
    drafts.readPendingRequest.mockImplementation(scope => scope.entity.endsWith('/CREATE_RESTATEMENT')
      ? { state: 'ready', draft: { value: { requestId, requestHash: hash }, submissionPending: true } } : { state: 'absent' });
    page.f.evidence = 'Private draft'; api.get.mockRejectedValueOnce(new Error('Access denied.')); await page.loadClient(a);
    expect(page.f.evidence).toBe(''); expect(drafts.clear).not.toHaveBeenCalled();
    await page.loadClient(a); expect(page.cmd.uncertain()).toBe(true);
    api.get.mockResolvedValueOnce({ found: true, receipt: receipt(requestId) }); await page.flow.reconcile('CREATE_RESTATEMENT');
    expect(page.flow.receipt()?.requestId).toBe(requestId); expect(page.cmd.uncertain()).toBe(false);
    expect(api.command.mock.calls).toHaveLength(writes);
  });
  it('restores only the exact request reference after a fresh authorized client context read', async () => {
    drafts.readPendingRequest.mockImplementation(scope => scope.entity.endsWith('/CREATE_RESTATEMENT') ? { state: 'ready', draft: { value: { requestId: b, requestHash: hash }, submissionPending: true } } : { state: 'absent' });
    await page.loadClient(a); expect(page.cmd.uncertain()).toBe(true); expect(page.flow.reviewed()).toBe(false); expect(page.flow.preview()).toBeNull();
    api.get.mockResolvedValueOnce({ found: true, receipt: receipt(b) });
    await page.flow.reconcile('CREATE_RESTATEMENT'); expect(page.cmd.uncertain()).toBe(false); expect(page.flow.receipt()?.requestId).toBe(b);
    expect(api.command).not.toHaveBeenCalled();
  });
  for (const mismatch of ['actorId', 'actorEpoch', 'clientId', 'action', 'requestId', 'requestHash'] as const) {
    it(`keeps uncertainty when recovered receipt mismatches ${mismatch}`, async () => {
      await preview(); const id = page.flow.preview()!.requestId;
      page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
      api.get.mockResolvedValueOnce({ found: true, receipt: { ...receipt(id), [mismatch]: mismatch === 'actorEpoch' ? '9007199254740992' : mismatch === 'action' ? 'ROLL_FORWARD' : mismatch === 'requestHash' ? basis : b } });
      await page.flow.reconcile('CREATE_RESTATEMENT'); expect(page.cmd.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
    });
  }
  it('retains absent request identity and allows only fresh same-hash same-request review', async () => {
    await preview(); const id = page.flow.preview()!.requestId;
    page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
    api.get.mockResolvedValueOnce({ found: false, receipt: null }); await page.flow.reconcile('CREATE_RESTATEMENT');
    expect(page.cmd.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
    const before = api.command.mock.calls.length; await page.prepareCreate();
    expect(page.flow.preview()?.requestId).toBe(id); expect(page.flow.reviewed()).toBe(false);
    expect(api.command.mock.calls.slice(before).every(c => c[0].endsWith('/preview'))).toBe(true);
    page.flow.reviewed.set(true); await page.execute();
    expect(api.command.mock.calls.at(-1)![1].requestId).toBe(id);
  });
  it('refuses a changed preview hash for an absent retained request without creating another identity', async () => {
    await preview(); const id = page.flow.preview()!.requestId;
    page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
    api.get.mockResolvedValueOnce({ found: false, receipt: null }); await page.flow.reconcile('CREATE_RESTATEMENT');
    api.command.mockImplementationOnce(async (_url, input) => ({ ok: true, value: { requestId: input.requestId, requestHash: basis, state: state(), canonicalInput: input, summary: 'changed' } }));
    await page.prepareCreate(); expect(api.command.mock.calls.at(-1)![1].requestId).toBe(id);
    expect(page.flow.preview()).toBeNull(); expect(page.cmd.uncertain()).toBe(true); expect(drafts.clear).not.toHaveBeenCalled();
  });
  it('accepts a delayed exact receipt after a point-in-time missing lookup without another POST', async () => {
    await preview(); const id = page.flow.preview()!.requestId;
    page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
    const count = api.command.mock.calls.length;
    api.get.mockResolvedValueOnce({ found: false, receipt: null }); await page.flow.reconcile('CREATE_RESTATEMENT');
    api.get.mockResolvedValueOnce({ found: true, receipt: receipt(id) }); await page.flow.reconcile('CREATE_RESTATEMENT');
    expect(page.cmd.uncertain()).toBe(false); expect(page.flow.receipt()?.requestId).toBe(id); expect(api.command).toHaveBeenCalledTimes(count);
  });
  for (const failure of [false, true]) {
    it(`drops A-B-A late receipt lookup ${failure ? 'error' : 'success'}`, async () => {
      await preview(); const id = page.flow.preview()!.requestId;
      page.flow.reviewed.set(true); api.command.mockResolvedValueOnce({ ok: false, unknown: true, message: 'unknown' }); await page.execute();
      const read = deferred<unknown>(); api.get.mockReturnValueOnce(read.promise); const lookup = page.flow.reconcile('CREATE_RESTATEMENT');
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
  it('erases populated drafts on client switch, navigation, session loss and failed context refresh', async () => {
    await page.loadClient(a);
    const populate = () => { Object.assign(page.f, { basis: 'PRIVATE BASIS', type: 'ERROR', affected: '2024', evidence: 'PRIVATE EVIDENCE', reason: 'PRIVATE REASON' }); };
    const erased = () => { expect(page.f.basis).toBe(''); expect(page.f.type).toBe(''); expect(page.f.affected).toBe(''); expect(page.f.evidence).toBe(''); expect(page.f.reason).toBe(''); };
    populate(); page.f.client = b; erased();
    await page.loadClient(a); populate(); events.next(new NavigationStart(1, '/elsewhere')); erased();
    await page.loadClient(a); populate(); session.invalidation.update(n => n + 1); TestBed.tick(); erased();
    await page.loadClient(a); populate(); api.get.mockRejectedValueOnce(new Error('Access denied.'));
    await page.loadClient(a); erased(); expect(page.f.client).toBe(''); expect(page.closedError()).toBe('Access denied.');
  });
  it('preserves entered evidence on a successful refresh of the same client', async () => {
    await page.loadClient(a); page.f.evidence = 'Same-client draft';
    await page.loadClient(a); expect(page.f.evidence).toBe('Same-client draft');
  });
  it('fences route, session and destroyed lifetime', async () => {
    const read = deferred<typeof context>(); api.get.mockReturnValueOnce(read.promise); const loading = page.loadClient(a);
    events.next(new NavigationStart(1, '/elsewhere')); read.resolve(context); await loading; expect(page.closed()).toEqual([]);
    const next = deferred<typeof context>(); api.get.mockReturnValueOnce(next.promise); const again = page.loadClient(a);
    session.invalidation.update(n => n + 1); TestBed.tick(); next.resolve(context); await again; expect(page.closed()).toEqual([]);
    const final = deferred<typeof context>(); api.get.mockReturnValueOnce(final.promise); const last = page.loadClient(a);
    TestBed.resetTestingModule(); final.resolve(context); await last; expect(page.closed()).toEqual([]);
  });
  it('previews independent approval and requires separate fresh assent', async () => {
    await page.prepareApproval(b); expect(page.flow.preview()?.state.action).toBe('APPROVE_RESTATEMENT');
    await page.execute(); expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(0);
    page.flow.reviewed.set(true); await page.execute(); expect(page.flow.receipt()?.action).toBe('APPROVE_RESTATEMENT');
  });
  for (const field of ['clientId', 'periodId', 'restatementId'] as const) {
    it(`refuses approval detail with mismatched ${field}`, async () => {
      api.command.mockImplementationOnce(async (_url, input) => ({ ok: true, value: { requestId: input.requestId, requestHash: hash,
        state: state('APPROVE_RESTATEMENT', b), canonicalInput: input, summary: 'approval', approvalDetail: { ...approvalDetail, [field]: field === 'restatementId' ? a : b } } }));
      await page.prepareApproval(b); expect(page.flow.preview()).toBeNull(); expect(page.cmd.failed()).toBe(true);
      page.flow.reviewed.set(true); await page.execute();
      expect(api.command.mock.calls.filter(c => c[0].endsWith('/actions'))).toHaveLength(0);
    });
  }
  it('requires complete submitted evidence and an independent actor for approval', async () => {
    for (const detail of [null, { ...approvalDetail, originalPackageHash: 'partial' }, { ...approvalDetail, createdByUserId: a }]) {
      api.command.mockImplementationOnce(async (_url, input) => ({ ok: true, value: { requestId: input.requestId, requestHash: hash,
        state: state('APPROVE_RESTATEMENT', b), canonicalInput: input, summary: 'approval', approvalDetail: detail } }));
      await page.prepareApproval(b); expect(page.flow.preview()).toBeNull(); expect(page.flow.reviewed()).toBe(false);
    }
  });
  it('refuses independent review without server permission', async () => {
    data.set({ ...data(), restatements: [] }); TestBed.tick(); await page.prepareApproval(b); expect(api.command).not.toHaveBeenCalled();
  });
  it('clears obsolete selections and resets assent on changed restatement fields and sources', async () => {
    for (const key of ['period', 'original', 'revised', 'basis', 'type', 'affected', 'reason', 'evidence'] as const) {
      await preview(); page.flow.reviewed.set(true); page.f[key] += 'changed'; expect(page.flow.preview()).toBeNull(); expect(page.flow.reviewed()).toBe(false);
    }
    await fields(); page.choosePeriod(b); expect(page.f.original).toBe(''); expect(page.f.revised).toBe('');
    await fields(); await page.loadClient(b); expect(page.f.original).toBe(''); expect(page.f.revised).toBe('');
    await fields(); data.set({ ...data() }); TestBed.tick(); expect(page.f.period).toBe(''); expect(page.f.original).toBe(''); expect(page.f.revised).toBe('');
  });
});


describe('PeriodRestatements approval evidence presentation', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('renders full submitted package hashes, dates, reason and treatment before assent', async () => {
    const current = signal({ staff: true, userId: a, firmId: a, generation: epoch });
    const overview = signal({ clients: [{ id: a, name: 'A' }], periods: [], restatements: context.restatements });
    const api = {
      resource: () => ({ data: overview, loading: signal(false), error: signal(''), reload: vi.fn() }),
      get: vi.fn(async (url: string) => url.endsWith('/context') ? context : { clientId: a, action: 'APPROVE_RESTATEMENT', periodId: a, restatementId: b, reviewBasis: basis, canProceed: true, blockers: [] }),
      command: vi.fn(async (_url: string, input) => ({ ok: true, value: { requestId: input.requestId, requestHash: hash,
        state: { clientId: a, action: 'APPROVE_RESTATEMENT', periodId: a, restatementId: b, reviewBasis: basis, canProceed: true, blockers: [] },
        canonicalInput: input, summary: 'Approve independently', approvalDetail } }))
    };
    TestBed.configureTestingModule({ imports: [PeriodRestatements], providers: [provideRouter([]),
      { provide: Api, useValue: api }, { provide: SessionService, useValue: { current, invalidation: signal(0) } },
      { provide: TabDrafts, useValue: { readPendingRequest: () => ({ state: 'absent' }), save: vi.fn(), clear: vi.fn() } }] });
    const fixture = TestBed.createComponent(PeriodRestatements);
    fixture.detectChanges(); TestBed.tick();
    await fixture.componentInstance.prepareApproval(b); fixture.detectChanges();
    const panel = fixture.nativeElement.querySelector('[aria-label="Exact maintenance preview"]') as HTMLElement;
    expect(panel).not.toBeNull();
    for (const value of [approvalDetail.originalPackageId, approvalDetail.revisedPackageId, approvalDetail.originalPackageHash,
      approvalDetail.revisedPackageHash, approvalDetail.periodStart, approvalDetail.periodEnd, approvalDetail.currency,
      approvalDetail.reason, approvalDetail.changeType, approvalDetail.affectedPeriods, approvalDetail.evidenceReference]) {
      expect(panel.textContent).toContain(value);
    }
    expect(fixture.componentInstance.flow.reviewed()).toBe(false);
    const confirm = Array.from(panel.querySelectorAll('button')).find(button => button.textContent?.includes('Approve independently'));
    expect(confirm?.disabled).toBe(true);
    expect(api.command.mock.calls.every(call => call[0].endsWith('/preview'))).toBe(true);
  });
});
