import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { OpeningBalancesPanel, openingAmount } from './opening-balances';
import { openingBalanceRecord, openingBalanceState, openingBalancePreview } from './opening-balances-contracts';

type RecordView = ReturnType<typeof openingBalanceRecord>;
type State = ReturnType<typeof openingBalanceState>;
type Preview = ReturnType<typeof openingBalancePreview>;
const id = (n: number) => `${n.toString().repeat(8)}-${n.toString().repeat(4)}-4${n.toString().repeat(3)}-8${n.toString().repeat(3)}-${n.toString().repeat(12)}`;
const engagement = id(1), client = id(2), verification = id(3), actor = id(4), firm = id(5), preparer = id(6);
const date = '2026-12-31', base = `/api/ui/engagements/${engagement}/completion/opening-balances`;
const stateUrl = `${base}/state?asOfDate=${date}`;
const storageKey = `auditsphere-opening-balances-pending-v1:${firm}:${actor}:${engagement}`;
const sessionValue = { userId: actor, firmId: firm, generation: '7', staff: true };
const fields = { procedureId: null, priorPeriodId: null, priorReference: 'Prior signed statements', asOfDate: date,
  openingSignedTotal: '9999999999999.999999', agreedSignedTotal: '9999999999999.999999', accountingPoliciesConsistent: true,
  currency: 'USD', rationale: 'Agreed to prior statements', evidenceReferences: ['prior-statement:exact'] };
const record: RecordView = { ...fields, id: verification, engagementId: engagement, conclusion: 'AGREED', differenceAmount: '0',
  revision: '9007199254740993', recordedByUserId: preparer, reviewedByUserId: null, recordedAt: '2026-12-31T00:00:00Z',
  reviewedAt: null, reviewedBasisRevision: null, reviewComment: null, comparisonResolved: true, currentIndependentReview: false };
const blank: State = { engagementId: engagement, clientId: client, asOfDate: date, expectedRevision: '0', reviewBasis: 'a'.repeat(64),
  record: null, canPrepare: true, canReview: false, blockers: [] };
const reviewState: State = { ...blank, record, expectedRevision: record.revision, canPrepare: false, canReview: true };

describe('native exact opening-balances commands', () => {
  function setup() {
    TestBed.configureTestingModule({ imports: [OpeningBalancesPanel], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const session = TestBed.inject(SessionService); session.current.set(sessionValue);
    const fixture = TestBed.createComponent(OpeningBalancesPanel); fixture.componentRef.setInput('engagementId', engagement); fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController), session };
  }
  type Setup = ReturnType<typeof setup>;
  async function load(s: Setup, state = blank) {
    s.page.form.asOfDate = date;
    const pending = s.page.load(); s.http.expectOne(stateUrl).flush(state); await pending; s.fixture.detectChanges();
  }
  function fill(s: Setup) {
    Object.assign(s.page.form, { ...fields, procedureId: '', priorPeriodId: '', currency: '\u0085usd\u0085',
      rationale: '\u0085Agreed to prior statements\u0085', accountingPoliciesConsistent: 'true', evidenceReferences: 'prior-statement:exact' });
    s.page.inputsChanged(); s.fixture.detectChanges();
  }
  async function preview(s: Setup, state = blank, action: 'PREPARE' | 'REVIEW' = 'PREPARE') {
    const task = s.page.preparePreview(action);
    const request = s.http.expectOne(`${base}/${action === 'PREPARE' ? 'prepare' : `${verification}/review`}/preview`);
    expect(request.request.body.expectedRevision).toBe(state.expectedRevision);
    if (action === 'PREPARE') expect(request.request.body.fields).toEqual(fields);
    const response: Preview = { requestId: request.request.body.requestId, action, requestHash: 'b'.repeat(64), reviewBasis: state.reviewBasis,
      expectedRevision: state.expectedRevision, currentRecord: state.record, canProceed: true, blockers: [], summary: 'Exact intent' };
    request.flush({ value: response }); await task; s.page.assent = true;
    return response;
  }
  function receipt(p: Preview, result: RecordView) {
    return { id: id(7), engagementId: engagement, verificationId: verification, actorId: actor, actorEpoch: '7',
      requestId: p.requestId, requestHash: p.requestHash, action: p.action, createdAt: '2026-12-31T00:01:00Z', preview: p, result };
  }
  async function nextRequest(s: Setup, url: string) {
    let req: ReturnType<HttpTestingController['match']>[number] | undefined;
    await vi.waitFor(() => { req = s.http.match(url)[0] ?? req; expect(req).toBeDefined(); });
    return req!;
  }
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); vi.restoreAllMocks(); sessionStorage.clear(); });

  it('requires an explicit policy answer and preserves six-place financial strings', async () => {
    const s = setup(); await load(s); fill(s); s.page.form.accountingPoliciesConsistent = '';
    expect(s.page.canPreview('PREPARE')).toBe(false); await s.page.preparePreview('PREPARE'); s.http.expectNone(`${base}/prepare/preview`);
    s.page.form.accountingPoliciesConsistent = 'false'; expect(s.page.canPreview('PREPARE')).toBe(true);
    expect(openingAmount('-0001.200000')).toBe('-1.2'); expect(openingAmount('-0.000000')).toBe('0');
    expect(openingAmount(fields.openingSignedTotal)).toBe('9999999999999.999999');
    expect(openingAmount('1.0000001')).toBeNull(); expect(openingAmount('10000000000000')).toBeNull();
    for (const field of ['openingSignedTotal', 'agreedSignedTotal', 'differenceAmount', 'revision']) {
      expect(() => decode(openingBalanceRecord, { ...record, [field]: 1 })).toThrow();
    }
  });

  it('reads the full supported evidence bound and legacy unverifiable state without claiming clearance', async () => {
    const s = setup();
    const legacy: RecordView = { ...record, evidenceReferences: Array.from({ length: 101 }, (_, i) => `source-${i}`),
      conclusion: 'NOT_VERIFIABLE', comparisonResolved: false, currentIndependentReview: false };
    await load(s, { ...reviewState, record: legacy, canReview: false });
    expect(s.page.state()?.record?.evidenceReferences).toHaveLength(101);
    expect(s.page.state()?.record?.comparisonResolved).toBe(false);
    expect(s.page.canPreview('REVIEW')).toBe(false);
  });

  it('persists bounded recovery before sending exact normalized preparation, confirms unreviewed receipt and refreshes', async () => {
    const s = setup(); await load(s); fill(s); const p = await preview(s);
    const emitted = vi.spyOn(s.page.refreshed, 'emit'); const task = s.page.submit();
    const command = s.http.expectOne(`${base}/prepare`);
    const raw = sessionStorage.getItem(storageKey)!;
    expect(raw.length).toBeLessThan(4096); expect(JSON.parse(raw).requestId).toBe(p.requestId);
    for (const secret of [fields.rationale, fields.openingSignedTotal, fields.evidenceReferences[0]]) expect(raw).not.toContain(secret);
    expect(command.request.body).toMatchObject({ fields, expectedRequestHash: p.requestHash, reviewed: true });
    expect(s.page.state()).toBeNull();
    const result = { ...record, revision: '1', recordedByUserId: actor };
    command.flush({ value: receipt(p, result) });
    const refresh = await nextRequest(s, stateUrl); refresh.flush({ ...blank, expectedRevision: '1', record: result }); await task;
    expect(sessionStorage.getItem(storageKey)).toBeNull(); expect(s.page.pending()).toBeNull();
    expect(s.page.state()?.record?.reviewedByUserId).toBeNull(); expect(s.page.history()).toContain('Exact verification state refreshed'); expect(emitted).toHaveBeenCalledOnce();
  });

  it.each(['difference', 'self', 'denied'])('refuses ineligible review: %s', async kind => {
    const s = setup(); const r = { ...record };
    if (kind === 'self') r.recordedByUserId = actor;
    if (kind === 'difference') { r.agreedSignedTotal = '9999999999999.999998'; r.differenceAmount = '0.000001';
      r.conclusion = 'DIFFERENCES_UNRESOLVED'; r.comparisonResolved = false; }
    await load(s, { ...reviewState, record: r, canReview: kind !== 'denied' });
    expect(s.page.canPreview('REVIEW')).toBe(false); await s.page.preparePreview('REVIEW'); s.http.expectNone(`${base}/${verification}/review/preview`);
  });

  it('reviews the independent exact revision without rounding and retains unrelated blockers', async () => {
    const s = setup(); await load(s, reviewState); const p = await preview(s, reviewState, 'REVIEW');
    const task = s.page.submit(); const request = s.http.expectOne(`${base}/${verification}/review`);
    expect(request.request.body.expectedRevision).toBe('9007199254740993'); expect(request.request.body.fields).toBeNull();
    expect(request.request.body.expectedVerificationId).toBe(verification);
    const result = { ...record, revision: '9007199254740994', reviewedBasisRevision: record.revision, reviewedByUserId: actor, reviewedAt: '2026-12-31T00:01:00Z', currentIndependentReview: true };
    request.flush({ value: receipt(p, result) });
    const refresh = await nextRequest(s, stateUrl);
    refresh.flush({ ...reviewState, record: result, expectedRevision: result.revision, canReview: false,
      blockers: ['unrelated:gate'] }); await task; s.fixture.detectChanges();
    expect(s.page.state()?.record?.currentIndependentReview).toBe(true);
    expect(s.fixture.nativeElement.textContent).toContain('unrelated:gate');
  });

  it('amends the exact verification, invalidates review and preserves historical review metadata', async () => {
    const s = setup();
    const reviewed: RecordView = { ...record, recordedByUserId: actor, revision: '9007199254740994',
      reviewedByUserId: preparer, reviewedBasisRevision: record.revision, reviewedAt: '2026-12-31T00:01:00Z',
      reviewComment: 'Prior review', currentIndependentReview: true };
    const state: State = { ...blank, record: reviewed, expectedRevision: reviewed.revision };
    await load(s, state); fill(s); const p = await preview(s, state);
    const task = s.page.submit(), request = s.http.expectOne(`${base}/prepare`);
    expect(request.request.body.expectedVerificationId).toBe(verification);
    const result: RecordView = { ...record, revision: '9007199254740995', recordedByUserId: actor,
      reviewedBasisRevision: reviewed.reviewedBasisRevision, reviewComment: reviewed.reviewComment };
    request.flush({ value: receipt(p, result) });
    (await nextRequest(s, stateUrl)).flush({ ...state, record: result, expectedRevision: result.revision }); await task;
    expect(s.page.state()?.record?.comparisonResolved).toBe(true);
    expect(s.page.state()?.record?.currentIndependentReview).toBe(false);
    expect(s.page.state()?.record?.reviewedBasisRevision).toBe(reviewed.reviewedBasisRevision);
    expect(s.page.state()?.record?.reviewComment).toBe('Prior review');
  });

  it('discards a late state response after date navigation', async () => {
    const s = setup(); s.page.form.asOfDate = date;
    const task = s.page.load(), request = s.http.expectOne(stateUrl);
    s.page.form.asOfDate = '2027-01-01'; s.page.selectionChanged();
    request.flush(reviewState); await task;
    expect(s.page.state()).toBeNull(); expect(s.page.preview()).toBeNull();
  });

  it('invalidates changed review comments before submission', async () => {
    const s = setup(); await load(s, reviewState); await preview(s, reviewState, 'REVIEW');
    s.page.form.comment = 'Changed intent'; s.page.inputsChanged();
    await s.page.submit(); s.http.expectNone(`${base}/${verification}/review`);
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('retains unknown execution across remount, never resends and only clears after exact receipt lookup', async () => {
    let s = setup(); await load(s); fill(s); const p = await preview(s);
    const task = s.page.submit(); s.http.expectOne(`${base}/prepare`).flush({}, { status: 503, statusText: 'Unavailable' }); await task;
    const raw = sessionStorage.getItem(storageKey)!; expect(s.page.pending()?.requestId).toBe(p.requestId);
    s.fixture.destroy(); TestBed.resetTestingModule(); s = setup();
    expect(s.page.pending()?.requestId).toBe(p.requestId); expect(s.page.form.rationale).toBe('');
    await load(s); fill(s); expect(s.page.canPreview('PREPARE')).toBe(false); await s.page.submit(); s.http.expectNone(`${base}/prepare`);
    let recovery = s.page.recover(); s.http.expectOne(`${base}/receipts/${p.requestId}?requestHash=${p.requestHash}`).flush({ found: false, receipt: null }); await recovery;
    expect(sessionStorage.getItem(storageKey)).toBe(raw); expect(s.page.error()).toContain('remains unresolved');
    const result = { ...record, revision: '1', recordedByUserId: actor };
    recovery = s.page.recover(); s.http.expectOne(`${base}/receipts/${p.requestId}?requestHash=${p.requestHash}`).flush({ found: true, receipt: receipt(p, result) });
    (await nextRequest(s, stateUrl)).flush({ ...blank, expectedRevision: '1', record: result }); await recovery;
    expect(sessionStorage.getItem(storageKey)).toBeNull(); expect(s.page.history()).toContain('Confirmed prepare receipt');
  });

  it.each(['actor', 'epoch', 'content', 'basis', 'reviewed'])('rejects a mismatched receipt and retains recovery: %s', async change => {
    const s = setup(); await load(s); fill(s); const p = await preview(s), task = s.page.submit();
    const r = receipt(p, { ...record, revision: '1', recordedByUserId: actor });
    if (change === 'actor') r.actorId = preparer;
    if (change === 'epoch') r.actorEpoch = '8';
    if (change === 'content') r.result.rationale = 'Different professional intent';
    if (change === 'basis') r.preview = { ...p, currentRecord: record };
    if (change === 'reviewed') r.result.reviewedByUserId = preparer;
    s.http.expectOne(`${base}/prepare`).flush({ value: r }); await task;
    expect(sessionStorage.getItem(storageKey)).not.toBeNull(); expect(s.page.pending()).not.toBeNull(); expect(s.page.state()).toBeNull();
    expect(s.page.history()).toBe(''); s.http.expectNone(stateUrl);
  });

  it('invalidates changed inputs and changed session during asynchronous preview hashing', async () => {
    const s = setup(); await load(s); fill(s);
    const original = crypto.subtle.digest.bind(crypto.subtle);
    let finish!: (value: ArrayBuffer) => void;
    const paused = new Promise<ArrayBuffer>(resolve => { finish = resolve; });
    const hashing = vi.spyOn(crypto.subtle, 'digest').mockReturnValueOnce(paused);
    const task = s.page.preparePreview('PREPARE'), request = s.http.expectOne(`${base}/prepare/preview`);
    request.flush({ value: { requestId: request.request.body.requestId, action: 'PREPARE', requestHash: 'b'.repeat(64), reviewBasis: blank.reviewBasis,
      expectedRevision: '0', currentRecord: null, canProceed: true, blockers: [], summary: 'Exact intent' } });
    await vi.waitFor(() => expect(hashing).toHaveBeenCalledOnce());
    s.page.form.rationale = 'Changed'; s.page.inputsChanged(); s.session.current.set({ ...sessionValue, generation: '8' }); s.fixture.detectChanges();
    finish(await original('SHA-256', new TextEncoder().encode('old'))); await task;
    expect(s.page.preview()).toBeNull(); expect(s.page.state()).toBeNull(); expect(s.page.busy()).toBe(false);
  });

  it('cannot clear a changed reference or emit stale approval while receipt hashing is pending', async () => {
    const s = setup(); await load(s); fill(s); const p = await preview(s);
    const original = crypto.subtle.digest.bind(crypto.subtle); let finish!: (value: ArrayBuffer) => void;
    const paused = new Promise<ArrayBuffer>(resolve => { finish = resolve; });
    const hashing = vi.spyOn(crypto.subtle, 'digest').mockReturnValueOnce(paused);
    const task = s.page.submit(), r = receipt(p, { ...record, revision: '1', recordedByUserId: actor });
    s.http.expectOne(`${base}/prepare`).flush({ value: r });
    await vi.waitFor(() => expect(hashing).toHaveBeenCalledOnce());
    const replacement = JSON.stringify({ ...s.page.pending(), requestId: id(8) }); sessionStorage.setItem(storageKey, replacement);
    finish(await original('SHA-256', new TextEncoder().encode(JSON.stringify([null, null, fields.priorReference, date, 'USD', fields.openingSignedTotal, fields.agreedSignedTotal, true, fields.rationale, fields.evidenceReferences]))));
    await task;
    expect(sessionStorage.getItem(storageKey)).toBe(replacement); expect(s.page.history()).toBe(''); expect(s.page.error()).toContain('reference changed'); s.http.expectNone(stateUrl);
  });

  it('separates a confirmed receipt from a failed current refresh', async () => {
    const s = setup(); await load(s); fill(s); const p = await preview(s), task = s.page.submit();
    s.http.expectOne(`${base}/prepare`).flush({ value: receipt(p, { ...record, revision: '1', recordedByUserId: actor }) });
    (await nextRequest(s, stateUrl)).flush({}, { status: 403, statusText: 'Forbidden' }); await task;
    expect(s.page.pending()).toBeNull(); expect(s.page.state()).toBeNull(); expect(s.page.history()).toContain('Current state could not be refreshed');
  });

  it('refuses a command when persistent recovery storage is unavailable', async () => {
    const s = setup(); await load(s); fill(s); await preview(s);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Storage unavailable'); });
    await s.page.submit(); s.http.expectNone(`${base}/prepare`); expect(s.page.error()).toContain('Storage unavailable');
  });
  it.each(['missing-time', 'missing-basis', 'wrong-revision', 'self'])('refuses inconsistent review identity in authoritative read: %s', async kind => {
    const s = setup();
    const r = { ...record, revision: '9007199254740994', reviewedByUserId: actor, reviewedAt: '2026-12-31T00:01:00Z' as string | null,
      reviewedBasisRevision: record.revision as string | null, currentIndependentReview: true };
    if (kind === 'missing-time') r.reviewedAt = null;
    if (kind === 'missing-basis') r.reviewedBasisRevision = null;
    if (kind === 'wrong-revision') r.reviewedBasisRevision = '1';
    if (kind === 'self') r.reviewedByUserId = preparer;
    await load(s, { ...reviewState, record: r, expectedRevision: r.revision });
    expect(s.page.state()).toBeNull(); expect(s.page.error()).toContain('could not be verified');
    expect(s.fixture.nativeElement.querySelector('[data-ob-record]')).toBeNull();
  });

  it.each(['scope', 'session', 'inputs'])('invalidates a receipt while its content hash is awaiting: %s', async change => {
    const s = setup(); await load(s); fill(s); const p = await preview(s);
    const original = crypto.subtle.digest.bind(crypto.subtle); let finish!: (value: ArrayBuffer) => void;
    const hashing = vi.spyOn(crypto.subtle, 'digest').mockReturnValueOnce(new Promise<ArrayBuffer>(resolve => { finish = resolve; }));
    const task = s.page.submit(); s.http.expectOne(`${base}/prepare`).flush({ value: receipt(p, { ...record, revision: '1', recordedByUserId: actor }) });
    await vi.waitFor(() => expect(hashing).toHaveBeenCalledOnce());
    const raw = sessionStorage.getItem(storageKey);
    if (change === 'scope') { s.fixture.componentRef.setInput('engagementId', id(8)); s.fixture.detectChanges(); }
    if (change === 'session') { s.session.current.set({ ...sessionValue, generation: '8' }); s.fixture.detectChanges(); }
    if (change === 'inputs') { s.page.form.rationale = 'Changed while awaiting'; s.page.inputsChanged(); }
    finish(await original('SHA-256', new TextEncoder().encode(JSON.stringify([null, null, fields.priorReference, date, 'USD', fields.openingSignedTotal, fields.agreedSignedTotal, true, fields.rationale, fields.evidenceReferences]))));
    await task;
    expect(sessionStorage.getItem(storageKey)).toBe(raw); expect(s.page.history()).toBe(''); s.http.expectNone(stateUrl);
  });

});
